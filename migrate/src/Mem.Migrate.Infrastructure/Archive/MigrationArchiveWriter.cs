using System.IO.Compression;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Infrastructure.Archive;

public sealed class MigrationArchiveWriter : IMigrationArchiveWriter
{
    private static readonly DateTimeOffset DeterministicTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task<MigrationArchiveWriteResult> WriteAsync(
        string stagingRoot,
        string outputPath,
        ArchiveSafetyLimits limits,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(stagingRoot);
        var destination = Path.GetFullPath(outputPath);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Archive staging root does not exist: {root}");
        }

        var files = EnumerateFiles(root)
            .OrderBy(item => item.ArchivePath, StringComparer.Ordinal)
            .ToArray();

        ArchivePathPolicy.EnsureUnique(
            files.Select(item => item.ArchivePath),
            requireSorted: true);
        ValidateLimits(files, limits);

        var destinationDirectory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException(
                "Archive destination has no parent directory.");
        PrivateFilePermissions.EnsureDirectory(destinationDirectory);

        if (File.Exists(destination))
        {
            throw new IOException(
                $"Archive destination already exists: {destination}");
        }

        var partialPath = destination + ".partial";
        File.Delete(partialPath);
        var ownsDestination = false;

        try
        {
            await using (var output = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: true))
            using (var archive = new ZipArchive(
                output,
                ZipArchiveMode.Create,
                leaveOpen: false))
            {
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry(
                        file.ArchivePath,
                        CompressionLevel.NoCompression);
                    entry.LastWriteTime = DeterministicTimestamp;
                    entry.ExternalAttributes = unchecked((int)(0x8180u << 16));

                    await using var source = new FileStream(
                        file.FullPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 1024 * 1024,
                        useAsync: true);
                    await using var target = entry.Open();
                    await source.CopyToAsync(target, cancellationToken);
                }
            }

            PrivateFilePermissions.EnsureFile(partialPath);
            var size = new FileInfo(partialPath).Length;
            var sha256 = await Sha256File.ComputeAsync(
                partialPath,
                cancellationToken);
            File.Move(partialPath, destination);
            ownsDestination = true;
            PrivateFilePermissions.EnsureFile(destination);

            return new MigrationArchiveWriteResult(
                destination,
                sha256,
                size,
                files.Length,
                files.Sum(file => file.SizeBytes));
        }
        catch
        {
            File.Delete(partialPath);

            if (ownsDestination)
            {
                File.Delete(destination);
            }

            throw;
        }
    }

    private static IEnumerable<StagingFile> EnumerateFiles(string root)
    {
        var rootDirectory = new DirectoryInfo(root);
        UnixFileTypeSafety.EnsureDirectory(rootDirectory.FullName);

        if ((rootDirectory.Attributes & FileAttributes.ReparsePoint) != 0 ||
            rootDirectory.LinkTarget is not null)
        {
            throw new InvalidDataException(
                "Archive staging root may not be a symbolic link.");
        }

        var pending = new Stack<DirectoryInfo>();
        pending.Push(rootDirectory);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var children = directory
                .EnumerateFileSystemInfos()
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .ToArray();

            for (var index = children.Length - 1; index >= 0; index--)
            {
                var child = children[index];

                if ((child.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    child.LinkTarget is not null)
                {
                    throw new InvalidDataException(
                        $"Archive staging may not contain symbolic links: {child.FullName}");
                }

                if (child is DirectoryInfo childDirectory)
                {
                    UnixFileTypeSafety.EnsureDirectory(childDirectory.FullName);
                    pending.Push(childDirectory);
                    continue;
                }

                if (child is not FileInfo file ||
                    (file.Attributes & FileAttributes.Directory) != 0)
                {
                    throw new InvalidDataException(
                        $"Archive staging contains a non-regular filesystem entry: {child.FullName}");
                }

                UnixFileTypeSafety.EnsureRegularFile(file.FullName);

                if (SqliteSnapshotArtifactPolicy.IsTemporarySidecar(
                        file.FullName))
                {
                    continue;
                }

                yield return new StagingFile(
                    file.FullName,
                    ArchivePathPolicy.FromStagingPath(root, file.FullName),
                    file.Length);
            }
        }
    }

    private static void ValidateLimits(
        IReadOnlyCollection<StagingFile> files,
        ArchiveSafetyLimits limits)
    {
        if (files.Count > limits.MaximumEntries)
        {
            throw new InvalidDataException(
                $"Archive entry count {files.Count} exceeds the supported maximum {limits.MaximumEntries}.");
        }

        var total = 0L;

        foreach (var file in files)
        {
            if (file.SizeBytes > limits.MaximumEntryBytes)
            {
                throw new InvalidDataException(
                    $"Archive entry '{file.ArchivePath}' exceeds the supported per-file limit.");
            }

            total = checked(total + file.SizeBytes);

            if (total > limits.MaximumExpandedBytes)
            {
                throw new InvalidDataException(
                    "Archive expanded size exceeds the supported limit.");
            }
        }
    }

    private sealed record StagingFile(
        string FullPath,
        string ArchivePath,
        long SizeBytes);
}
