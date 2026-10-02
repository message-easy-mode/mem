namespace Modules.Setup.Platform.Coturn;

/// <summary>
/// Narrow Setup boundary for installing and inspecting the one shared MEM
/// platform Coturn runtime. The contract intentionally exposes only safe
/// readiness and ownership evidence; it never returns the shared secret or
/// secret-bearing configuration.
/// </summary>
public interface IPlatformCoturnSetupService
{
    Task<PlatformCoturnSetupResult> EnsureInstalledAsync(
        PlatformCoturnSetupRequest request,
        CancellationToken cancellationToken);

    Task<PlatformCoturnSetupResult> InspectAsync(
        CancellationToken cancellationToken);
}

public sealed record PlatformCoturnSetupRequest(
    string? ExternalIp = null,
    bool ForceRecreate = false);

public sealed record PlatformCoturnSetupResult(
    bool Ready,
    string Status,
    string Readiness,
    string ContainerState,
    string ContainerName,
    string PublicHost,
    string Realm,
    string ApprovedImageReference,
    string? ResolvedImageId,
    bool ContainerExists,
    bool Running,
    bool OwnershipVerified,
    bool ImageApproved,
    bool SecretPresent,
    bool SecretFilePermissionsApplied,
    bool RelayPortsPublished,
    bool SecurityPolicyApplied,
    bool Recreated,
    IReadOnlyList<string> PublishedPorts,
    IReadOnlyList<string> RequiredProductionFirewallPorts,
    IReadOnlyList<string> Warnings,
    string? Detail);

public static class PlatformCoturnSetupDefaults
{
    public const string ContainerName = "mem-coturn";
    public const string ComponentLabel = "platform";
    public const string ServiceKey = "coturn";
    public const int TurnPort = 3478;
    public const int RelayMinPort = 49160;
    public const int RelayMaxPort = 49200;

    public static string PublicHost(string baseDomain) =>
        $"turn.{baseDomain.Trim().TrimEnd('.').ToLowerInvariant()}";

    public static IReadOnlyList<string> RequiredPublishedPorts { get; } =
    [
        $"{TurnPort}/tcp",
        $"{TurnPort}/udp",
        $"{RelayMinPort}-{RelayMaxPort}/udp"
    ];
}
