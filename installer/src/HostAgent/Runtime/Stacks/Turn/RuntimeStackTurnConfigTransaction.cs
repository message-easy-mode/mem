using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Manifests;

namespace HostAgent.Runtime.Stacks.Turn;

public sealed record RuntimeStackTurnConfigSnapshot(
    string OperationDirectory,
    string ActiveConfigPath,
    string BeforeConfigPath,
    string CandidateConfigPath,
    string BeforeConfigSha256,
    string CandidateConfigSha256);

public sealed class RuntimeStackTurnConfigTransaction
{
    private readonly SynapseTurnConfigReader _reader;
    private readonly SynapseTurnConfigEditor _editor;

    public RuntimeStackTurnConfigTransaction(
        SynapseTurnConfigReader reader,
        SynapseTurnConfigEditor editor)
    {
        _reader = reader;
        _editor = editor;
    }

    public async Task<RuntimeStackTurnConfigSnapshot> CreateConnectSnapshotAsync(
        RuntimeStackManifest manifest,
        Guid operationId,
        CoturnSynapseConfig coturn,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(coturn);

        var dataPath = RequireDataPath(manifest.Matrix, "turn_connect");
        var activePath = RequireOwnedConfigPath(manifest.Matrix, dataPath, "turn_connect");
        var current = await _reader.ReadAsync(activePath, ct);
        if (!current.Supported || current.AnyTurnSettings)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_connect_review_stale",
                "The active Synapse TURN configuration changed after review.");
        }

        var operationDirectory = Path.Combine(
            dataPath,
            ".mem",
            "turn",
            operationId.ToString("D"));
        Directory.CreateDirectory(operationDirectory);
        ApplyDirectoryMode(operationDirectory);

        var beforePath = Path.Combine(operationDirectory, "homeserver.before.yaml");
        var candidatePath = Path.Combine(operationDirectory, "homeserver.candidate.yaml");
        var metadataPath = Path.Combine(operationDirectory, "operation-metadata.json");

        var beforeBytes = await File.ReadAllBytesAsync(activePath, ct);
        var candidateBytes = _editor.RenderConnect(beforeBytes, coturn);
        await WritePrivateFileAsync(beforePath, beforeBytes, ct);
        await WritePrivateFileAsync(candidatePath, candidateBytes, ct);

        var beforeHash = Hash(beforeBytes);
        var candidateHash = Hash(candidateBytes);
        var metadata = new
        {
            schemaVersion = "runtime-stack-turn-connect-snapshot-v1",
            operationId,
            runtimeStackId = manifest.StackId,
            slug = manifest.Slug,
            createdAtUtc = DateTimeOffset.UtcNow,
            beforeConfigSha256 = beforeHash,
            candidateConfigSha256 = candidateHash,
            publicHost = coturn.PublicHost,
            realm = coturn.Realm,
            turnUris = coturn.TurnUris,
            sharedSecretFingerprint = HashText(coturn.SharedSecret),
            userLifetime = coturn.UserLifetime,
            allowGuests = coturn.AllowGuests
        };
        await WritePrivateFileAsync(
            metadataPath,
            JsonSerializer.SerializeToUtf8Bytes(metadata, JsonOptions()),
            ct);

        return new RuntimeStackTurnConfigSnapshot(
            operationDirectory,
            activePath,
            beforePath,
            candidatePath,
            beforeHash,
            candidateHash);
    }

    public async Task<RuntimeStackTurnConfigSnapshot> CreateReplaceExternalSnapshotAsync(
        RuntimeStackManifest manifest,
        Guid operationId,
        CoturnSynapseConfig coturn,
        string expectedConfigSha256,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(coturn);

        var dataPath = RequireDataPath(manifest.Matrix, "turn_connect");
        var activePath = RequireOwnedConfigPath(manifest.Matrix, dataPath, "turn_connect");
        var current = await _reader.ReadAsync(activePath, ct);
        if (!current.Supported ||
            !current.AnyTurnSettings ||
            !FixedEquals(current.FileSha256, expectedConfigSha256))
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_connect_review_stale",
                "The reviewed external Synapse TURN configuration changed before replacement.");
        }

        var operationDirectory = Path.Combine(
            dataPath,
            ".mem",
            "turn",
            operationId.ToString("D"));
        Directory.CreateDirectory(operationDirectory);
        ApplyDirectoryMode(operationDirectory);

        var beforePath = Path.Combine(operationDirectory, "homeserver.before.yaml");
        var candidatePath = Path.Combine(operationDirectory, "homeserver.candidate.yaml");
        var metadataPath = Path.Combine(operationDirectory, "operation-metadata.json");

        var beforeBytes = await File.ReadAllBytesAsync(activePath, ct);
        var candidateBytes = _editor.RenderConnect(beforeBytes, coturn);
        await WritePrivateFileAsync(beforePath, beforeBytes, ct);
        await WritePrivateFileAsync(candidatePath, candidateBytes, ct);

        var beforeHash = Hash(beforeBytes);
        var candidateHash = Hash(candidateBytes);
        var metadata = new
        {
            schemaVersion = "runtime-stack-turn-replace-external-snapshot-v1",
            operationId,
            runtimeStackId = manifest.StackId,
            slug = manifest.Slug,
            createdAtUtc = DateTimeOffset.UtcNow,
            beforeConfigSha256 = beforeHash,
            candidateConfigSha256 = candidateHash,
            publicHost = coturn.PublicHost,
            realm = coturn.Realm,
            turnUris = coturn.TurnUris,
            sharedSecretFingerprint = HashText(coturn.SharedSecret),
            userLifetime = coturn.UserLifetime,
            allowGuests = coturn.AllowGuests,
            priorConfiguration = "external"
        };
        await WritePrivateFileAsync(
            metadataPath,
            JsonSerializer.SerializeToUtf8Bytes(metadata, JsonOptions()),
            ct);

        return new RuntimeStackTurnConfigSnapshot(
            operationDirectory,
            activePath,
            beforePath,
            candidatePath,
            beforeHash,
            candidateHash);
    }

    public async Task<RuntimeStackTurnConfigSnapshot> CreateDisconnectSnapshotAsync(
        RuntimeStackManifest manifest,
        Guid operationId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var dataPath = RequireDataPath(manifest.Matrix, "turn_disconnect");
        var activePath = RequireOwnedConfigPath(
            manifest.Matrix,
            dataPath,
            "turn_disconnect");
        var current = await _reader.ReadAsync(activePath, ct);
        if (!current.Supported ||
            !current.AnyTurnSettings ||
            !current.MemManagedMarkerPresent)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_disconnect_review_stale",
                "The active Synapse TURN configuration changed after review or is no longer provably MEM-managed.");
        }

        var operationDirectory = Path.Combine(
            dataPath,
            ".mem",
            "turn",
            operationId.ToString("D"));
        Directory.CreateDirectory(operationDirectory);
        ApplyDirectoryMode(operationDirectory);

        var beforePath = Path.Combine(operationDirectory, "homeserver.before.yaml");
        var candidatePath = Path.Combine(operationDirectory, "homeserver.candidate.yaml");
        var metadataPath = Path.Combine(operationDirectory, "operation-metadata.json");

        var beforeBytes = await File.ReadAllBytesAsync(activePath, ct);
        var candidateBytes = _editor.RenderDisconnect(beforeBytes);
        await WritePrivateFileAsync(beforePath, beforeBytes, ct);
        await WritePrivateFileAsync(candidatePath, candidateBytes, ct);

        var beforeHash = Hash(beforeBytes);
        var candidateHash = Hash(candidateBytes);
        var metadata = new
        {
            schemaVersion = "runtime-stack-turn-disconnect-snapshot-v1",
            operationId,
            runtimeStackId = manifest.StackId,
            slug = manifest.Slug,
            createdAtUtc = DateTimeOffset.UtcNow,
            beforeConfigSha256 = beforeHash,
            candidateConfigSha256 = candidateHash,
            priorTurnUris = current.TurnUris,
            priorSharedSecretFingerprint = current.SharedSecretFingerprint,
            priorUserLifetime = current.UserLifetime,
            priorAllowGuests = current.AllowGuests
        };
        await WritePrivateFileAsync(
            metadataPath,
            JsonSerializer.SerializeToUtf8Bytes(metadata, JsonOptions()),
            ct);

        return new RuntimeStackTurnConfigSnapshot(
            operationDirectory,
            activePath,
            beforePath,
            candidatePath,
            beforeHash,
            candidateHash);
    }

    public static Task ReplaceForDisconnectAsync(
        string sourcePath,
        string activePath,
        string? expectedActiveSha256,
        CancellationToken ct) =>
        ReplaceCoreAsync(
            sourcePath,
            activePath,
            expectedActiveSha256,
            "turn_disconnect_review_stale",
            "The active Synapse configuration changed before the disconnect replacement.",
            ct);

    public static Task ReplaceAsync(
        string sourcePath,
        string activePath,
        string? expectedActiveSha256,
        CancellationToken ct) =>
        ReplaceCoreAsync(
            sourcePath,
            activePath,
            expectedActiveSha256,
            "turn_connect_review_stale",
            "The active Synapse configuration changed before atomic replacement.",
            ct);

    private static async Task ReplaceCoreAsync(
        string sourcePath,
        string activePath,
        string? expectedActiveSha256,
        string staleErrorCode,
        string staleDetail,
        CancellationToken ct)
    {
        if (!File.Exists(sourcePath) || !File.Exists(activePath))
        {
            throw new FileNotFoundException(
                "The TURN configuration transaction source or active file is missing.");
        }

        if (!string.IsNullOrWhiteSpace(expectedActiveSha256))
        {
            var observed = Hash(await File.ReadAllBytesAsync(activePath, ct));
            if (!FixedEquals(observed, expectedActiveSha256))
            {
                throw new RuntimeStackTurnConnectionException(
                    staleErrorCode,
                    staleDetail);
            }
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(activePath))
            ?? throw new InvalidOperationException(
                "The active Synapse configuration directory could not be resolved.");
        var temporary = Path.Combine(
            directory,
            $".homeserver.yaml.mem-turn-{Guid.NewGuid():N}.tmp");
        var mode = TryGetMode(activePath);

        try
        {
            var bytes = await File.ReadAllBytesAsync(sourcePath, ct);
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }

            if (mode is not null)
            {
                File.SetUnixFileMode(temporary, mode.Value);
            }

            File.Move(temporary, activePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string RequireDataPath(
        RuntimeStackServiceManifest matrix,
        string operationPrefix)
    {
        if (string.IsNullOrWhiteSpace(matrix.DataPath))
        {
            throw new RuntimeStackTurnConnectionException(
                operationPrefix + "_matrix_data_path_missing",
                "The Runtime Stack manifest does not contain the Matrix data directory.");
        }

        var path = Path.GetFullPath(matrix.DataPath);
        if (!Directory.Exists(path))
        {
            throw new RuntimeStackTurnConnectionException(
                operationPrefix + "_matrix_data_path_missing",
                "The manifest-owned Matrix data directory could not be found.");
        }

        return path;
    }

    private static string RequireOwnedConfigPath(
        RuntimeStackServiceManifest matrix,
        string dataPath,
        string operationPrefix)
    {
        var configured = matrix.ConfigPath;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new RuntimeStackTurnConnectionException(
                operationPrefix + "_config_path_missing",
                "The Runtime Stack manifest does not contain the Synapse configuration path.");
        }

        var path = Path.GetFullPath(configured);
        var expected = Path.GetFullPath(Path.Combine(dataPath, "homeserver.yaml"));
        if (!string.Equals(path, expected, StringComparison.Ordinal) ||
            !File.Exists(path))
        {
            throw new RuntimeStackTurnConnectionException(
                operationPrefix + "_config_path_unowned",
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
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
        stream.Flush(flushToDisk: true);
        ApplyFileMode(path);
    }

    private static void ApplyDirectoryMode(string path)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
    }

    private static void ApplyFileMode(string path)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static UnixFileMode? TryGetMode(string path) =>
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            ? File.GetUnixFileMode(path)
            : null;

    public static string Hash(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string HashText(string value) =>
        Hash(System.Text.Encoding.UTF8.GetBytes(value));

    public static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        var leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
        var rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static JsonSerializerOptions JsonOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
}
