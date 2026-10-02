using System.Security.Cryptography;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Runtime.Operations;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Secrets;
using HostAgent.Runtime.Stacks.Identity;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Runtime.Backups.Artifacts.LocalBackups;

public sealed class LocalBackupCaptureService
{
    private const string PostgresContainerName = "mem-postgres";
    private const string BackupVersion = "mem-stack-backup-v2";

    private readonly MemDbContext _db;
    private readonly DockerClient _docker;
    private readonly RuntimeStackSecretService _secretService;
    private readonly RuntimeOperationStore _operationStore;
    private readonly LocalCapturedBackupCatalogRegistrationService _catalogRegistrationService;
    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly RuntimeStackLogoService _logoService;
    private readonly IConfiguration _configuration;
    private readonly MemControlPlaneRuntimeContext _runtimeContext;

    public LocalBackupCaptureService(
        MemDbContext db,
        DockerClient docker,
        RuntimeStackSecretService secretService,
        RuntimeOperationStore operationStore,
        LocalCapturedBackupCatalogRegistrationService catalogRegistrationService,
        RuntimeStackManifestStore manifestStore,
        RuntimeStackLogoService logoService,
        IConfiguration configuration,
        MemControlPlaneRuntimeContext runtimeContext)
    {
        _db = db;
        _docker = docker;
        _secretService = secretService;
        _operationStore = operationStore;
        _catalogRegistrationService = catalogRegistrationService;
        _manifestStore = manifestStore;
        _logoService = logoService;
        _configuration = configuration;
        _runtimeContext = runtimeContext;
    }

