using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Runtime.Services.TemporaryStaging;

/// <summary>
/// Bounded read-only source. Only explicitly selected labels and identifiers enter
/// the projection. Environment, mounts, logs, raw evidence and host paths do not.
/// A failed read is never converted into an authoritative empty inventory.
/// </summary>
public sealed class TemporaryStagingInventorySource(
    MemDbContext db,
    DockerClient docker,
    IConfiguration configuration,
    MemControlPlaneRuntimeContext? runtimeContext = null) : ITemporaryStagingInventorySource
{
    internal const int MaximumRecords = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
    private static readonly string[] AllowedLabels =
    [
        "mem.component", "mem.managed-by", "mem.restore-staging.id",
        "mem.restore-staging.source-kind", "mem.restore-staging.catalog-entry-id",
        "mem.migration-staging.candidate-id", "mem.service",
        MemDockerOwnershipLabels.ManagedKey, MemDockerOwnershipLabels.ServiceKey,
        MemDockerOwnershipLabels.ControlPlaneInstanceKey, MemDockerOwnershipLabels.ResourceKey
    ];

    public async Task<StagingInventorySnapshot> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var warnings = new List<string>();
        var resources = new List<StagingObservedResource>();
        var owners = new List<StagingRecordedOwner>();
        var history = new List<StagingRecordedRuntime>();
        var containersAvailable = await ReadBoundedAsync(async token =>
        {
            var containers = await docker.Containers.ListContainersAsync(
                new ContainersListParameters { All = true }, token);
            resources.AddRange(containers.Select(item => new StagingObservedResource(
                "container", item.ID, item.Names?.FirstOrDefault()?.TrimStart('/') ?? string.Empty,
                string.Equals(item.State, "running", StringComparison.Ordinal), SelectLabels(item.Labels))));
        }, "containers-unavailable", warnings, ct);
        var networksAvailable = await ReadBoundedAsync(async token =>
        {
            var networks = await docker.Networks.ListNetworksAsync(new NetworksListParameters(), token);
            resources.AddRange(networks.Select(item => new StagingObservedResource(
                "network", item.ID, item.Name ?? string.Empty, false, SelectLabels(item.Labels))));
        }, "networks-unavailable", warnings, ct);
        var ownershipAvailable = await ReadBoundedAsync(async token =>
        {
            owners.AddRange(await ReadOwnersAsync(token));
        }, "ownership-unavailable", warnings, ct);
        var historyAvailable = await ReadBoundedAsync(async token =>
        {
            history.AddRange(await ReadHistoryAsync(token));
        }, "history-unavailable", warnings, ct);

        return new StagingInventorySnapshot(containersAvailable, networksAvailable,
            ownershipAvailable, historyAvailable, resources, history, owners, warnings,
            runtimeContext?.ControlPlaneInstanceId);
    }

    internal async Task<IReadOnlyList<StagingRecordedOwner>> ReadOwnersAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var owners = new List<StagingRecordedOwner>();
        var migrationRuns = await db.MigrationStagingRuns.AsNoTracking()
            .Where(run => run.PrivateRuntimeStagingId != null)
            .OrderBy(run => run.Id)
            .Select(run => new
            {
                run.PrivateRuntimeStagingId,
                run.DestroyedAtUtc,
                run.StagingRunId,
                RetirementStatus = run.Retirement == null ? null : run.Retirement.Status,
                OwnerId = run.MigrationIntake.IntakeId,
                run.MigrationIntake.DisplayName,
                run.CandidateArtifact.CandidateArtifactId,
                BindingValid = run.CandidateArtifact.ConversionAttempt.MigrationIntakeEntityId == run.MigrationIntakeEntityId
            })
            .Take(MaximumRecords + 1).ToArrayAsync(ct);
        if (migrationRuns.Length > MaximumRecords) throw new InvalidDataException("Inventory limit reached.");
        foreach (var run in migrationRuns)
        {
            owners.Add(new StagingRecordedOwner(run.PrivateRuntimeStagingId!, "migration",
                run.OwnerId, run.DisplayName, run.CandidateArtifactId,
                run.BindingValid && TemporaryStagingInventoryService.IsSafeId(run.CandidateArtifactId),
                run.DestroyedAtUtc is not null && (run.RetirementStatus == null || run.RetirementStatus == "retired"),
                run.StagingRunId, run.RetirementStatus));
        }

        // The staging id must come from an exact private-test operation belonging
        // to the Restore Session. A shared catalog ID alone cannot select a session.
        var operations = await db.RuntimeOperations.AsNoTracking()
            .Where(operation => operation.Operation == "restore.private-test" &&
                                operation.RestoreAttemptId != null && operation.EvidenceJson != null)
            .OrderBy(operation => operation.Id)
            .Select(operation => new
            {
                operation.EvidenceJson,
                OwnerId = operation.RestoreAttempt!.RestoreSessionId,
                DisplayName = operation.RestoreAttempt!.SourceDisplayNameSnapshot,
                CatalogEntryId = operation.RestoreAttempt!.SourceCatalogEntryIdSnapshot,
                SourceKind = operation.RestoreAttempt!.SourceKind
            })
            .Take(MaximumRecords + 1).ToArrayAsync(ct);
        if (operations.Length > MaximumRecords) throw new InvalidDataException("Inventory limit reached.");
        foreach (var operation in operations)
        {
            ct.ThrowIfCancellationRequested();
            if (operation.EvidenceJson!.Length > 1024 * 1024)
                throw new InvalidDataException("Oversized staging evidence.");
            var evidence = JsonSerializer.Deserialize<RestorePrivateTestEvidence>(operation.EvidenceJson, JsonOptions);
            // A failed test can have no staging id. It did not establish runtime ownership.
            if (evidence is null || string.IsNullOrEmpty(evidence.StagingId)) continue;
            var valid = evidence.SourceKind == "backup-catalog" && operation.SourceKind == "backup-catalog" &&
                        evidence.CatalogEntryId == operation.CatalogEntryId &&
                        TemporaryStagingInventoryService.IsSafeId(operation.CatalogEntryId);
            owners.Add(new StagingRecordedOwner(evidence.StagingId, "restore", operation.OwnerId,
                operation.DisplayName, operation.CatalogEntryId, valid, false));
        }
        return owners;
    }

    internal async Task<IReadOnlyList<StagingRecordedRuntime>> ReadHistoryAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var root = Modules.Shared.Storage.MemDataRootResolver.Resolve(configuration);
        var stagingRoot = Path.Combine(root, "restore-staging");
        var historyRoot = Path.Combine(stagingRoot, "history");
        // Inspect attributes directly: Exists() can suppress access errors. Only a
        // genuine missing directory is an empty history. No symlink traversal.
        foreach (var directory in new[] { root, stagingRoot, historyRoot })
        {
            try { RejectReparsePoint(File.GetAttributes(directory)); }
            catch (DirectoryNotFoundException) { return []; }
            catch (FileNotFoundException) { return []; }
        }
        var paths = Directory.EnumerateDirectories(historyRoot).Take(MaximumRecords + 1).ToArray();
        if (paths.Length > MaximumRecords) throw new InvalidDataException("Inventory limit reached.");
        var result = new List<StagingRecordedRuntime>();
        foreach (var directory in paths)
        {
            ct.ThrowIfCancellationRequested();
            RejectReparsePoint(File.GetAttributes(directory));
            var id = Path.GetFileName(directory);
            if (!TemporaryStagingInventoryService.IsSafeId(id))
                throw new InvalidDataException("Invalid staging history identity.");
            var path = Path.Combine(directory, "restore-staging-result.json");
            FileInfo file;
            try
            {
                RejectReparsePoint(File.GetAttributes(path));
                file = new FileInfo(path);
                if (file.Length > 1024 * 1024) throw new InvalidDataException("Oversized staging history.");
            }
            catch (FileNotFoundException)
            {
                // A concurrently written/removed result is incomplete evidence, not
                // proof that this run has no retained resources.
                throw new InvalidDataException("Staging history result is unavailable.");
            }
            await using var stream = File.OpenRead(path);
            var run = await JsonSerializer.DeserializeAsync<PrivateStagingRunResult>(stream, JsonOptions, ct);
            if (run is null || run.StagingId != id || run.Safety is null || run.Runtime is null)
                throw new InvalidDataException("Staging history binding is invalid.");
            var resources = new List<StagingRecordedResource>();
            AddResource(resources, "container", "postgres", run.PostgresContainerId);
            AddResource(resources, "container", "synapse", run.SynapseContainerId);
            AddResource(resources, "container", "element", run.ElementContainerId);
            AddResource(resources, "network", "network", run.NetworkId);
            result.Add(new StagingRecordedRuntime(run.StagingId, run.SourceKind, run.CatalogEntryId,
                run.Status, PrivateStagingService.IsDestroyComplete(run), resources));
        }
        return result;
    }

    private static void AddResource(List<StagingRecordedResource> resources, string kind, string service, string? id)
    {
        if (!string.IsNullOrWhiteSpace(id)) resources.Add(new StagingRecordedResource(kind, service, id));
    }

    private static void RejectReparsePoint(FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked staging history is not accepted.");
    }

    private static IReadOnlyDictionary<string, string> SelectLabels(IDictionary<string, string>? labels) =>
        AllowedLabels.Where(key => labels?.ContainsKey(key) == true)
            .ToDictionary(key => key, key => labels![key], StringComparer.Ordinal);

    private static async Task<bool> ReadBoundedAsync(
        Func<CancellationToken, Task> read,
        string warningCode,
        List<string> warnings,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await read(timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // This browser projection never includes raw host exceptions or paths.
            warnings.Add(warningCode);
            return false;
        }
    }
}
