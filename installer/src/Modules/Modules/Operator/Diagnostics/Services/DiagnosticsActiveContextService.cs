using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Operator.Diagnostics.Contracts;
using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

public sealed record DiagnosticsActiveContextResolution(
    DiagnosticsActiveContext? Context,
    string? WarningCode = null);

/// <summary>
/// Selects one browser-safe, server-authored operational context for the
/// Diagnostics landing page. The projection intentionally contains no raw
/// errors, host paths, Docker identifiers, request payloads, or credentials.
/// </summary>
public sealed class DiagnosticsActiveContextService(
    MemDbContext db,
    DiagnosticsWorkspaceLinkBuilder workspaceLinks,
    TimeProvider timeProvider,
    ILogger<DiagnosticsActiveContextService> logger,
    DiagnosticsIncidentLifecycleReader? lifecycle = null)
{
    private static readonly IReadOnlySet<string> WorkflowResourceKinds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "migration",
            "migration-session",
            "restore",
            "restore-attempt",
            "restore-session",
            "installation",
            "install",
            "setup",
            "federation",
            "turn",
            "runtime-operation"
        };

    public async Task<DiagnosticsActiveContextResolution> ResolveAsync(
        IReadOnlyList<MemDiagnosticEvent> events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (lifecycle is null)
        {
            return await ResolveAsync(
                events,
                projections: null,
                lifecycleWarningCode: null,
                cancellationToken: cancellationToken);
        }

        var lifecycleRead = await lifecycle.ProjectEventsAsync(events, cancellationToken);
        return await ResolveAsync(
            events,
            lifecycleRead.Projections,
            lifecycleRead.WarningCode,
            cancellationToken);
    }

    internal async Task<DiagnosticsActiveContextResolution> ResolveAsync(
        IReadOnlyList<MemDiagnosticEvent> events,
        IReadOnlyDictionary<string, DiagnosticsIncidentLifecycleProjection>? projections,
        string? lifecycleWarningCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);

        var workflowIncident = SelectIncident(
            events,
            projections,
            workflowOnly: true);
        if (workflowIncident is not null)
        {
            return new DiagnosticsActiveContextResolution(
                workflowIncident,
                lifecycleWarningCode);
        }

        try
        {
            var running = await ResolveRunningContextAsync(cancellationToken);
            if (running is not null)
            {
                return new DiagnosticsActiveContextResolution(
                    running,
                    lifecycleWarningCode);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "MEM diagnostics could not resolve the current active workflow context.");

            return new DiagnosticsActiveContextResolution(
                SelectIncident(events, projections, workflowOnly: false),
                "diagnostics.active_context_unavailable");
        }

        return new DiagnosticsActiveContextResolution(
            SelectIncident(events, projections, workflowOnly: false),
            lifecycleWarningCode);
    }

    private DiagnosticsActiveContext? SelectIncident(
        IReadOnlyList<MemDiagnosticEvent> events,
        IReadOnlyDictionary<string, DiagnosticsIncidentLifecycleProjection>? projections,
        bool workflowOnly)
    {
        var selected = events
            .Where(@event =>
                !string.IsNullOrWhiteSpace(@event.IncidentId) &&
                DiagnosticsProjection.SeverityRank(@event.Severity) >= 2 &&
                IsOpen(@event.IncidentId!, projections) &&
                (!workflowOnly || IsWorkflowEvent(@event)))
            .OrderByDescending(@event => DiagnosticsProjection.SeverityRank(@event.Severity))
            .ThenByDescending(@event => @event.TimestampUtc)
            .ThenByDescending(@event => @event.EventId, StringComparer.Ordinal)
            .FirstOrDefault();

        if (selected is null)
        {
            return null;
        }

        var incidentId = selected.IncidentId!;
        var workspaceHref = workspaceLinks.Build(selected.Resource);
        var kind = NormalizeKind(selected.Resource?.Kind) ?? "incident";
        var code = workflowOnly
            ? "workflow_requires_attention"
            : "incident_requires_attention";

        return new DiagnosticsActiveContext(
            State: "attention",
            Kind: kind,
            Code: code,
            Feature: selected.Feature,
            Stage: selected.Stage,
            ResourceName: selected.Resource?.DisplayName ?? selected.Resource?.Id,
            Summary: selected.Message,
            UpdatedAtUtc: selected.TimestampUtc,
            WorkspaceHref: workspaceHref,
            IncidentId: incidentId,
            IncidentHref: $"/diagnostics/logs?incident={Uri.EscapeDataString(incidentId)}");
    }

    private static bool IsOpen(
        string incidentId,
        IReadOnlyDictionary<string, DiagnosticsIncidentLifecycleProjection>? projections)
    {
        if (projections is null || !projections.TryGetValue(incidentId, out var projection))
        {
            return true;
        }

        return string.Equals(
            projection.State,
            DiagnosticsIncidentLifecycleStates.Open,
            StringComparison.Ordinal);
    }

    private async Task<DiagnosticsActiveContext?> ResolveRunningContextAsync(
        CancellationToken cancellationToken)
    {
        var candidates = new List<DiagnosticsActiveContext>(4);

        var migration = await db.MigrationIntakes
            .AsNoTracking()
            .Where(x => x.LifecycleStatus == MigrationSessionLifecycleStatuses.Active)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new
            {
                x.IntakeId,
                x.DisplayName,
                x.UpdatedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (migration is not null)
        {
            candidates.Add(new DiagnosticsActiveContext(
                State: "running",
                Kind: "migration",
                Code: "migration_active",
                Feature: "migration",
                Stage: null,
                ResourceName: migration.DisplayName,
                Summary: null,
                UpdatedAtUtc: Utc(migration.UpdatedAtUtc),
                WorkspaceHref: $"/migrations/{Uri.EscapeDataString(migration.IntakeId)}",
                IncidentId: null,
                IncidentHref: null));
        }

        var restore = await db.RestoreAttempts
            .AsNoTracking()
            .Where(x => x.ActiveSourceKey != null)
            .OrderByDescending(x => x.LastEventAtUtc ?? x.UpdatedAtUtc)
            .Select(x => new
            {
                x.RestoreSessionId,
                x.SourceDisplayNameSnapshot,
                x.CurrentStage,
                x.UpdatedAtUtc,
                x.LastEventAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (restore is not null)
        {
            candidates.Add(new DiagnosticsActiveContext(
                State: "running",
                Kind: "restore",
                Code: "restore_active",
                Feature: "restore",
                Stage: restore.CurrentStage,
                ResourceName: restore.SourceDisplayNameSnapshot,
                Summary: null,
                UpdatedAtUtc: Utc(restore.LastEventAtUtc ?? restore.UpdatedAtUtc),
                WorkspaceHref: $"/restores/{Uri.EscapeDataString(restore.RestoreSessionId)}",
                IncidentId: null,
                IncidentHref: null));
        }

        var installation = await db.Installations
            .AsNoTracking()
            .Where(x => x.Status == "Running" || x.Status == "running")
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.UpdatedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (installation is not null)
        {
            candidates.Add(new DiagnosticsActiveContext(
                State: "running",
                Kind: "installation",
                Code: "installation_active",
                Feature: "installation",
                Stage: null,
                ResourceName: null,
                Summary: null,
                UpdatedAtUtc: Utc(installation.UpdatedAtUtc),
                WorkspaceHref: $"/setup/install/{installation.Id:D}",
                IncidentId: null,
                IncidentHref: null));
        }

        var staleCutoff = timeProvider.GetUtcNow().AddMinutes(-15).UtcDateTime;
        var operation = await db.RuntimeOperations
            .AsNoTracking()
            .Where(x =>
                x.Status == "running" &&
                (x.LockedUntilUtc == null || x.LockedUntilUtc >= staleCutoff))
            .OrderByDescending(x => x.StartedAtUtc ?? x.RequestedAtUtc)
            .Select(x => new
            {
                x.Operation,
                x.CurrentStep,
                x.RequestedAtUtc,
                x.StartedAtUtc,
                StackSlug = x.RuntimeStack == null ? null : x.RuntimeStack.Slug,
                RestoreSessionId = x.RestoreAttempt == null
                    ? null
                    : x.RestoreAttempt.RestoreSessionId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (operation is not null)
        {
            candidates.Add(RuntimeOperationContext(
                operation.Operation,
                operation.CurrentStep,
                operation.StackSlug,
                operation.RestoreSessionId,
                Utc(operation.StartedAtUtc ?? operation.RequestedAtUtc)));
        }

        return candidates
            .OrderByDescending(context => context.UpdatedAtUtc)
            .ThenBy(context => context.Kind, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static DiagnosticsActiveContext RuntimeOperationContext(
        string operation,
        string? currentStep,
        string? stackSlug,
        string? restoreSessionId,
        DateTimeOffset updatedAtUtc)
    {
        var escapedStackSlug = string.IsNullOrWhiteSpace(stackSlug)
            ? null
            : Uri.EscapeDataString(stackSlug);

        if (operation.StartsWith("restore.", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(restoreSessionId))
        {
            return Running(
                kind: "restore",
                code: "restore_active",
                feature: "restore",
                stage: currentStep,
                resourceName: null,
                updatedAtUtc: updatedAtUtc,
                workspaceHref: $"/restores/{Uri.EscapeDataString(restoreSessionId)}");
        }

        if (operation.StartsWith("seq.", StringComparison.OrdinalIgnoreCase))
        {
            return Running(
                kind: "seq",
                code: "seq_operation_active",
                feature: "logging",
                stage: currentStep,
                resourceName: "Seq",
                updatedAtUtc: updatedAtUtc,
                workspaceHref: "/diagnostics/seq");
        }

        if (string.Equals(operation, "apply-federation-policy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                operation,
                "update-private-network-federation-exception",
                StringComparison.OrdinalIgnoreCase))
        {
            return Running(
                kind: "federation",
                code: "federation_operation_active",
                feature: "federation",
                stage: currentStep,
                resourceName: stackSlug,
                updatedAtUtc: updatedAtUtc,
                workspaceHref: escapedStackSlug is null
                    ? "/diagnostics/logs"
                    : $"/stacks/{escapedStackSlug}/federation");
        }

        if (string.Equals(operation, "connect-stack-turn", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(operation, "disconnect-stack-turn", StringComparison.OrdinalIgnoreCase))
        {
            return Running(
                kind: "turn",
                code: "turn_operation_active",
                feature: "turn",
                stage: currentStep,
                resourceName: stackSlug,
                updatedAtUtc: updatedAtUtc,
                workspaceHref: escapedStackSlug is null
                    ? "/diagnostics/logs"
                    : $"/stacks/{escapedStackSlug}/services");
        }

        return Running(
            kind: "runtime-operation",
            code: "runtime_operation_active",
            feature: "runtime",
            stage: currentStep,
            resourceName: stackSlug,
            updatedAtUtc: updatedAtUtc,
            workspaceHref: escapedStackSlug is null
                ? "/diagnostics/logs"
                : $"/stacks/{escapedStackSlug}");
    }

    private static DiagnosticsActiveContext Running(
        string kind,
        string code,
        string feature,
        string? stage,
        string? resourceName,
        DateTimeOffset updatedAtUtc,
        string workspaceHref) =>
        new(
            State: "running",
            Kind: kind,
            Code: code,
            Feature: feature,
            Stage: stage,
            ResourceName: resourceName,
            Summary: null,
            UpdatedAtUtc: updatedAtUtc,
            WorkspaceHref: workspaceHref,
            IncidentId: null,
            IncidentHref: null);

    private static bool IsWorkflowEvent(MemDiagnosticEvent @event) =>
        WorkflowResourceKinds.Contains(@event.Feature.Trim()) ||
        (@event.Resource is not null &&
         WorkflowResourceKinds.Contains(@event.Resource.Kind.Trim()));

    private static string? NormalizeKind(string? kind) =>
        string.IsNullOrWhiteSpace(kind)
            ? null
            : kind.Trim().ToLowerInvariant();

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
