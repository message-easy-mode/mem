using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Modules.Auth.Services.Identity;
using Modules.Operator.Diagnostics.Contracts;
using Modules.Shared.Docker;
using Shared.ControlPlane.Runtime;
using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsSupportReportService(
    DiagnosticsIncidentService incidents,
    DiagnosticsLoggingHealthService loggingHealth,
    IMemDockerEvidenceReader dockerEvidenceReader,
    DiagnosticsApiOptions options,
    DiagnosticsSupportReportSizeLimiter sizeLimiter,
    MemControlPlaneRuntimeContext runtimeContext,
    IMemOperatorAuditService audit,
    ILogger<DiagnosticsSupportReportService> logger,
    TimeProvider timeProvider,
    IControlPlaneDockerOwnershipGuard? ownershipGuard = null,
    IControlPlaneExposureInspector? exposureInspector = null)
{
    public async Task<DiagnosticsSupportReport> GenerateAsync(
        DiagnosticsSupportReportRequest request,
        ClaimsPrincipal principal,
        Guid? operatorId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);

        var loaded = await incidents.LoadForReportAsync(
            request.IncidentId,
            cancellationToken);
        var events = loaded.Events
            .Take(options.SupportReportMaximumEvents)
            .Select(@event => DiagnosticsProjection.Event(
                @event,
                includeOwnerFacts: false))
            .ToArray();
        var warnings = new HashSet<string>(loaded.Warnings, StringComparer.Ordinal);
        var omissions = new List<string>
        {
            "Raw Serilog files are not included.",
            "Raw runtime-operation input, result, evidence, and error fields are not included.",
            "Credentials, tokens, private keys, connection strings, and unrestricted command output are not included.",
            "Host filesystem paths from MEM records are not included; bounded Docker output is sanitized but may retain application-emitted operational metadata."
        };

        DiagnosticsDockerEvidenceResponse? dockerEvidence = null;
        if (request.IncludeDockerEvidence)
        {
            var evidenceResult = await dockerEvidenceReader.ReadForIncidentAsync(
                loaded.Summary.IncidentId,
                loaded.Events,
                cancellationToken);
            dockerEvidence = DiagnosticsDockerEvidenceService.Project(evidenceResult);
            warnings.UnionWith(dockerEvidence.Warnings);
            if (!dockerEvidence.Available)
            {
                warnings.UnionWith(dockerEvidence.Warnings);
                if (!string.IsNullOrWhiteSpace(dockerEvidence.WarningCode))
                {
                    warnings.Add(dockerEvidence.WarningCode);
                }

                omissions.Add("Docker evidence was requested but was not available for this incident.");
            }
        }
        else
        {
            omissions.Add("Docker evidence was not requested.");
        }

        try
        {
            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "diagnostics.support-report.generated",
                    Outcome: "succeeded",
                    ActorOperatorId: operatorId,
                    CorrelationId: NormalizeCorrelationId(correlationId),
                    ReasonCode: loaded.Summary.IncidentId),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            warnings.Add("diagnostics.support_report_audit_failed");
            logger.LogWarning(
                ex,
                "MEM could not persist the support-report audit event. IncidentId={IncidentId}",
                loaded.Summary.IncidentId);
        }

        var reportHealth = loggingHealth.Get(principal);
        reportHealth = reportHealth with
        {
            LocalRecorder = reportHealth.LocalRecorder with
            {
                PersistentFilePath = null
            },
            Seq = reportHealth.Seq with
            {
                ServerUrl = null
            }
        };

        var ownership = ownershipGuard is null
            ? MemDockerOwnershipProjection.Unchecked
            : await ownershipGuard.InspectAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(ownership.WarningCode))
        {
            warnings.Add(ownership.WarningCode);
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

        var report = new DiagnosticsSupportReport(
            SchemaVersion: 1,
            GeneratedAtUtc: timeProvider.GetUtcNow(),
            MemVersion: runtimeContext.Version,
            RuntimeContext: runtimeProjection,
            Incident: loaded.Summary,
            Events: events,
            Operations: loaded.Operations,
            LoggingHealth: reportHealth,
            DockerEvidence: dockerEvidence,
            Redaction: new DiagnosticsSupportReportRedaction(
                PolicyVersion: "mem-diagnostics-redaction-v1",
                RedactionsApplied: true,
                OmittedContent: omissions.Distinct(StringComparer.Ordinal).ToArray()),
            Truncated: loaded.Truncated ||
                       loaded.Events.Count > options.SupportReportMaximumEvents,
            Warnings: warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray());

        report = sizeLimiter.Apply(report);

        logger.LogInformation(
            "MEM diagnostics support report generated. IncidentId={IncidentId} EventCount={EventCount} OperatorId={OperatorId} Truncated={Truncated}",
            loaded.Summary.IncidentId,
            report.Events.Count,
            operatorId,
            report.Truncated);

        return report;
    }

    private static string? NormalizeCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value.Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            .Take(128)
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
