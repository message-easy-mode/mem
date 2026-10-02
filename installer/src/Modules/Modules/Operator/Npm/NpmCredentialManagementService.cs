using System.Net;
using Core.RuntimeDefinition;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Services.Identity;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;
using Modules.Setup.InstallRuns;
using Modules.Setup.Secrets;
using Shared.ControlPlane.Runtime;

namespace Modules.Operator.Npm;

/// <summary>
/// Installed-state authority for the MEM-managed Nginx Proxy Manager
/// administrator credential. Browser callers receive only a safe projection;
/// plaintext is resolved only for an explicitly step-up-protected reveal or
/// for server-side verification of a replacement credential.
/// </summary>
public sealed class NpmCredentialManagementService(
    MemDbContext db,
    NpmAdminCredentialService credentialService,
    NpmRuntimeService runtimeService,
    NpmApiBaseUrlResolver apiBaseUrlResolver,
    NpmApiClient apiClient,
    INpmTokenProvider tokenProvider,
    IMemManagedServiceAuthorityResolver authorityResolver,
    IMemOperatorAuditService audit,
    TimeProvider timeProvider)
{
    public async Task<OperatorNpmSettingsProjection> GetAsync(
        CancellationToken cancellationToken)
    {
        var installation = await CurrentInstalledInstallationAsync(cancellationToken)
            ?? throw NotInstalled();

        var credential = await credentialService.GetForInstallationAsync(
            installation.Id,
            cancellationToken);
        var runtime = await runtimeService.GetStatusAsync(cancellationToken);
        var browserUrl = ResolveBrowserUrl(runtime.AdminHostPort);
        var runtimeImage = string.IsNullOrWhiteSpace(runtime.Container?.Image)
            ? null
            : runtime.Container.Image.Trim();
        var runtimeImageAligned = NpmRuntimeRelease.IsApprovedImage(runtimeImage);

        // A Setup-created NPM container may legitimately predate the generic
        // RuntimeServices catalog record. For installed credential management,
        // the live Docker inspection is the authority; do not surface the old
        // setup-time catalog warning when the container is healthy.
        var warning = runtime.Exists && runtime.Running
            ? null
            : runtime.Warnings.FirstOrDefault();

        if (browserUrl is null && runtime.Exists && runtime.AdminHostPort is not null)
        {
            warning ??= "MEM could not derive a safe browser authority for the Nginx Proxy Manager administration UI in this runtime.";
        }

        if (runtime.Exists && !runtimeImageAligned)
        {
            warning = $"NPM is running image '{runtimeImage ?? "<unknown>"}', but MEM 0.2.0 approves '{NpmRuntimeRelease.ApprovedImage}'. The runtime should be recreated on the approved release before final release validation.";
        }

        return new OperatorNpmSettingsProjection(
            RuntimeStatus: runtime.Running
                ? "Running"
                : runtime.Exists
                    ? "Stopped"
                    : "Unavailable",
            RuntimeExists: runtime.Exists,
            Running: runtime.Running,
            RuntimeState: runtime.State,
            RuntimeImage: runtimeImage,
            ApprovedRuntimeImage: NpmRuntimeRelease.ApprovedImage,
            ApprovedRuntimeVersion: NpmRuntimeRelease.ApprovedVersion,
            RuntimeImageAligned: runtimeImageAligned,
            BrowserUrl: browserUrl,
            AdministratorEmail: credential.AdministratorEmail,
            CredentialStored: credential.CredentialStored,
            CredentialStatus: credential.Status,
            LastVerifiedAtUtc: credential.VerifiedAtUtc,
            Warning: warning);
    }

    public async Task<OperatorNpmCredentialRevealResponse> RevealAsync(
        Guid? actorOperatorId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var installation = await CurrentInstalledInstallationAsync(cancellationToken)
            ?? throw NotInstalled();

        var credential = await credentialService.ResolveAsync(
            installation.Id,
            cancellationToken);
        if (credential is null)
        {
            throw new OperatorNpmCredentialException(
                OperatorNpmCredentialFailureKind.CredentialUnavailable,
                "npm_credential_unavailable",
                "No protected Nginx Proxy Manager administrator credential is available.");
        }

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "operator.npm.credential.revealed",
                Outcome: "succeeded",
                ActorOperatorId: actorOperatorId,
                CorrelationId: correlationId,
                ReasonCode: "step_up_verified"),
            cancellationToken);

        return new OperatorNpmCredentialRevealResponse(
            credential.Email,
            credential.Password);
    }

    public async Task<NpmAdminCredentialProjection> UpdateAsync(
        UpdateOperatorNpmCredentialRequest request,
        Guid? actorOperatorId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var installation = await CurrentInstalledInstallationAsync(cancellationToken)
            ?? throw NotInstalled();

        var validation = credentialService.ValidateForStorage(
            new NpmAdminCredentialRequest(request.Email, request.Password));
        if (!validation.IsValid)
        {
            throw new OperatorNpmCredentialException(
                OperatorNpmCredentialFailureKind.Validation,
                validation.ErrorCode!,
                validation.Message!);
        }

        NpmApiBaseUrlResolution resolution;
        try
        {
            resolution = await apiBaseUrlResolver.ResolveAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw new OperatorNpmCredentialException(
                OperatorNpmCredentialFailureKind.VerificationUnavailable,
                "npm_credential_verification_unavailable",
                "MEM could not resolve the Nginx Proxy Manager administration API for credential verification.");
        }

        try
        {
            _ = await apiClient.LoginAsync(
                resolution.BaseUrl,
                validation.Email!,
                validation.Password!,
                cancellationToken);
        }
        catch (HttpRequestException exception) when (
            exception.StatusCode is HttpStatusCode.BadRequest or
                HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden)
        {
            await WriteVerificationFailureAuditAsync(
                actorOperatorId,
                correlationId,
                "npm_credentials_rejected",
                cancellationToken);

            throw new OperatorNpmCredentialException(
                OperatorNpmCredentialFailureKind.CredentialRejected,
                "npm_credentials_rejected",
                "Nginx Proxy Manager did not accept the replacement administrator credential.");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or InvalidOperationException)
        {
            await WriteVerificationFailureAuditAsync(
                actorOperatorId,
                correlationId,
                "npm_credential_verification_unavailable",
                cancellationToken);

            throw new OperatorNpmCredentialException(
                OperatorNpmCredentialFailureKind.VerificationUnavailable,
                "npm_credential_verification_unavailable",
                "MEM could not verify the replacement administrator credential against Nginx Proxy Manager.");
        }

        var verifiedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var updated = await credentialService.StoreVerifiedForInstallationAsync(
            installation.Id,
            new NpmAdminCredentialRequest(validation.Email, validation.Password),
            verifiedAtUtc,
            cancellationToken);

        // Any token created from the prior credential must not survive a
        // successful replacement. The next managed NPM request must obtain a
        // token from the newly verified protected credential.
        tokenProvider.Invalidate();

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "operator.npm.credential.updated",
                Outcome: "succeeded",
                ActorOperatorId: actorOperatorId,
                CorrelationId: correlationId,
                ReasonCode: "verified_before_replace"),
            cancellationToken);

        return updated;
    }

    private Task WriteVerificationFailureAuditAsync(
        Guid? actorOperatorId,
        string? correlationId,
        string reasonCode,
        CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "operator.npm.credential.verification_failed",
                Outcome: "failed",
                ActorOperatorId: actorOperatorId,
                CorrelationId: correlationId,
                ReasonCode: reasonCode),
            cancellationToken);

    private string? ResolveBrowserUrl(int? publishedAdminPort)
    {
        if (publishedAdminPort is not (> 0 and <= 65535))
        {
            return null;
        }

        try
        {
            var authority = authorityResolver.Resolve(
                MemManagedServicePurposes.Browser,
                new MemManagedServiceAuthoritySource(
                    ServiceName: ManagedServiceNames.Npm,
                    PublishedHostPort: publishedAdminPort,
                    DockerNetworkAlias: ManagedNetworkAliases.Npm,
                    HealthPort: 81,
                    AdministrationPort: 81,
                    IngestionPort: null));

            return authority.Authority.ToString().TrimEnd('/');
        }
        catch (MemManagedServiceAuthorityException)
        {
            return null;
        }
    }

    private async Task<InstallationEntity?> CurrentInstalledInstallationAsync(
        CancellationToken cancellationToken) =>
        await db.Installations
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(
                x => x.Status == InstallationStatuses.Succeeded,
                cancellationToken);

    private static OperatorNpmCredentialException NotInstalled() => new(
        OperatorNpmCredentialFailureKind.NotInstalled,
        "npm_installed_state_unavailable",
        "No completed MEM installation is available for installed Nginx Proxy Manager credential management.");
}
