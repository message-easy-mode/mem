using Infrastructure.Data.Entities.Migrations;

namespace Modules.Operator.Migrations.Workspace;

/// <summary>Shared durable eligibility, not authorization or a substitute for execution-time validation.</summary>
public static class MigrationAcceptanceEligibility
{
    public static IReadOnlyList<string> BuildBlockers(
        MigrationProductionAdoptionEntity? plan,
        DateTime nowUtc,
        string? staleReason)
    {
        var blockers = new List<string>();
        if (plan is null)
        {
            blockers.Add("A completed normal-runtime production adoption plan is required before acceptance.");
            return blockers;
        }

        if (!string.IsNullOrWhiteSpace(staleReason))
        {
            blockers.Add(staleReason);
        }

        if (!string.Equals(plan.Status, "production-verification-passed", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.ProductionVerificationStatus, "passed", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("A passed durable production-verification run is required before acceptance.");
        }

        if (!string.Equals(plan.CutoverStatus, "public-awaiting-verification", StringComparison.OrdinalIgnoreCase) ||
            !plan.PublicRoutesCreated ||
            !plan.RuntimePromotionCompleted ||
            string.IsNullOrWhiteSpace(plan.CutoverExecutionId) ||
            plan.TargetPublicAtUtc is null)
        {
            blockers.Add("A completed controlled public cutover owned by the normal MEM runtime is required.");
        }

        _ = nowUtc;
        if (string.IsNullOrWhiteSpace(plan.ProductionVerificationId) ||
            plan.ProductionVerificationCompletedAtUtc is null)
        {
            blockers.Add("Completed production-verification evidence is required before acceptance.");
        }

        if (plan.ProductionVerificationCheckCount <= 0 ||
            plan.ProductionVerificationFailedCheckCount != 0 ||
            string.IsNullOrWhiteSpace(plan.ProductionVerificationEvidenceJson) ||
            !IsSha256(plan.ProductionVerificationEvidenceSha256) ||
            plan.ProductionVerificationReadinessReportId is null)
        {
            blockers.Add("Durable production-verification evidence is incomplete or contains failed checks.");
        }

        if (!string.IsNullOrWhiteSpace(plan.RollbackExecutionId) ||
            !string.IsNullOrWhiteSpace(plan.RollbackStatus) ||
            !string.IsNullOrWhiteSpace(plan.RollbackCompletionStatus))
        {
            blockers.Add("Migration acceptance is unavailable after the coordinated rollback lifecycle begins.");
        }

        return blockers.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
