using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using HostAgent.Runtime.Services.TemporaryStaging;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Migrations.Staging.Retirement;

/// <summary>Bounded access to the exact history/run directories. No recorded path is used as deletion authority.</summary>
public sealed class MigrationStagingRetirementHistory(IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string Workspace(string stagingId) => Path.Combine(Root(), "restore-staging", "runs", SafeId(stagingId));
    private string ResultPath(string stagingId) => Path.Combine(Root(), "restore-staging", "history", SafeId(stagingId), "restore-staging-result.json");
    private string Root() => Modules.Shared.Storage.MemDataRootResolver.Resolve(configuration);
    private static string SafeId(string value) => TemporaryStagingInventoryService.IsSafeId(value)
        ? value : throw new MigrationStagingRetirementException("ownership-unproven");

    public async Task<(PrivateStagingRunResult Result, MigrationStagingRetirementPlan Plan)> ReadAsync(
        string stagingId, string candidateArtifactId, CancellationToken ct)
    {
        var path = ResultPath(stagingId);
        if (!CheckPath(path) || new FileInfo(path).Length > 1024 * 1024)
            throw new MigrationStagingRetirementException("history-unavailable");
        await using var stream = File.OpenRead(path);
        var run = await JsonSerializer.DeserializeAsync<PrivateStagingRunResult>(stream, JsonOptions, ct)
            ?? throw new MigrationStagingRetirementException("history-unavailable");
        if (run.StagingId != stagingId || run.SourceKind != "migration-candidate" ||
            !string.IsNullOrEmpty(run.CatalogEntryId) || run.Safety is null || run.Runtime is null ||
            !run.Safety.PrivateOnly || !run.Safety.DockerNetworkInternal || run.Safety.PublicRoutesCreated ||
            run.Safety.ProductionContainersTouched || run.Safety.ProductionDatabasesTouched ||
            run.WorkspacePath != Workspace(stagingId) ||
            run.NetworkName != $"mem-restore-staging-{stagingId}" ||
            run.PostgresContainerName != $"mem-restore-staging-postgres-{stagingId}" ||
            run.SynapseContainerName != $"mem-restore-staging-synapse-{stagingId}" ||
            (!string.IsNullOrEmpty(run.ElementContainerName) && run.ElementContainerName != $"mem-restore-staging-element-{stagingId}"))
            throw new MigrationStagingRetirementException("ownership-unproven");
        // Excludes mutable cleanup status and evidence so interrupted cleanup can replay.
        var identity = JsonSerializer.Serialize(new { stagingId, candidateArtifactId, run.WorkspacePath,
            run.SourceKind, run.StartedAtUtc, run.ElementContainerId, run.SynapseContainerId,
            run.PostgresContainerId, run.NetworkId });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        var plan = new MigrationStagingRetirementPlan(stagingId, candidateArtifactId, hash,
            run.ElementContainerId, run.SynapseContainerId, run.PostgresContainerId, run.NetworkId);
        var ids = plan.Containers.Where(x => x.Id is not null).Select(x => x.Id!).ToArray();
        if (ids.Any(x => !IsDockerId(x)) || (plan.NetworkId is not null && !IsDockerId(plan.NetworkId)) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new MigrationStagingRetirementException("ownership-unproven");
        return (run, plan);
    }

    public bool WorkspacePresent(string stagingId) => CheckPath(Workspace(stagingId));

    public void ValidateWorkspace(string stagingId, CancellationToken ct)
    {
        var path = Workspace(stagingId);
        if (!CheckPath(path)) return;
        // Enumerate without following links; validate each directory before descending.
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                ct.ThrowIfCancellationRequested();
                var attrs = File.GetAttributes(entry);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    throw new MigrationStagingRetirementException("workspace-unsafe");
                if ((attrs & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
    }

    public void RemoveWorkspace(string stagingId, CancellationToken ct)
    {
        ValidateWorkspace(stagingId, ct);
        var path = Workspace(stagingId);
        if (CheckPath(path)) Directory.Delete(path, recursive: true);
        if (CheckPath(path)) throw new MigrationStagingRetirementException("cleanup-incomplete");
    }

    public async Task RecordRetiredAsync(MigrationStagingRetirementPlan accepted, CancellationToken ct)
    {
        var (existing, current) = await ReadAsync(accepted.StagingId, accepted.CandidateArtifactId, ct);
        if (current != accepted) throw new MigrationStagingRetirementException("ownership-changed");
        var result = existing with
        {
            Status = "destroyed",
            Destroy = new PrivateStagingDestroySummary(DateTimeOffset.UtcNow, true, true, true, true, [], true),
            Detail = "Migration-owned temporary staging was retired. Historical verification evidence is retained."
        };
        var path = ResultPath(accepted.StagingId);
        CheckPath(path);
        var temporary = path + ".retirement-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(result, JsonOptions), ct);
            CheckPath(path);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static bool IsDockerId(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    internal static bool Overlaps(string a, string b)
    {
        a = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar);
        b = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar);
        return a == b || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    // File.Exists/Directory.Exists hide permission failures. Only genuine absence is success.
    internal static bool CheckPath(string path)
    {
        var full = Path.GetFullPath(path);
        var ancestors = new Stack<string>();
        for (var next = full; next is not null; next = Path.GetDirectoryName(next))
        {
            ancestors.Push(next);
            if (next == Path.GetPathRoot(next)) break;
        }
        while (ancestors.TryPop(out var entry))
        {
            try
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    throw new MigrationStagingRetirementException("workspace-unsafe");
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }
        return true;
    }
}
