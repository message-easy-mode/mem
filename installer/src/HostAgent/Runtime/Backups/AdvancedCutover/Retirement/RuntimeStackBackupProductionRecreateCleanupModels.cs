namespace HostAgent.Runtime.Backups.AdvancedCutover.Retirement;

public sealed record RuntimeStackBackupProductionRecreateCleanupAssessment(
    string Source,
    string Status,
    string RecreateId,
    string CatalogEntryId,
    Guid RuntimeStackId,
    string TargetStackSlug,
    string? CandidateId,
    string? CandidateStatus,
    string? CandidateNetworkName,
    string? CutoverIngressNetworkName,
    bool CandidateResolved,
    bool CandidateAlreadyRetired,
    bool RequiresOperatorConfirmation,
    bool AutoCleanupAvailable,
    string RecommendedAction,
    IReadOnlyList<RuntimeStackBackupProductionRecreateCleanupCheck> Checks,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string Detail);

public sealed record RuntimeStackBackupProductionRecreateCleanupRequest(
    string? CandidateId,
    string? RetirementMode,
    bool AcknowledgeRetireCandidate);

public sealed record RuntimeStackBackupProductionRecreateCleanupResult(
    string Source,
    string Status,
    string CleanupId,
    string RecreateId,
    string CatalogEntryId,
    Guid RuntimeStackId,
    string TargetStackSlug,
    string? CandidateId,
    string RetirementMode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    bool CandidateDestroyRequested,
    bool CandidateDestroyed,
    bool CandidateElementContainerRemoved,
    bool CandidatePrivateRuntimeDestroyed,
    string? CutoverIngressNetworkName,
    bool CutoverIngressNetworkRemoved,
    RuntimeStackBackupProductionRecreateCleanupAssessment Assessment,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string Detail);

public sealed record RuntimeStackBackupProductionRecreateCleanupCheck(
    string Code,
    string Severity,
    bool Passed,
    string Message,
    string? Detail);

public sealed record RuntimeStackBackupProductionRecreateInventoryResponse(
    string Source,
    string Status,
    int TotalRestoredStacks,
    IReadOnlyList<RuntimeStackBackupProductionRecreateInventoryItem> Stacks,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record RuntimeStackBackupProductionRecreateInventoryItem(
    string RecreateId,
    string CatalogEntryId,
    Guid RuntimeStackId,
    string TargetStackSlug,
    string RecreateStatus,
    bool StackRegistered,
    bool ManifestFound,
    string? ManifestStatus,
    string? MatrixContainerName,
    string? ElementContainerName,
    string? MatrixPublicHost,
    string? ElementPublicHost,
    bool PublicReadinessPassed,
    string? CandidateId,
    string? CandidateStatus,
    bool CandidateRetired,
    bool CandidateCleanupPending,
    string RecommendedAction,
    DateTimeOffset RecreatedAtUtc,
    string Detail);
