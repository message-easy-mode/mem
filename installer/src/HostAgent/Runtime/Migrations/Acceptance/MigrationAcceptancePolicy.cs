using Infrastructure.Data.Entities.Migrations;

namespace HostAgent.Runtime.Migrations.Acceptance;

internal static class MigrationAcceptancePolicy
{
    internal static IReadOnlyList<string> BuildEligibilityBlockers(
        MigrationProductionAdoptionEntity? plan,
        DateTime nowUtc,
        string? staleReason) =>
        Modules.Operator.Migrations.Workspace.MigrationAcceptanceEligibility.BuildBlockers(plan, nowUtc, staleReason);

    internal static void ValidateRequest(AcceptMigrationRequest request)
    {
        if (request.RetentionDays is < 7 or > 30)
        {
            throw new InvalidOperationException("Legacy retention must be between 7 and 30 days.");
        }

        if (!request.AcknowledgeFreshPublicVerification ||
            !request.AcknowledgeTargetWriteDivergence ||
            !request.AcknowledgeRollbackBoundaryChanges ||
            !request.AcknowledgeLegacySourceResourcesRetained ||
            !request.AcknowledgeNoAutomaticLegacyDeletion)
        {
            throw new InvalidOperationException("Every acceptance acknowledgement is required.");
        }
    }

}
