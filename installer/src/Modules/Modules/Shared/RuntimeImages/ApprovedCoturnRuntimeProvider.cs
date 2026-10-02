using Microsoft.Extensions.Options;

namespace Modules.Shared.RuntimeImages;

public sealed record ApprovedCoturnRuntimePolicy(
    string ApprovedReference,
    string ExpectedVersion,
    bool AllowInstallPull,
    bool AllowOperationalPull);

public sealed record ApprovedCoturnRuntimeDescriptor(
    string ApprovedReference,
    string ResolvedImageId,
    IReadOnlyList<string> RepositoryDigests,
    string ExpectedVersion,
    DateTime VerifiedAtUtc);

public interface IApprovedCoturnRuntimeProvider
{
    ApprovedCoturnRuntimePolicy GetPolicy();

    Task<ApprovedCoturnRuntimeDescriptor> ResolveForInstallationAsync(
        CancellationToken cancellationToken);

    Task<ApprovedCoturnRuntimeDescriptor> ResolveForOperationAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves the MEM-approved Coturn image to a local immutable image ID.
/// Normal runtime inspection, ensure, repair, and stack operations never pull.
/// Installation resolution may pull only when explicitly invoked by an installation boundary.
/// </summary>
public sealed class ApprovedCoturnRuntimeProvider(
    IOptions<CoturnRuntimeImageOptions> options,
    IRuntimeImageInspector imageInspector,
    TimeProvider timeProvider) : IApprovedCoturnRuntimeProvider
{
    public ApprovedCoturnRuntimePolicy GetPolicy()
    {
        var value = options.Value;
        var approvedReference = ValidateRepositoryDigest(value.ApprovedReference);
        var expectedVersion = value.ExpectedVersion?.Trim();

        if (string.IsNullOrWhiteSpace(expectedVersion))
        {
            throw new InvalidOperationException(
                "RuntimeImages:Coturn:ExpectedVersion is required.");
        }

        if (value.AllowOperationalPull)
        {
            throw new InvalidOperationException(
                "RuntimeImages:Coturn:AllowOperationalPull must remain false. " +
                "Operational TURN work may not pull images.");
        }

        return new ApprovedCoturnRuntimePolicy(
            approvedReference,
            expectedVersion,
            value.AllowInstallPull,
            value.AllowOperationalPull);
    }

    public Task<ApprovedCoturnRuntimeDescriptor> ResolveForInstallationAsync(
        CancellationToken cancellationToken) =>
        ResolveAsync(GetPolicy().AllowInstallPull, cancellationToken);

    public Task<ApprovedCoturnRuntimeDescriptor> ResolveForOperationAsync(
        CancellationToken cancellationToken) =>
        ResolveAsync(allowPull: false, cancellationToken);

    private async Task<ApprovedCoturnRuntimeDescriptor> ResolveAsync(
        bool allowPull,
        CancellationToken cancellationToken)
    {
        var policy = GetPolicy();
        var inspection = await imageInspector.InspectAsync(
            policy.ApprovedReference,
            cancellationToken);

        if (inspection is null && allowPull)
        {
            await imageInspector.PullAsync(policy.ApprovedReference, cancellationToken);
            inspection = await imageInspector.InspectAsync(
                policy.ApprovedReference,
                cancellationToken);
        }

        if (inspection is null)
        {
            throw new InvalidOperationException(
                $"The MEM-approved Coturn runtime '{policy.ApprovedReference}' is not available locally. " +
                "MEM will not pull a replacement during this operation.");
        }

        return new ApprovedCoturnRuntimeDescriptor(
            policy.ApprovedReference,
            NormalizeImageId(inspection.ImageId),
            inspection.RepositoryDigests,
            policy.ExpectedVersion,
            timeProvider.GetUtcNow().UtcDateTime);
    }

    private static string ValidateRepositoryDigest(string? value)
    {
        var reference = value?.Trim() ?? string.Empty;
        const string marker = "@sha256:";
        var markerIndex = reference.LastIndexOf(marker, StringComparison.Ordinal);

        if (markerIndex <= 0)
        {
            throw new InvalidOperationException(
                "RuntimeImages:Coturn:ApprovedReference must be an immutable repository digest.");
        }

        var digest = reference[(markerIndex + marker.Length)..];
        if (digest.Length != 64 || !digest.All(char.IsAsciiHexDigit))
        {
            throw new InvalidOperationException(
                "RuntimeImages:Coturn:ApprovedReference contains an invalid SHA-256 digest.");
        }

        return reference.ToLowerInvariant();
    }

    private static string NormalizeImageId(string? value)
    {
        var imageId = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!imageId.StartsWith("sha256:", StringComparison.Ordinal) ||
            imageId.Length != "sha256:".Length + 64 ||
            !imageId["sha256:".Length..].All(char.IsAsciiHexDigit))
        {
            throw new InvalidOperationException(
                "Docker did not return a valid immutable local image ID for the approved Coturn runtime.");
        }

        return imageId;
    }
}
