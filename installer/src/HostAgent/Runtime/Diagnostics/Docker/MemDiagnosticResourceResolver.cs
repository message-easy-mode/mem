using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using HostAgent.Runtime.ServiceRuntime;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Diagnostics.Docker;

internal sealed record MemResolvedDockerResource(
    MemDiagnosticResource Resource,
    string ContainerReference,
    string LogicalName,
    IReadOnlyCollection<string> ExactSecrets);

internal interface IMemDiagnosticResourceResolver
{
    Task<MemResolvedDockerResource?> ResolveAsync(
        string incidentId,
        IReadOnlyCollection<MemDiagnosticEvent> incidentEvents,
        CancellationToken cancellationToken);

    Task<MemResolvedDockerResource?> ResolveResourceAsync(
        MemDiagnosticResource resource,
        CancellationToken cancellationToken);
}

internal interface IMemPrivateStagingEvidenceReader
{
    Task<PrivateStagingRunResult?> ReadAsync(
        string stagingId,
        CancellationToken cancellationToken);
}

internal sealed class MemPrivateStagingEvidenceReader(
    PrivateStagingHistoryService history)
    : IMemPrivateStagingEvidenceReader
{
    public async Task<PrivateStagingRunResult?> ReadAsync(
        string stagingId,
        CancellationToken cancellationToken) =>
        (await history.GetRunAsync(stagingId, cancellationToken))?.Run;
}

