using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Integrations.Npm.Services;
using Modules.Setup;
using Modules.Setup.Start;
using Modules.Setup.Platform.Coturn;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Certificates.Npm;

namespace Modules.Setup.Verification;

public sealed class VerificationReportService
{
    private readonly MemDbContext _db;
    private readonly SetupStartService _platformStatus;
    private readonly NpmReadinessService _npmReadiness;
    private readonly CertificateStorageService _certificateStorage;
    private readonly CertificateValidationService _certificateValidation;
    private readonly NpmCertificateImportProbe _npmCertificateProbe;
    private readonly IPlatformCoturnSetupService? _platformCoturnSetupService;

    public VerificationReportService(
        MemDbContext db,
        SetupStartService platformStatus,
        NpmReadinessService npmReadiness,
        CertificateStorageService certificateStorage,
        CertificateValidationService certificateValidation,
        NpmCertificateImportProbe npmCertificateProbe,
        IPlatformCoturnSetupService? platformCoturnSetupService = null)
    {
        _db = db;
        _platformStatus = platformStatus;
        _npmReadiness = npmReadiness;
        _certificateStorage = certificateStorage;
        _certificateValidation = certificateValidation;
        _npmCertificateProbe = npmCertificateProbe;
        _platformCoturnSetupService = platformCoturnSetupService;
    }

    public Task<VerificationReportResponse> GetReportAsync(
        CancellationToken cancellationToken)
    {
        return GetReportAsync(
            installationId: null,
            cancellationToken);
    }

    public async Task<VerificationReportResponse> GetReportAsync(
        Guid? installationId,
        CancellationToken cancellationToken)
    {
        var checks = new List<VerificationCheckResult>();

        if (installationId is not null)
        {
            checks.Add(await CheckInstallRunAsync(
                installationId.Value,
                cancellationToken));
        }

        checks.Add(await CheckPlatformAsync(cancellationToken));
        checks.Add(await CheckNpmReadinessAsync(cancellationToken));
        checks.Add(await CheckCertificateAsync(cancellationToken));
        checks.Add(await CheckNpmCertificateAsync(cancellationToken));
        checks.Add(await CheckCoturnAsync(cancellationToken));

        if (installationId is not null)
        {
            checks.AddRange(await CheckInstallStepEvidenceAsync(
                installationId.Value,
                cancellationToken));
        }

        var status = checks.Any(x => x.Status == "Failed")
            ? "Failed"
            : checks.Any(x => x.Status is "Warning" or "Pending" or "Skipped")
                ? "Warning"
                : "Succeeded";

        var message = status switch
        {
            "Succeeded" => "MEM platform verification completed successfully.",
            "Failed" => "One or more verification checks failed.",
            _ => "Core installer checks passed, but some checks are pending, skipped, or not fully wired yet."
        };

        return new VerificationReportResponse(
            Status: status,
            Message: message,
            CheckedAtUtc: DateTimeOffset.UtcNow,
            Checks: checks);
    }

