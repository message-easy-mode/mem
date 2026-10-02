using System.Net.Http.Headers;
using System.Text.Json;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Security;
using Mem.Migrate.Core.Target;

namespace Mem.Migrate.Infrastructure.Target;

public sealed class ActivationReadinessService(HttpClient httpClient, ITargetProfileCredentialResolver credentialResolver)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public async Task<ActivationReadinessReport> RunAsync(ActivationReadinessOptions options, CancellationToken ct)
    {
        var normalized = options.Normalize();
        var finalStage = JsonSerializer.Deserialize<FinalTargetStageReport>(await File.ReadAllTextAsync(normalized.FinalTargetStageReportPath, ct), JsonOptions)
            ?? throw new InvalidDataException("MM-06D final target stage report could not be parsed.");
        if (!string.Equals(finalStage.Status, "Completed", StringComparison.Ordinal) || !finalStage.FinalArchive || !finalStage.SourceFrozen ||
            !finalStage.PrivateOnly || !finalStage.PublishedRoutesAbsent || !finalStage.CandidateRetained)
            throw new InvalidDataException("MM-06D evidence does not prove a completed, retained, private, route-free final candidate.");

        var freeze = JsonSerializer.Deserialize<CutoverFreezeReport>(await File.ReadAllTextAsync(normalized.FreezeReportPath, ct), JsonOptions)
            ?? throw new InvalidDataException("MM-06B freeze report could not be parsed.");
        if (!string.Equals(freeze.Status, "Frozen", StringComparison.Ordinal) || !freeze.SourceFrozen || freeze.PublicRoutingMutationOccurred)
            throw new InvalidDataException("MM-06B evidence does not prove the old source remains frozen before activation readiness.");
        if (!string.Equals(freeze.SourceFingerprint, finalStage.SourceFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("MM-06B and MM-06D source fingerprints do not match.");

        var credential = await credentialResolver.ResolveAsync(normalized.ProfileName, ct);
        httpClient.BaseAddress = new Uri(credential.TargetBaseUrl + "/", UriKind.Absolute);
        httpClient.Timeout = TimeSpan.FromSeconds(normalized.HttpTimeoutSeconds);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.DeviceCredential);

        var path = $"/internal/host-agent/backups/catalog/{Uri.EscapeDataString(finalStage.CatalogEntryId)}/production-restore/pre-cutover-readiness" +
                   $"?candidateId={Uri.EscapeDataString(normalized.CandidateId)}&previewId={Uri.EscapeDataString(normalized.PreviewId)}&confirmationId={Uri.EscapeDataString(normalized.ConfirmationId)}";
        using var response = await httpClient.GetAsync(path, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Target MEM readiness request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}). {AssessmentRedactor.RedactText(body)}");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var targetStatus = ReadString(root, "status") ?? "unknown";
        var executionReady = ReadBool(root, "executionReady");
        var blockers = ReadStrings(root, "blockers");
        var warnings = ReadStrings(root, "warnings");

        var outputDirectory = Path.Combine(normalized.OutputPath, normalized.AttemptId!);
        PrivateFilePermissions.EnsureDirectory(outputDirectory);
        var targetResponsePath = Path.Combine(outputDirectory, "target-pre-cutover-readiness.json");
        await File.WriteAllTextAsync(targetResponsePath, JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }), ct);
        PrivateFilePermissions.EnsureFile(targetResponsePath);

        var reportPath = Path.Combine(outputDirectory, "activation-readiness-report.json");
        var nextSteps = executionReady
            ? new[] { "Review the target readiness evidence in a browser-authenticated Platform Owner session.", "Use the existing catalog public-cutover executor only after recent step-up and explicit acknowledgements.", "Do not accept the migration until public verification and a separate rollback rehearsal have passed." }
            : new[] { "Resolve every target readiness blocker before public activation.", "Do not mutate public routing while executionReady is false.", "Keep the old source frozen and the final target candidate retained." };
        var report = new ActivationReadinessReport(
            "mem-activation-readiness-report", 1, normalized.AttemptId!, executionReady ? "Ready" : "Blocked",
            DateTimeOffset.UtcNow, credential.ProfileName, credential.TargetBaseUrl,
            finalStage.CatalogEntryId, finalStage.RestoreSessionId, finalStage.StagingId,
            normalized.CandidateId, normalized.PreviewId, normalized.ConfirmationId,
            true, true, executionReady, targetStatus, blockers, warnings,
            targetResponsePath, reportPath, nextSteps);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), ct);
        PrivateFilePermissions.EnsureFile(reportPath);
        return report;
    }

    private static string? ReadString(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool ReadBool(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
    private static string[] ReadStrings(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray() : [];
}
