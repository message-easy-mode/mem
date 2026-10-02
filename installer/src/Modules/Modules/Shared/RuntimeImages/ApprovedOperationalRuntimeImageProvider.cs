using Microsoft.Extensions.Configuration;

namespace Modules.Shared.RuntimeImages;

public sealed record ApprovedOperationalRuntimeImageDescriptor(
    string Component,
    string ApprovedReference,
    string ResolvedImageId,
    IReadOnlyList<string> RepositoryDigests,
    DateTime VerifiedAtUtc);

public sealed record ApprovedOperationalRuntimeImagePolicyEntry(
    string Component,
    string Repository,
    string Version,
    string ApprovedReference);

public sealed record ApprovedOperationalRuntimeImagePolicy(
    int SchemaVersion,
    ApprovedOperationalRuntimeImagePolicyEntry Synapse,
    ApprovedOperationalRuntimeImagePolicyEntry Element);

public interface IApprovedOperationalRuntimeImageProvider
{
    ApprovedOperationalRuntimeImagePolicy GetPolicy();

    Task<ApprovedOperationalRuntimeImageDescriptor> PrepareSynapseAsync(
        CancellationToken cancellationToken);

    Task<ApprovedOperationalRuntimeImageDescriptor> PrepareElementAsync(
        CancellationToken cancellationToken);

    Task<ApprovedOperationalRuntimeImageDescriptor> ResolveSynapseForOperationAsync(
        CancellationToken cancellationToken);

    Task<ApprovedOperationalRuntimeImageDescriptor> ResolveElementForOperationAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves MEM-approved operational images from immutable repository digests.
/// Explicit preparation may pull the exact approved digest; later sensitive
/// operations are always local-only and never pull.
/// </summary>
public sealed class ApprovedOperationalRuntimeImageProvider(
    IConfiguration configuration,
    IRuntimeImageInspector imageInspector,
    TimeProvider timeProvider) : IApprovedOperationalRuntimeImageProvider
{
    public ApprovedOperationalRuntimeImagePolicy GetPolicy() => new(
        SchemaVersion: 1,
        Synapse: ReadPolicyEntry("Synapse"),
        Element: ReadPolicyEntry("Element"));

    public Task<ApprovedOperationalRuntimeImageDescriptor> PrepareSynapseAsync(
        CancellationToken cancellationToken) =>
        ResolveAsync("Synapse", allowPreparationPull: true, cancellationToken);

    public Task<ApprovedOperationalRuntimeImageDescriptor> PrepareElementAsync(
        CancellationToken cancellationToken) =>
        ResolveAsync("Element", allowPreparationPull: true, cancellationToken);

    public Task<ApprovedOperationalRuntimeImageDescriptor> ResolveSynapseForOperationAsync(
        CancellationToken cancellationToken) =>
        ResolveAsync("Synapse", allowPreparationPull: false, cancellationToken);

    public Task<ApprovedOperationalRuntimeImageDescriptor> ResolveElementForOperationAsync(
        CancellationToken cancellationToken) =>
        ResolveAsync("Element", allowPreparationPull: false, cancellationToken);

    private async Task<ApprovedOperationalRuntimeImageDescriptor> ResolveAsync(
        string component,
        bool allowPreparationPull,
        CancellationToken cancellationToken)
    {
        var section = $"RuntimeImages:{component}";
        var approvedReference = ValidateRepositoryDigest(
            configuration[$"{section}:ApprovedReference"],
            section);

        var preparationPullAllowed = configuration.GetValue<bool>($"{section}:AllowPreparationPull");
        if (configuration.GetValue<bool>($"{section}:AllowOperationalPull"))
        {
            throw new InvalidOperationException(
                $"{section}:AllowOperationalPull must remain false. Sensitive operations may not pull images.");
        }

        var inspection = await imageInspector.InspectAsync(
            approvedReference,
            cancellationToken);

        if (inspection is null && allowPreparationPull && preparationPullAllowed)
        {
            await imageInspector.PullAsync(approvedReference, cancellationToken);
            inspection = await imageInspector.InspectAsync(
                approvedReference,
                cancellationToken);
        }

        if (inspection is null)
        {
            var preparationHint = allowPreparationPull && !preparationPullAllowed
                ? " Preparation pulls are disabled by policy."
                : string.Empty;
            throw new InvalidOperationException(
                $"The MEM-approved {component} runtime '{approvedReference}' is not available locally." +
                preparationHint +
                " MEM will not pull a replacement during a sensitive operation.");
        }

        var resolvedImageId = NormalizeImageId(inspection.ImageId, component);

        return new ApprovedOperationalRuntimeImageDescriptor(
            component,
            approvedReference,
            resolvedImageId,
            inspection.RepositoryDigests,
            timeProvider.GetUtcNow().UtcDateTime);
    }

    private ApprovedOperationalRuntimeImagePolicyEntry ReadPolicyEntry(string component)
    {
        var section = $"RuntimeImages:{component}";
        var approvedReference = ValidateRepositoryDigest(
            configuration[$"{section}:ApprovedReference"],
            section);
        var version = configuration[$"{section}:ExpectedVersion"]?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException(
                $"{section}:ExpectedVersion is required for operator-visible release provenance.");
        }

        var repository = approvedReference[..approvedReference.LastIndexOf("@sha256:", StringComparison.Ordinal)];
        return new ApprovedOperationalRuntimeImagePolicyEntry(
            component,
            repository,
            version,
            approvedReference);
    }

    private static string ValidateRepositoryDigest(
        string? value,
        string section)
    {
        var reference = value?.Trim().ToLowerInvariant() ?? string.Empty;
        const string marker = "@sha256:";
        var markerIndex = reference.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex <= 0)
        {
            throw new InvalidOperationException(
                $"{section}:ApprovedReference must be an immutable repository digest (...@sha256:...). " +
                "A local sha256 image ID is not a reproducible release authority.");
        }

        var digest = reference[(markerIndex + marker.Length)..];
        if (digest.Length != 64 || !digest.All(char.IsAsciiHexDigit))
        {
            throw new InvalidOperationException(
                $"{section}:ApprovedReference contains an invalid SHA-256 repository digest.");
        }

        return reference;
    }

    private static string NormalizeImageId(string? imageId, string component)
    {
        var normalized = imageId?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!normalized.StartsWith("sha256:", StringComparison.Ordinal) ||
            normalized.Length != "sha256:".Length + 64 ||
            !normalized["sha256:".Length..].All(char.IsAsciiHexDigit))
        {
            throw new InvalidOperationException(
                $"Docker did not return a valid immutable local image ID for the approved {component} runtime.");
        }

        return normalized;
    }
}
