namespace HostAgent.Runtime.Filesystem;

public sealed record PreparedRuntimeDirectory(
    string Path,
    bool ExistsAfterPrepare);

public sealed class RuntimeDirectoryPreparer
{
    public Task<IReadOnlyList<PreparedRuntimeDirectory>> PrepareAsync(
        IReadOnlyCollection<string?> paths,
        CancellationToken cancellationToken)
    {
        var prepared = new List<PreparedRuntimeDirectory>();

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var normalizedPath = path.Trim();

            Directory.CreateDirectory(normalizedPath);

            prepared.Add(new PreparedRuntimeDirectory(
                Path: normalizedPath,
                ExistsAfterPrepare: Directory.Exists(normalizedPath)));
        }

        return Task.FromResult<IReadOnlyList<PreparedRuntimeDirectory>>(prepared);
    }
}