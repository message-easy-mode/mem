using Microsoft.AspNetCore.Http;
using System.Net;
using System.Security.Claims;
using Modules.Auth.Identity;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;

namespace Modules.Operator.Diagnostics.Services;

/// <summary>
/// Persists the optional browser-facing Seq UI authority after a guided,
/// verified deployment. This changes only server-owned bootstrap state; it
/// performs no Docker, Seq runtime, delivery, or secret mutation.
/// </summary>
public sealed class DiagnosticsSeqUiAuthorityService(
    ISeqBootstrapStateStore bootstrapStateStore,
    ISeqRuntimeStatusReader runtimeStatusReader,
    TimeProvider timeProvider)
{
    public async Task<DiagnosticsSeqUiAuthorityUpdateResponse> UpdateAsync(
        DiagnosticsSeqUiAuthorityUpdateRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);

        if (!principal.IsInRole(MemOperatorRoles.PlatformOwner))
        {
            throw new SeqOperationException(
                "seq_ui_authority_owner_required",
                StatusCodes.Status403Forbidden,
                "Platform Owner authority is required to configure Seq browser access.");
        }

        var stateResult = bootstrapStateStore.Read();
        if (!string.IsNullOrWhiteSpace(stateResult.WarningCode))
        {
            throw new SeqOperationException(
                stateResult.WarningCode,
                StatusCodes.Status409Conflict,
                "The Seq bootstrap state is unavailable.");
        }

        var state = stateResult.State;
        if (state is null ||
            !state.ManagementEnabled ||
            state.RuntimeVerifiedAtUtc is null)
        {
            throw new SeqOperationException(
                "seq_ui_authority_requires_verified_runtime",
                StatusCodes.Status409Conflict,
                "A verified MEM-managed Seq runtime is required before configuring browser access.");
        }

        var runtime = await runtimeStatusReader.GetStatusAsync(cancellationToken);
        if (!runtime.Exists ||
            !runtime.Managed ||
            !string.Equals(runtime.OwnershipState, "managed", StringComparison.Ordinal))
        {
            throw new SeqOperationException(
                "seq_ui_authority_requires_managed_runtime",
                StatusCodes.Status409Conflict,
                "The current Seq runtime is not proven MEM-managed.");
        }

        string? normalizedUrl = null;
        if (!string.IsNullOrWhiteSpace(request.Url))
        {
            if (!SeqDiagnosticsOptionsValidator.TryNormalizeUrl(request.Url, out var uri) ||
                uri is null ||
                IsUnspecifiedAddress(uri.Host))
            {
                throw new SeqOperationException(
                    "seq_ui_authority_invalid",
                    StatusCodes.Status400BadRequest,
                    "The Seq UI authority must be an absolute HTTP or HTTPS URL without credentials, query, fragment, or an unspecified bind address.");
            }

            normalizedUrl = uri.ToString();
        }

        await bootstrapStateStore.WriteAsync(
            state with { PrivateUiUrl = normalizedUrl },
            cancellationToken);

        return new DiagnosticsSeqUiAuthorityUpdateResponse(
            SchemaVersion: 1,
            Configured: normalizedUrl is not null,
            Url: normalizedUrl,
            UpdatedAtUtc: timeProvider.GetUtcNow());
    }

    private static bool IsUnspecifiedAddress(string host)
    {
        var candidate = host.Trim('[', ']');
        if (!IPAddress.TryParse(candidate, out var address))
        {
            return false;
        }

        return address.Equals(IPAddress.Any) ||
               address.Equals(IPAddress.IPv6Any);
    }
}
