namespace Modules.Integrations.Seq.Contracts;

public sealed record SeqPlanRequest(
    int? PreferredHostPort = null);

public sealed record SeqDeployRequest(
    int? PreferredHostPort = null,
    bool ForcePreferredPort = false);

public sealed record SeqPlanResponse(
    string ServiceName,
    string ContainerName,
    string ApprovedImageReference,
    string ExpectedVersion,
    int ContainerPort,
    int PreferredHostPort,
    int SelectedHostPort,
    bool IsPreferredPortAvailable,
    string HostDataPath,
    bool PublishesPublicIngress,
    IReadOnlyList<string> Warnings);

public sealed record SeqStatusResponse(
    string ServiceName,
    string ContainerName,
    string ExpectedVersion,
    string? HostDataPath,
    int? UiHostPort,
    bool Exists,
    bool Running,
    string? State,
    string? Image,
    bool UsesApprovedRuntime,
    IReadOnlyList<string> Warnings,
    bool Managed = false,
    string OwnershipState = "absent",
    string? WarningCode = null);