internal sealed class MemDiagnosticResourceResolver(
    MemDbContext db,
    IMemPrivateStagingEvidenceReader privateStaging)
    : IMemDiagnosticResourceResolver
{
    public async Task<MemResolvedDockerResource?> ResolveAsync(
        string incidentId,
        IReadOnlyCollection<MemDiagnosticEvent> incidentEvents,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(incidentId) || incidentEvents.Count == 0)
        {
            return null;
        }

        var resources = incidentEvents
            .Where(@event => string.Equals(
                @event.IncidentId,
                incidentId,
                StringComparison.Ordinal))
            .Where(@event => @event.Resource is not null)
            .OrderByDescending(@event => SeverityRank(@event.Severity))
            .ThenByDescending(@event => @event.TimestampUtc)
            .Select(@event => @event.Resource!)
            .DistinctBy(ResourceKey)
            .ToArray();

        foreach (var resource in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = await ResolveResourceAsync(resource, cancellationToken);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    public Task<MemResolvedDockerResource?> ResolveResourceAsync(
        MemDiagnosticResource resource,
        CancellationToken cancellationToken) =>
        Normalize(resource.Kind) switch
        {
            "stack" or "runtime-stack" or "chat-stack" =>
                ResolveStackAsync(resource, cancellationToken),
            "migration" or "migration-staging" =>
                ResolveMigrationAsync(resource, cancellationToken),
            "restore-staging" or "private-staging" =>
                ResolveRestoreStagingAsync(resource, cancellationToken),
            "platform" or "platform-service" or "service" =>
                ResolvePlatformAsync(resource, cancellationToken),
            _ => Task.FromResult<MemResolvedDockerResource?>(null)
        };

    private async Task<MemResolvedDockerResource?> ResolveStackAsync(
        MemDiagnosticResource resource,
        CancellationToken cancellationToken)
    {
        var stackIdText = FirstNonEmpty(resource.StackId, resource.Id);
        var stackSlug = FirstNonEmpty(resource.StackSlug, resource.Id);
        RuntimeStackEntity? stack;

        if (Guid.TryParse(stackIdText, out var stackId) && stackId != Guid.Empty)
        {
            stack = await db.RuntimeStacks
                .AsNoTracking()
                .Include(item => item.ServiceInstances)
                .SingleOrDefaultAsync(item => item.Id == stackId, cancellationToken);
        }
        else
        {
            stack = await db.RuntimeStacks
                .AsNoTracking()
                .Include(item => item.ServiceInstances)
                .SingleOrDefaultAsync(
                    item => item.Slug == stackSlug,
                    cancellationToken);
        }

        if (stack is null)
        {
            return null;
        }

        var serviceKey = NormalizeStackService(resource.Service);
        if (serviceKey is null)
        {
            return null;
        }

        var instance = stack.ServiceInstances
            .Where(item => string.Equals(
                item.ServiceKey,
                serviceKey,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefault();
        var containerReference = FirstNonEmpty(
            instance?.ContainerId,
            instance?.ContainerName);
        if (string.IsNullOrWhiteSpace(containerReference))
        {
            return null;
        }

        var serviceLabel = serviceKey == ServiceKeys.Matrix ? "Synapse" : "Element";
        var canonicalResource = resource with
        {
            Kind = "stack",
            Id = stack.Id.ToString("D"),
            DisplayName = FirstNonEmpty(stack.DisplayName, stack.Slug),
            StackId = stack.Id.ToString("D"),
            StackSlug = stack.Slug,
            Service = serviceKey
        };

        return new MemResolvedDockerResource(
            canonicalResource,
            containerReference,
            $"{FirstNonEmpty(stack.DisplayName, stack.Slug)} {serviceLabel}",
            Array.Empty<string>());
    }

    private async Task<MemResolvedDockerResource?> ResolveMigrationAsync(
        MemDiagnosticResource resource,
        CancellationToken cancellationToken)
    {
        var migrationId = resource.Id.Trim();
        var run = await db.MigrationStagingRuns
            .AsNoTracking()
            .Include(item => item.MigrationIntake)
            .Where(item =>
                item.MigrationIntake.IntakeId == migrationId &&
                item.PrivateRuntimeStagingId != null &&
                item.DestroyedAtUtc == null)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null || string.IsNullOrWhiteSpace(run.PrivateRuntimeStagingId))
        {
            return null;
        }

        var service = NormalizePrivateStagingService(resource.Service);
        if (service is null)
        {
            return null;
        }

        if (service == "element")
        {
            var elementReference = FirstNonEmpty(
                run.ElementContainerId,
                run.ElementContainerName);
            return string.IsNullOrWhiteSpace(elementReference)
                ? null
                : new MemResolvedDockerResource(
                    resource with
                    {
                        Kind = "migration",
                        Id = run.MigrationIntake.IntakeId,
                        DisplayName = run.MigrationIntake.DisplayName,
                        Service = "element"
                    },
                    elementReference,
                    "Migration private Element staging runtime",
                    Array.Empty<string>());
        }

        var staging = await privateStaging.ReadAsync(
            run.PrivateRuntimeStagingId,
            cancellationToken);
        if (staging is null || !string.Equals(
                staging.SourceKind,
                PrivateStagingSourceKinds.MigrationCandidate,
                StringComparison.Ordinal))
        {
            return null;
        }

        var containerReference = service switch
        {
            "synapse" => FirstNonEmpty(
                staging.SynapseContainerId,
                staging.SynapseContainerName),
            "postgres" => FirstNonEmpty(
                staging.PostgresContainerId,
                staging.PostgresContainerName),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(containerReference))
        {
            return null;
        }

        return new MemResolvedDockerResource(
            resource with
            {
                Kind = "migration",
                Id = run.MigrationIntake.IntakeId,
                DisplayName = run.MigrationIntake.DisplayName,
                Service = service
            },
            containerReference,
            $"Migration private {DisplayService(service)} staging runtime",
            Array.Empty<string>());
    }

    private async Task<MemResolvedDockerResource?> ResolveRestoreStagingAsync(
        MemDiagnosticResource resource,
        CancellationToken cancellationToken)
    {
        var staging = await privateStaging.ReadAsync(
            resource.Id.Trim(),
            cancellationToken);
        if (staging is null || !string.Equals(
                staging.SourceKind,
                PrivateStagingSourceKinds.BackupCatalog,
                StringComparison.Ordinal))
        {
            return null;
        }

        var service = NormalizePrivateStagingService(resource.Service);
        var containerReference = service switch
        {
            "synapse" => FirstNonEmpty(
                staging.SynapseContainerId,
                staging.SynapseContainerName),
            "element" => FirstNonEmpty(
                staging.ElementContainerId,
                staging.ElementContainerName),
            "postgres" => FirstNonEmpty(
                staging.PostgresContainerId,
                staging.PostgresContainerName),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(containerReference) || service is null)
        {
            return null;
        }

        return new MemResolvedDockerResource(
            resource with
            {
                Kind = "restore-staging",
                Id = staging.StagingId,
                DisplayName = "Private restore test",
                StackSlug = staging.TargetStackSlug,
                Service = service
            },
            containerReference,
            $"Private restore {DisplayService(service)} staging runtime",
            Array.Empty<string>());
    }

    private async Task<MemResolvedDockerResource?> ResolvePlatformAsync(
        MemDiagnosticResource resource,
        CancellationToken cancellationToken)
    {
        var serviceName = FirstNonEmpty(resource.Service, resource.Id);
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return null;
        }

        var runtime = await db.RuntimeServices
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.ServiceName == serviceName,
                cancellationToken);
        if (runtime is null)
        {
            return null;
        }

        var containerReference = FirstNonEmpty(
            runtime.ContainerId,
            runtime.ContainerName);
        if (string.IsNullOrWhiteSpace(containerReference))
        {
            return null;
        }

        return new MemResolvedDockerResource(
            resource with
            {
                Kind = "platform-service",
                Id = runtime.ServiceName,
                DisplayName = runtime.ServiceName,
                Service = runtime.ServiceName
            },
            containerReference,
            $"MEM platform service {runtime.ServiceName}",
            Array.Empty<string>());
    }

    private static string ResourceKey(MemDiagnosticResource resource) =>
        $"{Normalize(resource.Kind)}|{resource.Id}|{resource.StackId}|{resource.StackSlug}|{Normalize(resource.Service)}";

    private static string? NormalizeStackService(string? value) =>
        Normalize(value) switch
        {
            "matrix" or "synapse" => ServiceKeys.Matrix,
            "element" or "element-web" => ServiceKeys.ElementWeb,
            _ => null
        };

    private static string? NormalizePrivateStagingService(string? value) =>
        Normalize(value) switch
        {
            "matrix" or "synapse" => "synapse",
            "element" or "element-web" => "element",
            "postgres" or "database" => "postgres",
            _ => null
        };

    private static string DisplayService(string value) =>
        value switch
        {
            "synapse" => "Synapse",
            "element" => "Element",
            "postgres" => "Postgres",
            _ => value
        };

    private static int SeverityRank(string? severity) =>
        Normalize(severity) switch
        {
            "critical" => 4,
            "error" => 3,
            "warning" => 2,
            "information" => 1,
            _ => 0
        };

    private static string Normalize(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
