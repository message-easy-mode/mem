using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Legacy.V010.Capture;

public sealed class SourceCaptureFileSystem
{
    private const long FixedHeadroomBytes = 256L * 1024 * 1024;

    public CapturePlanSummary BuildPlan(
        AssessmentResult assessment,
        Guid sourceStackId,
        CaptureOptions options)
    {
        var files = new Dictionary<string, SourceFile>(StringComparer.Ordinal);

        var selectedStacks = assessment.FileSystem.Stacks
            .Where(stack => stack.StackId == sourceStackId)
            .ToArray();
        if (selectedStacks.Length != 1)
        {
            throw new InvalidOperationException(
                "The selected source stack was not found exactly once in the assessed filesystem inventory.");
        }

        foreach (var stack in selectedStacks)
        {
            AddObservedFile(files, stack.DataRoot, stack.SqliteDatabase);
            AddObservedFile(files, stack.DataRoot, stack.HomeserverConfiguration);
            AddObservedFile(files, stack.DataRoot, stack.SigningKey);

            if (stack.ElementConfiguration is not null)
            {
                AddObservedFile(
                    files,
                    stack.ElementDataRoot,
                    stack.ElementConfiguration);
            }

            var excludedConfigurationDirectories = new HashSet<string>(PathComparer);

            if (stack.MediaStore.Exists)
            {
                ValidateSourcePath(stack.DataRoot, stack.MediaStore.Path);
                excludedConfigurationDirectories.Add(
                    Path.GetFullPath(stack.MediaStore.Path)
                        .TrimEnd(Path.DirectorySeparatorChar));

                foreach (var file in EnumerateRegularFiles(stack.MediaStore.Path))
                {
                    files.TryAdd(file.FullPath, file);
                }
            }

            if (!string.IsNullOrWhiteSpace(stack.DataRoot))
            {
                foreach (var file in EnumerateRegularFiles(
                    stack.DataRoot,
                    excludedConfigurationDirectories))
                {
                    if (IsAdditionalConfigurationFile(file.FullPath))
                    {
                        files.TryAdd(file.FullPath, file);
                    }
                }
            }
        }

        var estimatedBytes = checked(
            files.Values.Sum(file => file.SizeBytes) +
            Math.Max(4L * 1024 * 1024, files.Count * 4096L));
        var estimatedEntries = checked(files.Count + 10);

        if (estimatedBytes > options.MaximumExpandedBytes)
        {
            throw new InvalidOperationException(
                $"Estimated capture size {estimatedBytes} bytes exceeds the supported expanded limit {options.MaximumExpandedBytes} bytes.");
        }

        if (files.Values.Any(file => file.SizeBytes > options.MaximumEntryBytes))
        {
            throw new InvalidOperationException(
                "At least one selected-stack source file exceeds the supported per-entry limit.");
        }

        if (estimatedEntries > options.MaximumEntries)
        {
            throw new InvalidOperationException(
                $"Estimated archive entry count {estimatedEntries} exceeds the supported maximum {options.MaximumEntries}.");
        }

        var workspaceAvailableBytes = GetAvailableBytes(
            options.Assessment.WorkspacePath);
        var outputAvailableBytes = GetAvailableBytes(options.OutputPath);
        var availableBytes = Math.Min(
            workspaceAvailableBytes,
            outputAvailableBytes);
        var effectiveMultiplier = string.IsNullOrWhiteSpace(options.AgeRecipient)
            ? options.RequiredFreeSpaceMultiplier
            : Math.Max(options.RequiredFreeSpaceMultiplier, 3.25);
        var requiredBytes = checked(
            (long)Math.Ceiling(estimatedBytes * effectiveMultiplier) +
            FixedHeadroomBytes);

        if (workspaceAvailableBytes < requiredBytes ||
            outputAvailableBytes < requiredBytes)
        {
            throw new InvalidOperationException(
                $"Insufficient private staging or output disk. Required at least {requiredBytes} bytes on each selected filesystem; workspace has {workspaceAvailableBytes} bytes and output has {outputAvailableBytes} bytes available.");
        }

        return new CapturePlanSummary(
            StackCount: 1,
            EstimatedExpandedBytes: estimatedBytes,
            AvailableBytes: availableBytes,
            RequiredAvailableBytes: requiredBytes,
            EstimatedEntryCount: estimatedEntries);
    }

