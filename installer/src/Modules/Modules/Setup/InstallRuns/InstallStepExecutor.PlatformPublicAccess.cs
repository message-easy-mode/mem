namespace Modules.Setup.InstallRuns;

public sealed partial class InstallStepExecutor
{
    private async Task<InstallStepResult> PreparePlatformPublicAccessAsync(
        InstallStepContext context,
        CancellationToken cancellationToken)
    {
        var config = ReadConfig(context.ConfigJson);
        if (config is null)
        {
            return Failed(
                "Platform public access preparation failed.",
                "Installation config could not be read.");
        }

        var bootstrap = await EnsureNpmAdministratorReadyAsync(
            context,
            config.Platform.Ingress,
            cancellationToken);
        if (!bootstrap.Succeeded)
        {
            return bootstrap;
        }

        var provision = await _platformCertificateProvisioner.EnsureAsync(
            context,
            cancellationToken);

        if (!provision.Succeeded)
        {
            return provision;
        }

        return await ValidatePublicAccessReadinessAsync(context, cancellationToken);
    }

    private async Task<InstallStepResult> ValidatePublicAccessReadinessAsync(
    InstallStepContext context,
    CancellationToken cancellationToken)
    {
        var resolved = await ResolvePlatformPublicAccessAsync(
            cancellationToken);

        if (resolved.Error is not null)
        {
            return resolved.Error;
        }

        var access = resolved.Access!;

        return Succeeded(
            $"Public stack certificate readiness is confirmed. Domain zone={access.Zone}, certificateId={access.CertificateId}, npmCertificateId={access.NpmCertificateId}, wildcard={access.WildcardCertificateName}.");
    }

    private sealed record ResolvedPlatformPublicAccess(
        string Zone,
        string CertificateId,
        int NpmCertificateId,
        string WildcardCertificateName);

    private async Task PersistNpmCertificateImportAsync(
        string certificateId,
        int npmCertificateId,
        CancellationToken cancellationToken)
    {
        var trackedCertificate = await _db.Certificates
            .FirstOrDefaultAsync(
                x => x.CertificateId == certificateId,
                cancellationToken);

        if (trackedCertificate is null)
        {
            _logger.LogWarning(
                "Could not persist NPM certificate import status because certificate was not found in DB. CertificateId={CertificateId} NpmCertificateId={NpmCertificateId}",
                certificateId,
                npmCertificateId);

            return;
        }

        trackedCertificate.NpmCertificateId = npmCertificateId;
        trackedCertificate.ImportedToNpm = true;
        trackedCertificate.LastImportedToNpmAtUtc = DateTime.UtcNow;
        trackedCertificate.LastError = null;

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(ResolvedPlatformPublicAccess? Access, InstallStepResult? Error)> ResolvePlatformPublicAccessAsync(
        CancellationToken cancellationToken)
    {
        var domain = await _db.Domains
            .Include(x => x.ActiveCertificate)
            .Include(x => x.Certificates)
            .AsNoTracking()
            .Where(x => x.IsMainPlatformDomain && x.Status == "Active")
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (domain is null)
        {
            return (
                null,
                ActionRequired(
                    "No main platform domain has been selected.",
                    "Return to Setup → Domain and select or create the main platform domain before continuing installation."));
        }

        var certificate = domain.ActiveCertificate
            ?? domain.Certificates
                .Where(x => x.IsActive && x.Status != "Deleted")
                .OrderByDescending(x => x.IsMainPlatformCertificate)
                .ThenByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault();

        if (certificate is null)
        {
            return (
                null,
                ActionRequired(
                    "No active platform certificate was found.",
                    "Return to Setup → Domain and issue or select a wildcard certificate before continuing installation."));
        }

        var certificateId = certificate.CertificateId;

        var localValidation = await _certificateValidation.ValidateAsync(
            certificateId,
            cancellationToken);

        if (!localValidation.Succeeded)
        {
            return (
                null,
                ActionRequired(
                    "The selected platform certificate is not valid.",
                    $"{localValidation.Message} Return to Setup → Domain and repair or reissue the certificate."));
        }

        var npm = await _npmReadiness.GetReadinessAsync(cancellationToken);

        var npmReady =
            npm.ContainerRunning &&
            npm.AdminUiReachable &&
            npm.Initialized &&
            npm.ApiAuthenticated &&
            npm.CertificateApiReachable;

        if (!npmReady)
        {
            return (
                null,
                ActionRequired(
                    "NPM is not ready for platform ingress automation.",
                    $"Resolve NPM readiness before continuing. Recommended action: {npm.RecommendedAction}."));
        }

        var probe = await _npmCertificateProbe.ProbeAsync(
            certificateId,
            cancellationToken);

        var npmCertificateId = probe.Succeeded && probe.MatchingCertificateId is > 0
            ? probe.MatchingCertificateId.Value
            : 0;

        if (npmCertificateId <= 0)
        {
            var import = await _npmCertificateProbe.ImportAsync(
                certificateId,
                cancellationToken);

            if (!import.Succeeded || import.MatchingCertificateId is null or <= 0)
            {
                return (
                    null,
                    ActionRequired(
                        "The platform certificate could not be imported into NPM.",
                        import.ErrorDetail ?? import.Message));
            }

            npmCertificateId = import.MatchingCertificateId.Value;
        }

        await PersistNpmCertificateImportAsync(
            certificateId,
            npmCertificateId,
            cancellationToken);

        var zone = !string.IsNullOrWhiteSpace(domain.DnsZone)
            ? domain.DnsZone.Trim().Trim('.').ToLowerInvariant()
            : domain.BaseDomain.Trim().Trim('.').ToLowerInvariant();

        var wildcardCertificateName = $"*.{zone}";

        return (
            new ResolvedPlatformPublicAccess(
                Zone: zone,
                CertificateId: certificateId,
                NpmCertificateId: npmCertificateId,
                WildcardCertificateName: wildcardCertificateName),
            null);
    }
}
