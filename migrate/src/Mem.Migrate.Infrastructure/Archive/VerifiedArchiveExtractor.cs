using System.IO.Compression;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Conversion;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Infrastructure.Archive;

public sealed class VerifiedArchiveExtractor(
    IMigrationArchiveReader archiveReader,
    ArchiveSafetyLimits safetyLimits) : IVerifiedArchiveExtractor
{
    public async Task ExtractFilesAsync(
        string archivePath,
        IReadOnlyDictionary<string, string> archivePathToDestination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archivePathToDestination);
        if (archivePathToDestination.Count == 0)
        {
            throw new ArgumentException("At least one archive file must be selected.");
        }

        var verification = await archiveReader.VerifyAsync(
            archivePath,
            safetyLimits,
            cancellationToken);
        if (!verification.Valid || verification.Manifest is null)
        {
            var detail = verification.Findings.FirstOrDefault()?.Message
                ?? "Archive verification failed.";
            throw new InvalidDataException(detail);
        }

        var allowed = verification.Manifest.IncludedFiles.ToDictionary(
            file => file.Path,
            StringComparer.Ordinal);
        var normalizedRequests = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in archivePathToDestination)
        {
            var normalized = ArchivePathPolicy.Normalize(pair.Key);
            if (!allowed.ContainsKey(normalized))
            {
                throw new InvalidDataException(
                    $"Archive file '{normalized}' is not present in the verified manifest inventory.");
            }

            var destination = Path.GetFullPath(pair.Value);
            if (File.Exists(destination))
            {
                throw new IOException(
                    $"Verified extraction refuses to overwrite '{destination}'.");
            }

            PrivateFilePermissions.EnsureDirectory(Path.GetDirectoryName(destination)!);
            normalizedRequests.Add(normalized, destination);
        }

        await using var stream = new FileStream(
            Path.GetFullPath(archivePath),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .ToDictionary(entry => ArchivePathPolicy.Normalize(entry.FullName), StringComparer.Ordinal);

        foreach (var pair in normalizedRequests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(pair.Key, out var entry))
            {
                throw new InvalidDataException(
                    $"Verified archive file '{pair.Key}' is missing during extraction.");
            }

            await using (var input = entry.Open())
            await using (var output = new FileStream(
                pair.Value,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            PrivateFilePermissions.EnsureFile(pair.Value);

            var expected = allowed[pair.Key];
            var actual = await Sha256File.ComputeAsync(pair.Value, cancellationToken);
            if (new FileInfo(pair.Value).Length != expected.SizeBytes ||
                !string.Equals(actual, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(pair.Value);
                throw new InvalidDataException(
                    $"Extracted file '{pair.Key}' did not match its verified manifest receipt.");
            }
        }
    }
}
