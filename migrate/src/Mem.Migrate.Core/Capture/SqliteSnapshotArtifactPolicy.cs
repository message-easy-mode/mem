namespace Mem.Migrate.Core.Capture;

public static class SqliteSnapshotArtifactPolicy
{
    private static readonly string[] TemporarySidecarSuffixes =
    [
        ".partial-wal",
        ".partial-shm",
        ".partial-journal"
    ];

    public static bool IsTemporarySidecar(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fileName = Path.GetFileName(path);

        return TemporarySidecarSuffixes.Any(suffix =>
            fileName.EndsWith(suffix, StringComparison.Ordinal));
    }

    public static string[] GetTemporarySidecarPaths(
        string partialDatabasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partialDatabasePath);

        return
        [
            partialDatabasePath + "-wal",
            partialDatabasePath + "-shm",
            partialDatabasePath + "-journal"
        ];
    }
}
