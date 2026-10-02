using System.IO.Compression;
using System.Text.Json;

namespace HostAgent.Runtime.Migrations.Staging;

public sealed record MigrationArchiveMaterialPaths(
    Guid SourceStackId,
    string MatrixServerName,
    string HomeserverConfigurationPath,
    string SigningKeyPath,
    string? MediaPath,
    string? ElementConfigurationPath);

public static class MigrationArchiveMaterialResolver
{
    private const string Root = "mem-migration";
    private const string ManifestPath = Root + "/migration-manifest.json";
    private const long MaximumManifestBytes = 16L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static MigrationArchiveMaterialPaths Resolve(
        ZipArchive archive,
        Guid expectedSourceStackId,
        string expectedMatrixServerName)
    {
        ArgumentNullException.ThrowIfNull(archive);

        if (expectedSourceStackId == Guid.Empty)
            throw new InvalidDataException("Migration candidate provenance contains an invalid source stack identity.");
        if (string.IsNullOrWhiteSpace(expectedMatrixServerName))
            throw new InvalidDataException("Migration candidate provenance contains no Matrix server name.");

        var manifestEntry = archive.GetEntry(ManifestPath)
            ?? throw new InvalidDataException($"Migration archive is missing required entry '{ManifestPath}'.");
        if (manifestEntry.Length <= 0 || manifestEntry.Length > MaximumManifestBytes)
            throw new InvalidDataException("Migration archive manifest has an invalid size.");

        ArchiveManifest manifest;
        try
        {
            using var manifestStream = manifestEntry.Open();
            manifest = JsonSerializer.Deserialize<ArchiveManifest>(manifestStream, JsonOptions)
                ?? throw new JsonException("Migration archive manifest was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Migration archive manifest is invalid.", exception);
        }

        if (!string.Equals(manifest.Schema, "mem-v010-migration", StringComparison.Ordinal) ||
            manifest.SchemaVersion != 2 ||
            manifest.Stacks is null)
        {
            throw new InvalidDataException("Migration archive manifest is unsupported or incomplete.");
        }

        var matchingStacks = manifest.Stacks
            .Where(stack => stack is not null && stack.SourceStackId == expectedSourceStackId)
            .ToArray();
        if (matchingStacks.Length != 1)
        {
            throw new InvalidDataException(
                $"Migration archive does not contain exactly one stack matching candidate source stack '{expectedSourceStackId:D}'.");
        }

        var stack = matchingStacks[0]!;
        if (!string.Equals(stack.MatrixServerName, expectedMatrixServerName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Migration candidate Matrix server identity does not match the validated archive manifest.");
        }

        var stackRoot = $"{Root}/stacks/{expectedSourceStackId:D}";
        var homeserverPath = RequireCanonicalFile(
            stack.HomeserverConfigurationPath,
            $"{stackRoot}/synapse/homeserver.yaml",
            "homeserver configuration");
        var signingKeyPath = RequireCanonicalFile(
            stack.SigningKeyPath,
            $"{stackRoot}/synapse/signing.key",
            "signing key");
        var mediaPath = OptionalCanonicalDirectory(
            stack.MediaPath,
            $"{stackRoot}/media",
            "media store");
        var elementPath = OptionalCanonicalFile(
            stack.ElementConfigurationPath,
            $"{stackRoot}/element/config.json",
            "Element configuration");

        EnsureEntryExists(archive, homeserverPath);
        EnsureEntryExists(archive, signingKeyPath);
        if (elementPath is not null)
            EnsureEntryExists(archive, elementPath);

        return new MigrationArchiveMaterialPaths(
            stack.SourceStackId,
            stack.MatrixServerName,
            homeserverPath,
            signingKeyPath,
            mediaPath,
            elementPath);
    }

    private static string RequireCanonicalFile(string? actual, string expected, string description)
    {
        if (string.IsNullOrWhiteSpace(actual))
            throw new InvalidDataException($"Migration archive manifest does not identify the stack {description}.");

        var normalized = NormalizeFilePath(actual);
        if (!string.Equals(normalized, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"Migration archive manifest contains a non-canonical {description} path.");
        return normalized;
    }

    private static string? OptionalCanonicalFile(string? actual, string expected, string description)
    {
        if (string.IsNullOrWhiteSpace(actual)) return null;
        var normalized = NormalizeFilePath(actual);
        if (!string.Equals(normalized, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"Migration archive manifest contains a non-canonical {description} path.");
        return normalized;
    }

    private static string? OptionalCanonicalDirectory(string? actual, string expected, string description)
    {
        if (string.IsNullOrWhiteSpace(actual)) return null;
        var normalized = NormalizeFilePath(actual).TrimEnd('/');
        if (!string.Equals(normalized, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"Migration archive manifest contains a non-canonical {description} path.");
        return normalized;
    }

    private static string NormalizeFilePath(string value)
    {
        var path = value.Trim();
        if (path.Length == 0 ||
            path.Contains('\\') ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            path.Contains('\0') ||
            path.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException("Migration archive manifest contains an unsafe material path.");
        }

        return path;
    }

    private static void EnsureEntryExists(ZipArchive archive, string path)
    {
        if (archive.GetEntry(path) is null)
            throw new InvalidDataException($"Migration archive is missing required entry '{path}'.");
    }

    private sealed record ArchiveManifest(
        string Schema,
        int SchemaVersion,
        ArchiveStack[]? Stacks);

    private sealed record ArchiveStack(
        Guid SourceStackId,
        string MatrixServerName,
        string HomeserverConfigurationPath,
        string SigningKeyPath,
        string? MediaPath,
        string? ElementConfigurationPath);
}