    public async Task<LocalBackupCaptureResult> BackupAsync(
        string slugOrId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slugOrId))
        {
            throw new InvalidOperationException("Stack slug or id is required.");
        }

        var stack = await ResolveStackAsync(slugOrId, ct);

        var stackId = stack.Id;
        var stackSlug = stack.Slug;

        var operationId = await _operationStore.StartAsync(
            runtimeStackId: stackId,
            operation: "backup-stack",
            idempotencyKey: $"backup-stack:{stackId:N}:{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            requestedBy: "host-agent",
            hostMutationLevel: "filesystem,postgres-read",
            input: new
            {
                StackId = stackId,
                StackSlug = stackSlug
            },
            ct);

        var warnings = new List<string>();

        try
        {
            var database = await _db.RuntimeStackDatabases
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RuntimeStackId == stackId, ct);

            if (database is null)
            {
                throw new InvalidOperationException(
                    $"No RuntimeStackDatabases row exists for stack '{stackSlug}'. Backup v1 requires a Postgres-backed stack.");
            }

            var databasePassword = await _secretService.GetSecretValueAsync(
                stackId,
                database.PasswordSecretKind,
                ct);

            if (string.IsNullOrWhiteSpace(databasePassword))
            {
                throw new InvalidOperationException(
                    $"Database password secret '{database.PasswordSecretKind}' was not found for stack '{stackSlug}'.");
            }

            var backupId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssZ");

            var backupRoot = Path.Combine(
                GetStackBackupsRoot(),
                stackSlug,
                backupId);

            var databaseDir = Path.Combine(backupRoot, "database");
            var matrixDir = Path.Combine(backupRoot, "matrix");
            var elementDir = Path.Combine(backupRoot, "element");

            Directory.CreateDirectory(databaseDir);
            Directory.CreateDirectory(matrixDir);
            Directory.CreateDirectory(elementDir);

            var logoManifest = await CaptureStackLogoAsync(
                stackId,
                backupRoot,
                warnings,
                ct);

            var databaseDumpPath = Path.Combine(databaseDir, "synapse.sql");

            await DumpPostgresDatabaseAsync(
                database.DatabaseName,
                database.DatabaseUsername,
                databasePassword,
                databaseDumpPath,
                ct);

            var matrixService = await _db.RuntimeServiceInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.RuntimeStackId == stackId &&
                    x.ServiceKey == "matrix",
                    ct);

            if (matrixService is null)
            {
                throw new InvalidOperationException(
                    $"Matrix runtime service was not found for stack '{stackSlug}'.");
            }

            var matrixDataPath = matrixService.DataPath;

            if (string.IsNullOrWhiteSpace(matrixDataPath) ||
                !Directory.Exists(matrixDataPath))
            {
                throw new DirectoryNotFoundException(
                    $"Matrix data path was not found: {matrixDataPath}");
            }

            var homeserverSource = Path.Combine(matrixDataPath, "homeserver.yaml");

            var signingKeySource = !string.IsNullOrWhiteSpace(matrixService.ServerName)
                ? Path.Combine(matrixDataPath, $"{matrixService.ServerName}.signing.key")
                : Path.Combine(matrixDataPath, "signing.key");

            var fallbackSigningKeySource = Path.Combine(matrixDataPath, "signing.key");
            var mediaSource = Path.Combine(matrixDataPath, "media_store");

            string? homeserverBackupPath = null;
            string? signingKeyBackupPath = null;
            string? mediaBackupPath = null;

            if (File.Exists(homeserverSource))
            {
                homeserverBackupPath = Path.Combine(matrixDir, "homeserver.yaml");

                File.Copy(
                    homeserverSource,
                    homeserverBackupPath,
                    overwrite: true);
            }
            else
            {
                warnings.Add("homeserver.yaml was not found in Matrix data path.");
            }

            if (File.Exists(signingKeySource))
            {
                signingKeyBackupPath = Path.Combine(
                    matrixDir,
                    Path.GetFileName(signingKeySource));

                File.Copy(
                    signingKeySource,
                    signingKeyBackupPath,
                    overwrite: true);
            }
            else if (File.Exists(fallbackSigningKeySource))
            {
                signingKeyBackupPath = Path.Combine(matrixDir, "signing.key");

                File.Copy(
                    fallbackSigningKeySource,
                    signingKeyBackupPath,
                    overwrite: true);
            }
            else
            {
                warnings.Add("Matrix signing key was not found. This is critical for federation identity.");
            }

            if (Directory.Exists(mediaSource))
            {
                mediaBackupPath = Path.Combine(matrixDir, "media_store");

                CopyDirectory(
                    mediaSource,
                    mediaBackupPath);
            }
            else
            {
                warnings.Add("Matrix media_store directory was not found.");
            }

            var elementService = await _db.RuntimeServiceInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.RuntimeStackId == stackId &&
                    x.ServiceKey == "element-web",
                    ct);

            string? elementConfigBackupPath = null;
            string? elementBackupPath = null;
            LocalBackupElementManifest? elementManifest = null;

            if (elementService is not null &&
                !string.IsNullOrWhiteSpace(elementService.DataPath) &&
                Directory.Exists(elementService.DataPath))
            {
                var elementConfigSource = Path.Combine(
                    elementService.DataPath,
                    "config.json");

                elementBackupPath = elementDir;

                if (File.Exists(elementConfigSource))
                {
                    elementConfigBackupPath = Path.Combine(
                        elementDir,
                        "config.json");

                    File.Copy(
                        elementConfigSource,
                        elementConfigBackupPath,
                        overwrite: true);
                }
                else
                {
                    warnings.Add("Element config.json was not found.");
                }

                elementManifest = new LocalBackupElementManifest(
                    DataPath: elementService.DataPath,
                    ConfigPath: File.Exists(elementConfigSource) ? elementConfigSource : null,
                    BackupPath: elementBackupPath);
            }
            else
            {
                warnings.Add("Element runtime service/data path was not found.");
            }

            var routeManifest = BuildRouteManifest(
                stack,
                matrixService,
                elementService,
                warnings);

            var runtimeMetadata = DeserializeRuntimeMetadata(
                matrixService.RuntimeMetadataJson,
                warnings);

            var coturnManifest = LocalBackupTurnManifestFactory.Create(
                homeserverBackupPath ?? homeserverSource,
                runtimeMetadata,
                warnings);

            var databaseDumpStats = new LocalBackupFileStats(
                Included: File.Exists(databaseDumpPath),
                Path: File.Exists(databaseDumpPath) ? databaseDumpPath : null,
                Bytes: GetFileSize(databaseDumpPath));

            var homeserverStats = new LocalBackupFileStats(
                Included: homeserverBackupPath is not null && File.Exists(homeserverBackupPath),
                Path: homeserverBackupPath,
                Bytes: GetFileSize(homeserverBackupPath));

            var signingKeyStats = new LocalBackupFileStats(
                Included: signingKeyBackupPath is not null && File.Exists(signingKeyBackupPath),
                Path: signingKeyBackupPath,
                Bytes: GetFileSize(signingKeyBackupPath));

            var mediaDirectoryStats = GetDirectoryStats(mediaBackupPath);

            var mediaStats = new LocalBackupDirectoryStats(
                Included: mediaBackupPath is not null && Directory.Exists(mediaBackupPath),
                Path: mediaBackupPath,
                Bytes: mediaDirectoryStats.Bytes,
                Files: mediaDirectoryStats.Files);

            var elementConfigStats = new LocalBackupFileStats(
                Included: elementConfigBackupPath is not null && File.Exists(elementConfigBackupPath),
                Path: elementConfigBackupPath,
                Bytes: GetFileSize(elementConfigBackupPath));

            var totalBytes =
                databaseDumpStats.Bytes +
                homeserverStats.Bytes +
                signingKeyStats.Bytes +
                mediaStats.Bytes +
                elementConfigStats.Bytes +
                (logoManifest?.Bytes ?? 0);

            var totalFiles =
                (databaseDumpStats.Included ? 1 : 0) +
                (homeserverStats.Included ? 1 : 0) +
                (signingKeyStats.Included ? 1 : 0) +
                mediaStats.Files +
                (elementConfigStats.Included ? 1 : 0) +
                (logoManifest is not null ? 1 : 0);

            var stats = new LocalBackupStats(
                DatabaseDump: databaseDumpStats,
                HomeserverConfig: homeserverStats,
                SigningKey: signingKeyStats,
                MediaStore: mediaStats,
                ElementConfig: elementConfigStats,
                TotalBytes: totalBytes,
                TotalFiles: totalFiles);

            var manifest = new LocalBackupManifest(
                BackupVersion: BackupVersion,
                BackupId: backupId,
                CreatedAtUtc: DateTime.UtcNow,
                RuntimeStackId: stackId,
                StackSlug: stackSlug,
                Database: new LocalBackupDatabaseManifest(
                    Engine: database.DatabaseEngine,
                    Host: database.DatabaseHost,
                    Port: database.DatabasePort,
                    DatabaseName: database.DatabaseName,
                    DatabaseUsername: database.DatabaseUsername,
                    PasswordSecretKind: database.PasswordSecretKind,
                    DumpPath: databaseDumpPath),
                Matrix: new LocalBackupMatrixManifest(
                    DataPath: matrixDataPath,
                    HomeserverYamlPath: File.Exists(homeserverSource) ? homeserverSource : null,
                    SigningKeyPath: File.Exists(signingKeySource)
                        ? signingKeySource
                        : File.Exists(fallbackSigningKeySource)
                            ? fallbackSigningKeySource
                            : null,
                    MediaStorePath: Directory.Exists(mediaSource) ? mediaSource : null,
                    BackupPath: matrixDir),
                Element: elementManifest,
                Stats: stats,
                Warnings: warnings)
            {
                Routes = routeManifest,
                Coturn = coturnManifest,
                MemVersion = _runtimeContext.Version,
                MatrixServerName = matrixService.ServerName,
                Logo = logoManifest
            };

            var manifestPath = Path.Combine(
                backupRoot,
                "backup-manifest.json");

            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }),
                ct);

            var result = new LocalBackupCaptureResult(
                OperationId: operationId,
                RuntimeStackId: stackId,
                StackSlug: stackSlug,
                BackupId: backupId,
                BackupRootPath: backupRoot,
                ManifestPath: manifestPath,
                DatabaseDumpPath: databaseDumpPath,
                MatrixConfigPath: homeserverBackupPath,
                MatrixSigningKeyPath: signingKeyBackupPath,
                MatrixMediaBackupPath: mediaBackupPath,
                ElementConfigPath: elementConfigBackupPath,
                Stats: stats,
                Warnings: warnings,
                CreatedAtUtc: DateTime.UtcNow);

            try
            {
                var catalogRegistration = await _catalogRegistrationService
                    .RegisterCapturedAsync(
                        result,
                        manifest,
                        ct);

                result = result with
                {
                    CatalogEntryId = catalogRegistration.CatalogEntryId
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The durable payload is already present. Preserve that backup
                // and make the registration gap explicit; an operator can run
                // the idempotent catalog backfill after resolving the issue.
                warnings.Add(
                    "Backup payload was captured, but catalog registration did not complete: " +
                    ex.Message);
            }

            await _operationStore.CompleteAsync(
                operationId,
                status: warnings.Count == 0 ? "completed" : "completed_with_warnings",
                currentStep: "completed",
                result: new
                {
                    result.RuntimeStackId,
                    result.StackSlug,
                    result.BackupId,
                    result.BackupRootPath,
                    result.ManifestPath,
                    result.DatabaseDumpPath,
                    result.CatalogEntryId,
                    result.Stats,
                    Warnings = result.Warnings
                },
                evidence: Array.Empty<object>(),
                ct);

            return result;
        }
        catch (Exception ex)
        {
            await _operationStore.FailAsync(
                operationId,
                currentStep: "failed",
                error: ex.Message,
                evidence: Array.Empty<object>(),
                ct);

            throw;
        }
    }


    /// <summary>
    /// Captures the first native MEM baseline backup from an accepted
    /// Migration-owned production candidate. All paths, containers, and
    /// identities are resolved by trusted server-side migration evidence.
    /// </summary>
    public async Task<LocalBackupCaptureResult> BackupMigrationCandidateAsync(
        LocalBackupMigrationCandidateSource source,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.RuntimeIdentity == Guid.Empty ||
            string.IsNullOrWhiteSpace(source.StackSlug) ||
            string.IsNullOrWhiteSpace(source.MatrixServerName) ||
            string.IsNullOrWhiteSpace(source.PostgresContainerName) ||
            string.IsNullOrWhiteSpace(source.DatabaseName) ||
            string.IsNullOrWhiteSpace(source.DatabaseUsername) ||
            string.IsNullOrWhiteSpace(source.MatrixDataPath))
        {
            throw new InvalidOperationException(
                "Trusted migration baseline-backup source evidence is incomplete.");
        }

        if (!Directory.Exists(source.MatrixDataPath))
        {
            throw new DirectoryNotFoundException(
                $"Migration candidate Matrix data path was not found: {source.MatrixDataPath}");
        }

        var operationId = await _operationStore.StartAsync(
            runtimeStackId: null,
            operation: "backup-migration-baseline",
            idempotencyKey: $"backup-migration-baseline:{source.RuntimeIdentity:N}:{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            requestedBy: "host-agent",
            hostMutationLevel: "filesystem,postgres-read",
            input: new
            {
                source.RuntimeIdentity,
                source.StackSlug,
                source.MatrixServerName,
                source.PostgresContainerName,
            },
            ct);

        var warnings = new List<string>();

        try
        {
            var backupId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssZ");
            var backupRoot = Path.Combine(GetStackBackupsRoot(), source.StackSlug, backupId);
            var databaseDir = Path.Combine(backupRoot, "database");
            var matrixDir = Path.Combine(backupRoot, "matrix");
            var elementDir = Path.Combine(backupRoot, "element");
            Directory.CreateDirectory(databaseDir);
            Directory.CreateDirectory(matrixDir);
            Directory.CreateDirectory(elementDir);

            var databaseDumpPath = Path.Combine(databaseDir, "synapse.sql");
            var databasePassword = await ReadContainerEnvironmentValueAsync(
                source.PostgresContainerName,
                "POSTGRES_PASSWORD",
                ct);

            await DumpPostgresDatabaseAsync(
                source.PostgresContainerName,
                source.DatabaseName,
                source.DatabaseUsername,
                databasePassword,
                databaseDumpPath,
                ct);

            var homeserverSource = Path.Combine(source.MatrixDataPath, "homeserver.yaml");
            var signingKeySource = Path.Combine(source.MatrixDataPath, "signing.key");
            var namedSigningKeySource = Path.Combine(
                source.MatrixDataPath,
                $"{source.MatrixServerName}.signing.key");
            var mediaSource = Path.Combine(source.MatrixDataPath, "media_store");

            string? homeserverBackupPath = null;
            string? signingKeyBackupPath = null;
            string? mediaBackupPath = null;

            if (File.Exists(homeserverSource))
            {
                homeserverBackupPath = Path.Combine(matrixDir, "homeserver.yaml");
                File.Copy(homeserverSource, homeserverBackupPath, overwrite: true);
            }
            else
            {
                warnings.Add("homeserver.yaml was not found in the accepted migration runtime.");
            }

            var selectedSigningKey = File.Exists(signingKeySource)
                ? signingKeySource
                : File.Exists(namedSigningKeySource)
                    ? namedSigningKeySource
                    : null;
            if (selectedSigningKey is not null)
            {
                signingKeyBackupPath = Path.Combine(matrixDir, "signing.key");
                File.Copy(selectedSigningKey, signingKeyBackupPath, overwrite: true);
            }
            else
            {
                warnings.Add("Matrix signing key was not found in the accepted migration runtime.");
            }

            if (Directory.Exists(mediaSource))
            {
                mediaBackupPath = Path.Combine(matrixDir, "media_store");
                CopyDirectory(mediaSource, mediaBackupPath);
            }
            else
            {
                warnings.Add("Matrix media_store directory was not found in the accepted migration runtime.");
            }

            string? elementConfigBackupPath = null;
            LocalBackupElementManifest? elementManifest = null;
            if (!string.IsNullOrWhiteSpace(source.ElementDataPath) &&
                Directory.Exists(source.ElementDataPath))
            {
                var elementConfigSource = Path.Combine(source.ElementDataPath, "config.json");
                if (File.Exists(elementConfigSource))
                {
                    elementConfigBackupPath = Path.Combine(elementDir, "config.json");
                    File.Copy(elementConfigSource, elementConfigBackupPath, overwrite: true);
                }
                else
                {
                    warnings.Add("Element config.json was not found in the accepted migration runtime.");
                }

                elementManifest = new LocalBackupElementManifest(
                    source.ElementDataPath,
                    File.Exists(elementConfigSource) ? elementConfigSource : null,
                    elementDir);
            }
            else
            {
                warnings.Add("Element runtime data path was not found in the accepted migration runtime.");
            }

            var databaseDumpStats = new LocalBackupFileStats(
                File.Exists(databaseDumpPath),
                File.Exists(databaseDumpPath) ? databaseDumpPath : null,
                GetFileSize(databaseDumpPath));
            var homeserverStats = new LocalBackupFileStats(
                homeserverBackupPath is not null && File.Exists(homeserverBackupPath),
                homeserverBackupPath,
                GetFileSize(homeserverBackupPath));
            var signingKeyStats = new LocalBackupFileStats(
                signingKeyBackupPath is not null && File.Exists(signingKeyBackupPath),
                signingKeyBackupPath,
                GetFileSize(signingKeyBackupPath));
            var mediaDirectoryStats = GetDirectoryStats(mediaBackupPath);
            var mediaStats = new LocalBackupDirectoryStats(
                mediaBackupPath is not null && Directory.Exists(mediaBackupPath),
                mediaBackupPath,
                mediaDirectoryStats.Bytes,
                mediaDirectoryStats.Files);
            var elementStats = new LocalBackupFileStats(
                elementConfigBackupPath is not null && File.Exists(elementConfigBackupPath),
                elementConfigBackupPath,
                GetFileSize(elementConfigBackupPath));
            var stats = new LocalBackupStats(
                databaseDumpStats,
                homeserverStats,
                signingKeyStats,
                mediaStats,
                elementStats,
                databaseDumpStats.Bytes + homeserverStats.Bytes + signingKeyStats.Bytes + mediaStats.Bytes + elementStats.Bytes,
                (databaseDumpStats.Included ? 1 : 0) +
                (homeserverStats.Included ? 1 : 0) +
                (signingKeyStats.Included ? 1 : 0) +
                mediaStats.Files +
                (elementStats.Included ? 1 : 0));

            var routeManifest = new LocalBackupRouteManifest(
                MatrixHost: ExtractHostFromUrl(source.MatrixPublicUrl) ?? source.MatrixServerName,
                MatrixPublicUrl: source.MatrixPublicUrl ?? ToHttpsUrl(source.MatrixServerName),
                ElementHost: ExtractHostFromUrl(source.ElementPublicUrl),
                ElementPublicUrl: source.ElementPublicUrl,
                RequiresDns: true);
            var coturnManifest = LocalBackupTurnManifestFactory.Create(
                homeserverBackupPath ?? homeserverSource,
                new Dictionary<string, string?>(StringComparer.Ordinal),
                warnings);

            var manifest = new LocalBackupManifest(
                BackupVersion,
                backupId,
                DateTime.UtcNow,
                source.RuntimeIdentity,
                source.StackSlug,
                new LocalBackupDatabaseManifest(
                    "postgresql",
                    source.PostgresContainerName,
                    5432,
                    source.DatabaseName,
                    source.DatabaseUsername,
                    "migration-baseline-runtime",
                    databaseDumpPath),
                new LocalBackupMatrixManifest(
                    source.MatrixDataPath,
                    File.Exists(homeserverSource) ? homeserverSource : null,
                    selectedSigningKey,
                    Directory.Exists(mediaSource) ? mediaSource : null,
                    matrixDir),
                elementManifest,
                stats,
                warnings)
            {
                Routes = routeManifest,
                Coturn = coturnManifest,
                MemVersion = _runtimeContext.Version,
                MatrixServerName = source.MatrixServerName,
            };

            var manifestPath = Path.Combine(backupRoot, "backup-manifest.json");
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                ct);

            var result = new LocalBackupCaptureResult(
                operationId,
                source.RuntimeIdentity,
                source.StackSlug,
                backupId,
                backupRoot,
                manifestPath,
                databaseDumpPath,
                homeserverBackupPath,
                signingKeyBackupPath,
                mediaBackupPath,
                elementConfigBackupPath,
                stats,
                warnings,
                DateTime.UtcNow);

            try
            {
                var registration = await _catalogRegistrationService.RegisterCapturedAsync(
                    result,
                    manifest,
                    ct);
                result = result with { CatalogEntryId = registration.CatalogEntryId };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add(
                    "Baseline backup payload was captured, but Backup Catalog registration did not complete: " +
                    ex.Message);
            }

            await _operationStore.CompleteAsync(
                operationId,
                warnings.Count == 0 ? "completed" : "completed_with_warnings",
                "completed",
                new
                {
                    result.StackSlug,
                    result.BackupId,
                    result.CatalogEntryId,
                    result.Stats,
                    Warnings = result.Warnings,
                },
                Array.Empty<object>(),
                ct);

            return result;
        }
        catch (Exception ex)
        {
            await _operationStore.FailAsync(
                operationId,
                "failed",
                ex.Message,
                Array.Empty<object>(),
                ct);
            throw;
        }
    }

    private async Task<RuntimeStackEntity> ResolveStackAsync(
        string slugOrId,
        CancellationToken ct)
    {
        var normalized = slugOrId.Trim();

        if (Guid.TryParse(normalized, out var id))
        {
            var byId = await _db.RuntimeStacks
                .AsNoTracking()
                .Include(x => x.Routes)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (byId is not null)
            {
                return byId;
            }
        }

        var bySlug = await _db.RuntimeStacks
            .AsNoTracking()
            .Include(x => x.Routes)
            .FirstOrDefaultAsync(x => x.Slug == normalized, ct);

        if (bySlug is not null)
        {
            return bySlug;
        }

        throw new InvalidOperationException(
            $"Runtime stack '{slugOrId}' was not found.");
    }

    private static LocalBackupRouteManifest BuildRouteManifest(
        RuntimeStackEntity stack,
        RuntimeServiceInstanceEntity matrixService,
        RuntimeServiceInstanceEntity? elementService,
        List<string> warnings)
    {
        var matrixRoute = stack.Routes
            .Where(route => route.IsPublic &&
                            string.Equals(route.ServiceKey, "matrix", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(route => route.LastVerifiedAtUtc)
            .FirstOrDefault();

        var elementRoute = stack.Routes
            .Where(route => route.IsPublic &&
                            (string.Equals(route.ServiceKey, "element-web", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(route.ServiceKey, "element", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(route => route.LastVerifiedAtUtc)
            .FirstOrDefault();

        var matrixHost = FirstNonEmpty(
            matrixRoute?.PublicHost,
            matrixService.PublicHost,
            ExtractHostFromUrl(matrixRoute?.PublicBaseUrl),
            ExtractHostFromUrl(matrixService.PublicBaseUrl),
            ExtractHostFromUrl(stack.MatrixPublicBaseUrl),
            matrixService.ServerName);

        var elementHost = FirstNonEmpty(
            elementRoute?.PublicHost,
            elementService?.PublicHost,
            ExtractHostFromUrl(elementRoute?.PublicBaseUrl),
            ExtractHostFromUrl(elementService?.PublicBaseUrl),
            ExtractHostFromUrl(stack.ElementPublicBaseUrl));

        var matrixPublicUrl = FirstNonEmpty(
            matrixRoute?.PublicBaseUrl,
            matrixService.PublicBaseUrl,
            stack.MatrixPublicBaseUrl,
            ToHttpsUrl(matrixHost));

        var elementPublicUrl = FirstNonEmpty(
            elementRoute?.PublicBaseUrl,
            elementService?.PublicBaseUrl,
            stack.ElementPublicBaseUrl,
            ToHttpsUrl(elementHost));

        if (string.IsNullOrWhiteSpace(matrixHost))
        {
            warnings.Add("Matrix public route metadata was unavailable when this backup was created.");
        }

        if (elementService is not null && string.IsNullOrWhiteSpace(elementHost))
        {
            warnings.Add("Element public route metadata was unavailable when this backup was created. A restore will require an Element host override.");
        }

        return new LocalBackupRouteManifest(
            MatrixHost: matrixHost,
            MatrixPublicUrl: matrixPublicUrl,
            ElementHost: elementHost,
            ElementPublicUrl: elementPublicUrl,
            RequiresDns: !string.IsNullOrWhiteSpace(matrixHost) || !string.IsNullOrWhiteSpace(elementHost));
    }

    private static string? ExtractHostFromUrl(
        string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : null;

    private static string? ToHttpsUrl(
        string? host) =>
        string.IsNullOrWhiteSpace(host)
            ? null
            : $"https://{host}";

    private static string? FirstNonEmpty(
        params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private Task DumpPostgresDatabaseAsync(
        string databaseName,
        string databaseUsername,
        string databasePassword,
        string hostDumpPath,
        CancellationToken ct) =>
        DumpPostgresDatabaseAsync(
            PostgresContainerName,
            databaseName,
            databaseUsername,
            databasePassword,
            hostDumpPath,
            ct);

    private async Task DumpPostgresDatabaseAsync(
        string postgresContainerName,
        string databaseName,
        string databaseUsername,
        string? databasePassword,
        string hostDumpPath,
        CancellationToken ct)
    {
        var containerDumpPath = $"/tmp/mem-backup-{Guid.NewGuid():N}.sql";
        var environment = string.IsNullOrWhiteSpace(databasePassword)
            ? Array.Empty<string>()
            : new[] { $"PGPASSWORD={databasePassword}" };

        var exec = await _docker.Exec.ExecCreateContainerAsync(
            postgresContainerName,
            new ContainerExecCreateParameters
            {
                AttachStdout = true,
                AttachStderr = true,
                Env = environment,
                Cmd =
                [
                    "sh",
                    "-lc",
                    $"pg_dump -U {ShellQuote(databaseUsername)} -d {ShellQuote(databaseName)} --no-owner --no-privileges --format=plain > {ShellQuote(containerDumpPath)}"
                ]
            },
            ct);

        using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
            exec.ID,
            tty: false,
            cancellationToken: ct);

        var output = await stream.ReadOutputToEndAsync(ct);

        var inspect = await _docker.Exec.InspectContainerExecAsync(
            exec.ID,
            ct);

        if (inspect.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"pg_dump failed with exit code {inspect.ExitCode}: {output.stderr}");
        }

        await CopyFileFromContainerAsync(
            postgresContainerName,
            containerDumpPath,
            hostDumpPath,
            ct);

        await TryDeleteContainerFileAsync(
            postgresContainerName,
            containerDumpPath,
            ct);
    }

    private async Task<string?> ReadContainerEnvironmentValueAsync(
        string containerName,
        string key,
        CancellationToken ct)
    {
        var inspect = await _docker.Containers.InspectContainerAsync(containerName, ct);
        var prefix = key + "=";
        var value = inspect.Config?.Env?
            .FirstOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal));
        return value is null ? null : value[prefix.Length..];
    }

    private async Task CopyFileFromContainerAsync(
        string containerName,
        string containerPath,
        string hostPath,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(hostPath)!);

        var exec = await _docker.Exec.ExecCreateContainerAsync(
            containerName,
            new ContainerExecCreateParameters
            {
                AttachStdout = true,
                AttachStderr = true,
                Cmd =
                [
                    "cat",
                    containerPath
                ]
            },
            ct);

        using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
            exec.ID,
            tty: false,
            cancellationToken: ct);

        var output = await stream.ReadOutputToEndAsync(ct);

        var inspect = await _docker.Exec.InspectContainerExecAsync(
            exec.ID,
            ct);

        if (inspect.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to copy container file '{containerPath}' from '{containerName}': {output.stderr}");
        }

        await File.WriteAllTextAsync(
            hostPath,
            output.stdout ?? string.Empty,
            ct);
    }

    private async Task TryDeleteContainerFileAsync(
        string containerName,
        string containerPath,
        CancellationToken ct)
    {
        try
        {
            var exec = await _docker.Exec.ExecCreateContainerAsync(
                containerName,
                new ContainerExecCreateParameters
                {
                    AttachStdout = false,
                    AttachStderr = false,
                    Cmd =
                    [
                        "rm",
                        "-f",
                        containerPath
                    ]
                },
                ct);

            using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
                exec.ID,
                tty: false,
                cancellationToken: ct);

            await stream.ReadOutputToEndAsync(ct);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.GetDirectories(
                     sourceDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            var targetDirectory = directory.Replace(
                sourceDirectory,
                destinationDirectory,
                StringComparison.Ordinal);

            Directory.CreateDirectory(targetDirectory);
        }

        foreach (var file in Directory.GetFiles(
                     sourceDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            var targetFile = file.Replace(
                sourceDirectory,
                destinationDirectory,
                StringComparison.Ordinal);

            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

            File.Copy(
                file,
                targetFile,
                overwrite: true);
        }
    }

    private static (long Bytes, long Files) GetDirectoryStats(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return (0, 0);
        }

        long bytes = 0;
        long files = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(
                         path,
                         "*",
                         SearchOption.AllDirectories))
            {
                try
                {
                    var info = new FileInfo(file);

                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    bytes += info.Length;
                    files++;
                }
                catch
                {
                    // Ignore individual unreadable files for backup stats v1.
                }
            }
        }
        catch
        {
            // Ignore unreadable directory for backup stats v1.
        }

        return (bytes, files);
    }

    private static long GetFileSize(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return 0;
        }

        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static IReadOnlyDictionary<string, string?> DeserializeRuntimeMetadata(
        string? json,
        ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            return metadata is null
                ? new Dictionary<string, string?>(StringComparer.Ordinal)
                : new Dictionary<string, string?>(metadata, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            warnings.Add(
                "Recorded Matrix runtime metadata could not be parsed and was ignored for TURN backup classification.");

            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
    }

    private async Task<LocalBackupLogoManifest?> CaptureStackLogoAsync(
        Guid stackId,
        string backupRoot,
        List<string> warnings,
        CancellationToken ct)
    {
        var manifest = await _manifestStore.FindAsync(stackId.ToString(), ct);
        if (manifest is null)
        {
            return null;
        }

        var metadata = RuntimeStackIdentity.ResolveLogoMetadata(manifest);
        if (metadata is null)
        {
            return null;
        }

        var resolved = _logoService.Resolve(manifest);
        if (resolved is null)
        {
            warnings.Add(
                "The stack declares a custom logo, but its logo payload was unavailable and could not be included in this backup.");
            return null;
        }

        try
        {
            var identityDirectory = Path.Combine(backupRoot, "identity");
            Directory.CreateDirectory(identityDirectory);
            var targetPath = Path.Combine(identityDirectory, "logo.png");
            File.Copy(resolved.Path, targetPath, overwrite: true);

            var copiedLength = new FileInfo(targetPath).Length;
            await using var copiedStream = File.OpenRead(targetPath);
            var copiedSha256 = Convert.ToHexString(
                    await SHA256.HashDataAsync(copiedStream, ct))
                .ToLowerInvariant();

            if (copiedLength != resolved.Bytes ||
                !string.Equals(copiedSha256, resolved.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(targetPath);
                warnings.Add(
                    "The stack logo changed or failed integrity verification while the backup was being captured, so it was not included.");
                return null;
            }

            return new LocalBackupLogoManifest(
                File: "identity/logo.png",
                Sha256: copiedSha256,
                Bytes: copiedLength,
                Width: resolved.Width,
                Height: resolved.Height,
                Present: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add(
                $"The custom stack logo could not be copied into the backup payload: {ex.Message}");
            return null;
        }
    }

    private string GetStackBackupsRoot()
    {
        return Path.Combine(
            GetDataRoot(),
            "backups",
            "stacks");
    }

    private string GetDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static string ShellQuote(
        string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }
}