    public async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CaptureWriteBudget budget,
        CancellationToken cancellationToken,
        string? containmentRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(containmentRoot))
        {
            ValidateSourcePath(containmentRoot, sourcePath);
        }

        var source = ValidateRegularFile(sourcePath);
        var sourceLength = source.Length;
        var sourceLastWriteAtUtc = source.LastWriteTimeUtc;
        budget.Reserve(sourceLength);
        var destination = Path.GetFullPath(destinationPath);
        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException(
                "Capture destination has no parent directory.");
        PrivateFilePermissions.EnsureDirectory(parent);
        if (File.Exists(destination))
        {
            throw new IOException(
                $"Capture destination already exists: {destination}");
        }

        var partialPath = destination + ".partial";
        File.Delete(partialPath);
        var ownsDestination = false;

        try
        {
            await using var input = new FileStream(
                source.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1024 * 1024,
                useAsync: true);
            await using var output = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: true);
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);

            source.Refresh();

            if (!source.Exists ||
                source.Length != sourceLength ||
                source.LastWriteTimeUtc != sourceLastWriteAtUtc)
            {
                throw new IOException(
                    $"Source file changed while it was being captured: {source.FullName}");
            }

            PrivateFilePermissions.EnsureFile(partialPath);
            File.Move(partialPath, destination);
            ownsDestination = true;
            PrivateFilePermissions.EnsureFile(destination);
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

    public async Task CopyDirectoryAsync(
        string sourceRoot,
        string destinationRoot,
        CaptureWriteBudget budget,
        CancellationToken cancellationToken,
        string? containmentRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(containmentRoot))
        {
            ValidateSourcePath(containmentRoot, sourceRoot);
        }

        var source = ValidateDirectory(sourceRoot);
        PrivateFilePermissions.EnsureDirectory(destinationRoot);

        foreach (var file in EnumerateRegularFiles(source.FullName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source.FullName, file.FullPath);
            var destination = Path.GetFullPath(
                Path.Combine(destinationRoot, relative));
            EnsureContained(destinationRoot, destination);
            await CopyFileAsync(
                file.FullPath,
                destination,
                budget,
                cancellationToken,
                source.FullName);
        }
    }


    public async Task<string[]> CopyAdditionalConfigurationAsync(
        string sourceRoot,
        string destinationRoot,
        IEnumerable<string> excludedFiles,
        IEnumerable<string> excludedDirectories,
        CaptureWriteBudget budget,
        CancellationToken cancellationToken)
    {
        var source = ValidateDirectory(sourceRoot);
        var excludedFileSet = excludedFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToHashSet(PathComparer);
        var excludedDirectorySet = excludedDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar))
            .ToHashSet(PathComparer);
        var copied = new List<string>();

        foreach (var file in EnumerateRegularFiles(
            source.FullName,
            excludedDirectorySet))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (excludedFileSet.Contains(file.FullPath) ||
                !IsAdditionalConfigurationFile(file.FullPath))
            {
                continue;
            }

            var relative = Path.GetRelativePath(source.FullName, file.FullPath);
            var destination = Path.GetFullPath(
                Path.Combine(destinationRoot, relative));
            EnsureContained(destinationRoot, destination);
            await CopyFileAsync(
                file.FullPath,
                destination,
                budget,
                cancellationToken,
                source.FullName);
            copied.Add(destination);
        }

        return copied
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    public static void ValidateSourcePath(
        string? rootPath,
        string candidatePath)
    {
        EnsureContained(rootPath, candidatePath);
        var root = ValidateDirectory(rootPath!);
        var candidate = Path.GetFullPath(candidatePath);
        var relative = Path.GetRelativePath(root.FullName, candidate);
        var current = root.FullName;

        foreach (var segment in relative.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo information = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);

            if (!information.Exists)
            {
                throw new FileNotFoundException(
                    "A required source path was not found.",
                    current);
            }

            if ((information.Attributes & FileAttributes.ReparsePoint) != 0 ||
                information.LinkTarget is not null)
            {
                throw new InvalidDataException(
                    $"Source capture refuses symbolic-link path segments: {current}");
            }
        }
    }

    public static void EnsureContained(string? rootPath, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidDataException(
                "A source file did not have an assessed containment root.");
        }

        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar);
        var rootPrefix = root + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(candidatePath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.Equals(candidate, root, comparison) &&
            !candidate.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidDataException(
                $"Source path escaped its assessed data root: {candidate}");
        }
    }

    private static void AddObservedFile(
        IDictionary<string, SourceFile> files,
        string? root,
        FileObservation observation)
    {
        if (!observation.Exists || !observation.IsRegularFile)
        {
            throw new InvalidDataException(
                $"Required source file is unavailable: {observation.Path}");
        }

        ValidateSourcePath(root, observation.Path);
        var information = ValidateRegularFile(observation.Path);
        files.TryAdd(
            information.FullName,
            new SourceFile(information.FullName, information.Length));
    }

    private static IEnumerable<SourceFile> EnumerateRegularFiles(
        string rootPath,
        ISet<string>? excludedDirectories = null)
    {
        var root = ValidateDirectory(rootPath);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

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
                        $"Source capture refuses symbolic links: {child.FullName}");
                }

                if (child is DirectoryInfo childDirectory)
                {
                    UnixFileTypeSafety.EnsureDirectory(childDirectory.FullName);
                    var normalizedDirectory = childDirectory.FullName
                        .TrimEnd(Path.DirectorySeparatorChar);

                    if (excludedDirectories is null ||
                        !excludedDirectories.Contains(normalizedDirectory))
                    {
                        pending.Push(childDirectory);
                    }

                    continue;
                }

                if (child is not FileInfo childFile)
                {
                    throw new InvalidDataException(
                        $"Source capture refuses a non-regular filesystem entry: {child.FullName}");
                }

                UnixFileTypeSafety.EnsureRegularFile(childFile.FullName);
                yield return new SourceFile(
                    childFile.FullName,
                    childFile.Length);
            }
        }
    }

    private static bool IsAdditionalConfigurationFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".yml", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".conf", StringComparison.OrdinalIgnoreCase);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static FileInfo ValidateRegularFile(string path)
    {
        var information = new FileInfo(Path.GetFullPath(path));

        if (!information.Exists)
        {
            throw new FileNotFoundException(
                "A required source file was not found.",
                information.FullName);
        }

        RejectSymbolicLinkAncestors(information.FullName);
        UnixFileTypeSafety.EnsureRegularFile(information.FullName);

        if ((information.Attributes & FileAttributes.ReparsePoint) != 0 ||
            information.LinkTarget is not null)
        {
            throw new InvalidDataException(
                $"Source capture refuses symbolic links: {information.FullName}");
        }

        return information;
    }

    private static DirectoryInfo ValidateDirectory(string path)
    {
        var information = new DirectoryInfo(Path.GetFullPath(path));

        if (!information.Exists)
        {
            throw new DirectoryNotFoundException(
                $"A required source directory was not found: {information.FullName}");
        }

        RejectSymbolicLinkAncestors(information.FullName);
        UnixFileTypeSafety.EnsureDirectory(information.FullName);

        if ((information.Attributes & FileAttributes.ReparsePoint) != 0 ||
            information.LinkTarget is not null)
        {
            throw new InvalidDataException(
                $"Source capture refuses symbolic-link directories: {information.FullName}");
        }

        return information;
    }

    private static void RejectSymbolicLinkAncestors(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidDataException(
                "A source path had no filesystem root.");
        var relative = Path.GetRelativePath(root, fullPath);
        var current = root;

        foreach (var segment in relative.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo information = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);

            if (information.Exists &&
                ((information.Attributes & FileAttributes.ReparsePoint) != 0 ||
                 information.LinkTarget is not null))
            {
                throw new InvalidDataException(
                    $"Source capture refuses symbolic-link path segments: {current}");
            }
        }
    }

    private static long GetAvailableBytes(string targetPath)
    {
        var current = Path.GetFullPath(targetPath);

        while (!Directory.Exists(current))
        {
            current = Path.GetDirectoryName(current)
                ?? throw new IOException(
                    "Could not resolve an existing parent for the capture output path.");
        }

        var root = Path.GetPathRoot(current)
            ?? throw new IOException(
                "Could not resolve the capture output filesystem root.");
        return new DriveInfo(root).AvailableFreeSpace;
    }

    private sealed record SourceFile(string FullPath, long SizeBytes);
}

public sealed class CaptureWriteBudget(
    long maximumEntryBytes,
    long maximumExpandedBytes,
    int maximumEntries)
{
    private long _expandedBytes;
    private int _entries;

    public long ExpandedBytes => _expandedBytes;
    public int Entries => _entries;

    public void Reserve(long bytes)
    {
        if (bytes < 0 || bytes > maximumEntryBytes)
        {
            throw new InvalidDataException(
                "A captured file exceeds the supported per-entry limit.");
        }

        var expanded = checked(_expandedBytes + bytes);
        var entries = checked(_entries + 1);

        if (expanded > maximumExpandedBytes)
        {
            throw new InvalidDataException(
                "Captured files exceed the supported expanded-size limit.");
        }

        if (entries > maximumEntries)
        {
            throw new InvalidDataException(
                "Captured files exceed the supported entry-count limit.");
        }

        _expandedBytes = expanded;
        _entries = entries;
    }
}
