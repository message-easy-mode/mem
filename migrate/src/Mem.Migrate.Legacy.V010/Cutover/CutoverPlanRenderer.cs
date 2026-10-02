using System.Text;
using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Legacy.V010.Cutover;

public static class CutoverPlanRenderer
{
    public static async Task<CutoverPreparationReport> WriteAsync(
        CutoverPlanDocument plan,
        string planHash,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetFullPath(outputRoot), plan.PlanId);
        PrivateFilePermissions.EnsureDirectory(directory);
        var jsonPath = Path.Combine(directory, "cutover-plan.json");
        var markdownPath = Path.Combine(directory, "cutover-plan.md");
        var report = new CutoverPreparationReport(
            "mem-cutover-preparation-report",
            1,
            "Prepared",
            planHash,
            plan,
            jsonPath,
            markdownPath);
        var json = JsonSerializer.Serialize(report, CaptureJson.Options);
        await File.WriteAllTextAsync(jsonPath, json, cancellationToken);
        await File.WriteAllTextAsync(
            markdownPath,
            RenderMarkdown(report),
            cancellationToken);
        PrivateFilePermissions.EnsureFile(jsonPath);
        PrivateFilePermissions.EnsureFile(markdownPath);
        return report;
    }

    public static string RenderMarkdown(CutoverPreparationReport report)
    {
        var plan = report.Plan;
        var text = new StringBuilder();
        text.AppendLine("# MEM MM-06A Reviewed Cutover Plan");
        text.AppendLine();
        text.AppendLine($"- **Plan ID:** `{plan.PlanId}`");
        text.AppendLine($"- **Plan hash:** `{report.PlanHash}`");
        text.AppendLine($"- **Generated:** `{plan.GeneratedAtUtc:O}`");
        text.AppendLine($"- **Valid until:** `{plan.ValidUntilUtc:O}`");
        text.AppendLine($"- **Source fingerprint:** `{plan.SourceFingerprint}`");
        text.AppendLine($"- **Source classification:** `{plan.SourceClassification}`");
        text.AppendLine($"- **Source mutation performed:** `{plan.SourceMutationOccurred}`");
        text.AppendLine($"- **Public routing mutation performed:** `{plan.PublicRoutingMutationOccurred}`");
        text.AppendLine($"- **Development external control plane:** `{plan.DevelopmentExternalControlPlane}`");
        text.AppendLine();
        text.AppendLine("## Source containers to freeze");
        text.AppendLine();
        text.AppendLine("| Role | Container | ID | State | Restart policy | Stack |");
        text.AppendLine("|---|---|---|---|---|---|");
        foreach (var container in plan.SourceContainersToFreeze)
        {
            text.AppendLine(
                $"| {Escape(container.Role)} | {Escape(container.ContainerName)} | `{container.ContainerId}` | {Escape(container.State)} | {Escape(container.RestartPolicy)} | {container.StackId?.ToString("D") ?? "—"} |");
        }

        text.AppendLine();
        text.AppendLine("## Retained source containers");
        text.AppendLine();
        foreach (var container in plan.RetainedSourceContainers)
        {
            text.AppendLine(
                $"- `{container.ContainerName}` ({container.Role}) — {container.RetentionAction}");
        }

        text.AppendLine();
        text.AppendLine("## Stacks");
        text.AppendLine();
        foreach (var stack in plan.Stacks)
        {
            text.AppendLine(
                $"- **{Escape(stack.DisplayName)}** (`{stack.Slug}`): `{stack.MatrixServerName}`");
        }

        text.AppendLine();
        text.AppendLine("## Route hints requiring live compare-and-swap validation");
        text.AppendLine();
        text.AppendLine("| Role | Stack | Route ID | Public host | Forward target | Evidence |");
        text.AppendLine("|---|---|---|---|---|---|");
        foreach (var route in plan.RouteHints)
        {
            var forward = string.IsNullOrWhiteSpace(route.ForwardHost)
                ? "—"
                : $"{route.ForwardHost}:{route.ForwardPort?.ToString() ?? "?"}";
            text.AppendLine(
                $"| {Escape(route.Role)} | {route.StackId?.ToString("D") ?? "—"} | {Escape(route.RouteId ?? "—")} | {Escape(route.PublicHost ?? "—")} | {Escape(forward)} | {Escape(route.EvidenceKind)} |");
        }

        text.AppendLine();
        text.AppendLine("## Target rehearsal evidence");
        text.AppendLine();
        text.AppendLine($"- Target profile: `{plan.TargetEvidence.TargetProfileName}`");
        text.AppendLine($"- Target base URL: `{plan.TargetEvidence.TargetBaseUrl}`");
        text.AppendLine($"- Stage attempt: `{plan.TargetEvidence.StageAttemptId}`");
        text.AppendLine($"- Catalog entry: `{plan.TargetEvidence.CatalogEntryId}`");
        text.AppendLine($"- Restore session: `{plan.TargetEvidence.RestoreSessionId}`");
        text.AppendLine($"- Private-only verification: `{plan.TargetEvidence.PrivateOnly}`");
        text.AppendLine($"- Database import: `{plan.TargetEvidence.DatabaseImportSucceeded}`");
        text.AppendLine($"- Synapse health: `{plan.TargetEvidence.SynapseHealthPassed}`");
        text.AppendLine($"- Public routes absent: `{plan.TargetEvidence.PublishedRoutesAbsent}`");
        text.AppendLine($"- Staging destroyed: `{plan.TargetEvidence.StagingDestroyed}`");

        AppendList(text, "Preconditions", plan.Preconditions);
        AppendSteps(text, "Execution sequence", plan.ExecutionSteps);
        AppendSteps(text, "Rollback sequence", plan.RollbackSteps);
        AppendList(text, "Warnings", plan.Warnings);
        AppendList(text, "Next steps", plan.NextSteps);
        return text.ToString();
    }

    private static void AppendList(
        StringBuilder text,
        string heading,
        IEnumerable<string> values)
    {
        text.AppendLine();
        text.AppendLine($"## {heading}");
        text.AppendLine();
        foreach (var value in values)
        {
            text.AppendLine($"- {value}");
        }
    }

    private static void AppendSteps(
        StringBuilder text,
        string heading,
        IEnumerable<CutoverPlanStep> steps)
    {
        text.AppendLine();
        text.AppendLine($"## {heading}");
        text.AppendLine();
        foreach (var step in steps.OrderBy(item => item.Order))
        {
            text.AppendLine($"{step.Order}. **{step.Code}** — {step.Description}");
        }
    }

    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}
