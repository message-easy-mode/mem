using System.Diagnostics;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Setup.Domains.Certificates;
using Modules.Setup.InstallPlans;
using Modules.Setup.Secrets;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Renewal;
using Shared.Diagnostics;

namespace Modules.Setup.InstallRuns;

public sealed class InstallPlatformCertificateProvisioner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly MemDbContext _db;
    private readonly ISetupPlatformCertificateService _platformCertificates;
    private readonly IInstallationSecretStore _secretStore;
    private readonly DomainCertificateRenewalService _renewalService;
    private readonly ILogger<InstallPlatformCertificateProvisioner> _logger;
    private readonly IMemDiagnosticEventWriter? _diagnostics;
    private readonly InstallProgressReporter? _progressReporter;

    public InstallPlatformCertificateProvisioner(
        MemDbContext db,
        ISetupPlatformCertificateService platformCertificates,
        IInstallationSecretStore secretStore,
        DomainCertificateRenewalService renewalService,
        ILogger<InstallPlatformCertificateProvisioner> logger,
        IMemDiagnosticEventWriter? diagnostics = null,
        InstallProgressReporter? progressReporter = null)
    {
        _db = db;
        _platformCertificates = platformCertificates;
        _secretStore = secretStore;
        _renewalService = renewalService;
        _logger = logger;
        _diagnostics = diagnostics;
        _progressReporter = progressReporter;
    }

    public async Task<InstallStepResult> EnsureAsync(
        InstallStepContext context,
        CancellationToken cancellationToken)
    {
        var frozen = ReadPlan(context.ConfigJson);
        if (frozen is null)
        {
            return Failed("Platform certificate preparation failed.", "The reviewed installation plan could not be read.");
        }

        if (frozen.Review is null || string.IsNullOrWhiteSpace(frozen.Review.PlanSha256))
        {
            return Failed("Platform certificate preparation is blocked.", "Review authority is missing from the frozen installation plan.");
        }

        var access = frozen.PublicAccess;
        if (access.Preparation is null ||
            !string.Equals(access.Preparation.Status, "Validated", StringComparison.OrdinalIgnoreCase) ||
            !access.Preparation.ProviderAccessConfirmed ||
            string.IsNullOrWhiteSpace(access.Zone) ||
            string.IsNullOrWhiteSpace(access.Domain) ||
            string.IsNullOrWhiteSpace(access.AcmeEmail))
        {
            return Failed(
                "Platform certificate preparation is blocked.",
                "The frozen Domain plan is incomplete. Return to Domain, validate the plan, and accept Review again.");
        }

        await ProgressPhaseAsync(
            context.InstallationId,
            "certificate.prepare",
            "Preparing the reviewed TLS certificate request.",
            cancellationToken);

        var startedAt = Stopwatch.GetTimestamp();
        _logger.LogInformation(
            "Reviewed platform certificate pipeline started. InstallationId={InstallationId} Zone={Zone} Domain={Domain} Provider={Provider} Environment={Environment}",
            context.InstallationId,
            access.Zone,
            access.Domain,
            access.DnsProvider,
            access.UseStaging ? "staging" : "production");

        await RecordAsync(
            context.InstallationId,
            "installation.certificate.pipeline.started",
            MemDiagnosticSeverities.Information,
            "The reviewed platform certificate pipeline started.",
            access,
            result: "running",
            failureClass: null,
            certificateId: null,
            npmCertificateId: null,
            exactSecrets: null);

        string? providerToken = null;
        try
        {
            var current = await _db.Installations
                .FirstAsync(x => x.Id == context.InstallationId, cancellationToken);
            var runtimePlan = ReadPlan(current.ConfigJson) ?? frozen;
            var runtimeAccess = runtimePlan.PublicAccess;

            var existingCertificateId = runtimeAccess.CertificateId?.Trim();
            if (!string.IsNullOrWhiteSpace(existingCertificateId))
            {
                await ProgressPhaseAsync(
                    context.InstallationId,
                    "certificate.reuse",
                    "Checking whether the existing managed certificate can be reused.",
                    cancellationToken);

                var existing = await _db.Certificates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.CertificateId == existingCertificateId &&
                             x.IsActive &&
                             x.Status != "Deleted",
                        cancellationToken);

                if (existing is not null)
                {
                    var imported = existing.ImportedToNpm && existing.NpmCertificateId is > 0;
                    if (!imported)
                    {
                        await ProgressPhaseAsync(
                            context.InstallationId,
                            "certificate.npm-import",
                            "Importing the managed TLS certificate into Nginx Proxy Manager.",
                            cancellationToken);

                        var import = await _platformCertificates.EnsureNpmImportAsync(
                            existing.CertificateId,
                            cancellationToken);

                        if (!import.Succeeded || import.MatchingCertificateId is null or <= 0)
                        {
                            return await PipelineFailedAsync(
                                context.InstallationId,
                                access,
                                import.ErrorCode ?? "NpmCertificateImportFailed",
                                import.ErrorDetail ?? import.Message,
                                providerToken,
                                existing.CertificateId,
                                null,
                                cancellationToken);
                        }

                        existing = await _db.Certificates
                            .AsNoTracking()
                            .FirstAsync(x => x.CertificateId == existing.CertificateId, cancellationToken);
                    }

                    await ProgressPhaseAsync(
                        context.InstallationId,
                        "certificate.persist",
                        "Recording the managed certificate outcome in the installation state.",
                        cancellationToken);

                    await PersistOutcomeAsync(
                        context.InstallationId,
                        existing.CertificateId,
                        existing.NpmCertificateId,
                        cancellationToken);

                    var existingRenewalTransitionFailure = await TransitionSetupCredentialToDomainRenewalAsync(
                        context.InstallationId,
                        access,
                        existing.CertificateId,
                        providerToken: null,
                        cancellationToken);
                    if (existingRenewalTransitionFailure is not null)
                    {
                        return existingRenewalTransitionFailure;
                    }

                    return await PipelineSucceededAsync(
                        context.InstallationId,
                        access,
                        existing.CertificateId,
                        existing.NpmCertificateId,
                        startedAt,
                        cancellationToken);
                }
            }

            await ProgressPhaseAsync(
                context.InstallationId,
                "certificate.credential",
                "Resolving the protected DNS provider credential for the certificate request.",
                cancellationToken);

            providerToken = await _secretStore.ResolveProtectedAsync(
                context.InstallationId,
                InstallationSecretNames.DnsCategory,
                InstallationSecretNames.DesecProviderToken,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(providerToken))
            {
                return await PipelineWaitingAsync(
                    context.InstallationId,
                    access,
                    "DnsProviderCredentialMissing",
                    "The protected deSEC credential is unavailable. Return to Domain, re-enter the credential, accept Review again, and retry installation.",
                    null,
                    cancellationToken);
            }

            await ProgressPhaseAsync(
                context.InstallationId,
                "certificate.issue",
                "Requesting the wildcard TLS certificate from Let's Encrypt. DNS and ACME processing can take a few minutes.",
                cancellationToken);

            var issue = await RunWithProgressHeartbeatAsync(
                context.InstallationId,
                "Requesting the wildcard TLS certificate from Let's Encrypt. DNS and ACME processing is still in progress.",
                () => _platformCertificates.IssuePlatformCertificateAsync(
                    new CertificateIssueRequest(
                        Domain: access.Domain,
                        Zone: access.Zone,
                        IsWildcard: true,
                        Email: access.AcmeEmail,
                        Provider: access.DnsProvider,
                        ProviderToken: providerToken,
                        UseStaging: access.UseStaging,
                        StorageName: $"platform-{access.Zone}-{(access.UseStaging ? "staging" : "production")}"),
                    cancellationToken,
                    progressCallback: (progress, token) => ReportCertificateProgressAsync(
                        context.InstallationId,
                        progress,
                        token)),
                cancellationToken);

            var certificateId = EvidenceValue(issue, "certificateId");

            if (!string.IsNullOrWhiteSpace(certificateId))
            {
                var partial = await _db.Certificates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.CertificateId == certificateId, cancellationToken);

                if (partial is not null)
                {
                    await PersistOutcomeAsync(
                        context.InstallationId,
                        partial.CertificateId,
                        partial.NpmCertificateId,
                        cancellationToken);
                }
            }

            if (!issue.Succeeded)
            {
                if (RequiresPlanRepair(issue.ErrorCode))
                {
                    return await PipelineWaitingAsync(
                        context.InstallationId,
                        access,
                        issue.ErrorCode ?? "CertificateIssueFailed",
                        issue.ErrorDetail ?? issue.Message,
                        providerToken,
                        cancellationToken);
                }

                return await PipelineFailedAsync(
                    context.InstallationId,
                    access,
                    issue.ErrorCode ?? "CertificateIssueFailed",
                    issue.ErrorDetail ?? issue.Message,
                    providerToken,
                    certificateId,
                    null,
                    cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(certificateId))
            {
                return await PipelineFailedAsync(
                    context.InstallationId,
                    access,
                    "CertificateIdMissing",
                    "Certificate issuance completed without a durable certificate identifier.",
                    providerToken,
                    null,
                    null,
                    cancellationToken);
            }

            var certificate = await _db.Certificates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CertificateId == certificateId, cancellationToken);

            if (certificate is null)
            {
                return await PipelineFailedAsync(
                    context.InstallationId,
                    access,
                    "CertificateRegistryMissing",
                    "The issued certificate was not found in the durable Domain registry.",
                    providerToken,
                    certificateId,
                    null,
                    cancellationToken);
            }

            if (!certificate.ImportedToNpm || certificate.NpmCertificateId is null or <= 0)
            {
                await ProgressPhaseAsync(
                    context.InstallationId,
                    "certificate.npm-import",
                    "Importing the managed TLS certificate into Nginx Proxy Manager.",
                    cancellationToken);

                var import = await _platformCertificates.EnsureNpmImportAsync(
                    certificate.CertificateId,
                    cancellationToken);

                if (!import.Succeeded || import.MatchingCertificateId is null or <= 0)
                {
                    await PersistOutcomeAsync(
                        context.InstallationId,
                        certificate.CertificateId,
                        null,
                        cancellationToken);

                    return await PipelineFailedAsync(
                        context.InstallationId,
                        access,
                        import.ErrorCode ?? "NpmCertificateImportFailed",
                        import.ErrorDetail ?? import.Message,
                        providerToken,
                        certificate.CertificateId,
                        null,
                        cancellationToken);
                }

                certificate = await _db.Certificates
                    .AsNoTracking()
                    .FirstAsync(x => x.CertificateId == certificate.CertificateId, cancellationToken);
            }

            await ProgressPhaseAsync(
                context.InstallationId,
                "certificate.persist",
                "Recording the managed certificate outcome in the installation state.",
                cancellationToken);

            await PersistOutcomeAsync(
                context.InstallationId,
                certificate.CertificateId,
                certificate.NpmCertificateId,
                cancellationToken);

            var renewalTransitionFailure = await TransitionSetupCredentialToDomainRenewalAsync(
                context.InstallationId,
                access,
                certificate.CertificateId,
                providerToken,
                cancellationToken);
            if (renewalTransitionFailure is not null)
            {
                return renewalTransitionFailure;
            }

            return await PipelineSucceededAsync(
                context.InstallationId,
                access,
                certificate.CertificateId,
                certificate.NpmCertificateId,
                startedAt,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Reviewed platform certificate pipeline failed unexpectedly. InstallationId={InstallationId} Zone={Zone}",
                context.InstallationId,
                access.Zone);

            return await PipelineFailedAsync(
                context.InstallationId,
                access,
                "CertificatePipelineException",
                ex.Message,
                providerToken,
                null,
                null,
                CancellationToken.None);
        }
    }

    private Task ProgressPhaseAsync(
        Guid installationId,
        string phaseCode,
        string safeSummary,
        CancellationToken cancellationToken) =>
        _progressReporter?.BeginCurrentPhaseAsync(
            installationId,
            phaseCode,
            safeSummary,
            cancellationToken) ?? Task.CompletedTask;

    private Task ReportCertificateProgressAsync(
        Guid installationId,
        CertificateIssueProgress progress,
        CancellationToken cancellationToken)
    {
        if (_progressReporter is null)
        {
            return Task.CompletedTask;
        }

        if (progress.Transition != CertificateIssueProgressTransition.None)
        {
            return _progressReporter.SetPhaseRecoveryStatusAsync(
                installationId,
                progress.PhaseCode,
                recovered: progress.Transition == CertificateIssueProgressTransition.Recovered,
                safeSummary: progress.SafeSummary,
                cancellationToken: cancellationToken);
        }

        return progress.IsHeartbeat
            ? _progressReporter.HeartbeatCurrentAsync(
                installationId,
                progress.PhaseCode,
                progress.SafeSummary,
                cancellationToken)
            : _progressReporter.BeginCurrentPhaseAsync(
                installationId,
                progress.PhaseCode,
                progress.SafeSummary,
                cancellationToken);
    }

    private async Task<T> RunWithProgressHeartbeatAsync<T>(
        Guid installationId,
        string safeSummary,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var task = operation();

        while (!task.IsCompleted)
        {
            var delay = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var completed = await Task.WhenAny(task, delay);
            if (completed == task)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (_progressReporter is not null)
            {
                await _progressReporter.HeartbeatCurrentAsync(
                    installationId,
                    safeSummary,
                    cancellationToken);
            }
        }

        return await task;
    }

    private async Task PersistOutcomeAsync(
        Guid installationId,
        string certificateId,
        int? npmCertificateId,
        CancellationToken cancellationToken)
    {
        var installation = await _db.Installations
            .FirstAsync(x => x.Id == installationId, cancellationToken);
        var plan = ReadPlan(installation.ConfigJson)
            ?? throw new InvalidOperationException("Installation config could not be read while persisting certificate outcome.");

        installation.ConfigJson = JsonSerializer.Serialize(
            plan with
            {
                PublicAccess = plan.PublicAccess with
                {
                    CertificateId = certificateId,
                    NpmCertificateId = npmCertificateId,
                    CertificateValidated = true,
                    ImportedToNpm = npmCertificateId is > 0,
                    LastVerifiedAtUtc = DateTime.UtcNow
                }
            },
            JsonOptions);
        installation.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<InstallStepResult?> TransitionSetupCredentialToDomainRenewalAsync(
        Guid installationId,
        PublicAccessSetupConfig access,
        string certificateId,
        string? providerToken,
        CancellationToken cancellationToken)
    {
        if (access.UseStaging)
        {
            await DeleteProviderCredentialAsync(installationId, cancellationToken);
            return null;
        }

        var token = providerToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            token = await _secretStore.ResolveProtectedAsync(
                installationId,
                InstallationSecretNames.DnsCategory,
                InstallationSecretNames.DesecProviderToken,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return await PipelineWaitingAsync(
                installationId,
                access,
                "RenewalCredentialTransitionCredentialMissing",
                "The reviewed production certificate is ready, but the temporary deSEC credential is unavailable for the automatic-renewal handoff. Re-enter the Domain credential and retry Setup.",
                null,
                cancellationToken);
        }

        var certificate = await _db.Certificates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CertificateId == certificateId, cancellationToken);

        if (certificate is null || certificate.IsStaging)
        {
            return await PipelineWaitingAsync(
                installationId,
                access,
                "RenewalCredentialTransitionCertificateMissing",
                "The production certificate could not be resolved from the Domain registry for the automatic-renewal handoff. The temporary Setup credential was retained.",
                token,
                cancellationToken);
        }

        try
        {
            await _renewalService.ConfigureFromSuccessfulProductionIssuanceAsync(
                certificate.DomainId,
                access.AcmeEmail,
                token,
                cancellationToken);

            var state = await _renewalService.GetAsync(certificate.DomainId, cancellationToken);
            if (state is null ||
                !state.PolicyConfigured ||
                !state.AutoRenewEnabled ||
                !state.CredentialConfigured ||
                string.IsNullOrWhiteSpace(state.AcmeEmail))
            {
                throw new InvalidOperationException(
                    "The Domain renewal foundation could not be verified after protected credential persistence.");
            }

            await DeleteProviderCredentialAsync(installationId, cancellationToken);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Automatic-renewal credential handoff failed. InstallationId={InstallationId} CertificateId={CertificateId}. The temporary Setup credential was retained.",
                installationId,
                certificateId);

            return await PipelineWaitingAsync(
                installationId,
                access,
                "RenewalCredentialPersistenceFailed",
                "The production certificate is ready, but MEM could not verify the protected Domain renewal credential. The temporary Setup credential was retained so the handoff can be retried safely.",
                token,
                cancellationToken);
        }
    }

    private Task DeleteProviderCredentialAsync(Guid installationId, CancellationToken cancellationToken) =>
        _secretStore.DeleteAsync(
            installationId,
            InstallationSecretNames.DnsCategory,
            InstallationSecretNames.DesecProviderToken,
            cancellationToken);

    private async Task<InstallStepResult> PipelineSucceededAsync(
        Guid installationId,
        PublicAccessSetupConfig access,
        string certificateId,
        int? npmCertificateId,
        long startedAt,
        CancellationToken cancellationToken)
    {
        var durationMs = Math.Round(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, 1);
        _logger.LogInformation(
            "Reviewed platform certificate pipeline completed. InstallationId={InstallationId} Zone={Zone} Result={Result} CertificateId={CertificateId} NpmCertificateId={NpmCertificateId} DurationMs={DurationMs}",
            installationId,
            access.Zone,
            "Succeeded",
            certificateId,
            npmCertificateId,
            durationMs);

        await RecordAsync(
            installationId,
            "installation.certificate.pipeline.succeeded",
            MemDiagnosticSeverities.Information,
            "The reviewed platform certificate pipeline completed successfully.",
            access,
            result: "succeeded",
            failureClass: null,
            certificateId,
            npmCertificateId,
            exactSecrets: null);

        var renewalSummary = access.UseStaging
            ? " This staging certificate did not establish production automatic-renewal authority."
            : " The verified deSEC credential was transferred into protected Domain-owned storage for automatic renewal.";

        return new InstallStepResult(
            Succeeded: true,
            Message: $"Wildcard certificate '{access.Domain}' was issued, stored, selected as the main platform certificate, and imported into NPM (certificateId={certificateId}, npmCertificateId={npmCertificateId}).{renewalSummary}");
    }

    private async Task<InstallStepResult> PipelineFailedAsync(
        Guid installationId,
        PublicAccessSetupConfig access,
        string failureClass,
        string detail,
        string? providerToken,
        string? certificateId,
        int? npmCertificateId,
        CancellationToken cancellationToken)
    {
        var failedPhase = FailurePhaseFor(failureClass);
        var certificateReusable =
            string.Equals(failedPhase, "npm-import", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(certificateId);

        _logger.LogWarning(
            "Reviewed platform certificate pipeline failed. InstallationId={InstallationId} Zone={Zone} FailureClass={FailureClass} FailedPhase={FailedPhase} CertificateId={CertificateId} NpmCertificateId={NpmCertificateId} CertificateReusable={CertificateReusable}",
            installationId,
            access.Zone,
            failureClass,
            failedPhase,
            certificateId,
            npmCertificateId,
            certificateReusable);

        await RecordAsync(
            installationId,
            "installation.certificate.pipeline.failed",
            MemDiagnosticSeverities.Error,
            "The reviewed platform certificate pipeline failed.",
            access,
            result: "failed",
            failureClass,
            certificateId,
            npmCertificateId,
            exactSecrets: string.IsNullOrWhiteSpace(providerToken) ? null : [providerToken]);

        return Failed(
            FailureMessage(failureClass, certificateReusable),
            FailureDetail(failureClass, detail, certificateReusable));
    }

    private async Task<InstallStepResult> PipelineWaitingAsync(
        Guid installationId,
        PublicAccessSetupConfig access,
        string failureClass,
        string detail,
        string? providerToken,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Reviewed platform certificate pipeline requires operator action. InstallationId={InstallationId} Zone={Zone} FailureClass={FailureClass}",
            installationId,
            access.Zone,
            failureClass);

        await RecordAsync(
            installationId,
            "installation.certificate.pipeline.action_required",
            MemDiagnosticSeverities.Warning,
            "The reviewed platform certificate pipeline requires operator action.",
            access,
            result: "waiting-for-user",
            failureClass,
            certificateId: null,
            npmCertificateId: null,
            exactSecrets: string.IsNullOrWhiteSpace(providerToken) ? null : [providerToken]);

        return new InstallStepResult(
            Succeeded: false,
            Message: "Certificate installation needs updated Domain credentials or plan details.",
            ErrorMessage: $"{failureClass}: {detail}".Trim(),
            StepStatus: InstallationStepStatuses.WaitingForUser);
    }

    private Task<MemDiagnosticWriteResult?> RecordAsync(
        Guid installationId,
        string eventCode,
        string severity,
        string message,
        PublicAccessSetupConfig access,
        string result,
        string? failureClass,
        string? certificateId,
        int? npmCertificateId,
        IReadOnlyCollection<string>? exactSecrets) =>
        _diagnostics.TryWriteWorkflowEventAsync(new MemDiagnosticWriteRequest(
            Severity: severity,
            EventCode: eventCode,
            Source: "api.install-certificate",
            Feature: "installation",
            Stage: "platform-certificate",
            Message: message,
            CreateIncident: false,
            Resource: new MemDiagnosticResource(
                Kind: "installation",
                Id: installationId.ToString("D"),
                DisplayName: "MEM installation",
                WorkspacePath: "/setup"),
            Observed: new Dictionary<string, string?>
            {
                ["result"] = result
            },
            Details: new Dictionary<string, string?>
            {
                ["zone"] = access.Zone,
                ["domain"] = access.Domain,
                ["provider"] = access.DnsProvider,
                ["certificateEnvironment"] = access.UseStaging ? "staging" : "production",
                ["failureClass"] = failureClass,
                ["failedPhase"] = string.IsNullOrWhiteSpace(failureClass)
                    ? null
                    : FailurePhaseFor(failureClass),
                ["certificateReusable"] = string.IsNullOrWhiteSpace(certificateId)
                    ? null
                    : (string.Equals(FailurePhaseFor(failureClass), "npm-import", StringComparison.Ordinal)
                        ? "true"
                        : "false"),
                ["certificateId"] = certificateId,
                ["npmCertificateId"] = npmCertificateId?.ToString(),
                ["mutationBoundary"] = "post-review-installation"
            },
            Retryable: !string.Equals(result, "succeeded", StringComparison.OrdinalIgnoreCase),
            ExactSecrets: exactSecrets));

    private static string FailurePhaseFor(string? failureClass) =>
        failureClass switch
        {
            "NpmCertificateImportFailed" => "npm-import",
            "NpmCertificateIdMissing" => "npm-import",
            "DesecAuthenticationFailed" => "dns-provider",
            "DesecAccessDenied" => "dns-provider",
            "DesecZoneOrRecordNotFound" => "dns-challenge",
            "DesecZoneNotAccessible" => "dns-challenge",
            "DnsProviderCredentialMissing" => "dns-provider",
            "DesecRequestTimedOut" => "dns-provider",
            "RenewalCredentialTransitionCredentialMissing" => "renewal-credential",
            "RenewalCredentialTransitionCertificateMissing" => "renewal-credential",
            "RenewalCredentialPersistenceFailed" => "renewal-credential",
            "CertificateRegistryMissing" => "certificate-persistence",
            "CertificateIdMissing" => "certificate-persistence",
            _ when failureClass?.Contains("Dns", StringComparison.OrdinalIgnoreCase) == true => "dns-challenge",
            _ when failureClass?.Contains("Acme", StringComparison.OrdinalIgnoreCase) == true => "acme-issuance",
            _ when failureClass?.Contains("Certificate", StringComparison.OrdinalIgnoreCase) == true => "certificate-issuance",
            _ => "certificate-pipeline"
        };

    private static string FailureMessage(string failureClass, bool certificateReusable)
    {
        if (string.Equals(failureClass, "NpmCertificateImportFailed", StringComparison.Ordinal))
        {
            return certificateReusable
                ? "Wildcard certificate is ready, but NPM import failed."
                : "Certificate import into NPM failed.";
        }

        return failureClass switch
        {
            "AuthoritativeDnsVisibilityTimedOut" => "DNS challenge did not reach all authoritative DNS servers in time.",
            "AuthoritativeDnsServersUnavailable" => "Authoritative DNS visibility could not be confirmed.",
            "PublicDnsVisibilityTimedOut" => "DNS challenge did not become publicly visible in time.",
            "PublicDnsResolversUnavailable" => "Public DNS visibility could not be confirmed.",
            "DnsChallengeVisibilityNotStable" => "DNS challenge visibility did not remain stable long enough.",
            "DesecRequestTimedOut" => "The DNS provider request timed out.",
            "AcmeChallengeValidationTimedOut" => "Let's Encrypt validation did not complete in time.",
            "AcmeSecondaryDnsValidationNotConverged" => "Let's Encrypt secondary DNS validation still could not observe the challenge after MEM's bounded recovery attempt.",
            "AcmeDnsValidationAttemptsExhausted" => "Let's Encrypt DNS validation exhausted MEM's bounded automatic recovery policy.",
            "AcmeOrderFinalizationTimedOut" => "Let's Encrypt certificate finalization did not complete in time.",
            _ => "Platform certificate preparation failed."
        };
    }

    private static string FailureDetail(
        string failureClass,
        string detail,
        bool certificateReusable)
    {
        var normalized = $"{failureClass}: {detail}".Trim();

        if (!certificateReusable)
        {
            return normalized;
        }

        return normalized +
               " The wildcard certificate is already stored by MEM and will be reused when Setup is retried; a new certificate request is not required.";
    }

    private static bool RequiresPlanRepair(string? errorCode) =>
        errorCode is "DesecTokenMissing" or
            "DesecAuthenticationFailed" or
            "DesecAccessDenied" or
            "DesecZoneOrRecordNotFound";

    private static string? EvidenceValue(CertificateOperationResult result, string key) =>
        result.Evidence
            .FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static InstallPlan? ReadPlan(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InstallPlan>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static InstallStepResult Failed(string message, string detail) =>
        new(false, message, detail);
}
