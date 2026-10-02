using Core.Runtime;
using Core.RuntimeDefinition;
using Infrastructure.Data.Entities;
using Infrastructure.Docker.Models;
using Modules.Integrations.Seq.Contracts;
using Shared.ControlPlane.Runtime;

namespace Modules.Integrations.Seq.Mappers;

public static class SeqMappers
{
    public static SeqPlanResponse ToPlanResponse(
        RuntimeServicePlan plan,
        string approvedImageReference,
        string expectedVersion)
    {
        var portPlan = plan.Ports.Single();

        return new SeqPlanResponse(
            ServiceName: plan.ServiceName,
            ContainerName: plan.ContainerName,
            ApprovedImageReference: approvedImageReference,
            ExpectedVersion: expectedVersion,
            ContainerPort: portPlan.ContainerPort,
            PreferredHostPort: portPlan.PreferredHostPort,
            SelectedHostPort: portPlan.SelectedHostPort,
            IsPreferredPortAvailable: portPlan.IsPreferredPortAvailable,
            HostDataPath: plan.HostDataPath ?? string.Empty,
            PublishesPublicIngress: false,
            Warnings: plan.Warnings);
    }

    public static SeqStatusResponse ToStatusResponse(
        RuntimeServiceEntity? record,
        DockerContainerInspection? container,
        string fallbackServiceName,
        string fallbackContainerName,
        string expectedVersion,
        string approvedImageReference,
        IReadOnlyList<string> warnings,
        MemControlPlaneRuntimeContext? runtimeContext = null)
    {
        var uiHostPort = container?.Ports
            .FirstOrDefault(x => x.PrivatePort == 80)?.PublicPort;
        var observedImage = container?.Image ?? record?.Image;
        var recordMatchesExpectedContainer = record is not null &&
            string.Equals(
                record.ContainerName,
                fallbackContainerName,
                StringComparison.OrdinalIgnoreCase);
        var identityMatches = recordMatchesExpectedContainer &&
            container is not null &&
            (string.IsNullOrWhiteSpace(record!.ContainerId) ||
             string.Equals(record.ContainerId, container.Id, StringComparison.Ordinal));
        var serviceLabelMismatch = container is not null &&
            container.Labels.TryGetValue(ManagedContainerLabels.CanonicalServiceKey, out var serviceLabel) &&
            !string.Equals(serviceLabel, fallbackServiceName, StringComparison.Ordinal);
        var controlPlaneMismatch = runtimeContext is not null &&
            container is not null &&
            container.Labels.TryGetValue(ManagedContainerLabels.ControlPlaneInstanceKey, out var ownerLabel) &&
            Guid.TryParse(ownerLabel, out var ownerId) &&
            ownerId != runtimeContext.ControlPlaneInstanceId;
        var managed = recordMatchesExpectedContainer &&
                      container is not null &&
                      identityMatches &&
                      !serviceLabelMismatch &&
                      !controlPlaneMismatch;
        var approved = managed &&
                       !string.IsNullOrWhiteSpace(record!.Image) &&
                       (string.Equals(
                            observedImage,
                            record.Image,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            observedImage,
                            approvedImageReference,
                            StringComparison.OrdinalIgnoreCase));
        var ownershipState = (record, container, recordMatchesExpectedContainer, serviceLabelMismatch, controlPlaneMismatch, identityMatches) switch
        {
            (null, null, _, _, _, _) => "absent",
            (not null, null, _, _, _, _) => "record-only",
            (null, not null, _, _, _, _) => "unmanaged-conflict",
            (not null, not null, false, _, _, _) => "unmanaged-conflict",
            (not null, not null, true, true, _, _) => "unmanaged-conflict",
            (not null, not null, true, false, true, _) => "control-plane-mismatch",
            (not null, not null, true, false, false, false) => "identity-mismatch",
            _ => "managed"
        };
        var warningCode = ownershipState switch
        {
            "record-only" => "seq_runtime_record_conflict",
            "unmanaged-conflict" => "seq_unmanaged_container",
            "control-plane-mismatch" => "seq_control_plane_ownership_mismatch",
            "identity-mismatch" => "seq_container_identity_mismatch",
            _ => null
        };

        return new SeqStatusResponse(
            ServiceName: record?.ServiceName ?? fallbackServiceName,
            ContainerName: record?.ContainerName ?? fallbackContainerName,
            ExpectedVersion: expectedVersion,
            HostDataPath: record?.HostPath,
            UiHostPort: uiHostPort is 0 or null ? null : (int?)uiHostPort,
            Exists: container is not null,
            Running: container?.Running ?? false,
            State: container?.State,
            Image: observedImage,
            UsesApprovedRuntime: approved,
            Warnings: warnings,
            Managed: managed,
            OwnershipState: ownershipState,
            WarningCode: warningCode);
    }
}
