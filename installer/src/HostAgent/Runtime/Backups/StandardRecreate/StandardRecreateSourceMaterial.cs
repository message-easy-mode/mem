using System.Security.Cryptography;
using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Runtime.Backups.Catalog;

namespace HostAgent.Runtime.Backups.StandardRecreate;

/// <summary>
/// Internal source material used by catalog-native Standard Recreate execution.
/// Standard Recreate always resolves recovery files from the managed Backup
/// Catalog payload; validation receipts and uploaded ZIP paths are not restore
/// sources after materialisation.
/// </summary>
internal abstract class StandardRecreateSourceMaterial
{
    protected StandardRecreateSourceMaterial(
        string catalogEntryId,
        Guid catalogEntryDatabaseId,
        string restoreSessionId,
        string sourceStackSlug,
        string matrixServerName,
        string? sourceElementHost,
        bool declaresCoturnConfigured,
        MemStackExportCoturnManifest? backupCoturn,
        BackupCatalogStackLogoMaterial? stackLogo)
    {
        if (string.IsNullOrWhiteSpace(catalogEntryId))
        {
            throw new InvalidOperationException(
                "Backup Catalog entry id is required for Standard Recreate.");
        }

        if (catalogEntryDatabaseId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Backup Catalog database identity is required for Standard Recreate.");
        }

        if (string.IsNullOrWhiteSpace(restoreSessionId))
        {
            throw new InvalidOperationException(
                "Restore session id is required for Standard Recreate.");
        }

        CatalogEntryId = catalogEntryId;
        CatalogEntryDatabaseId = catalogEntryDatabaseId;
        RestoreSessionId = restoreSessionId;
        SourceStackSlug = sourceStackSlug;
        MatrixServerName = matrixServerName;
        SourceElementHost = sourceElementHost;
        DeclaresCoturnConfigured = declaresCoturnConfigured;
        BackupCoturn = backupCoturn;
        StackLogo = stackLogo;
    }

    public string SourceKind => "backup-catalog";

    public string CatalogEntryId { get; }

    public Guid CatalogEntryDatabaseId { get; }

    public string RestoreSessionId { get; }

    public string SourceStackSlug { get; }

    public string MatrixServerName { get; }

    public string? SourceElementHost { get; }

    public bool DeclaresCoturnConfigured { get; }

    public MemStackExportCoturnManifest? BackupCoturn { get; }

    public BackupCatalogStackLogoMaterial? StackLogo { get; }

    public abstract string CopySummaryVerb { get; }

    public abstract Task<StandardRecreateSourceCopyResult> CopyToRuntimeAsync(
        string databaseDumpPath,
        string matrixDataPath,
        string elementConfigPath,
        CancellationToken ct);

    public static StandardRecreateSourceMaterial CreateCatalog(
        BackupCatalogStandardRecreateExecutionMaterial material,
        string restoreSessionId)
    {
        ArgumentNullException.ThrowIfNull(material);

        var sourceStackSlug = string.IsNullOrWhiteSpace(material.SourceStackSlug)
            ? "restored-stack"
            : material.SourceStackSlug.Trim();

        var matrixServerName = StandardRecreateMatrixIdentity.NormalizeHost(
            material.MatrixServerName)
            ?? throw new InvalidDataException(
                "The Backup Catalog payload does not declare the original Matrix server identity required for a standard restore.");

        return new CatalogStandardRecreateSourceMaterial(
            material,
            restoreSessionId,
            sourceStackSlug,
            matrixServerName);
    }

