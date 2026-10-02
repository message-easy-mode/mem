using Microsoft.AspNetCore.Http;
using Modules.Integrations.Portainer.Contracts;
using Modules.Integrations.Portainer.Services;
using Modules.Operator.Diagnostics.Contracts;
using Shared.Diagnostics;
using Shared.Exceptions;
using Shared.ControlPlane.Runtime;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsPortainerService(
    IPortainerRuntimeStatusReader runtimeService,
    DiagnosticsPortainerLinkBuilder links,
    IDiagnosticsIncidentResourceReader incidents,
    IMemDockerResourceLocator resourceLocator,
    TimeProvider timeProvider,
    MemControlPlaneRuntimeContext? runtimeContext = null)
{
    public async Task<DiagnosticsPortainerOverviewResponse> GetOverviewAsync(
        CancellationToken cancellationToken)
    {
        var status = await runtimeService.GetStatusAsync(cancellationToken);
        var configuredLinks = links.Build(RouteVersion(status));
        var projectedLinks = AddHostNativeDevelopmentHome(configuredLinks, status);
        var warnings = new HashSet<string>(status.Warnings, StringComparer.Ordinal);

        if (configuredLinks.Home is null)
        {
            warnings.Add("portainer_ui_not_configured");
        }

        if (projectedLinks.EnvironmentId is null)
        {
            warnings.Add("portainer_environment_not_configured");
        }

        if (status.Exists && !status.Ready)
        {
            warnings.Add("portainer_unavailable");
        }

        var runtimeState = status.Exists
            ? status.Ready
                ? "ready"
                : status.Running ? "starting" : "stopped"
            : "absent";
        var available = status.Ready && projectedLinks.Home is not null;

        return new DiagnosticsPortainerOverviewResponse(
            SchemaVersion: 1,
            ObservedAtUtc: timeProvider.GetUtcNow(),
            Available: available,
            Managed: status.Managed,
            RuntimeState: runtimeState,
            OwnershipState: status.OwnershipState,
            Version: status.ObservedVersion,
            ApprovedVersion: status.ApprovedVersion,
            EnvironmentConfigured: projectedLinks.EnvironmentId.HasValue,
            ExactResourceLinksSupported: projectedLinks.ExactResourceLinksSupported,
            Links: new DiagnosticsPortainerLinks(
                Home: available ? projectedLinks.Home : null,
                Environment: available ? projectedLinks.Environment : null,
                Containers: available ? projectedLinks.Containers : null),
            Capabilities: new DiagnosticsPortainerCapabilities(
                CanOpenHome: available && projectedLinks.Home is not null,
                CanOpenEnvironment: available && projectedLinks.Environment is not null,
                CanOpenContainers: available && projectedLinks.Containers is not null,
                CanOpenExactResource: available &&
                                      projectedLinks.ExactResourceLinksSupported &&
                                      projectedLinks.Containers is not null),
            Warnings: warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    public async Task<DiagnosticsPortainerHandoff> HandoffIncidentAsync(
        string incidentId,
        CancellationToken cancellationToken)
    {
        var incident = await incidents.LoadAsync(
            incidentId,
            cancellationToken);
        var location = await resourceLocator.LocateForIncidentAsync(
            incident.IncidentId,
            incident.Events,
            cancellationToken);
        return await BuildHandoffAsync(location, cancellationToken);
    }

    public async Task<DiagnosticsPortainerHandoff> HandoffResourceAsync(
        string resourceKind,
        string resourceId,
        string? service,
        CancellationToken cancellationToken)
    {
        var resource = ParseLogicalResource(resourceKind, resourceId, service);
        var location = await resourceLocator.LocateAsync(resource, cancellationToken);
        return await BuildHandoffAsync(location, cancellationToken);
    }

    public Task<DiagnosticsPortainerHandoff> HandoffSeqAsync(
        CancellationToken cancellationToken) =>
        HandoffResourceAsync(
            "platform-service",
            "seq",
            "seq",
            cancellationToken);

    private async Task<DiagnosticsPortainerHandoff> BuildHandoffAsync(
        MemDockerResourceLocation? resource,
        CancellationToken cancellationToken)
    {
        var status = await runtimeService.GetStatusAsync(cancellationToken);
        var projectedLinks = AddHostNativeDevelopmentHome(
            links.Build(RouteVersion(status)),
            status);
        if (!status.Ready || projectedLinks.Home is null)
        {
            var code = !status.Exists
                ? "portainer_not_configured"
                : !status.Ready
                    ? "portainer_unavailable"
                    : "portainer_not_configured";
            throw new MemProblemException(
                StatusCodes.Status503ServiceUnavailable,
                code,
                "Portainer is unavailable",
                "MEM could not open the configured or host-native Portainer workspace.",
                retryable: true,
                suggestedAction:
                    "Check the Portainer runtime and its server-owned UI configuration.",
                feature: "diagnostics",
                stage: "portainer-handoff");
        }

        if (projectedLinks.Containers is null)
        {
            return new DiagnosticsPortainerHandoff(
                projectedLinks.Home,
                "home-fallback");
        }

        var exact = resource is null
            ? null
            : links.BuildContainer(projectedLinks, resource.ContainerId);
        return exact is not null
            ? new DiagnosticsPortainerHandoff(exact, "exact")
            : new DiagnosticsPortainerHandoff(
                projectedLinks.Containers,
                "containers-fallback");
    }


    private DiagnosticsPortainerLinkSet AddHostNativeDevelopmentHome(
        DiagnosticsPortainerLinkSet projectedLinks,
        PortainerRuntimeStatus status)
    {
        if (projectedLinks.Home is not null ||
            runtimeContext is null ||
            !IsHostNativeDevelopmentRuntime(runtimeContext.RuntimeMode) ||
            !status.Ready ||
            status.UiHostPort is not > 0)
        {
            return projectedLinks;
        }

        var localHome = new UriBuilder(
            Uri.UriSchemeHttps,
            "localhost",
            status.UiHostPort.Value).Uri.ToString().TrimEnd('/');

        return projectedLinks with { Home = localHome };
    }

    private static bool IsHostNativeDevelopmentRuntime(string runtimeMode) =>
        string.Equals(
            runtimeMode,
            MemRuntimeModes.LocalDevelopment,
            StringComparison.Ordinal) ||
        string.Equals(
            runtimeMode,
            MemRuntimeModes.ContainerizedDevelopment,
            StringComparison.Ordinal);

    private static string? RouteVersion(PortainerRuntimeStatus status) =>
        !string.IsNullOrWhiteSpace(status.ObservedVersion)
            ? status.ObservedVersion
            : status.Managed && status.UsesApprovedRuntime
                ? status.ApprovedVersion
                : null;

    private static MemDiagnosticResource ParseLogicalResource(
        string resourceKind,
        string resourceId,
        string? service)
    {
        var kind = NormalizeIdentifier(resourceKind, 40);
        var id = NormalizeIdentifier(resourceId, 160, lowerCase: false);
        var normalizedService = string.IsNullOrWhiteSpace(service)
            ? null
            : NormalizeIdentifier(service, 60);

        return kind switch
        {
            "stack" or "runtime-stack" or "chat-stack" when
                normalizedService is "matrix" or "synapse" or "element" or "element-web" =>
                new MemDiagnosticResource(
                    Kind: "stack",
                    Id: id,
                    StackId: id,
                    StackSlug: id,
                    Service: normalizedService),
            "migration" or "migration-staging" when
                normalizedService is "synapse" or "matrix" or "element" or "element-web" or "postgres" or "database" =>
                new MemDiagnosticResource(
                    Kind: "migration",
                    Id: id,
                    Service: normalizedService),
            "restore-staging" or "private-staging" when
                normalizedService is "synapse" or "matrix" or "element" or "element-web" or "postgres" or "database" =>
                new MemDiagnosticResource(
                    Kind: "restore-staging",
                    Id: id,
                    Service: normalizedService),
            "platform-service" when
                (id == "seq" || id == "coturn") &&
                (normalizedService is null || normalizedService == id) =>
                new MemDiagnosticResource(
                    Kind: "platform-service",
                    Id: id,
                    Service: id),
            _ => throw new MemProblemException(
                StatusCodes.Status400BadRequest,
                "diagnostic_resource_not_supported",
                "Diagnostic resource is not supported",
                "The requested logical resource cannot be opened in Portainer.",
                feature: "diagnostics",
                stage: "portainer-handoff")
        };
    }

    private static string NormalizeIdentifier(
        string? value,
        int maximumLength,
        bool lowerCase = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw InvalidResource();
        }

        var trimmed = value.Trim();
        var source = lowerCase ? trimmed.ToLowerInvariant() : trimmed;
        if (source.Length > maximumLength ||
            source.Any(character =>
                !char.IsLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.'))
        {
            throw InvalidResource();
        }

        return source;
    }

    private static MemProblemException InvalidResource() => new(
        StatusCodes.Status400BadRequest,
        "diagnostic_resource_invalid",
        "Diagnostic resource is invalid",
        "The supplied logical resource identifier could not be accepted.",
        feature: "diagnostics",
        stage: "portainer-handoff");
}

public sealed record DiagnosticsPortainerHandoff(
    string Location,
    string Kind);
