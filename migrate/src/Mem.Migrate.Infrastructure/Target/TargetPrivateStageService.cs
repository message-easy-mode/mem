using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Security;
using Mem.Migrate.Core.Target;

namespace Mem.Migrate.Infrastructure.Target;

public sealed class TargetPrivateStageService : ITargetPrivateStageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;
    private readonly ITargetImportJournal _importJournal;
    private readonly ITargetPrivateStageJournal _stageJournal;
    private readonly ITargetProfileCredentialResolver _credentialResolver;

    public TargetPrivateStageService(HttpClient httpClient, ITargetImportJournal importJournal, ITargetPrivateStageJournal stageJournal, ITargetProfileCredentialResolver credentialResolver)
    {
        _httpClient = httpClient; _importJournal = importJournal; _stageJournal = stageJournal; _credentialResolver = credentialResolver;
    }

    public async Task<TargetPrivateStageReport> RunAsync(TargetPrivateStageOptions options, CancellationToken ct)
    {
        var normalized = options.Normalize();
        await _importJournal.InitializeAsync(ct);
        var imported = await _importJournal.GetAsync(normalized.ImportAttemptId, ct)
            ?? throw new InvalidOperationException($"MM-05C import attempt '{normalized.ImportAttemptId}' was not found.");
        if (!string.Equals(imported.Status, "Completed", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(imported.CatalogEntryId))
            throw new InvalidOperationException("MM-05C import attempt is not completed with a Backup Catalog entry.");
        var catalogEntryId = imported.CatalogEntryId;

        var credential = await _credentialResolver.ResolveAsync(normalized.ProfileName, ct);
        if (!string.Equals(imported.TargetProfileName, credential.ProfileName, StringComparison.Ordinal) ||
            !string.Equals(imported.TargetBaseUrl, credential.TargetBaseUrl, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected profile does not match the target identity journaled by MM-05C.");

        await _stageJournal.InitializeAsync(ct);
        var stored = await _stageJournal.GetAsync(normalized.StageAttemptId!, ct);
        if (stored is not null)
        {
            if (!normalized.Resume) throw new InvalidOperationException("Private staging attempt already exists. Use --resume.");
            if (!string.Equals(stored.ImportAttemptId, normalized.ImportAttemptId, StringComparison.Ordinal) ||
                !string.Equals(stored.TargetBaseUrl, credential.TargetBaseUrl, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Resumed private staging inputs do not match the journaled target identity.");
            if (stored.Status == "Completed" && stored.ReportJson is not null)
                return JsonSerializer.Deserialize<TargetPrivateStageReport>(stored.ReportJson, JsonOptions)
                    ?? throw new InvalidDataException("Completed private staging report could not be read.");
        }
        else
        {
            await _stageJournal.StartAsync(normalized.StageAttemptId!, DateTimeOffset.UtcNow, normalized.ImportAttemptId, credential.ProfileName, credential.TargetBaseUrl, catalogEntryId, ct);
            stored = await _stageJournal.GetAsync(normalized.StageAttemptId!, ct);
        }

        var started = stored!.StartedAtUtc;
        try
        {
            ConfigureClient(normalized, credential);
            var restoreId = stored.RestoreSessionId;
            if (string.IsNullOrWhiteSpace(restoreId))
            {
                var restore = await PostEmptyAsync<CatalogRestoreSessionResponse>($"/internal/host-agent/backups/catalog/{Uri.EscapeDataString(catalogEntryId)}/restore-session", ct);
                if (!string.Equals(restore.Status, "ok", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(restore.RestoreSessionId))
                    throw new InvalidOperationException("Target did not create or resume a catalog-backed restore session.");
                restoreId = restore.RestoreSessionId;
                await _stageJournal.SaveProgressAsync(normalized.StageAttemptId!, restoreId, null, ct);
            }

            var privateTest = await PostEmptyAsync<PrivateTestResponse>($"/internal/host-agent/backups/restores/{Uri.EscapeDataString(restoreId)}/private-test", ct);
            if (!string.Equals(privateTest.Status, "ready", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(privateTest.StagingId) || privateTest.PrivateOnly != true ||
                privateTest.DatabaseImportSucceeded != true || privateTest.SynapseHealthPassed != true)
                throw new InvalidOperationException("Private target staging did not return healthy private-only evidence.");
            var stagingId = privateTest.StagingId;
            await _stageJournal.SaveProgressAsync(normalized.StageAttemptId!, restoreId, stagingId, ct);

            var workspaceJson = await GetRawAsync($"/internal/host-agent/backups/restores/{Uri.EscapeDataString(restoreId)}/workspace", ct);
            var outputDirectory = Path.Combine(normalized.OutputPath, normalized.StageAttemptId!);
            PrivateFilePermissions.EnsureDirectory(outputDirectory);
            var evidencePath = Path.Combine(outputDirectory, "target-private-stage-workspace.json");
            await File.WriteAllTextAsync(evidencePath, workspaceJson, ct);
            PrivateFilePermissions.EnsureFile(evidencePath);

            var destroySucceeded = false;
            if (normalized.DestroyAfterVerification)
            {
                await PostEmptyRawAsync($"/internal/host-agent/backups/verification/private-runtime/private-staging/{Uri.EscapeDataString(stagingId)}/destroy", ct);
                destroySucceeded = true;
            }

            var warnings = new List<string>();
            if (!normalized.DestroyAfterVerification)
                warnings.Add("The private staging runtime is retained and requires explicit destruction.");
            if (normalized.AllowInsecureTls)
                warnings.Add("TLS certificate validation was explicitly disabled for this private target rehearsal.");

            var completed = DateTimeOffset.UtcNow;
            var report = new TargetPrivateStageReport(
                "mem-target-private-stage-report", 1, normalized.StageAttemptId!, "Completed", started, completed,
                normalized.ImportAttemptId, credential.ProfileName, credential.TargetBaseUrl, catalogEntryId,
                restoreId, stagingId, true, true, true, true,
                privateTest.RequiresExplicitDestroy ?? true, normalized.DestroyAfterVerification, destroySucceeded,
                evidencePath, warnings.ToArray(), normalized.DestroyAfterVerification
                    ? new[] { "Review the retained target rehearsal evidence and proceed to cutover planning only after operator approval." }
                    : new[] { "Inspect the private staging runtime, then destroy it explicitly before continuing." });
            var json = JsonSerializer.Serialize(report, CaptureJson.Options);
            var reportPath = Path.Combine(outputDirectory, "target-private-stage-report.json");
            await File.WriteAllTextAsync(reportPath, json, ct); PrivateFilePermissions.EnsureFile(reportPath);
            await _stageJournal.CompleteAsync(report, json, ct);
            return report;
        }
        catch (Exception ex)
        {
            await _stageJournal.FailAsync(normalized.StageAttemptId!, DateTimeOffset.UtcNow, "target_private_stage_failed", ex.Message, ct);
            throw;
        }
    }

    private void ConfigureClient(TargetPrivateStageOptions options, TargetProfileCredential credential)
    {
        _httpClient.BaseAddress = new Uri(credential.TargetBaseUrl + "/", UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.DeviceCredential);
    }

    private async Task<T> PostEmptyAsync<T>(string path, CancellationToken ct)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, new { }, JsonOptions, ct);
        return await ReadAsync<T>(response, ct);
    }
    private async Task PostEmptyRawAsync(string path, CancellationToken ct)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, new { }, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Target MEM request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}). {AssessmentRedactor.RedactText(body)}");
        }
    }
    private async Task<string> GetRawAsync(string path, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(path, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Target MEM request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}). {AssessmentRedactor.RedactText(body)}");
        return body;
    }
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Target MEM request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}). {AssessmentRedactor.RedactText(body)}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions) ?? throw new InvalidDataException("Target MEM returned invalid JSON.");
    }

    private sealed record CatalogRestoreSessionResponse(string Status, string CatalogEntryId, string RestoreSessionId, bool RestoreAttemptCreated, bool RestoreAttemptResumed);
    private sealed record PrivateTestResponse(string Status, string RestoreSessionId, string? CatalogEntryId, string? StagingId, bool? PrivateOnly, bool? DatabaseImportSucceeded, bool? SynapseHealthPassed, bool? RequiresExplicitDestroy);
}
