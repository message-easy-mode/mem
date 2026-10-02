using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HostAgent.Runtime.Manifests;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;

namespace HostAgent.Matrix.Federation;

public sealed record RuntimeStackFederationSnapshot(
    string OperationDirectory,
    string ActiveConfigPath,
    string BeforeConfigPath,
    string CandidateConfigPath,
    string NpmRouteSnapshotPath,
    string NpmLookupDomain,
    NpmProxyHostSnapshot NpmRouteSnapshot,
    string BeforeConfigSha256,
    string CandidateConfigSha256,
    string NpmRouteSnapshotSha256);

public interface IRuntimeStackFederationSnapshotService
{
    Task<RuntimeStackFederationSnapshot> CreateAsync(
        RuntimeStackManifest manifest,
        Guid operationId,
        CanonicalFederationPolicyRequest request,
        CancellationToken ct);
}

public sealed class RuntimeStackFederationSnapshotService
    : IRuntimeStackFederationSnapshotService
{
    private readonly SynapseFederationConfigReader _reader;
    private readonly SynapseFederationConfigEditor _editor;
    private readonly NpmProxyHostService _npm;

    public RuntimeStackFederationSnapshotService(
        SynapseFederationConfigReader reader,
        SynapseFederationConfigEditor editor,
        NpmProxyHostService npm)
    {
        _reader = reader;
        _editor = editor;
        _npm = npm;
    }

    public async Task<RuntimeStackFederationSnapshot> CreateAsync(
        RuntimeStackManifest manifest,
        Guid operationId,
        CanonicalFederationPolicyRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(request);

        var dataPath = RequireDataPath(manifest.Matrix);
        var activePath = RequireOwnedConfigPath(manifest.Matrix, dataPath);
        var activeRead = await _reader.ReadAsync(activePath, ct);
        if (!activeRead.Supported)
        {
            throw new InvalidOperationException(
                "The current Synapse federation configuration is not safe for automatic editing.");
        }

        var operationDirectory = Path.Combine(
            dataPath,
            ".mem",
            "federation",
            operationId.ToString("D"));
        Directory.CreateDirectory(operationDirectory);
        ApplyDirectoryMode(operationDirectory);

        var beforePath = Path.Combine(operationDirectory, "homeserver.before.yaml");
        var candidatePath = Path.Combine(operationDirectory, "homeserver.candidate.yaml");
        var npmPath = Path.Combine(operationDirectory, "npm-route.before.json");
        var metadataPath = Path.Combine(operationDirectory, "operation-metadata.json");

        var beforeBytes = await File.ReadAllBytesAsync(activePath, ct);
        var candidateBytes = _editor.Render(beforeBytes, request);
        await WritePrivateFileAsync(beforePath, beforeBytes, ct);
        await WritePrivateFileAsync(candidatePath, candidateBytes, ct);

        var publicHost = manifest.Matrix.PublicHost?.Trim();
        if (string.IsNullOrWhiteSpace(publicHost))
        {
            throw new InvalidOperationException(
                "The Runtime Stack manifest does not contain the Matrix public host.");
        }

        var route = await _npm.GetByDomainAsync(publicHost, ct)
            ?? throw new InvalidOperationException(
                "The canonical Matrix NPM route could not be captured before federation mutation.");
        var routeSnapshot = NpmProxyHostService.CaptureSnapshot(route);
        var routeBytes = JsonSerializer.SerializeToUtf8Bytes(routeSnapshot, JsonOptions());
        await WritePrivateFileAsync(npmPath, routeBytes, ct);

        var metadata = new
        {
            schemaVersion = "federation-operation-snapshot-v1",
            operationId,
            runtimeStackId = manifest.StackId,
            slug = manifest.Slug,
            requestedMode = request.Mode,
            canonicalAllowlist = request.Allowlist,
            createdAtUtc = DateTimeOffset.UtcNow,
            beforeConfigSha256 = Hash(beforeBytes),
            candidateConfigSha256 = Hash(candidateBytes),
            npmRouteSnapshotSha256 = Hash(routeBytes)
        };
        await WritePrivateFileAsync(
            metadataPath,
            JsonSerializer.SerializeToUtf8Bytes(metadata, JsonOptions()),
            ct);

        return new RuntimeStackFederationSnapshot(
            OperationDirectory: operationDirectory,
            ActiveConfigPath: activePath,
            BeforeConfigPath: beforePath,
            CandidateConfigPath: candidatePath,
            NpmRouteSnapshotPath: npmPath,
            NpmLookupDomain: publicHost,
            NpmRouteSnapshot: routeSnapshot,
            BeforeConfigSha256: Hash(beforeBytes),
            CandidateConfigSha256: Hash(candidateBytes),
            NpmRouteSnapshotSha256: Hash(routeBytes));
    }

    private static string RequireDataPath(RuntimeStackServiceManifest matrix)
    {
        if (string.IsNullOrWhiteSpace(matrix.DataPath))
        {
            throw new InvalidOperationException(
                "The Runtime Stack manifest does not contain the Matrix data directory.");
        }

        var path = Path.GetFullPath(matrix.DataPath);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                "The manifest-owned Matrix data directory could not be found.");
        }

        return path;
    }

    private static string RequireOwnedConfigPath(
        RuntimeStackServiceManifest matrix,
        string dataPath)
    {
        var configured = matrix.ConfigPath;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "The Runtime Stack manifest does not contain the Synapse configuration path.");
        }

        var path = Path.GetFullPath(configured);
        var expected = Path.GetFullPath(Path.Combine(dataPath, "homeserver.yaml"));
        if (!string.Equals(path, expected, StringComparison.Ordinal) || !File.Exists(path))
        {
            throw new InvalidOperationException(
                "The Synapse configuration is not present at the manifest-owned homeserver.yaml path.");
        }

        return path;
    }

    private static async Task WritePrivateFileAsync(
        string path,
        byte[] bytes,
        CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            options: FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
        stream.Flush(flushToDisk: true);
        ApplyFileMode(path);
    }

    private static void ApplyDirectoryMode(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
    }

    private static void ApplyFileMode(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static string Hash(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static JsonSerializerOptions JsonOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
}

public sealed class RuntimeStackFederationConfigStateChangedException : InvalidOperationException
{
    public RuntimeStackFederationConfigStateChangedException(string detail)
        : base(detail)
    {
    }
}

public interface IRuntimeStackFederationConfigTransaction
{
    Task ApplyCandidateAsync(
        RuntimeStackFederationSnapshot snapshot,
        CancellationToken ct);

    Task RestoreBeforeAsync(
        RuntimeStackFederationSnapshot snapshot,
        CancellationToken ct);
}

public sealed class RuntimeStackFederationConfigTransaction
    : IRuntimeStackFederationConfigTransaction
{
    public Task ApplyCandidateAsync(
        RuntimeStackFederationSnapshot snapshot,
        CancellationToken ct) =>
        ReplaceAsync(
            snapshot.CandidateConfigPath,
            snapshot.ActiveConfigPath,
            snapshot.BeforeConfigSha256,
            ct);

    public Task RestoreBeforeAsync(
        RuntimeStackFederationSnapshot snapshot,
        CancellationToken ct) =>
        ReplaceAsync(
            snapshot.BeforeConfigPath,
            snapshot.ActiveConfigPath,
            expectedActiveSha256: null,
            ct);

    private static async Task ReplaceAsync(
        string sourcePath,
        string activePath,
        string? expectedActiveSha256,
        CancellationToken ct)
    {
        if (!File.Exists(sourcePath) || !File.Exists(activePath))
        {
            throw new FileNotFoundException(
                "The federation configuration transaction source or active file is missing.");
        }

        if (!string.IsNullOrWhiteSpace(expectedActiveSha256))
        {
            var activeBytes = await File.ReadAllBytesAsync(activePath, ct);
            var observed = Hash(activeBytes);
            if (!CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(observed),
                    System.Text.Encoding.UTF8.GetBytes(expectedActiveSha256)))
            {
                throw new RuntimeStackFederationConfigStateChangedException(
                    "The active Synapse configuration changed after federation review and snapshot capture.");
            }
        }

        var activeDirectory = Path.GetDirectoryName(Path.GetFullPath(activePath))
            ?? throw new InvalidOperationException(
                "The active Synapse configuration directory could not be resolved.");
        var temporaryPath = Path.Combine(
            activeDirectory,
            $".homeserver.yaml.mem-{Guid.NewGuid():N}.tmp");
        var mode = TryGetMode(activePath);

        try
        {
            var bytes = await File.ReadAllBytesAsync(sourcePath, ct);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }

            if (mode is not null)
            {
                File.SetUnixFileMode(temporaryPath, mode.Value);
            }

            File.Move(temporaryPath, activePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string Hash(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static UnixFileMode? TryGetMode(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return null;
        }

        return File.GetUnixFileMode(path);
    }
}
