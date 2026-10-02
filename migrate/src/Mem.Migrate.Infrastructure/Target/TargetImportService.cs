using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Security;
using Mem.Migrate.Core.Target;

namespace Mem.Migrate.Infrastructure.Target;

public sealed class TargetImportService : ITargetImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly HttpClient _httpClient;
    private readonly ITargetImportJournal _journal;
    private readonly ITargetProfileCredentialResolver _profileCredentialResolver;

    public TargetImportService(
        HttpClient httpClient,
        ITargetImportJournal journal,
        ITargetProfileCredentialResolver profileCredentialResolver)
    {
        _httpClient = httpClient;
        _journal = journal;
        _profileCredentialResolver = profileCredentialResolver;
    }

    public async Task<TargetImportReport> ImportAsync(TargetImportOptions options, CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        EnsureInputFiles(normalized);
        var targetCredential = await _profileCredentialResolver.ResolveAsync(
            normalized.ProfileName,
            cancellationToken);
        var started = DateTimeOffset.UtcNow;
        var manifestSha = await Sha256File.ComputeAsync(normalized.ManifestPath, cancellationToken);
        var zipSha = await Sha256File.ComputeAsync(normalized.StackExportPath, cancellationToken);
        var zipBytes = new FileInfo(normalized.StackExportPath).Length;
        using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(normalized.ManifestPath, cancellationToken));
        var artifact = FindMatchingArtifact(manifestDocument.RootElement, zipSha, zipBytes);
        var artifactId = artifact.GetProperty("artifactId").GetString()
            ?? throw new InvalidDataException("Neutral manifest artifactId is required.");

        await _journal.InitializeAsync(cancellationToken);
        var stored = await _journal.GetAsync(normalized.AttemptId!, cancellationToken);
        if (stored is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(stored.ManifestSha256),
                    Convert.FromHexString(manifestSha)) ||
                !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(stored.StackExportSha256),
                    Convert.FromHexString(zipSha)))
            {
                throw new InvalidOperationException(
                    "The resumed target import inputs do not match the " +
                    "journaled manifest and ZIP receipts.");
            }

            if (string.IsNullOrWhiteSpace(stored.TargetProfileName) ||
                string.IsNullOrWhiteSpace(stored.TargetBaseUrl))
            {
                throw new InvalidOperationException(
                    "The existing target import attempt predates profile-bound " +
                    "authentication. Start a new attempt ID.");
            }

            if (!string.Equals(
                    stored.TargetProfileName,
                    targetCredential.ProfileName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    stored.TargetBaseUrl,
                    targetCredential.TargetBaseUrl,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The resumed target import profile does not match the " +
                    "journaled target identity.");
            }
            if (!normalized.Resume)
                throw new InvalidOperationException("Target import attempt already exists. Use --resume with the same inputs.");
            if (stored.Status == "Completed" && stored.ReportJson is not null)
                return JsonSerializer.Deserialize<TargetImportReport>(stored.ReportJson, JsonOptions)
                    ?? throw new InvalidDataException("Completed target import report could not be read from the journal.");
        }
        else
        {
            await _journal.StartAsync(
                normalized.AttemptId!,
                started,
                manifestSha,
                zipSha,
                targetCredential.ProfileName,
                targetCredential.TargetBaseUrl,
                cancellationToken);
            stored = await _journal.GetAsync(normalized.AttemptId!, cancellationToken);
        }

        try
        {
            ConfigureClient(normalized, targetCredential);
            var intakeId = stored?.IntakeId;
            var validationId = stored?.ValidationId;
            var catalogEntryId = stored?.CatalogEntryId;

            if (string.IsNullOrWhiteSpace(intakeId))
            {
                var preview = await PostJsonAsync<IntakeDetail>(
                    "/api/operator/migrations/intakes/preview",
                    new { displayName = normalized.DisplayName, manifest = manifestDocument.RootElement.Clone() },
                    cancellationToken);
                if (preview.Intake.ErrorCount > 0)
                    throw new InvalidOperationException("Target rejected the neutral migration manifest during preview.");
                intakeId = preview.Intake.IntakeId;
                await _journal.SaveProgressAsync(normalized.AttemptId!, intakeId, null, null, artifactId, cancellationToken);
            }

            var intake = await GetAsync<IntakeDetail>($"/api/operator/migrations/intakes/{Uri.EscapeDataString(intakeId)}", cancellationToken);
            if (intake.Intake.Status == "previewed")
                intake = await PostJsonAsync<IntakeDetail>($"/api/operator/migrations/intakes/{Uri.EscapeDataString(intakeId)}/commit", new { }, cancellationToken);
            if (intake.Intake.Status != "committed")
                throw new InvalidOperationException($"Target migration intake is in unsupported status '{intake.Intake.Status}'.");

            if (string.IsNullOrWhiteSpace(validationId) || string.IsNullOrWhiteSpace(catalogEntryId))
            {
                var upload = await UploadAsync(normalized.StackExportPath, cancellationToken);
                if (!string.Equals(upload.Status, "valid", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(upload.CatalogEntryId))
                    throw new InvalidOperationException("Target validated import did not produce an available Backup Catalog entry.");
                validationId = upload.ValidationId;
                catalogEntryId = upload.CatalogEntryId;
                await _journal.SaveProgressAsync(normalized.AttemptId!, intakeId, validationId, catalogEntryId, artifactId, cancellationToken);
            }

            var binding = await PostJsonAsync<BindingResponse>(
                $"/internal/host-agent/migrations/intakes/{Uri.EscapeDataString(intakeId)}/artifacts/{Uri.EscapeDataString(artifactId)}/bind-catalog",
                new { validationId, catalogEntryId },
                cancellationToken);
            var validationDetail = await GetAsync<ValidationDetail>(
                $"/internal/host-agent/backups/artifacts/validated-imports/{Uri.EscapeDataString(validationId!)}",
                cancellationToken);

            if (!string.Equals(binding.Status, "bound", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(binding.ExpectedSha256, binding.ActualSha256, StringComparison.OrdinalIgnoreCase) ||
                binding.ExpectedBytes != binding.ActualBytes)
                throw new InvalidOperationException("Target artifact binding did not return an exact receipt match.");

            var completed = DateTimeOffset.UtcNow;
            var warnings = validationDetail.Warnings.ToList();
            if (normalized.AllowInsecureTls)
                warnings.Add("TLS certificate validation was explicitly disabled for this private target import.");
            var report = new TargetImportReport(
                "mem-target-import-report", 2, normalized.AttemptId!, "Completed", started, completed,
                targetCredential.ProfileName, targetCredential.TargetBaseUrl,
                normalized.ManifestPath, manifestSha, normalized.StackExportPath, zipSha,
                zipBytes, artifactId, intakeId, validationId!, catalogEntryId!, binding.Status,
                binding.ExpectedSha256, binding.ActualSha256, binding.ExpectedBytes, binding.ActualBytes,
                warnings.ToArray(),
                new[] { "Create a private restore session from the bound Backup Catalog entry.", "Do not activate public routes during rehearsal." });
            var reportJson = JsonSerializer.Serialize(report, CaptureJson.Options);
            await _journal.CompleteAsync(report, reportJson, cancellationToken);
            return report;
        }
        catch (Exception ex)
        {
            await _journal.FailAsync(normalized.AttemptId!, DateTimeOffset.UtcNow, "target_import_failed", ex.Message, cancellationToken);
            throw;
        }
    }

    private void ConfigureClient(
        TargetImportOptions options,
        TargetProfileCredential credential)
    {
        _httpClient.BaseAddress = new Uri(
            credential.TargetBaseUrl + "/",
            UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(
            options.HttpTimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                credential.DeviceCredential);
    }

    private async Task<UploadResponse> UploadAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(file);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", Path.GetFileName(path));
        using var response = await _httpClient.PostAsync("/internal/host-agent/backups/artifacts/validated-imports", content, cancellationToken);
        return await ReadResponseAsync<UploadResponse>(response, cancellationToken);
    }

    private async Task<T> PostJsonAsync<T>(string path, object payload, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, payload, JsonOptions, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Target MEM request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}). {AssessmentRedactor.RedactText(body)}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidDataException("Target MEM returned an empty or invalid JSON response.");
    }

    private static JsonElement FindMatchingArtifact(JsonElement root, string zipSha, long zipBytes)
    {
        if (!root.TryGetProperty("contractVersion", out var version) || version.GetString() != "mem-migration-import/v1")
            throw new InvalidDataException("Neutral manifest contractVersion must be mem-migration-import/v1.");
        if (!root.TryGetProperty("artifacts", out var artifacts) || artifacts.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Neutral manifest artifacts array is required.");
        var matches = artifacts.EnumerateArray().Where(item =>
            string.Equals(item.GetProperty("kind").GetString(), "mem-stack-export", StringComparison.Ordinal) &&
            string.Equals(item.GetProperty("targetKind").GetString(), "backup-catalog-import", StringComparison.Ordinal) &&
            string.Equals(item.GetProperty("sha256").GetString(), zipSha, StringComparison.OrdinalIgnoreCase) &&
            item.GetProperty("sizeBytes").GetInt64() == zipBytes).ToArray();
        return matches.Length == 1
            ? matches[0].Clone()
            : throw new InvalidDataException("Exactly one neutral mem-stack-export artifact must match the supplied ZIP SHA-256 and byte length.");
    }

    private static void EnsureInputFiles(TargetImportOptions options)
    {
        if (!File.Exists(options.ManifestPath)) throw new FileNotFoundException("Migration intake manifest was not found.", options.ManifestPath);
        if (!File.Exists(options.StackExportPath)) throw new FileNotFoundException("MEM stack export ZIP was not found.", options.StackExportPath);
    }

    private sealed record IntakeSummary(string IntakeId, string Status, int ErrorCount);
    private sealed record IntakeDetail(IntakeSummary Intake);
    private sealed record UploadResponse(string Status, string ValidationId, long ZipBytes, string? CatalogEntryId, string? CatalogPayloadState);
    private sealed record ValidationDetail(string Status, string ValidationId, IReadOnlyList<string> Warnings);
    private sealed record BindingResponse(string Status, string IntakeId, string ArtifactId, string ValidationId, string CatalogEntryId, string ExpectedSha256, string ActualSha256, long ExpectedBytes, long ActualBytes);
}
