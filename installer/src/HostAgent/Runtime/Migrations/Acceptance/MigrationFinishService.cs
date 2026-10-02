namespace HostAgent.Runtime.Migrations.Acceptance;

/// <summary>
/// Coordinates the normal operator journey for accepting the verified Runtime Stack,
/// retaining the old server, and starting the first native MEM baseline backup.
/// Existing acceptance and baseline-backup services remain authoritative.
/// </summary>
public sealed class MigrationFinishService(
    MigrationAcceptanceService acceptanceService)
{
    private static readonly TimeSpan FinishTimeout = TimeSpan.FromHours(2);

    public async Task<MigrationAcceptanceStateResponse> FinishAsync(
        string migrationId,
        string acceptedBy,
        FinishMigrationRequest request,
        CancellationToken requestCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateConfirmations(request);

        var current = await acceptanceService.GetStateAsync(
            migrationId,
            requestCancellationToken);
        if (current.Accepted)
        {
            return current;
        }

        using var operationTimeout = new CancellationTokenSource(FinishTimeout);
        return await acceptanceService.AcceptAsync(
            migrationId,
            acceptedBy,
            BuildAcceptanceRequest(request),
            operationTimeout.Token);
    }

    internal static IReadOnlyList<string> GetMissingConfirmations(
        FinishMigrationRequest request)
    {
        var missing = new List<string>();
        if (!request.ConfirmVerifiedServerIsAuthoritative)
        {
            missing.Add("confirmVerifiedServerIsAuthoritative");
        }
        if (!request.ConfirmRecoveryBoundaryChanges)
        {
            missing.Add("confirmRecoveryBoundaryChanges");
        }
        if (!request.ConfirmRetainOldServerAndNoAutomaticDeletion)
        {
            missing.Add("confirmRetainOldServerAndNoAutomaticDeletion");
        }

        return missing;
    }

    internal static AcceptMigrationRequest BuildAcceptanceRequest(
        FinishMigrationRequest request) =>
        new(
            Note: null,
            RetentionDays: request.RetentionDays,
            AcknowledgeFreshPublicVerification: true,
            AcknowledgeTargetWriteDivergence: true,
            AcknowledgeRollbackBoundaryChanges: true,
            AcknowledgeLegacySourceResourcesRetained: true,
            AcknowledgeNoAutomaticLegacyDeletion: true);

    private static void ValidateConfirmations(FinishMigrationRequest request)
    {
        var missing = GetMissingConfirmations(request);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing required confirmations: {string.Join(", ", missing)}");
        }

        MigrationAcceptancePolicy.ValidateRequest(BuildAcceptanceRequest(request));
    }
}
