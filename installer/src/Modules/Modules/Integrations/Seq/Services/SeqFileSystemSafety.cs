namespace Modules.Integrations.Seq.Services;

internal static class SeqFileSystemSafety
{
    public static string ResolveFile(
        string value,
        string contentRootPath,
        string code)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw SafeException(code, "The configured Seq file path is invalid.");
        }

        try
        {
            var fullPath = Path.IsPathRooted(value)
                ? Path.GetFullPath(value)
                : Path.GetFullPath(Path.Combine(contentRootPath, value));
            if (string.IsNullOrWhiteSpace(Path.GetFileName(fullPath)))
            {
                throw SafeException(code, "The configured Seq file path is invalid.");
            }

            return fullPath;
        }
        catch (SeqOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or
                PathTooLongException)
        {
            throw SafeException(code, "The configured Seq file path is invalid.");
        }
    }

    public static string ResolveDirectory(
        string value,
        string contentRootPath,
        string code)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw SafeException(code, "The configured Seq directory path is invalid.");
        }

        try
        {
            var fullPath = Path.IsPathRooted(value)
                ? Path.GetFullPath(value)
                : Path.GetFullPath(Path.Combine(contentRootPath, value));
            var root = Path.GetPathRoot(fullPath);
            if (string.Equals(
                    fullPath.TrimEnd(Path.DirectorySeparatorChar),
                    root?.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.Ordinal))
            {
                throw SafeException(code, "The configured Seq directory path is invalid.");
            }

            return fullPath;
        }
        catch (SeqOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or
                PathTooLongException)
        {
            throw SafeException(code, "The configured Seq directory path is invalid.");
        }
    }

    public static void EnsureFileWithinRoot(
        string rootPath,
        string filePath,
        string code)
    {
        if (!SeqDiagnosticsOptionsValidator.IsPathWithinRoot(rootPath, filePath))
        {
            throw SafeException(code, "The configured Seq secret file is outside the approved server-owned root.");
        }
    }

    public static void EnsureSafeParentDirectory(
        string rootPath,
        string filePath,
        string code)
    {
        var root = Path.GetFullPath(rootPath);
        var parent = Path.GetDirectoryName(filePath)
            ?? throw SafeException(code, "The configured Seq file path has no parent directory.");

        EnsureExistingComponentsAreNotLinks(root, code);
        EnsureExistingComponentsAreNotLinks(parent, code);
        Directory.CreateDirectory(parent);
        EnsureExistingComponentsAreNotLinks(parent, code);
        SetDirectoryMode(root);
        SetDirectoryMode(parent);
    }

    public static void EnsureTargetIsNotLink(string path, string code)
    {
        if (IsLink(path))
        {
            throw SafeException(code, "The configured Seq file target is a symbolic link.");
        }
    }

    public static void EnsurePathContainsNoLinks(string path, string code)
    {
        EnsureExistingComponentsAreNotLinks(path, code);
    }

    public static bool IsLink(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (file.LinkTarget is not null)
            {
                return true;
            }

            var directory = new DirectoryInfo(path);
            if (directory.LinkTarget is not null)
            {
                return true;
            }

            if (File.Exists(path) || Directory.Exists(path))
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }

        return false;
    }

    public static void SetSecretFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public static void SetDirectoryMode(string path)
    {
        if (!OperatingSystem.IsWindows() && Directory.Exists(path))
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
    }

    private static void EnsureExistingComponentsAreNotLinks(
        string path,
        string code)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw SafeException(code, "The configured Seq path is invalid.");
        }

        var current = root;
        var relative = Path.GetRelativePath(root, fullPath);
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current) || IsLink(current)) &&
                IsLink(current))
            {
                throw SafeException(code, "The configured Seq path contains a symbolic link.");
            }
        }
    }

    private static SeqOperationException SafeException(string code, string message) =>
        new(code, Microsoft.AspNetCore.Http.StatusCodes.Status409Conflict, message);
}
