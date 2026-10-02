using System.Security.Cryptography;
using System.Text;
using Mem.Migrate.Core.Assessment;

namespace Mem.Migrate.Core.Fingerprints;

public static class SourceFingerprint
{
    public static string Compute(
        HostObservation host,
        DockerInventoryObservation docker,
        SystemConfigObservation systemConfig,
        LegacyDatabaseObservation database,
        LegacyFileSystemObservation fileSystem)
    {
        var lines = new List<string>
        {
            $"host.os={host.OperatingSystem}",
            $"host.arch={host.Architecture}",
            $"config.product={systemConfig.ProductName}",
            $"config.version={systemConfig.ProductVersion}"
        };

        foreach (var container in docker.Containers.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            lines.Add(
                $"container={container.Id}|{container.Name}|{container.Image}|{container.ImageId}");

            foreach (var label in container.ManagedLabels.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                lines.Add($"container.label={container.Id}|{label.Key}|{label.Value}");
            }

            foreach (var mount in container.Mounts
                         .OrderBy(x => x.Destination, StringComparer.Ordinal)
                         .ThenBy(x => x.Source, StringComparer.Ordinal))
            {
                lines.Add(
                    $"container.mount={container.Id}|{mount.Type}|{mount.Source}|{mount.Destination}");
            }
        }

        foreach (var candidate in database.Candidates
                     .OrderBy(x => x.ContainerId, StringComparer.Ordinal)
                     .ThenBy(x => x.DatabaseName, StringComparer.Ordinal))
        {
            lines.Add(
                $"database={candidate.ContainerId}|{candidate.DatabaseName}|{candidate.ServerVersion}");

            foreach (var migration in candidate.MigrationIds.OrderBy(x => x, StringComparer.Ordinal))
            {
                lines.Add($"database.migration={candidate.ContainerId}|{migration}");
            }

            foreach (var table in candidate.TableNames.OrderBy(x => x, StringComparer.Ordinal))
            {
                lines.Add($"database.table={candidate.ContainerId}|{table}");
            }

            foreach (var count in candidate.RowCounts.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                lines.Add($"database.count={candidate.ContainerId}|{count.Key}|{count.Value}");
            }

            foreach (var stack in candidate.Stacks.OrderBy(x => x.Id))
            {
                lines.Add(
                    $"stack={stack.Id:D}|{stack.Slug}|{stack.Name}|{stack.Status}|{stack.MatrixInstanceId}");
            }

            foreach (var service in candidate.Services.OrderBy(x => x.Id))
            {
                lines.Add(
                    $"service={service.Id:D}|{service.StackId:D}|{service.ServiceKey}|{service.Image}|{service.Version}|{service.DockerContainerId}|{service.ServerName}|{service.DataPath}");
            }
        }

        foreach (var stack in fileSystem.Stacks.OrderBy(x => x.StackId))
        {
            lines.Add(
                $"files.stack={stack.StackId:D}|{stack.MatrixServiceId:D}|{stack.DataRoot}");

            AddFile(lines, "homeserver", stack.HomeserverConfiguration);
            AddFile(lines, "sqlite", stack.SqliteDatabase);
            AddFile(lines, "signing", stack.SigningKey);
            lines.Add(
                $"files.media={stack.StackId:D}|{stack.MediaStore.Path}|{stack.MediaStore.TotalBytes}|{stack.MediaStore.FileCount}|{stack.MediaStore.Complete}");
        }

        var canonical = string.Join(
            "\n",
            lines.OrderBy(x => x, StringComparer.Ordinal));

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static void AddFile(
        ICollection<string> lines,
        string kind,
        FileObservation file)
    {
        lines.Add(
            $"files.{kind}={file.Path}|{file.Exists}|{file.SizeBytes}|{file.LastWriteAtUtc:O}");
    }
}
