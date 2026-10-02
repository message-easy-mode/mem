namespace HostAgent.Runtime.Backups.StandardRecreate;

/// <summary>
/// Explicit HTTP body for the catalog-native Standard Recreate preflight.
/// The catalog entry remains the restore source; these values describe the
/// intended replacement target and are assessed read-only.
/// </summary>
public sealed record CatalogStandardRecreatePreflightInput(
    string? TargetStackSlug,
    string? RequestedDomainId,
    string? MatrixHost,
    string? ElementHost)
{
    public StandardRecreatePreflightRequest ToPreflightRequest() => new(
        TargetStackSlug,
        RequestedDomainId,
        MatrixHost,
        ElementHost);
}
