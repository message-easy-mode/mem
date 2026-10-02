using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modules.Shared.Docker;
using Shared.ControlPlane.Runtime;
using Shared.Diagnostics;

namespace Api.Diagnostics;

public sealed class ControlPlaneStartupDiagnosticPublisher(
    IMemDiagnosticEventWriter diagnosticWriter,
    MemControlPlaneRuntimeContext runtimeContext,
    IServiceScopeFactory scopeFactory,
    ILogger<ControlPlaneStartupDiagnosticPublisher> logger)
{
    private int _published;

    public async Task<MemDiagnosticWriteResult?> PublishAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _published, 1, 0) != 0)
        {
            return null;
        }

        try
        {
            var exposure = await InspectExposureAsync(cancellationToken);
            var result = await diagnosticWriter.WriteAsync(
                new MemDiagnosticWriteRequest(
                    Severity: MemDiagnosticSeverities.Information,
                    EventCode: "control_plane.started",
                    Source: nameof(ControlPlaneStartupDiagnosticPublisher),
                    Feature: "control-plane",
                    Message: "MEM control plane started.",
                    Stage: "startup",
                    CreateIncident: false,
                    Details: new Dictionary<string, string?>
                    {
                        ["runtimeMode"] = runtimeContext.RuntimeMode,
                        ["controlPlaneInstanceId"] =
                            runtimeContext.ControlPlaneInstanceId.ToString("D"),
                        ["apiProcessInstanceId"] =
                            runtimeContext.ApiProcessInstanceId.ToString("D"),
                        ["environment"] = runtimeContext.EnvironmentName,
                        ["uiDeliveryMode"] = runtimeContext.UiDeliveryMode,
                        ["stateRootKind"] = runtimeContext.StateRootKind,
                        ["version"] = runtimeContext.Version,
                        ["commit"] = runtimeContext.Commit,
                        ["controlPlaneExposureState"] = exposure.State,
                        ["controlPlaneAccessMode"] = exposure.AccessMode,
                        ["controlPlaneHostAddress"] = exposure.HostAddress,
                        ["controlPlaneHostPort"] = exposure.HostPort?.ToString(),
                        ["controlPlaneExposureWarningCode"] = exposure.WarningCode
                    }),
                cancellationToken);

            if (!result.Stored)
            {
                logger.LogWarning(
                    "MEM control-plane startup diagnostic event was not stored. WarningCode={WarningCode}",
                    result.WarningCode);
            }

            await PublishExposureAttentionAsync(exposure, cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogWarning(
                ex,
                "MEM control-plane startup diagnostic event could not be written.");
            return null;
        }
    }
    private async Task<MemControlPlaneExposureProjection> InspectExposureAsync(
        CancellationToken cancellationToken)
    {
        if (!MemRuntimeModes.IsContainerized(runtimeContext.RuntimeMode))
        {
            return MemControlPlaneExposureProjection.NotApplicable;
        }

        using var scope = scopeFactory.CreateScope();
        var inspector = scope.ServiceProvider.GetRequiredService<IControlPlaneExposureInspector>();
        return await inspector.InspectAsync(cancellationToken);
    }

    private async Task PublishExposureAttentionAsync(
        MemControlPlaneExposureProjection exposure,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                exposure.State,
                MemControlPlaneExposureStates.Unavailable,
                StringComparison.Ordinal))
        {
            var unavailableWrite = await diagnosticWriter.WriteAsync(
                new MemDiagnosticWriteRequest(
                    Severity: MemDiagnosticSeverities.Warning,
                    EventCode: "control_plane.exposure.unavailable",
                    Source: nameof(ControlPlaneStartupDiagnosticPublisher),
                    Feature: "control-plane",
                    Message: "MEM could not prove the Control Plane host binding is private.",
                    Stage: "private-administration",
                    CreateIncident: false,
                    Details: new Dictionary<string, string?>
                    {
                        ["state"] = exposure.State,
                        ["warningCode"] = exposure.WarningCode
                    },
                    SuggestedAction: "Restore Docker inspection, then verify the Control Plane is bound only to 127.0.0.1 or an explicitly trusted RFC1918 host address."),
                cancellationToken);

            if (!unavailableWrite.Stored)
            {
                logger.LogWarning(
                    "MEM Control Plane exposure diagnostic was not stored. WarningCode={WarningCode}",
                    unavailableWrite.WarningCode);
            }

            return;
        }

        if (!string.Equals(
                exposure.State,
                MemControlPlaneExposureStates.NeedsAttention,
                StringComparison.Ordinal))
        {
            return;
        }

        var incidentId = $"inc_control_plane_exposure_{runtimeContext.ControlPlaneInstanceId:N}";
        var write = await diagnosticWriter.WriteAsync(
            new MemDiagnosticWriteRequest(
                Severity: MemDiagnosticSeverities.Error,
                EventCode: "control_plane.exposure.unsupported",
                Source: nameof(ControlPlaneStartupDiagnosticPublisher),
                Feature: "control-plane",
                Message: "MEM detected a Control Plane host binding outside the supported private administration boundary.",
                Stage: "private-administration",
                CreateIncident: true,
                IncidentId: incidentId,
                Resource: new MemDiagnosticResource(
                    Kind: "control-plane",
                    Id: runtimeContext.ControlPlaneInstanceId.ToString("D"),
                    DisplayName: runtimeContext.ConfiguredContainerName),
                Observed: new Dictionary<string, string?>
                {
                    ["state"] = exposure.State,
                    ["accessMode"] = exposure.AccessMode,
                    ["hostAddress"] = exposure.HostAddress,
                    ["hostPort"] = exposure.HostPort?.ToString(),
                    ["bindingCount"] = exposure.BindingCount.ToString(),
                    ["warningCode"] = exposure.WarningCode
                },
                SuggestedAction: "Run the MEM bootstrap again and select SSH tunnel/local-only or an explicitly trusted private LAN address. Do not publish the Control Plane through public ingress or NAT."),
            cancellationToken);

        if (!write.Stored)
        {
            logger.LogWarning(
                "MEM Control Plane exposure diagnostic was not stored. WarningCode={WarningCode}",
                write.WarningCode);
        }
    }

}