    private async Task<VerificationCheckResult> CheckInstallRunAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var installation = await _db.Installations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == installationId,
                cancellationToken);

        if (installation is null)
        {
            return new VerificationCheckResult(
                Key: "install.run",
                Title: "Install run exists",
                Description: "The referenced setup install run exists.",
                Status: "Failed",
                Message: $"Install run '{installationId}' was not found.",
                Evidence:
                [
                    new VerificationEvidence(
                        Key: "installation.id",
                        Value: installationId.ToString(),
                        Status: "Failed")
                ]);
        }

        var status = installation.Status switch
        {
            "Succeeded" => "Succeeded",
            "Running" => "Pending",
            "WaitingForUser" => "Warning",
            "Failed" => "Failed",
            _ => "Warning"
        };

        var message = installation.Status switch
        {
            "Succeeded" => "Install run completed successfully.",
            "Running" => "Install run is still running.",
            "WaitingForUser" => "Install run is waiting for operator action.",
            "Failed" => installation.LastError ?? "Install run failed.",
            _ => $"Install run has status '{installation.Status}'."
        };

        return new VerificationCheckResult(
            Key: "install.run",
            Title: "Install run completed",
            Description: "The setup install workflow completed before handoff.",
            Status: status,
            Message: message,
            Evidence:
            [
                new VerificationEvidence(
                    Key: "installation.id",
                    Value: installation.Id.ToString(),
                    Status: "Succeeded"),
                new VerificationEvidence(
                    Key: "installation.status",
                    Value: installation.Status,
                    Status: status),
                new VerificationEvidence(
                    Key: "installation.startedAtUtc",
                    Value: SetupUtcDateTime.ToOffset(installation.StartedAtUtc)?.ToString("O") ?? "-",
                    Status: installation.StartedAtUtc is null ? "Warning" : "Succeeded"),
                new VerificationEvidence(
                    Key: "installation.completedAtUtc",
                    Value: SetupUtcDateTime.ToOffset(installation.CompletedAtUtc)?.ToString("O") ?? "-",
                    Status: installation.CompletedAtUtc is null ? "Warning" : "Succeeded")
            ]);
    }

    private async Task<IReadOnlyList<VerificationCheckResult>> CheckInstallStepEvidenceAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var steps = await _db.InstallationStepExecutions
            .AsNoTracking()
            .Where(x => x.InstallationId == installationId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(cancellationToken);

        if (steps.Count == 0)
        {
            return
            [
                new VerificationCheckResult(
                    Key: "install.steps",
                    Title: "Install steps recorded",
                    Description: "The setup install workflow recorded step evidence.",
                    Status: "Warning",
                    Message: "No install step records were found for this installation.",
                    Evidence: [])
            ];
        }

        var failed = steps.Where(x => x.Status == "Failed").ToList();
        var waiting = steps.Where(x => x.Status == "WaitingForUser").ToList();
        var running = steps.Where(x => x.Status == "Running").ToList();
        var pending = steps.Where(x => x.Status == "Pending").ToList();

        var status =
            failed.Count > 0 ? "Failed" :
            waiting.Count > 0 ? "Warning" :
            running.Count > 0 || pending.Count > 0 ? "Pending" :
            "Succeeded";

        var message = status switch
        {
            "Succeeded" => "All recorded install steps completed successfully.",
            "Failed" => $"{failed.Count} install step(s) failed.",
            "Warning" => $"{waiting.Count} install step(s) still require operator action.",
            _ => "Some install steps are still running or pending."
        };

        var evidence = steps
            .Select(step => new VerificationEvidence(
                Key: $"step.{step.Sequence:00}.{Slug(step.StepName)}",
                Value: $"{step.StepName}: {step.Status}" +
                       (string.IsNullOrWhiteSpace(step.ErrorMessage)
                           ? ""
                           : $" - {step.ErrorMessage}"),
                Status: MapStepStatus(step.Status)))
            .ToList();

        return
        [
            new VerificationCheckResult(
                Key: "install.steps",
                Title: "Install steps completed",
                Description: "Recorded setup workflow steps completed without unresolved failures.",
                Status: status,
                Message: message,
                Evidence: evidence)
        ];
    }

    private async Task<VerificationCheckResult> CheckPlatformAsync(
        CancellationToken cancellationToken)
    {
        var status = await _platformStatus.GetAsync(cancellationToken);

        var dockerOk = status.Docker.Reachable;

        var required = status.RequiredServices
            .Where(x => x.Key is "postgres" or "npm" or "coturn")
            .ToList();

        var requiredRunning = required.Count > 0 &&
                              required.All(x => x.Installed && x.Running);

        var ok = dockerOk && requiredRunning;

        return new VerificationCheckResult(
            Key: "platform.resources",
            Title: "Docker resources present",
            Description: "Required containers and Docker runtime resources are present.",
            Status: ok ? "Succeeded" : "Failed",
            Message: ok
                ? "Docker is reachable and the required MEM 0.2.0 platform dependencies are running."
                : "Docker or one or more required MEM 0.2.0 platform dependencies are not ready.",
            Evidence:
            [
                new VerificationEvidence(
                    Key: "docker.reachable",
                    Value: dockerOk.ToString(),
                    Status: dockerOk ? "Succeeded" : "Failed"),
                .. required.Select(x => new VerificationEvidence(
                    Key: $"service.{x.Key}",
                    Value: $"{x.State}",
                    Status: x.Running ? "Succeeded" : "Failed"))
            ]);
    }

    private async Task<VerificationCheckResult> CheckNpmReadinessAsync(
        CancellationToken cancellationToken)
    {
        var npm = await _npmReadiness.GetReadinessAsync(cancellationToken);

        var ok =
            npm.ContainerRunning &&
            npm.AdminUiReachable &&
            npm.Initialized &&
            npm.ApiAuthenticated &&
            npm.CertificateApiReachable;

        return new VerificationCheckResult(
            Key: "ingress.npm.ready",
            Title: "Ingress reachable",
            Description: "NPM / ingress is initialized and ready for certificate automation.",
            Status: ok ? "Succeeded" : "Failed",
            Message: ok
                ? "NPM is running, initialized, authenticated, and its certificate API is reachable."
                : $"NPM is not ready. Recommended action: {npm.RecommendedAction}.",
            Evidence:
            [
                new VerificationEvidence(
                    Key: "npm.containerRunning",
                    Value: npm.ContainerRunning.ToString(),
                    Status: npm.ContainerRunning ? "Succeeded" : "Failed"),
                new VerificationEvidence(
                    Key: "npm.adminUiReachable",
                    Value: npm.AdminUiReachable.ToString(),
                    Status: npm.AdminUiReachable ? "Succeeded" : "Failed"),
                new VerificationEvidence(
                    Key: "npm.initialized",
                    Value: npm.Initialized.ToString(),
                    Status: npm.Initialized ? "Succeeded" : "Failed"),
                new VerificationEvidence(
                    Key: "npm.apiAuthenticated",
                    Value: npm.ApiAuthenticated.ToString(),
                    Status: npm.ApiAuthenticated ? "Succeeded" : "Failed"),
                new VerificationEvidence(
                    Key: "npm.certificateApiReachable",
                    Value: npm.CertificateApiReachable.ToString(),
                    Status: npm.CertificateApiReachable ? "Succeeded" : "Failed"),
                new VerificationEvidence(
                    Key: "npm.certificateCount",
                    Value: npm.CertificateCount.ToString(),
                    Status: "Succeeded"),
                new VerificationEvidence(
                    Key: "npm.recommendedAction",
                    Value: npm.RecommendedAction)
            ]);
    }

    private async Task<VerificationCheckResult> CheckCoturnAsync(
        CancellationToken cancellationToken)
    {
        if (_platformCoturnSetupService is null)
        {
            return new VerificationCheckResult(
                Key: "platform.coturn.ready",
                Title: "Shared platform TURN ready",
                Description: "The MEM-owned shared Coturn runtime is structurally ready for Matrix stack voice/video configuration.",
                Status: "Failed",
                Message: "The shared platform TURN verification service is unavailable.",
                Evidence: []);
        }

        try
        {
            var coturn = await _platformCoturnSetupService.InspectAsync(cancellationToken);
            var evidence = new List<VerificationEvidence>
            {
                new("coturn.container", coturn.ContainerName, Status: coturn.ContainerExists ? "Succeeded" : "Failed"),
                new("coturn.running", coturn.Running.ToString(), Status: coturn.Running ? "Succeeded" : "Failed"),
                new("coturn.ownershipVerified", coturn.OwnershipVerified.ToString(), Status: coturn.OwnershipVerified ? "Succeeded" : "Failed"),
                new("coturn.imageApproved", coturn.ImageApproved.ToString(), Status: coturn.ImageApproved ? "Succeeded" : "Failed"),
                new("coturn.secretPresent", coturn.SecretPresent.ToString(), Sensitive: true, Status: coturn.SecretPresent ? "Succeeded" : "Failed"),
                new("coturn.relayPortsPublished", coturn.RelayPortsPublished.ToString(), Status: coturn.RelayPortsPublished ? "Succeeded" : "Failed"),
                new("coturn.securityPolicyApplied", coturn.SecurityPolicyApplied.ToString(), Status: coturn.SecurityPolicyApplied ? "Succeeded" : "Failed"),
                new("coturn.publicHost", coturn.PublicHost, Status: coturn.Ready ? "Succeeded" : "Warning")
            };

            foreach (var warning in coturn.Warnings)
            {
                evidence.Add(new VerificationEvidence(
                    "coturn.warning",
                    warning,
                    Status: "Warning"));
            }

            return new VerificationCheckResult(
                Key: "platform.coturn.ready",
                Title: "Shared platform TURN ready",
                Description: "The MEM-owned shared Coturn runtime is structurally ready for Matrix stack voice/video configuration.",
                Status: coturn.Ready ? "Succeeded" : "Failed",
                Message: coturn.Ready
                    ? "The shared Coturn runtime is structurally ready. DNS, NAT, firewall, and real external-client reachability remain separate network evidence."
                    : coturn.Detail ?? "The shared Coturn runtime is not structurally ready.",
                Evidence: evidence);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new VerificationCheckResult(
                Key: "platform.coturn.ready",
                Title: "Shared platform TURN ready",
                Description: "The MEM-owned shared Coturn runtime is structurally ready for Matrix stack voice/video configuration.",
                Status: "Failed",
                Message: "Coturn verification could not complete safely. Review the Coturn workspace and Diagnostics for bounded evidence.",
                Evidence: []);
        }
    }

    private async Task<VerificationCheckResult> CheckCertificateAsync(
        CancellationToken cancellationToken)
    {
        var certificateId = await ResolveMainCertificateIdAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(certificateId))
        {
            return new VerificationCheckResult(
                Key: "certificate.valid",
                Title: "MEM-managed certificate valid",
                Description: "A MEM-managed certificate exists and validates locally.",
                Status: "Failed",
                Message: "No active main platform certificate was found.",
                Evidence: []);
        }

        var validation = await _certificateValidation.ValidateAsync(
            certificateId,
            cancellationToken);

        return new VerificationCheckResult(
            Key: "certificate.valid",
            Title: "MEM-managed certificate valid",
            Description: "A MEM-managed certificate exists, parses correctly, and its private key matches.",
            Status: validation.Succeeded ? "Succeeded" : "Failed",
            Message: validation.Message,
            Evidence: validation.Evidence
                .Select(x => new VerificationEvidence(
                    Key: x.Key,
                    Value: x.Sensitive ? "stored, sensitive" : x.Value,
                    Sensitive: x.Sensitive,
                    Status: x.Status))
                .ToList());
    }

    private async Task<VerificationCheckResult> CheckNpmCertificateAsync(
        CancellationToken cancellationToken)
    {
        var certificateId = await ResolveMainCertificateIdAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(certificateId))
        {
            return new VerificationCheckResult(
                Key: "ingress.certificate.usable",
                Title: "NPM certificate usable",
                Description: "NPM can consume the MEM-managed certificate.",
                Status: "Skipped",
                Message: "Skipped because no active main platform certificate was found.",
                Evidence: []);
        }

        var result = await _npmCertificateProbe.ProbeAsync(
            certificateId,
            cancellationToken);

        return new VerificationCheckResult(
            Key: "ingress.certificate.usable",
            Title: "NPM certificate usable",
            Description: "NPM can see the MEM-managed certificate record.",
            Status: result.Succeeded && result.MatchingCertificateId is not null
                ? "Succeeded"
                : "Warning",
            Message: result.Message,
            Evidence: result.Evidence
                .Select(x => new VerificationEvidence(
                    Key: x.Key,
                    Value: x.Sensitive ? "stored, sensitive" : x.Value,
                    Sensitive: x.Sensitive,
                    Status: x.Status))
                .ToList());
    }

    private async Task<string?> ResolveMainCertificateIdAsync(
        CancellationToken cancellationToken)
    {
        var domain = await _db.Domains
            .Include(x => x.ActiveCertificate)
            .Include(x => x.Certificates)
            .AsNoTracking()
            .Where(x => x.IsMainPlatformDomain && x.Status == "Active")
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var certificate = domain?.ActiveCertificate
            ?? domain?.Certificates
                .Where(x => x.IsActive && x.Status != "Deleted")
                .OrderByDescending(x => x.IsMainPlatformCertificate)
                .ThenByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(certificate?.CertificateId))
        {
            return certificate.CertificateId;
        }

        var latest = (await _certificateStorage.ListAsync(cancellationToken))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

        return latest?.CertificateId;
    }

    private static string MapStepStatus(string status)
    {
        return status switch
        {
            "Succeeded" => "Succeeded",
            "Failed" => "Failed",
            "WaitingForUser" => "Warning",
            "Running" => "Pending",
            "Pending" => "Pending",
            _ => "Warning"
        };
    }

    private static string Slug(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var slug = new string(chars);

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }
}