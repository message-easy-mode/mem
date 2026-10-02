using System.Text;
using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Legacy.V010.Cutover;

public static class CutoverFreezeRenderer
{
    public static async Task<CutoverFreezeReport> WriteAsync(
        CutoverFreezeReport report,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(outputRoot, report.FreezeAttemptId);
        PrivateFilePermissions.EnsureDirectory(directory);
        var jsonPath = Path.Combine(directory, "cutover-freeze-report.json");
        var markdownPath = Path.Combine(directory, "cutover-freeze-report.md");
        var finalized = report with
        {
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

    private static string RenderMarkdown(CutoverFreezeReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# MEM MM-06B Source Freeze Report");
        text.AppendLine();
        text.AppendLine($"- Status: `{report.Status}`");
        text.AppendLine($"- Freeze attempt: `{report.FreezeAttemptId}`");
        text.AppendLine($"- Plan: `{report.PlanId}`");
        text.AppendLine($"- Plan hash: `{report.PlanHash}`");
        text.AppendLine($"- Source fingerprint: `{report.SourceFingerprint}`");
        text.AppendLine($"- Source frozen: `{report.SourceFrozen}`");
        text.AppendLine($"- Public routing changed: `{report.PublicRoutingMutationOccurred}`");
        text.AppendLine();
        text.AppendLine("## Frozen containers");
        text.AppendLine();
        foreach (var container in report.Containers)
        {
            text.AppendLine(
                $"- `{container.Role}` / `{container.ContainerName}`: state `{container.FinalState}`, restart `{container.FinalRestartPolicy}`, was running `{container.WasRunning}`");
        }
        text.AppendLine();
        text.AppendLine("## Rollback checkpoint");
        text.AppendLine();
        text.AppendLine(report.RollbackCheckpoint.Warning);
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
