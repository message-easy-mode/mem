using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Modules.Integrations.Npm.Services;
using Modules.Operator.Diagnostics.Services;
using Modules.Setup;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;
using Modules.Shared.Docker;
using Shared.ControlPlane.Runtime;
using Shared.Diagnostics;
using Shared.Exceptions;

namespace Modules.Setup.SupportReports;

public sealed class SetupInstallationSupportReportService(
    MemDbContext db,
    DiagnosticsApiOptions diagnosticsOptions,
    IMemDiagnosticEventReader eventReader,
    IMemDiagnosticHealthReader diagnosticHealthReader,
    IMemDiagnosticTextRedactor textRedactor,
    IMemDockerEvidenceReader dockerEvidenceReader,
    MemControlPlaneRuntimeContext runtimeContext,
    IConfiguration configuration,
    SetupInstallationSupportReportSizeLimiter sizeLimiter,
    TimeProvider timeProvider,
    ILogger<SetupInstallationSupportReportService> logger,
    IMemDiagnosticEventWriter? diagnosticWriter = null,
    IMemLocalLogHealthReader? localLogHealthReader = null,
    IControlPlaneDockerOwnershipGuard? ownershipGuard = null,
    IControlPlaneExposureInspector? exposureInspector = null)
{
    private static readonly JsonSerializerOptions PlanJsonOptions =
        new(JsonSerializerDefaults.Web);

    private const string InstallationResourceKind = "installation";
    private const string PlatformNetworkName = "mem-gateway";
    private const string NpmDataVolumeName = "mem_npm_data";
    private const string NpmLetsEncryptVolumeName = "mem_npm_letsencrypt";
    private const int MaximumStepTextCharacters = 4000;
    private const int MaximumEventTextCharacters = 3000;

    public async Task<SetupInstallationSupportReport> GenerateAsync(
        Guid? installationId,
        SetupInstallationSupportReportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var traceId = NormalizeTraceId(request.TraceId);
        var traceEvents = traceId is null
            ? new CollectedDiagnosticEvents([], Truncated: false, [])
            : await CollectAcrossRetentionAsync(
                new MemDiagnosticQuery(TraceId: traceId),
                diagnosticsOptions.SupportReportMaximumEvents,
                cancellationToken);
        warnings.UnionWith(traceEvents.Warnings);

        var inferredInstallationId = installationId ?? InferInstallationId(traceEvents.Events);
        var installation = await ResolveInstallationAsync(
            inferredInstallationId,
            installationIdWasExplicit: installationId is not null,
            useLatestWhenMissing: installationId is null && traceId is null,
            cancellationToken);

        var steps = installation is null
            ? []
            : await db.InstallationStepExecutions
                .AsNoTracking()
                .Where(step => step.InstallationId == installation.Id)
                .OrderBy(step => step.Sequence)
                .ToListAsync(cancellationToken);

        var installationEvents = installation is null
            ? new CollectedDiagnosticEvents([], Truncated: false, [])
            : await CollectInstallationEventsAsync(
                installation.Id,
                cancellationToken);
        warnings.UnionWith(installationEvents.Warnings);

        var events = traceEvents.Events
            .Concat(installationEvents.Events)
            .GroupBy(@event => @event.EventId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(@event => @event.TimestampUtc)
            .ThenByDescending(@event => @event.EventId, StringComparer.Ordinal)
            .Take(diagnosticsOptions.SupportReportMaximumEvents)
            .ToArray();

        var textWasTruncated = false;
        var projectedSteps = steps
            .Select(step => ProjectStep(step, ref textWasTruncated))
            .ToArray();
        var projectedEvents = events
            .Select(@event => ProjectEvent(@event, ref textWasTruncated))
            .ToArray();
        var incidents = ProjectIncidents(events);

        var ownership = ownershipGuard is null
            ? MemDockerOwnershipProjection.Unchecked
            : await ownershipGuard.InspectAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(ownership.WarningCode))
        {
            warnings.Add(ownership.WarningCode);
        }

        SetupSupportDockerEvidence? dockerEvidence = null;
        if (request.IncludeDockerEvidence)
        {
            if (string.Equals(ownership.State, "conflict", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("setup.support_report_docker_ownership_conflict");
            }
            else
            {
                dockerEvidence = await TryReadDockerEvidenceAsync(
                    events,
                    warnings,
                    cancellationToken);
            }
        }
        else
        {
            warnings.Add("setup.support_report_docker_evidence_not_requested");
        }

        var exposure = exposureInspector is null
            ? MemControlPlaneExposureProjection.Unchecked
            : await exposureInspector.InspectAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(exposure.WarningCode))
        {
            warnings.Add(exposure.WarningCode);
        }

        var runtimeProjection = runtimeContext.ToSafeProjection() with
        {
            MutationsAllowed = runtimeContext.MutationsAllowed && ownership.MutationsAllowed,
            DockerOwnership = ownership,
            ControlPlaneExposure = exposure
        };

        var safeLastError = ProjectText(installation?.LastError, MaximumStepTextCharacters);
        textWasTruncated |= safeLastError.Truncated;

        var plan = ReadPlan(installation);
        var handoffStep = steps.FirstOrDefault(step =>
            string.Equals(
                step.StepName,
                InstallStepNames.CompleteSetupHandoff,
                StringComparison.Ordinal));
        var report = new SetupInstallationSupportReport(
            SchemaVersion: 1,
            GeneratedAtUtc: timeProvider.GetUtcNow(),
            MemVersion: runtimeContext.Version,
            Selection: BuildSelection(installationId, installation, traceId),
            RuntimeContext: runtimeProjection,
            Setup: installation is null
                ? null
                : new SetupSupportLifecycle(
                    installation.Id,
                    installation.Status,
                    DetermineStage(installation, steps),
                    SetupUtcDateTime.ToOffset(installation.CreatedAtUtc),
                    SetupUtcDateTime.ToOffset(installation.UpdatedAtUtc),
                    SetupUtcDateTime.ToOffset(installation.StartedAtUtc),
                    SetupUtcDateTime.ToOffset(installation.CompletedAtUtc),
                    DetermineHandoffState(handoffStep),
                    EmptyToNull(safeLastError.Value)),
            ReviewedPlan: ProjectReviewedPlan(plan),
            ServerChecks: ProjectServerChecks(plan?.Preflight),
            Steps: projectedSteps,
            Diagnostics: new SetupSupportDiagnostics(
                ProjectLoggingHealth(warnings),
                projectedEvents,
                incidents,
                dockerEvidence),
            Recovery: ProjectRecovery(installation, steps),
            Redaction: new SetupSupportRedaction(
                PolicyVersion: "mem-diagnostics-redaction-v1",
                RedactionsApplied: true,
                OmittedContent:
                [
                    "DNS provider tokens are omitted.",
                    "Passwords and credentials are omitted.",
                    "Private keys are omitted.",
                    "Raw authorization headers and cookies are omitted.",
                    "Recovery codes and setup codes are omitted.",
                    "Connection strings are omitted.",
                    "Complete container environment arrays are omitted.",
                    "Unrestricted command output and raw CLEF files are omitted."
                ]),
            Truncated: textWasTruncated ||
                       traceEvents.Truncated ||
                       installationEvents.Truncated ||
                       dockerEvidence?.LogTail?.Truncated == true ||
                       events.Length >= diagnosticsOptions.SupportReportMaximumEvents,
            Warnings: warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray());

        report = sizeLimiter.Apply(report);

        await WriteAuditEventAsync(
            installation,
            traceId,
            request,
            report,
            cancellationToken);

        logger.LogInformation(
            "MEM setup installation support report generated. InstallationId={InstallationId} Selection={Selection} EventCount={EventCount} IncludeDockerEvidence={IncludeDockerEvidence} Truncated={Truncated}",
            report.Setup?.InstallationId,
            report.Selection.Mode,
            report.Diagnostics.Events.Count,
            request.IncludeDockerEvidence,
            report.Truncated);

        return report;
    }

    private async Task<InstallationEntity?> ResolveInstallationAsync(
        Guid? installationId,
        bool installationIdWasExplicit,
        bool useLatestWhenMissing,
        CancellationToken cancellationToken)
    {
        if (installationId is not null)
        {
            var installation = await db.Installations
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    value => value.Id == installationId.Value,
                    cancellationToken);
            if (installation is null && installationIdWasExplicit)
            {
                throw new MemProblemException(
                    StatusCodes.Status404NotFound,
                    "setup_support_report_installation_not_found",
                    "The installation support report could not be generated",
                    "The requested installation was not found.",
                    createIncident: false,
                    retryable: false,
                    feature: "setup",
                    stage: "support-report");
            }

            return installation;
        }

        if (!useLatestWhenMissing)
        {
            return null;
        }

        return await db.Installations
            .AsNoTracking()
            .OrderByDescending(value => value.UpdatedAtUtc)
            .ThenByDescending(value => value.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<CollectedDiagnosticEvents> CollectInstallationEventsAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var scanLimit = Math.Min(
            4000,
            Math.Max(
                diagnosticsOptions.SupportReportMaximumEvents,
                diagnosticsOptions.SupportReportMaximumEvents * 4));
        var scanned = await CollectAcrossRetentionAsync(
            new MemDiagnosticQuery(ResourceKind: InstallationResourceKind),
            scanLimit,
            cancellationToken);
        var installationIdText = installationId.ToString();
        var filtered = scanned.Events
            .Where(@event =>
                string.Equals(
                    @event.Resource?.Kind,
                    InstallationResourceKind,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    @event.Resource?.Id,
                    installationIdText,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(@event => @event.TimestampUtc)
            .Take(diagnosticsOptions.SupportReportMaximumEvents)
            .ToArray();

        return new CollectedDiagnosticEvents(
            filtered,
            scanned.Truncated || filtered.Length >= diagnosticsOptions.SupportReportMaximumEvents,
            scanned.Warnings);
    }

    private async Task<CollectedDiagnosticEvents> CollectAcrossRetentionAsync(
        MemDiagnosticQuery template,
        int maximumEvents,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var earliest = now.AddDays(-diagnosticsOptions.RetentionDays);
        var until = now;
        var events = new List<MemDiagnosticEvent>(Math.Min(maximumEvents, 256));
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var truncated = false;

        while (until > earliest && events.Count < maximumEvents)
        {
            var from = until.AddHours(-diagnosticsOptions.MaximumQueryWindowHours);
            if (from < earliest)
            {
                from = earliest;
            }

            var query = template with
            {
                FromUtc = from,
                UntilUtc = until,
                Cursor = null,
                PageSize = Math.Min(
                    diagnosticsOptions.MaximumPageSize,
                    maximumEvents - events.Count)
            };

            while (events.Count < maximumEvents)
            {
                var page = await eventReader.QueryAsync(query, cancellationToken);
                events.AddRange(page.Events.Take(maximumEvents - events.Count));
                warnings.UnionWith(page.Warnings);

                if (string.IsNullOrWhiteSpace(page.NextCursor))
                {
                    break;
                }

                if (events.Count >= maximumEvents)
                {
                    truncated = true;
                    break;
                }

                query = query with { Cursor = page.NextCursor };
            }

            if (events.Count >= maximumEvents)
            {
                truncated = true;
                break;
            }

            until = from;
        }

        return new CollectedDiagnosticEvents(
            events
                .GroupBy(@event => @event.EventId, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderByDescending(@event => @event.TimestampUtc)
                .ThenByDescending(@event => @event.EventId, StringComparer.Ordinal)
                .Take(maximumEvents)
                .ToArray(),
            truncated,
            warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private async Task<SetupSupportDockerEvidence?> TryReadDockerEvidenceAsync(
        IReadOnlyList<MemDiagnosticEvent> events,
        ISet<string> warnings,
        CancellationToken cancellationToken)
    {
        var incidentId = events
            .Where(@event => !string.IsNullOrWhiteSpace(@event.IncidentId))
            .OrderByDescending(@event => @event.TimestampUtc)
            .Select(@event => @event.IncidentId)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(incidentId))
        {
            warnings.Add("setup.support_report_docker_evidence_no_incident");
            return null;
        }

        var incidentEvents = await CollectAcrossRetentionAsync(
            new MemDiagnosticQuery(IncidentId: incidentId),
            diagnosticsOptions.IncidentMaximumEvents,
            cancellationToken);
        warnings.UnionWith(incidentEvents.Warnings);
        var result = await dockerEvidenceReader.ReadForIncidentAsync(
            incidentId,
            incidentEvents.Events,
            cancellationToken);
        warnings.UnionWith(result.Warnings);

        if (!result.Available || result.Evidence is null)
        {
            if (!string.IsNullOrWhiteSpace(result.WarningCode))
            {
                warnings.Add(result.WarningCode);
            }

            return new SetupSupportDockerEvidence(
                Available: false,
                WarningCode: result.WarningCode,
                ResourceKind: null,
                ResourceId: null,
                ObservedAtUtc: null,
                Container: null,
                LogTail: null,
                Warnings: result.Warnings);
        }

        var evidence = result.Evidence;

        return new SetupSupportDockerEvidence(
            Available: true,
            WarningCode: result.WarningCode,
            ResourceKind: evidence.Resource.Kind,
            ResourceId: evidence.Resource.Id,
            ObservedAtUtc: evidence.ObservedAtUtc,
            Container: new SetupSupportDockerContainer(
                evidence.Container.LogicalName,
                evidence.Container.ObservedState,
                evidence.Container.ExitCode,
                evidence.Container.Health,
                evidence.Container.StartedAtUtc,
                evidence.Container.FinishedAtUtc,
                evidence.Container.RestartCount,
                evidence.Container.Image),
            LogTail: new SetupSupportDockerLogTail(
                evidence.LogTail.RequestedLines,
                evidence.LogTail.ReturnedLines,
                evidence.LogTail.MaximumCharacters,
                evidence.LogTail.Content,
                evidence.LogTail.Truncated,
                evidence.LogTail.RedactionsApplied),
            Warnings: result.Warnings
                .Concat(evidence.Warnings)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
    }

    private SetupSupportInstallStep ProjectStep(
        InstallationStepExecutionEntity step,
        ref bool textWasTruncated)
    {
        var message = ProjectText(step.Message, MaximumStepTextCharacters);
        var error = ProjectText(step.ErrorMessage, MaximumStepTextCharacters);
        textWasTruncated |= message.Truncated || error.Truncated;

        return new SetupSupportInstallStep(
            step.Sequence,
            step.StepName,
            step.Status,
            step.AttemptCount,
            SetupUtcDateTime.ToOffset(step.StartedAtUtc),
            SetupUtcDateTime.ToOffset(step.CompletedAtUtc),
            EmptyToNull(message.Value),
            EmptyToNull(error.Value),
            message.RedactionsApplied || error.RedactionsApplied,
            message.Truncated || error.Truncated);
    }

    private SetupSupportDiagnosticEvent ProjectEvent(
        MemDiagnosticEvent source,
        ref bool textWasTruncated)
    {
        var message = ProjectText(source.Message, MaximumEventTextCharacters);
        var action = ProjectText(source.SuggestedAction, 1200);
        textWasTruncated |= message.Truncated || action.Truncated;

        return new SetupSupportDiagnosticEvent(
            source.EventId,
            source.TimestampUtc,
            source.Severity,
            source.EventCode,
            source.Source,
            source.Feature,
            source.Stage,
            message.Value,
            source.IncidentId,
            source.TraceId,
            source.CorrelationId,
            EmptyToNull(action.Value),
            source.Retryable,
            source.RedactionsApplied || message.RedactionsApplied || action.RedactionsApplied,
            source.Truncated || message.Truncated || action.Truncated);
    }

    private static IReadOnlyList<SetupSupportIncident> ProjectIncidents(
        IEnumerable<MemDiagnosticEvent> events) =>
        events
            .Where(@event => !string.IsNullOrWhiteSpace(@event.IncidentId))
            .GroupBy(@event => @event.IncidentId!, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(@event => @event.TimestampUtc)
                .Select(@event => new SetupSupportIncident(
                    group.Key,
                    @event.Severity,
                    @event.EventCode,
                    @event.Stage,
                    @event.TimestampUtc,
                    @event.TraceId,
                    @event.Retryable))
                .First())
            .OrderByDescending(value => value.LastSeenAtUtc)
            .ToArray();

    private SetupSupportLoggingHealth ProjectLoggingHealth(ISet<string> warnings)
    {
        var store = diagnosticHealthReader.GetHealth();
        var local = localLogHealthReader?.GetHealth();
        foreach (var code in new[]
                 {
                     store.LastWriteErrorCode,
                     store.LastReadWarningCode,
                     store.LastRetentionErrorCode,
                     store.Storage?.WarningCode,
                     local?.WarningCode,
                     local?.Storage?.WarningCode
                 })
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                warnings.Add(code);
            }
        }

        var status = string.Equals(store.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
                     (local is null || string.Equals(local.Status, "ready", StringComparison.OrdinalIgnoreCase))
            ? "ready"
            : "degraded";

        return new SetupSupportLoggingHealth(
            Status: status,
            LocalRecorderStatus: local?.Status,
            SafeEventStoreStatus: store.Status,
            SafeEventStoreLastWriteAtUtc: store.LastWriteAtUtc,
            SafeEventStoreEventCount: store.StoredEventCount,
            Warnings: warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private SetupSupportReviewedPlan? ProjectReviewedPlan(InstallPlan? plan)
    {
        if (plan is null)
        {
            return null;
        }

        var containers = new List<string>();
        if (plan.Platform.Postgres.Enabled)
        {
            containers.Add(plan.Platform.Postgres.ContainerName);
        }
        if (plan.Platform.Ingress.Enabled)
        {
            containers.Add(plan.Platform.Ingress.ContainerName);
        }
        containers.Add(Modules.Setup.Platform.Coturn.PlatformCoturnSetupDefaults.ContainerName);
        if (plan.Platform.MemApi.Enabled)
        {
            containers.Add(plan.Platform.MemApi.ContainerName);
        }
        if (plan.Platform.MemWeb.Enabled)
        {
            containers.Add(plan.Platform.MemWeb.ContainerName);
        }

        var services = new List<string>();
        if (plan.Platform.Postgres.Enabled) services.Add("PostgreSQL");
        if (plan.Platform.Ingress.Enabled) services.Add("Nginx Proxy Manager");
        services.Add("Coturn (TURN)");
        if (plan.SupportTools.Seq.Enabled) services.Add("Seq");
        if (plan.SupportTools.PgAdmin.Enabled) services.Add("pgAdmin");
        if (plan.SupportTools.Portainer.Enabled) services.Add("Portainer");

        var supportTools = new List<string>();
        if (plan.SupportTools.Seq.Enabled) supportTools.Add("Seq");
        if (plan.SupportTools.PgAdmin.Enabled) supportTools.Add("pgAdmin");
        if (plan.SupportTools.Portainer.Enabled) supportTools.Add("Portainer");

        var volumes = new HashSet<string>(StringComparer.Ordinal);
        if (plan.Platform.Postgres.UseDockerVolume &&
            !string.IsNullOrWhiteSpace(plan.Platform.Postgres.VolumeName))
        {
            volumes.Add(plan.Platform.Postgres.VolumeName);
        }
        if (plan.Platform.Ingress.Enabled)
        {
            volumes.Add(NpmDataVolumeName);
            volumes.Add(NpmLetsEncryptVolumeName);
        }

        var ports = new Dictionary<string, int>(StringComparer.Ordinal);
        if (plan.Platform.Ingress.Enabled)
        {
            ports["npm.http"] = plan.Platform.Ingress.HttpPort;
            ports["npm.https"] = plan.Platform.Ingress.HttpsPort;
            ports["npm.admin"] = plan.Platform.Ingress.AdminPort;
        }
        ports["coturn.turn"] = Modules.Setup.Platform.Coturn.PlatformCoturnSetupDefaults.TurnPort;
        ports["coturn.relayMinUdp"] = Modules.Setup.Platform.Coturn.PlatformCoturnSetupDefaults.RelayMinPort;
        ports["coturn.relayMaxUdp"] = Modules.Setup.Platform.Coturn.PlatformCoturnSetupDefaults.RelayMaxPort;
        if (plan.SupportTools.Seq.Enabled) ports["seq"] = plan.SupportTools.Seq.HostPort;
        if (plan.SupportTools.PgAdmin.Enabled) ports["pgadmin"] = plan.SupportTools.PgAdmin.HostPort;
        if (plan.SupportTools.Portainer.Enabled) ports["portainer"] = plan.SupportTools.Portainer.HostPort;

        var approvedImages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nginxProxyManager"] = NpmRuntimeRelease.ApprovedImage
        };
        var postgresImage = configuration["RuntimeImages:Postgres:ApprovedReference"]?.Trim();
        if (!string.IsNullOrWhiteSpace(postgresImage))
        {
            approvedImages["postgres"] = postgresImage;
        }
        var coturnImage = configuration["RuntimeImages:Coturn:ApprovedReference"]?.Trim();
        if (!string.IsNullOrWhiteSpace(coturnImage))
        {
            approvedImages["coturn"] = coturnImage;
        }

        return new SetupSupportReviewedPlan(
            Fingerprint: plan.Review?.PlanSha256,
            ReviewedAtUtc: SetupUtcDateTime.ToOffset(plan.Review?.AcceptedAtUtc),
            BaseDomain: EmptyToNull(plan.PublicAccess.Domain),
            WildcardCertificate: string.IsNullOrWhiteSpace(plan.PublicAccess.Domain)
                ? null
                : $"*.{plan.PublicAccess.Domain.Trim().TrimStart('*', '.')}".ToLowerInvariant(),
            DnsZone: EmptyToNull(plan.PublicAccess.Zone),
            DnsProvider: EmptyToNull(plan.PublicAccess.DnsProvider),
            AcmeEnvironment: plan.PublicAccess.UseStaging ? "staging" : "production",
            Services: services,
            ApprovedImages: approvedImages,
            Containers: containers.Distinct(StringComparer.Ordinal).ToArray(),
            Networks: [PlatformNetworkName],
            Volumes: volumes.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Ports: ports,
            SupportTools: supportTools,
            VerificationPlan:
            [
                "install.run",
                "platform.resources",
                "ingress.npm.ready",
                "certificate.valid",
                "ingress.certificate.usable",
                "install.steps"
            ],
            SecretsOmitted: true);
    }

    private static SetupSupportServerChecks? ProjectServerChecks(PreflightSetupConfig? preflight) =>
        preflight is null
            ? null
            : new SetupSupportServerChecks(
                preflight.RunId,
                preflight.CompletedAtUtc,
                preflight.RunStatus,
                preflight.Passed,
                preflight.Warnings,
                preflight.Failed,
                preflight.Skipped,
                preflight.Unavailable,
                preflight.Unknown,
                preflight.BlockingIssueCount,
                preflight.WarningCheckKeys,
                preflight.UnavailableCheckKeys);

    private SetupSupportRecovery ProjectRecovery(
        InstallationEntity? installation,
        IReadOnlyList<InstallationStepExecutionEntity> steps)
    {
        if (installation is null)
        {
            return new SetupSupportRecovery(
                RetryAllowed: false,
                RetryScope: "not-applicable",
                RequiredOperatorAction: "Review the trace-linked setup evidence and restore normal Setup access before retrying.",
                SuggestedRoute: "/setup/start",
                ProvenComplete: [],
                StateUncertain: []);
        }

        var firstAttention = steps.FirstOrDefault(step =>
            step.Status is InstallationStepStatuses.Running or
                InstallationStepStatuses.WaitingForUser or
                InstallationStepStatuses.Failed);
        var proven = steps
            .Where(step => step.Status is InstallationStepStatuses.Succeeded or InstallationStepStatuses.Skipped)
            .Select(step => step.StepName)
            .ToArray();
        var uncertain = steps
            .Where(step => step.Status is InstallationStepStatuses.Running or
                InstallationStepStatuses.WaitingForUser or
                InstallationStepStatuses.Failed or
                InstallationStepStatuses.Pending)
            .Select(step => step.StepName)
            .ToArray();

        var requiredAction = firstAttention is null
            ? null
            : ProjectText(
                firstAttention.ErrorMessage ?? firstAttention.Message,
                MaximumStepTextCharacters).Value;

        if (string.Equals(installation.Status, InstallationStatuses.Succeeded, StringComparison.OrdinalIgnoreCase))
        {
            var handoff = steps.FirstOrDefault(step =>
                string.Equals(step.StepName, InstallStepNames.CompleteSetupHandoff, StringComparison.Ordinal));
            var handoffPending = handoff is not null &&
                string.Equals(handoff.Status, InstallationStepStatuses.WaitingForUser, StringComparison.OrdinalIgnoreCase);
            return new SetupSupportRecovery(
                RetryAllowed: false,
                RetryScope: "none",
                RequiredOperatorAction: handoffPending ? "Finish setup to acknowledge the operator handoff." : null,
                SuggestedRoute: handoffPending
                    ? $"/setup/handoff/{installation.Id}"
                    : "/dashboard",
                ProvenComplete: proven,
                StateUncertain: uncertain);
        }

        var retryAllowed = string.Equals(installation.Status, InstallationStatuses.Failed, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(installation.Status, InstallationStatuses.WaitingForUser, StringComparison.OrdinalIgnoreCase);
        var route = installation.Status switch
        {
            InstallationStatuses.Draft => "/setup/start",
            InstallationStatuses.Ready => "/setup/review",
            _ => $"/setup/install/{installation.Id}"
        };

        return new SetupSupportRecovery(
            RetryAllowed: retryAllowed,
            RetryScope: retryAllowed ? "failed-or-waiting-step-and-later" : "server-owned-worker",
            RequiredOperatorAction: EmptyToNull(requiredAction),
            SuggestedRoute: route,
            ProvenComplete: proven,
            StateUncertain: uncertain);
    }

    private async Task WriteAuditEventAsync(
        InstallationEntity? installation,
        string? traceId,
        SetupInstallationSupportReportRequest request,
        SetupInstallationSupportReport report,
        CancellationToken cancellationToken)
    {
        var resource = installation is null
            ? null
            : new MemDiagnosticResource(
                InstallationResourceKind,
                installation.Id.ToString(),
                "MEM installation",
                WorkspacePath: $"/setup/install/{installation.Id}");
        var result = await diagnosticWriter.TryWriteWorkflowEventAsync(
            new MemDiagnosticWriteRequest(
                Severity: MemDiagnosticSeverities.Information,
                EventCode: "setup.install_support_report.generated",
                Source: "setup-support-report",
                Feature: "setup",
                Stage: "support-report",
                Message: "A bounded Setup installation support report was generated.",
                Resource: resource,
                Details: new Dictionary<string, string?>
                {
                    ["format"] = SetupSupportReportFormats.Normalize(request.Format),
                    ["dockerEvidenceRequested"] = request.IncludeDockerEvidence.ToString(),
                    ["selection"] = report.Selection.Mode,
                    ["truncated"] = report.Truncated.ToString()
                },
                Context: string.IsNullOrWhiteSpace(traceId)
                    ? null
                    : new MemDiagnosticContext(TraceId: traceId)));
        if (result is { Stored: false, WarningCode: not null })
        {
            logger.LogWarning(
                "MEM could not persist the setup support-report audit event. WarningCode={WarningCode}",
                result.WarningCode);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private InstallPlan? ReadPlan(InstallationEntity? installation)
    {
        var json = installation?.FrozenConfigJson ?? installation?.ConfigJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InstallPlan>(json, PlanJsonOptions);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "MEM could not parse the installation plan while generating a support report. InstallationId={InstallationId}",
                installation?.Id);
            return null;
        }
    }

    private MemDiagnosticSafeText ProjectText(string? value, int maximumCharacters) =>
        textRedactor.Project(value, maximumCharacters, sanitizeSourcePaths: true);

    private static SetupSupportReportSelection BuildSelection(
        Guid? explicitInstallationId,
        InstallationEntity? installation,
        string? traceId)
    {
        var mode = explicitInstallationId is not null
            ? "installation-id"
            : traceId is not null
                ? installation is null ? "trace-id" : "trace-id-with-installation"
                : installation is null ? "none" : "latest";
        return new SetupSupportReportSelection(mode, installation?.Id, traceId);
    }

    private static Guid? InferInstallationId(IEnumerable<MemDiagnosticEvent> events)
    {
        foreach (var @event in events.OrderByDescending(value => value.TimestampUtc))
        {
            if (string.Equals(
                    @event.Resource?.Kind,
                    InstallationResourceKind,
                    StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(@event.Resource?.Id, out var id))
            {
                return id;
            }
        }

        return null;
    }

    private static string DetermineStage(
        InstallationEntity installation,
        IReadOnlyList<InstallationStepExecutionEntity> steps)
    {
        var active = steps.FirstOrDefault(step =>
            step.Status is InstallationStepStatuses.Running or
                InstallationStepStatuses.WaitingForUser or
                InstallationStepStatuses.Failed);
        if (active is not null)
        {
            return active.StepName;
        }

        if (string.Equals(installation.Status, InstallationStatuses.Draft, StringComparison.OrdinalIgnoreCase))
        {
            return "Plan setup";
        }
        if (string.Equals(installation.Status, InstallationStatuses.Ready, StringComparison.OrdinalIgnoreCase))
        {
            return "Review accepted; installation ready";
        }
        if (string.Equals(installation.Status, InstallationStatuses.Succeeded, StringComparison.OrdinalIgnoreCase))
        {
            return "Installation and verification complete";
        }

        return installation.Status;
    }

    private static string DetermineHandoffState(InstallationStepExecutionEntity? handoffStep)
    {
        if (handoffStep is null)
        {
            return "not-recorded";
        }

        return handoffStep.Status switch
        {
            InstallationStepStatuses.Succeeded => "completed",
            InstallationStepStatuses.WaitingForUser => "waiting-for-user",
            InstallationStepStatuses.Pending => "pending",
            _ => handoffStep.Status.ToLowerInvariant()
        };
    }

    private static void ValidateRequest(SetupInstallationSupportReportRequest request)
    {
        if (!SetupSupportReportFormats.IsSupported(request.Format))
        {
            throw new MemProblemException(
                StatusCodes.Status400BadRequest,
                "setup_support_report_format_invalid",
                "The installation support report format is invalid",
                "format must be either 'json' or 'text'.",
                createIncident: false,
                retryable: false,
                feature: "setup",
                stage: "support-report");
        }

        _ = NormalizeTraceId(request.TraceId);
    }

    private static string? NormalizeTraceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > 128 ||
            normalized.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new MemProblemException(
                StatusCodes.Status400BadRequest,
                "setup_support_report_trace_invalid",
                "The installation support report trace reference is invalid",
                "traceId must be a bounded technical trace reference.",
                createIncident: false,
                retryable: false,
                feature: "setup",
                stage: "support-report");
        }

        return normalized;
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record CollectedDiagnosticEvents(
        IReadOnlyList<MemDiagnosticEvent> Events,
        bool Truncated,
        IReadOnlyList<string> Warnings);
}
