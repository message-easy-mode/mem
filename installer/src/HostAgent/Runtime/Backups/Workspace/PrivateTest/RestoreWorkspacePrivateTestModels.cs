namespace HostAgent.Runtime.Backups.Workspace.PrivateTest;

/// <summary>
/// Browser-safe result of starting a private test from a canonical restore
/// workspace. Detailed staging diagnostics remain available through the
/// controlled history/evidence APIs rather than this action response.
/// </summary>
public sealed record RestoreWorkspacePrivateTestActionResponse(
    string Source,
    string Status,
    string RestoreSessionId,
    Guid OperationId,
    string SourceKind,
    string? CatalogEntryId,
    string? StagingId,
    bool? PrivateOnly,
    bool? DatabaseImportSucceeded,
    bool? SynapseHealthPassed,
    bool? RequiresExplicitDestroy,
    string Detail);
