using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Modules.Setup.SupportReports;

public sealed record SetupInstallationSupportReportDocument(
    string ContentType,
    string FileExtension,
    string Content);

public sealed class SetupInstallationSupportReportFormatter
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    public SetupInstallationSupportReportDocument Format(
        SetupInstallationSupportReport report,
        string? format)
    {
        ArgumentNullException.ThrowIfNull(report);
        var normalized = SetupSupportReportFormats.Normalize(format);
        return normalized == SetupSupportReportFormats.Text
            ? new SetupInstallationSupportReportDocument(
                "text/plain",
                "txt",
                FormatText(report))
            : new SetupInstallationSupportReportDocument(
                "application/json",
                "json",
                JsonSerializer.Serialize(report, JsonOptions));
    }

    private static string FormatText(SetupInstallationSupportReport report)
    {
        var builder = new StringBuilder();
        Line(builder, "MEM Setup installation support report");
        Line(builder, "=====================================");
        Value(builder, "Generated", report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        Value(builder, "MEM version", report.MemVersion);
        Value(builder, "Runtime mode", report.RuntimeContext.RuntimeMode);
        Value(builder, "Control Plane instance", report.RuntimeContext.ControlPlaneInstanceId.ToString());
        Value(
            builder,
            "Active API process instance",
            report.RuntimeContext.ApiProcessInstanceId == Guid.Empty
                ? "unavailable"
                : report.RuntimeContext.ApiProcessInstanceId.ToString());
        foreach (var warning in report.RuntimeContext.Warnings)
        {
            Line(builder, $"Runtime warning: {warning}");
        }
        Value(builder, "Selection", report.Selection.Mode);
        Value(builder, "Installation ID", report.Setup?.InstallationId.ToString() ?? "-");
        Value(builder, "Trace ID", report.Selection.TraceId ?? "-");
        Line(builder);

        Line(builder, "Setup lifecycle");
        Line(builder, "---------------");
        if (report.Setup is null)
        {
            Line(builder, "No Installation record was resolved. Trace-linked diagnostics may still be present.");
        }
        else
        {
            Value(builder, "Status", report.Setup.Status);
            Value(builder, "Stage", report.Setup.Stage);
            Value(builder, "Handoff", report.Setup.HandoffState);
            Value(builder, "Created", report.Setup.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            Value(builder, "Updated", report.Setup.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            Value(builder, "Last safe error", report.Setup.LastSafeError ?? "-");
        }
        Line(builder);

        Line(builder, "Reviewed plan");
        Line(builder, "-------------");
        if (report.ReviewedPlan is null)
        {
            Line(builder, "No reviewed/frozen plan is available.");
        }
        else
        {
            Value(builder, "Fingerprint", report.ReviewedPlan.Fingerprint ?? "-");
            Value(builder, "Reviewed", report.ReviewedPlan.ReviewedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "-");
            Value(builder, "Domain", report.ReviewedPlan.BaseDomain ?? "-");
            Value(builder, "Certificate", report.ReviewedPlan.WildcardCertificate ?? "-");
            Value(builder, "DNS provider", report.ReviewedPlan.DnsProvider ?? "-");
            Value(builder, "ACME environment", report.ReviewedPlan.AcmeEnvironment);
            Value(builder, "Services", Join(report.ReviewedPlan.Services));
            Value(builder, "Containers", Join(report.ReviewedPlan.Containers));
            Value(builder, "Networks", Join(report.ReviewedPlan.Networks));
            Value(builder, "Volumes", Join(report.ReviewedPlan.Volumes));
        }
        Line(builder);

        if (report.ServerChecks is not null)
        {
            Line(builder, "Server checks");
            Line(builder, "-------------");
            Value(builder, "Run ID", report.ServerChecks.RunId);
            Value(builder, "Status", report.ServerChecks.RunStatus);
            Value(builder, "Counts", $"{report.ServerChecks.Passed} passed, {report.ServerChecks.Warnings} warnings, {report.ServerChecks.Failed} failed, {report.ServerChecks.Unavailable} unavailable");
            Value(builder, "Blockers", report.ServerChecks.BlockingIssueCount.ToString(CultureInfo.InvariantCulture));
            Line(builder);
        }

        Line(builder, "Installation timeline");
        Line(builder, "---------------------");
        if (report.Steps.Count == 0)
        {
            Line(builder, "No installation steps are recorded.");
        }
        foreach (var step in report.Steps)
        {
            Line(builder, $"{step.Sequence}. {step.Name} — {step.Status} (attempt {step.AttemptCount})");
            if (!string.IsNullOrWhiteSpace(step.SafeMessage)) Line(builder, $"   Message: {step.SafeMessage}");
            if (!string.IsNullOrWhiteSpace(step.SafeError)) Line(builder, $"   Error: {step.SafeError}");
        }
        Line(builder);

        Line(builder, "Diagnostics");
        Line(builder, "-----------");
        Value(builder, "Logging health", report.Diagnostics.LoggingHealth.Status);
        Value(builder, "Safe event store", report.Diagnostics.LoggingHealth.SafeEventStoreStatus);
        Value(builder, "Related events", report.Diagnostics.Events.Count.ToString(CultureInfo.InvariantCulture));
        Value(builder, "Incidents", report.Diagnostics.Incidents.Count.ToString(CultureInfo.InvariantCulture));
        Value(builder, "Docker evidence", report.Diagnostics.DockerEvidence is null
            ? "not included"
            : report.Diagnostics.DockerEvidence.Available ? "available" : "unavailable");
        foreach (var incident in report.Diagnostics.Incidents.Take(20))
        {
            Line(builder, $"- Incident {incident.IncidentId}: {incident.EventCode} ({incident.Severity})");
        }
        Line(builder);

        Line(builder, "Recovery");
        Line(builder, "--------");
        Value(builder, "Retry allowed", report.Recovery.RetryAllowed.ToString());
        Value(builder, "Retry scope", report.Recovery.RetryScope);
        Value(builder, "Operator action", report.Recovery.RequiredOperatorAction ?? "-");
        Value(builder, "Suggested route", report.Recovery.SuggestedRoute);
        Value(builder, "Proven complete", Join(report.Recovery.ProvenComplete));
        Value(builder, "State uncertain", Join(report.Recovery.StateUncertain));
        Line(builder);

        Line(builder, "Redaction and bounds");
        Line(builder, "--------------------");
        Value(builder, "Policy", report.Redaction.PolicyVersion);
        Value(builder, "Truncated", report.Truncated.ToString());
        foreach (var omission in report.Redaction.OmittedContent)
        {
            Line(builder, $"- {omission}");
        }
        foreach (var warning in report.Warnings)
        {
            Line(builder, $"WARNING: {warning}");
        }

        return builder.ToString();
    }

    private static void Value(StringBuilder builder, string name, string value) =>
        Line(builder, $"{name}: {value}");

    private static void Line(StringBuilder builder, string value = "") =>
        builder.AppendLine(value);

    private static string Join(IEnumerable<string> values)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return items.Length == 0 ? "-" : string.Join(", ", items);
    }
}
