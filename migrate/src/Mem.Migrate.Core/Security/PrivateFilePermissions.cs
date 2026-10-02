namespace Mem.Migrate.Core.Security;

public static class PrivateFilePermissions
{
    public static void EnsureDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        EnsureNoSymbolicLinkSegments(fullPath);
        Directory.CreateDirectory(fullPath);
        EnsureNoSymbolicLinkSegments(fullPath);

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                fullPath,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Could not enforce private permissions on directory '{fullPath}'.",
                ex);
        }
    }

    public static void EnsureFile(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            return;
        }

        EnsureNoSymbolicLinkSegments(fullPath);

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                fullPath,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Could not enforce private permissions on file '{fullPath}'.",
                ex);
        }
    }

    private static void EnsureNoSymbolicLinkSegments(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new IOException(
                "A private migration path had no filesystem root.");
        var relative = Path.GetRelativePath(root, fullPath);
        var current = root;

        foreach (var segment in relative.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo? information = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : File.Exists(current)
                    ? new FileInfo(current)
                    : null;

            if (information is not null &&
                ((information.Attributes & FileAttributes.ReparsePoint) != 0 ||
                 information.LinkTarget is not null))
            {
                throw new IOException(
                    $"Private migration paths may not contain symbolic links: {current}");
            }
        }
    }
}
