namespace HostAgent.Runtime.Services.TemporaryStaging;

/// <summary>
/// Read-only, browser-safe inventory. A workspace association is navigation evidence,
/// not authorization to retire resources or proof that they are safe to remove.
/// </summary>
public sealed record TemporaryStagingInventoryResponse(
    string Source,
    DateTimeOffset ObservedAtUtc,
    string Status,
    IReadOnlyList<string> WarningCodes,
    IReadOnlyList<TemporaryStagingInventoryItem> Items);

public sealed record TemporaryStagingInventoryItem(
    string ResourceGroupId,
    string? StagingId,
    string OwnershipStatus,
    TemporaryStagingOwner? Owner,
    string RuntimeStatus,
    string RecordedStatus,
    int ContainerCount,
    int RunningContainerCount,
    int NetworkCount,
    IReadOnlyList<string> ContainerIds,
    IReadOnlyList<string> ReasonCodes,
    bool CanRetire = false,
    TemporaryStagingRetirementReviewTarget? RetirementReview = null);

public sealed record TemporaryStagingRetirementReviewTarget(string MigrationId, string StagingRunId, string? Status);

public sealed record TemporaryStagingOwner(
    string Kind,
    string Id,
    string DisplayName,
    string? WorkspaceHref);

// Source-side data only. Never return these records directly from an endpoint.
public sealed record StagingInventorySnapshot(
    bool ContainersAvailable,
    bool NetworksAvailable,
    bool OwnershipAvailable,
    bool HistoryAvailable,
    IReadOnlyList<StagingObservedResource> Resources,
    IReadOnlyList<StagingRecordedRuntime> History,
    IReadOnlyList<StagingRecordedOwner> Owners,
    IReadOnlyList<string> WarningCodes,
    Guid? ControlPlaneInstanceId = null);

public sealed record StagingObservedResource(
    string Kind,
    string Id,
    string Name,
    bool Running,
    IReadOnlyDictionary<string, string> Labels);

public sealed record StagingRecordedResource(string Kind, string Service, string Id);

public sealed record StagingRecordedRuntime(
    string StagingId,
    string? SourceKind,
    string? CatalogEntryId,
    string Status,
    bool Destroyed,
    IReadOnlyList<StagingRecordedResource> Resources);

public sealed record StagingRecordedOwner(
    string StagingId,
    string Kind,
    string OwnerId,
    string DisplayName,
    string SourceIdentity,
    bool BindingValid,
    bool Retired,
    string? StagingRunId = null,
    string? RetirementStatus = null);

public interface ITemporaryStagingInventorySource
{
    Task<StagingInventorySnapshot> ReadAsync(CancellationToken cancellationToken);
}