    private sealed class CatalogStandardRecreateSourceMaterial
        : StandardRecreateSourceMaterial
    {
        private readonly BackupCatalogStandardRecreateExecutionMaterial _material;

        public CatalogStandardRecreateSourceMaterial(
            BackupCatalogStandardRecreateExecutionMaterial material,
            string restoreSessionId,
            string sourceStackSlug,
            string matrixServerName)
            : base(
                catalogEntryId: material.CatalogEntryId,
                catalogEntryDatabaseId: material.CatalogEntryDatabaseId,
                restoreSessionId: restoreSessionId,
                sourceStackSlug: sourceStackSlug,
                matrixServerName: matrixServerName,
                sourceElementHost: StandardRecreateMatrixIdentity.NormalizeHost(
                    material.SourceElementHost),
                declaresCoturnConfigured: material.DeclaresCoturnConfigured,
                backupCoturn: material.BackupCoturn,
                stackLogo: material.StackLogo)
        {
            _material = material;
        }

        public override string CopySummaryVerb =>
            "copied directly from the managed Backup Catalog payload";

        public override async Task<StandardRecreateSourceCopyResult> CopyToRuntimeAsync(
            string databaseDumpPath,
            string matrixDataPath,
            string elementConfigPath,
            CancellationToken ct)
        {
            await CopyRequiredFileAsync(
                _material.DatabaseDumpPath,
                databaseDumpPath,
                ct);
            await CopyRequiredFileAsync(
                _material.HomeserverPath,
                Path.Combine(matrixDataPath, "homeserver.yaml"),
                ct);
            var runtimeSigningKeyPath = Path.Combine(matrixDataPath, "signing.key");
            await CopyRequiredFileAsync(
                _material.SigningKeyPath,
                runtimeSigningKeyPath,
                ct);

            var signingKeyFidelity = await VerifySigningKeyCopyAsync(
                _material.SigningKeyPath,
                runtimeSigningKeyPath,
                ct);

            var media = await CopyMediaStoreAsync(
                _material.MediaStorePath,
                matrixDataPath,
                ct);

            var elementConfigCopied = false;
            if (!string.IsNullOrWhiteSpace(_material.ElementConfigPath) &&
                File.Exists(_material.ElementConfigPath))
            {
                await CopyRequiredFileAsync(
                    _material.ElementConfigPath,
                    elementConfigPath,
                    ct);
                elementConfigCopied = true;
            }

            return new StandardRecreateSourceCopyResult(
                MediaFiles: media.Files,
                MediaBytes: media.Bytes,
                ElementConfigCopied: elementConfigCopied,
                SigningKeyBytes: signingKeyFidelity.Bytes,
                SigningKeySha256: signingKeyFidelity.Sha256);
        }

        private static async Task<(long Bytes, byte[] Sha256)> VerifySigningKeyCopyAsync(
            string sourcePath,
            string targetPath,
            CancellationToken ct)
        {
            var sourceHash = await ComputeSha256Async(sourcePath, ct);
            var targetHash = await ComputeSha256Async(targetPath, ct);

            if (!CryptographicOperations.FixedTimeEquals(sourceHash, targetHash))
            {
                throw new InvalidDataException(
                    "The restored Matrix signing key did not match the managed Backup Catalog payload after copy.");
            }

            var bytes = new FileInfo(targetPath).Length;
            if (bytes <= 0)
            {
                throw new InvalidDataException(
                    "The managed Backup Catalog Matrix signing key is empty.");
            }

            return (bytes, targetHash);
        }

        private static async Task<byte[]> ComputeSha256Async(
            string path,
            CancellationToken ct)
        {
            await using var stream = File.OpenRead(path);
            return await SHA256.HashDataAsync(stream, ct);
        }

        private static async Task CopyRequiredFileAsync(
            string sourcePath,
            string targetPath,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !File.Exists(sourcePath))
            {
                throw new InvalidDataException(
                    $"Required Backup Catalog source material is unavailable: '{sourcePath}'.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await using var source = File.OpenRead(sourcePath);
            await using var target = File.Create(targetPath);
            await source.CopyToAsync(target, ct);
        }

        private static async Task<(long Files, long Bytes)> CopyMediaStoreAsync(
            string? sourcePath,
            string matrixDataPath,
            CancellationToken ct)
        {
            var targetRoot = Path.Combine(matrixDataPath, "media_store");
            Directory.CreateDirectory(targetRoot);

            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !Directory.Exists(sourcePath))
            {
                return (0, 0);
            }

            var files = 0L;
            var bytes = 0L;

            foreach (var sourceFile in Directory.EnumerateFiles(
                         sourcePath,
                         "*",
                         SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                var relative = Path.GetRelativePath(sourcePath, sourceFile);
                if (relative.StartsWith("..", StringComparison.Ordinal) ||
                    Path.IsPathRooted(relative))
                {
                    throw new InvalidDataException(
                        "Backup Catalog media material contains an unsafe relative path.");
                }

                var targetPath = Path.Combine(targetRoot, relative);
                await CopyRequiredFileAsync(sourceFile, targetPath, ct);
                files++;
                bytes += new FileInfo(sourceFile).Length;
            }

            return (files, bytes);
        }
    }
}

internal sealed record StandardRecreateSourceCopyResult(
    long MediaFiles,
    long MediaBytes,
    bool ElementConfigCopied,
    long SigningKeyBytes,
    byte[] SigningKeySha256);
