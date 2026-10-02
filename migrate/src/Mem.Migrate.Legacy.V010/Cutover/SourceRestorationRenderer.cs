using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Legacy.V010.Cutover;

public static class SourceRestorationRenderer
{
    public const string CompletionSchemaVersion =
        "mem.migration.source-restoration-completion.v1";

    private static readonly JsonSerializerOptions CompletionJsonOptions =
        new(JsonSerializerDefaults.Web);

    public static async Task<SourceRestorationReport> WriteAsync(
        SourceRestorationReport report,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(outputRoot, report.RestorationAttemptId);
        PrivateFilePermissions.EnsureDirectory(directory);
        var completionPath = Path.Combine(
            directory,
            "source-restoration-completion.json");
        var jsonPath = Path.Combine(directory, "source-restoration-report.json");
        var markdownPath = Path.Combine(directory, "source-restoration-report.md");

        var completionPayload = new SourceRestorationCompletionPayload(
            report.RestorationAttemptId,
            report.CompletedAtUtc.UtcDateTime,
            report.SourceHandoffId,
            report.SourceHandoffSha256,
            report.MigrationId,
            report.SourceMigrationId,
            report.TargetRollbackExecutionId,
            report.FreezeAttemptId,
            report.FreezePlanId,
            report.FreezePlanHash,
            report.SourceFingerprint,
            report.SourceStackSlug,
            report.MatrixServerName,
            report.SourceRestored,
            report.RestartPoliciesRestored,
            report.OriginalRunningStatesRestored,
            report.MatrixVerified,
            report.ElementVerified,
            report.TargetRollbackAuthorityVerified,
            report.DevelopmentExternalControlPlane,
            report.Containers,
            report.TargetRouteEvidence);
        var completionSha256 = ComputePayloadSha256(completionPayload);
        var completion = new SourceRestorationCompletionEnvelope(
            CompletionSchemaVersion,
            completionSha256,
            completionPayload);
        await File.WriteAllTextAsync(
            completionPath,
            JsonSerializer.Serialize(completion, CaptureJson.Options),
            cancellationToken);
        PrivateFilePermissions.EnsureFile(completionPath);

        var finalized = report with
        {
            CompletionEvidencePath = completionPath,
            CompletionEvidenceSha256 = completionSha256,
            JsonPath = jsonPath,
            MarkdownPath = markdownPath
        };
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(finalized, CaptureJson.Options),
            cancellationToken);
        PrivateFilePermissions.EnsureFile(jsonPath);
        await File.WriteAllTextAsync(
            markdownPath,
            RenderMarkdown(finalized),
            cancellationToken);
        PrivateFilePermissions.EnsureFile(markdownPath);
        return finalized;
    }

    public static string ComputePayloadSha256(
        SourceRestorationCompletionPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, CompletionJsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    private static string RenderMarkdown(SourceRestorationReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# MEM MIG-PRODUCTION-01C-B Source Restoration Report");
        text.AppendLine();
        text.AppendLine($"- Status: `{report.Status}`");
        text.AppendLine($"- Restoration attempt: `{report.RestorationAttemptId}`");
        text.AppendLine($"- Source handoff: `{report.SourceHandoffId}`");
        text.AppendLine($"- Handoff payload SHA-256: `{report.SourceHandoffSha256}`");
        text.AppendLine($"- Completion payload SHA-256: `{report.CompletionEvidenceSha256}`");
        text.AppendLine($"- Migration: `{report.MigrationId}`");
        text.AppendLine($"- Source migration: `{report.SourceMigrationId}`");
        text.AppendLine($"- Target rollback execution: `{report.TargetRollbackExecutionId}`");
        text.AppendLine($"- Freeze attempt: `{report.FreezeAttemptId}`");
        text.AppendLine($"- Freeze plan: `{report.FreezePlanId}`");
        text.AppendLine($"- Source fingerprint: `{report.SourceFingerprint}`");
        text.AppendLine($"- Source restored: `{report.SourceRestored}`");
        text.AppendLine($"- Matrix verified: `{report.MatrixVerified}`");
        text.AppendLine($"- Element verified: `{report.ElementVerified}`");
        text.AppendLine($"- Development external control plane: `{report.DevelopmentExternalControlPlane}`");
        text.AppendLine();
        text.AppendLine("## Restored source containers");
        text.AppendLine();
        foreach (var container in report.Containers)
        {
            text.AppendLine(
                $"- `{container.Role}` / `{container.ContainerName}`: state `{container.FinalState}`, " +
                $"restart `{container.FinalRestartPolicy}`, was running `{container.WasRunning}`, " +
                $"verified `{container.ServiceVerified}`");
        }

        text.AppendLine();
        text.AppendLine("## Target rollback authority");
        text.AppendLine();
        foreach (var route in report.TargetRouteEvidence)
        {
            text.AppendLine(
                $"- `{route.ServiceKey}` / `{route.PublicHost}`: `{route.RestoredState}`, " +
                $"forward `{route.ForwardScheme}://{route.ForwardHost}:{route.ForwardPort}`, enabled `{route.Enabled}`");
        }

        if (report.Warnings.Length > 0)
        {
            text.AppendLine();
            text.AppendLine("## Warnings");
            text.AppendLine();
            foreach (var warning in report.Warnings)
            {
                text.AppendLine($"- {warning}");
            }
        }

        text.AppendLine();
        text.AppendLine("## Next steps");
        text.AppendLine();
        foreach (var step in report.NextSteps)
        {
            text.AppendLine($"- {step}");
        }

        return text.ToString();
    }
}
