using System.Text;

namespace Mem.Migrate.Core.Capture;

public static class ArchivePathPolicy
{
    public const string Root = "mem-migration";

    public static string Normalize(string path) =>
        NormalizeCore(path, allowRootOnly: false);

    public static string NormalizeDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var trimmed = path.EndsWith("/", StringComparison.Ordinal)
            ? path[..^1]
            : path;

        if (trimmed.Length == 0 ||
            trimmed.EndsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Archive directory paths may have at most one trailing slash.");
        }

        return NormalizeCore(trimmed, allowRootOnly: true);
    }

    public static void EnsureUnique(
        IEnumerable<string> paths,
        bool requireSorted = false)
    {
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var caseFolded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? previous = null;

        foreach (var original in paths)
        {
            var path = Normalize(original);

            if (!exact.Add(path))
            {
                throw new InvalidDataException(
                    $"Duplicate archive path detected: '{path}'.");
            }

            if (!caseFolded.Add(path))
            {
                throw new InvalidDataException(
                    $"Case-insensitive archive path collision detected: '{path}'.");
            }

            if (requireSorted && previous is not null &&
                string.CompareOrdinal(previous, path) > 0)
            {
                throw new InvalidDataException(
                    "Archive paths are not in deterministic ordinal order.");
            }

            previous = path;
        }
    }

    public static string FromStagingPath(
        string stagingRoot,
        string fullPath)
    {
        var root = Path.GetFullPath(stagingRoot);
        var candidate = Path.GetFullPath(fullPath);
        var relative = Path.GetRelativePath(root, candidate)
            .Replace(Path.DirectorySeparatorChar, '/');

        if (relative.StartsWith("../", StringComparison.Ordinal) ||
            relative == "..")
        {
            throw new InvalidDataException(
                "A staging file escaped the archive root.");
        }

        return Normalize($"{Root}/{relative}");
    }

    private static string NormalizeCore(string path, bool allowRootOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (path.IndexOf('\0') >= 0)
        {
            throw new InvalidDataException("Archive paths may not contain NUL characters.");
        }

        if (path.Contains('\\'))
        {
            throw new InvalidDataException("Archive paths must use forward slashes.");
        }

        if (Path.IsPathRooted(path) ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            HasDrivePrefix(path))
        {
            throw new InvalidDataException("Archive paths must be relative.");
        }

        var unicodeNormalized = path.Normalize(NormalizationForm.FormC);

        if (!string.Equals(path, unicodeNormalized, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Archive paths must already use Unicode normalization form C.");
        }

        var segments = path.Split('/', StringSplitOptions.None);

        if (segments.Any(segment =>
                segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException(
                "Archive paths may not contain empty, current, or parent segments.");
        }

        if (!string.Equals(segments[0], Root, StringComparison.Ordinal) ||
            (!allowRootOnly && segments.Length < 2))
        {
            throw new InvalidDataException(
                $"Archive paths must begin with '{Root}/'.");
        }

        foreach (var segment in segments)
        {
            if (segment.EndsWith(" ", StringComparison.Ordinal) ||
                segment.EndsWith(".", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Archive path segments may not end with a space or period.");
            }

            if (segment.Any(character => char.IsControl(character)))
            {
                throw new InvalidDataException(
                    "Archive paths may not contain control characters.");
            }
        }

        return string.Join('/', segments);
    }

    private static bool HasDrivePrefix(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';
}
