using Microsoft.AspNetCore.Http;
using Modules.Shared.RuntimeImages;

namespace Modules.Integrations.Seq.Services;

public sealed record SeqResolvedRuntimeImage(
    string ApprovedReference,
    string ResolvedImageId,
    IReadOnlyList<string> RepositoryDigests,
    string ExpectedVersion,
    bool PreparedByPull = false);

public sealed class SeqRuntimeImageResolver(
    SeqDiagnosticsOptions options,
    IRuntimeImageInspector imageInspector,
    SeqEffectiveConfigurationProvider? effectiveConfigurationProvider = null)
{
    public Task<SeqResolvedRuntimeImage> ResolveForOperationAsync(
        CancellationToken cancellationToken)
    {
        var effectiveOptions = GetEffectiveOptions();
        SeqDiagnosticsOptionsValidator.ThrowIfInvalidForManagement(effectiveOptions);
        return ResolveAsync(
            effectiveOptions,
            allowPreparationPull: false,
            setupBoundary: false,
            cancellationToken);
    }

    /// <summary>
    /// Explicit guided setup boundary. This is the only Seq path allowed to
    /// prepare the approved exact image when it is not already local.
    /// </summary>
    public async Task<SeqResolvedRuntimeImage> ResolveForSetupAsync(
        CancellationToken cancellationToken)
    {
        var effectiveOptions = GetEffectiveOptions();
        SeqDiagnosticsOptionsValidator.ThrowIfInvalidForSetup(effectiveOptions);
        return await ResolveAsync(
            effectiveOptions,
            allowPreparationPull: effectiveOptions.AllowSetupPull,
            setupBoundary: true,
            cancellationToken);
    }

    private async Task<SeqResolvedRuntimeImage> ResolveAsync(
        SeqDiagnosticsOptions effectiveOptions,
        bool allowPreparationPull,
        bool setupBoundary,
        CancellationToken cancellationToken)
    {
        var inspection = await imageInspector.InspectAsync(
            effectiveOptions.ApprovedImageReference,
            cancellationToken);
        var pulled = false;

        if (inspection is null && allowPreparationPull)
        {
            await imageInspector.PullAsync(
                effectiveOptions.ApprovedImageReference,
                cancellationToken);
            pulled = true;
            inspection = await imageInspector.InspectAsync(
                effectiveOptions.ApprovedImageReference,
                cancellationToken);
        }

        if (inspection is null)
        {
            var code = setupBoundary
                ? allowPreparationPull
                    ? "seq_setup_image_preparation_failed"
                    : "seq_setup_image_preparation_disabled"
                : "seq_approved_image_missing";
            var message = setupBoundary
                ? allowPreparationPull
                    ? "The approved Seq runtime image could not be prepared through the explicit setup workflow."
                    : "Seq image preparation is disabled by server policy."
                : "The approved Seq runtime image is not available locally. MEM will not pull it during an operational request.";
            throw new SeqOperationException(
                code,
                StatusCodes.Status409Conflict,
                message);
        }

        var imageId = NormalizeImageId(inspection.ImageId);
        if (imageId is null)
        {
            throw new SeqOperationException(
                "seq_approved_image_invalid",
                StatusCodes.Status409Conflict,
                "Docker did not return a valid immutable local image identity for the approved Seq runtime.");
        }

        return new SeqResolvedRuntimeImage(
            effectiveOptions.ApprovedImageReference,
            imageId,
            inspection.RepositoryDigests,
            effectiveOptions.ExpectedVersion,
            pulled);
    }

    private SeqDiagnosticsOptions GetEffectiveOptions() =>
        effectiveConfigurationProvider?.CreateEffectiveOptions() ?? options;

    private static string? NormalizeImageId(string? value)
    {
        var imageId = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return imageId.StartsWith("sha256:", StringComparison.Ordinal) &&
               imageId.Length == "sha256:".Length + 64 &&
               imageId["sha256:".Length..].All(char.IsAsciiHexDigit)
            ? imageId
            : null;
    }
}
