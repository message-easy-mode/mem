using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Mem.Cli.Config;
using Mem.Cli.Models;
using Mem.Cli.Output;

namespace Mem.Cli.Clients;

public sealed partial class HostAgentClient : IDisposable
{
    private readonly HttpClient _client;
    private readonly CliOptions _options;
    private bool _authenticated;

    private const string BackupsBasePath = "internal/host-agent/backups";
    private const string BackupCatalogBasePath = BackupsBasePath + "/catalog";
    private const string PortableExportsBasePath = BackupsBasePath + "/artifacts/portable-exports";
    private const string ValidatedImportsBasePath = BackupsBasePath + "/artifacts/validated-imports";

    public HostAgentClient(CliOptions options)
        : this(
            options,
            new HttpClientHandler
            {
                // CLI device credentials are bearer credentials, not browser
                // cookies. The legacy installer-token fallback below carries
                // its own in-memory cookie jar only when explicitly used.
                UseCookies = false
            })
    {
    }

    /// <summary>
    /// Testable transport boundary for CLI commands. The client takes ownership
    /// of the supplied handler for its own lifetime, so tests can provide an
    /// in-memory handler without opening a live network connection.
    /// </summary>
    public HostAgentClient(
        CliOptions options,
        HttpMessageHandler messageHandler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(messageHandler);

        _options = options;

        var transport = string.IsNullOrWhiteSpace(options.DeviceCredential)
            ? new CookieContainerHandler(messageHandler)
            : messageHandler;

        _client = new HttpClient(
            transport,
            disposeHandler: true)
        {
            BaseAddress = new Uri(options.HostAgentUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public async Task<HostAgentStatusResponse> GetHostStatusAsync()
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.GetAsync(
                "internal/host-agent/status");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new CliDeviceSessionRejectedException();
            }

            if (!response.IsSuccessStatusCode)
            {
                return new HostAgentStatusResponse(
                    Source: "control-plane",
                    HostAgent: "error",
                    DockerReachable: false,
                    RuntimeNetworkName: null,
                    NpmReady: false,
                    Status: "error",
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var result = await response.Content.ReadFromJsonAsync<HostAgentStatusResponse>(
                CliOutput.JsonOptions());

            return result ?? new HostAgentStatusResponse(
                Source: "control-plane",
                HostAgent: "error",
                DockerReachable: false,
                RuntimeNetworkName: null,
                NpmReady: false,
                Status: "error",
                Detail: ControlPlaneFailureDetails.EmptyResponse);
        }
        catch (CliDeviceSessionRejectedException)
        {
            throw;
        }
        catch (Exception)
        {
            return new HostAgentStatusResponse(
                Source: "control-plane",
                HostAgent: "unreachable",
                DockerReachable: false,
                RuntimeNetworkName: null,
                NpmReady: false,
                Status: "unreachable",
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    public async Task<RuntimeStackListResponse> GetRuntimeStackListAsync()
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.GetAsync(
                "internal/host-agent/runtime-stacks");

            if (!response.IsSuccessStatusCode)
            {

                return new RuntimeStackListResponse(
                    Source: "control-plane",
                    Status: "error",
                    Stacks: [],
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var result = await response.Content.ReadFromJsonAsync<RuntimeStackListResponse>(
                CliOutput.JsonOptions());

            return result ?? new RuntimeStackListResponse(
                Source: "control-plane",
                Status: "error",
                Stacks: [],
                Detail: ControlPlaneFailureDetails.EmptyResponse);
        }
        catch (Exception)
        {
            return new RuntimeStackListResponse(
                Source: "control-plane",
                Status: "unreachable",
                Stacks: [],
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    public async Task<RuntimeStackInspectResponse> GetRuntimeStackInspectAsync(
        string slugOrId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.GetAsync(
                $"internal/host-agent/runtime-stacks/{Uri.EscapeDataString(slugOrId)}");

            if (!response.IsSuccessStatusCode)
            {

                return new RuntimeStackInspectResponse(
                    Source: "control-plane",
                    Status: "error",
                    StackId: null,
                    Slug: slugOrId,
                    Matrix: null,
                    Element: null,
                    LastVerifiedAtUtc: null,
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var result = await response.Content.ReadFromJsonAsync<RuntimeStackInspectResponse>(
                CliOutput.JsonOptions());

            return result ?? new RuntimeStackInspectResponse(
                Source: "control-plane",
                Status: "error",
                StackId: null,
                Slug: slugOrId,
                Matrix: null,
                Element: null,
                LastVerifiedAtUtc: null,
                Detail: ControlPlaneFailureDetails.EmptyResponse);
        }
        catch (Exception)
        {
            return new RuntimeStackInspectResponse(
                Source: "control-plane",
                Status: "unreachable",
                StackId: null,
                Slug: slugOrId,
                Matrix: null,
                Element: null,
                LastVerifiedAtUtc: null,
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    public async Task<RuntimeStackDoctorResponse> GetRuntimeStackDoctorAsync(
        string slugOrId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.PostAsync(
                $"internal/host-agent/runtime-stacks/{Uri.EscapeDataString(slugOrId)}/doctor",
                content: null);

            if (!response.IsSuccessStatusCode)
            {

                return new RuntimeStackDoctorResponse(
                    Source: "control-plane",
                    Status: "error",
                    StackId: null,
                    Slug: slugOrId,
                    LastVerifiedStatus: null,
                    LastVerifiedAtUtc: null,
                    CheckedAtUtc: DateTimeOffset.UtcNow,
                    AllPassed: false,
                    Checks: [],
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode),
                    OperationId: null,
                    ReportId: null);
            }

            var result = await response.Content.ReadFromJsonAsync<RuntimeStackDoctorResponse>(
                CliOutput.JsonOptions());

            return result ?? new RuntimeStackDoctorResponse(
                Source: "control-plane",
                Status: "error",
                StackId: null,
                Slug: slugOrId,
                LastVerifiedStatus: null,
                LastVerifiedAtUtc: null,
                CheckedAtUtc: DateTimeOffset.UtcNow,
                AllPassed: false,
                Checks: [],
                Detail: ControlPlaneFailureDetails.EmptyResponse,
                OperationId: null,
                ReportId: null);
        }
        catch (Exception)
        {
            return new RuntimeStackDoctorResponse(
                Source: "control-plane",
                Status: "unreachable",
                StackId: null,
                Slug: slugOrId,
                LastVerifiedStatus: null,
                LastVerifiedAtUtc: null,
                CheckedAtUtc: DateTimeOffset.UtcNow,
                AllPassed: false,
                Checks: [],
                Detail: ControlPlaneFailureDetails.Unreachable,
                OperationId: null,
                ReportId: null);
        }
    }

    public async Task<RuntimeStackOperationsResponse> GetRuntimeStackOperationsAsync(
        string slugOrId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.GetAsync(
                $"internal/host-agent/runtime-stacks/{Uri.EscapeDataString(slugOrId)}/operations");

            if (!response.IsSuccessStatusCode)
            {

                return new RuntimeStackOperationsResponse(
                    Source: "control-plane",
                    Status: "error",
                    StackId: null,
                    Slug: slugOrId,
                    Operations: [],
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var result = await response.Content.ReadFromJsonAsync<RuntimeStackOperationsResponse>(
                CliOutput.JsonOptions());

            return result ?? new RuntimeStackOperationsResponse(
                Source: "control-plane",
                Status: "error",
                StackId: null,
                Slug: slugOrId,
                Operations: [],
                Detail: ControlPlaneFailureDetails.EmptyResponse);
        }
        catch (Exception)
        {
            return new RuntimeStackOperationsResponse(
                Source: "control-plane",
                Status: "unreachable",
                StackId: null,
                Slug: slugOrId,
                Operations: [],
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    /// <summary>
    /// Reads the durable Backup Catalog inventory. This is the canonical backup
    /// list for operator-facing CLI work; it deliberately does not use local
    /// backup filesystem paths or legacy stack/backup restore identity.
    /// </summary>
    public async Task<BackupCatalogListCliResult> GetBackupCatalogAsync()
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.GetAsync(BackupCatalogBasePath);

            if (!response.IsSuccessStatusCode)
            {

                return new BackupCatalogListCliResult(
                    Source: "control-plane",
                    Status: "error",
                    TotalCount: 0,
                    Entries: [],
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<BackupCatalogListApiResponse>(
                CliOutput.JsonOptions());

            return payload is null
                ? new BackupCatalogListCliResult(
                    Source: "control-plane",
                    Status: "error",
                    TotalCount: 0,
                    Entries: [],
                    Detail: ControlPlaneFailureDetails.EmptyResponse)
                : new BackupCatalogListCliResult(
                    Source: "control-plane",
                    Status: "ok",
                    TotalCount: payload.TotalCount,
                    Entries: payload.Entries ?? [],
                    Detail: null);
        }
        catch (Exception)
        {
            return new BackupCatalogListCliResult(
                Source: "control-plane",
                Status: "unreachable",
                TotalCount: 0,
                Entries: [],
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    /// <summary>
    /// Reads one durable Backup Catalog entry by its public catalog identity.
    /// The result contains only the server's safe catalog projection.
    /// </summary>
    public async Task<BackupCatalogDetailCliResult> GetBackupCatalogDetailAsync(
        string catalogEntryId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.GetAsync(
                $"{BackupCatalogBasePath}/{Uri.EscapeDataString(catalogEntryId)}");

            if (!response.IsSuccessStatusCode)
            {

                return new BackupCatalogDetailCliResult(
                    Source: "control-plane",
                    Status: "error",
                    Entry: null,
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<BackupCatalogDetailApiResponse>(
                CliOutput.JsonOptions());

            return payload is null
                ? new BackupCatalogDetailCliResult(
                    Source: "control-plane",
                    Status: "error",
                    Entry: null,
                    Detail: ControlPlaneFailureDetails.EmptyResponse)
                : new BackupCatalogDetailCliResult(
                    Source: "control-plane",
                    Status: "ok",
                    Entry: payload,
                    Detail: null);
        }
        catch (Exception)
        {
            return new BackupCatalogDetailCliResult(
                Source: "control-plane",
                Status: "unreachable",
                Entry: null,
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    /// <summary>
    /// Reads delete readiness for one catalog source. This is a safe,
    /// metadata-only lifecycle projection; the Host Agent remains authoritative
    /// for the actual permanent delete.
    /// </summary>
    public async Task<BackupCatalogLifecycleCliResult> GetBackupCatalogLifecycleAsync(
        string catalogEntryId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.GetAsync(
                $"{BackupCatalogBasePath}/{Uri.EscapeDataString(catalogEntryId)}/lifecycle");

            if (!response.IsSuccessStatusCode)
            {

                return new BackupCatalogLifecycleCliResult(
                    Source: "control-plane",
                    Status: "error",
                    Lifecycle: null,
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<BackupCatalogLifecycleApiResponse>(
                CliOutput.JsonOptions());

            return payload is null
                ? new BackupCatalogLifecycleCliResult(
                    Source: "control-plane",
                    Status: "error",
                    Lifecycle: null,
                    Detail: ControlPlaneFailureDetails.EmptyResponse)
                : new BackupCatalogLifecycleCliResult(
                    Source: "control-plane",
                    Status: "ok",
                    Lifecycle: payload,
                    Detail: null);
        }
        catch (Exception)
        {
            return new BackupCatalogLifecycleCliResult(
                Source: "control-plane",
                Status: "unreachable",
                Lifecycle: null,
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    /// <summary>
    /// Permanently deletes a catalog source. The request body identifies the
    /// caller as mem-cli for the server audit projection. The CLI requires
    /// --yes before this method is reached, and the Host Agent still enforces
    /// active-restore protection in case state changes after preflight.
    /// </summary>
    public async Task<BackupCatalogPermanentDeleteCliResult> DeleteBackupCatalogAsync(
        string catalogEntryId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                $"{BackupCatalogBasePath}/{Uri.EscapeDataString(catalogEntryId)}")
            {
                Content = JsonContent.Create(
                    new BackupCatalogPermanentDeleteRequest("mem-cli"),
                    options: CliOutput.JsonOptions())
            };

            var response = await _client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {

                return new BackupCatalogPermanentDeleteCliResult(
                    Source: "control-plane",
                    Status: "error",
                    CatalogEntryId: catalogEntryId,
                    OriginKind: null,
                    DeletedBy: null,
                    PayloadDeleted: false,
                    OriginalArchiveDeleted: false,
                    PortableExportsDeleted: 0,
                    DetachedRestoreAttempts: 0,
                    ActiveRestoreSessionId: null,
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<BackupCatalogPermanentDeleteCliResult>(
                CliOutput.JsonOptions());

            return payload ?? new BackupCatalogPermanentDeleteCliResult(
                Source: "control-plane",
                Status: "error",
                CatalogEntryId: catalogEntryId,
                OriginKind: null,
                DeletedBy: null,
                PayloadDeleted: false,
                OriginalArchiveDeleted: false,
                PortableExportsDeleted: 0,
                DetachedRestoreAttempts: 0,
                ActiveRestoreSessionId: null,
                Detail: ControlPlaneFailureDetails.EmptyResponse);
        }
        catch (Exception)
        {
            return new BackupCatalogPermanentDeleteCliResult(
                Source: "control-plane",
                Status: "unreachable",
                CatalogEntryId: catalogEntryId,
                OriginKind: null,
                DeletedBy: null,
                PayloadDeleted: false,
                OriginalArchiveDeleted: false,
                PortableExportsDeleted: 0,
                DetachedRestoreAttempts: 0,
                ActiveRestoreSessionId: null,
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    /// <summary>
    /// Generates a fresh portable export from managed catalog payload material.
    /// No original uploaded ZIP archive is used as the export source.
    /// </summary>
    public async Task<BackupCatalogPortableExportCreateCliResult> CreateBackupCatalogPortableExportAsync(
        string catalogEntryId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.PostAsync(
                $"{BackupCatalogBasePath}/{Uri.EscapeDataString(catalogEntryId)}/portable-export",
                content: null);

            if (!response.IsSuccessStatusCode)
            {

                return new BackupCatalogPortableExportCreateCliResult(
                    Source: "control-plane",
                    Status: "error",
                    CatalogEntryId: catalogEntryId,
                    OriginKind: null,
                    SourceStackSlug: null,
                    ExportId: null,
                    DownloadName: null,
                    SizeBytes: null,
                    Warnings: [],
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<BackupCatalogPortableExportCreateCliResult>(
                CliOutput.JsonOptions());

            return payload ?? new BackupCatalogPortableExportCreateCliResult(
                Source: "control-plane",
                Status: "error",
                CatalogEntryId: catalogEntryId,
                OriginKind: null,
                SourceStackSlug: null,
                ExportId: null,
                DownloadName: null,
                SizeBytes: null,
                Warnings: [],
                Detail: ControlPlaneFailureDetails.EmptyResponse);
        }
        catch (Exception)
        {
            return new BackupCatalogPortableExportCreateCliResult(
                Source: "control-plane",
                Status: "unreachable",
                CatalogEntryId: catalogEntryId,
                OriginKind: null,
                SourceStackSlug: null,
                ExportId: null,
                DownloadName: null,
                SizeBytes: null,
                Warnings: [],
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    /// <summary>
    /// Downloads a previously generated portable export by its opaque export id.
    /// The byte payload is intentionally internal to the CLI and is never
    /// emitted in human or JSON output.
    /// </summary>
    public async Task<PortableExportDownloadCliResult> DownloadPortableExportAsync(
        string exportId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.GetAsync(
                $"{PortableExportsBasePath}/{Uri.EscapeDataString(exportId)}/download");

            if (!response.IsSuccessStatusCode)
            {

                return new PortableExportDownloadCliResult(
                    Source: "control-plane",
                    Status: "error",
                    ExportId: exportId,
                    DownloadName: null,
                    ContentType: null,
                    Bytes: [],
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();

            return new PortableExportDownloadCliResult(
                Source: "control-plane",
                Status: "downloaded",
                ExportId: exportId,
                DownloadName: GetDownloadNameFromContentDisposition(
                    response.Content.Headers.ContentDisposition?.ToString()) ??
                    $"mem-stack-{exportId}.zip",
                ContentType: response.Content.Headers.ContentType?.ToString() ?? "application/zip",
                Bytes: bytes,
                Detail: null);
        }
        catch (Exception)
        {
            return new PortableExportDownloadCliResult(
                Source: "control-plane",
                Status: "unreachable",
                ExportId: exportId,
                DownloadName: null,
                ContentType: null,
                Bytes: [],
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    /// <summary>
    /// Uploads a portable MEM export ZIP, validates it, and materialises valid
    /// recovery material into the durable Backup Catalog. The returned
    /// catalogEntryId, not validationId, is the identity used for later restore
    /// work.
    /// </summary>
    public async Task<BackupCatalogImportCliResult> ImportPortableZipAsync(
        string zipPath)
    {
        await EnsureAuthenticatedAsync();

        var fullPath = Path.GetFullPath(zipPath);
        var uploadedFileName = Path.GetFileName(fullPath);

        if (!File.Exists(fullPath))
        {
            return new BackupCatalogImportCliResult(
                Source: "cli",
                Status: "invalid",
                ValidationId: null,
                UploadedFileName: uploadedFileName,
                ZipBytes: 0,
                ZipEntryCount: 0,
                TotalUncompressedBytes: 0,
                ManifestPresent: false,
                ChecksumsPresent: false,
                Manifest: null,
                Integrity: EmptyImportIntegrity(),
                Checks: [],
                Warnings: [],
                Errors: [$"File does not exist: {uploadedFileName}"],
                Detail: "The ZIP file could not be found locally.",
                CatalogEntryId: null,
                CatalogPayloadState: null,
                CatalogMaterialisationAction: null);
        }

        try
        {
            await using var fileStream = File.OpenRead(fullPath);

            using var multipart = new MultipartFormDataContent();

            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

            multipart.Add(
                fileContent,
                "file",
                uploadedFileName);

            var response = await _client.PostAsync(
                ValidatedImportsBasePath,
                multipart);

            if (!response.IsSuccessStatusCode)
            {
                return new BackupCatalogImportCliResult(
                    Source: "control-plane",
                    Status: "error",
                    ValidationId: null,
                    UploadedFileName: uploadedFileName,
                    ZipBytes: new FileInfo(fullPath).Length,
                    ZipEntryCount: 0,
                    TotalUncompressedBytes: 0,
                    ManifestPresent: false,
                    ChecksumsPresent: false,
                    Manifest: null,
                    Integrity: EmptyImportIntegrity(),
                    Checks: [],
                    Warnings: [],
                    Errors: [ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode)],
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode),
                    CatalogEntryId: null,
                    CatalogPayloadState: null,
                    CatalogMaterialisationAction: null);
            }

            var result = await response.Content.ReadFromJsonAsync<BackupCatalogImportCliResult>(
                CliOutput.JsonOptions());

            return result ?? new BackupCatalogImportCliResult(
                Source: "control-plane",
                Status: "error",
                ValidationId: null,
                UploadedFileName: uploadedFileName,
                ZipBytes: new FileInfo(fullPath).Length,
                ZipEntryCount: 0,
                TotalUncompressedBytes: 0,
                ManifestPresent: false,
                ChecksumsPresent: false,
                Manifest: null,
                Integrity: EmptyImportIntegrity(),
                Checks: [],
                Warnings: [],
                Errors: [ControlPlaneFailureDetails.EmptyResponse],
                Detail: ControlPlaneFailureDetails.EmptyResponse,
                CatalogEntryId: null,
                CatalogPayloadState: null,
                CatalogMaterialisationAction: null);
        }
        catch (Exception)
        {
            return new BackupCatalogImportCliResult(
                Source: "control-plane",
                Status: "unreachable",
                ValidationId: null,
                UploadedFileName: uploadedFileName,
                ZipBytes: File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0,
                ZipEntryCount: 0,
                TotalUncompressedBytes: 0,
                ManifestPresent: false,
                ChecksumsPresent: false,
                Manifest: null,
                Integrity: EmptyImportIntegrity(),
                Checks: [],
                Warnings: [],
                Errors: ["The ZIP could not be read or uploaded."],
                Detail: "The ZIP could not be read locally or the control plane could not be reached.",
                CatalogEntryId: null,
                CatalogPayloadState: null,
                CatalogMaterialisationAction: null);
        }
    }

    /// <summary>
    /// Reads safe retained-upload provenance by validation receipt. This is
    /// archive management only; validationId is never used as restore identity.
    /// </summary>
    public async Task<UploadedZipArchiveDetailCliResult> GetUploadedZipArchiveAsync(
        string validationId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.GetAsync(
                $"{ValidatedImportsBasePath}/{Uri.EscapeDataString(validationId)}");

            if (!response.IsSuccessStatusCode)
            {
                return UploadedZipArchiveError(
                    validationId,
                    ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var result = await response.Content.ReadFromJsonAsync<UploadedZipArchiveDetailCliResult>(
                CliOutput.JsonOptions());

            return result ?? UploadedZipArchiveError(
                validationId,
                ControlPlaneFailureDetails.EmptyResponse);
        }
        catch (Exception)
        {
            return UploadedZipArchiveError(
                validationId,
                ControlPlaneFailureDetails.Unreachable,
                status: "unreachable");
        }
    }

    /// <summary>
    /// Deletes only the retained imported archive after the CLI has required an
    /// explicit operator acknowledgement. Materialised catalog payload and
    /// durable restore history are controlled by separate lifecycles.
    /// </summary>
    public async Task<UploadedZipArchiveDeleteCliResult> DeleteUploadedZipArchiveAsync(
        string validationId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.DeleteAsync(
                $"{ValidatedImportsBasePath}/{Uri.EscapeDataString(validationId)}?acknowledgeDelete=true");

            if (!response.IsSuccessStatusCode)
            {
                return new UploadedZipArchiveDeleteCliResult(
                    Source: "control-plane",
                    Status: "error",
                    ValidationId: validationId,
                    ArchiveState: "unknown",
                    DeletedBytes: 0,
                    DeletedAtUtc: null,
                    Warnings: [],
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var result = await response.Content.ReadFromJsonAsync<UploadedZipArchiveDeleteCliResult>(
                CliOutput.JsonOptions());

            return result ?? new UploadedZipArchiveDeleteCliResult(
                Source: "control-plane",
                Status: "error",
                ValidationId: validationId,
                ArchiveState: "unknown",
                DeletedBytes: 0,
                DeletedAtUtc: null,
                Warnings: [],
                Detail: ControlPlaneFailureDetails.EmptyResponse);
        }
        catch (Exception)
        {
            return new UploadedZipArchiveDeleteCliResult(
                Source: "control-plane",
                Status: "unreachable",
                ValidationId: validationId,
                ArchiveState: "unknown",
                DeletedBytes: 0,
                DeletedAtUtc: null,
                Warnings: [],
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    private static BackupCatalogImportIntegritySummary EmptyImportIntegrity()
    {
        return new BackupCatalogImportIntegritySummary(
            ChecksumLines: 0,
            CheckedFiles: 0,
            MissingFiles: 0,
            FailedFiles: 0,
            PassedFiles: 0);
    }

    private static UploadedZipArchiveDetailCliResult UploadedZipArchiveError(
        string validationId,
        string detail,
        string status = "error")
    {
        return new UploadedZipArchiveDetailCliResult(
            Source: "control-plane",
            Status: status,
            ValidationId: validationId,
            SourceKind: "uploaded-zip",
            UploadedFileName: null,
            RecordedAtUtc: null,
            ArchiveBytes: null,
            ArchiveState: "unknown",
            Validation: new UploadedZipArchiveValidationCliSummary(
                Status: "unknown",
                Summary: "Validation details are unavailable.",
                ZipEntryCount: 0,
                TotalUncompressedBytes: 0,
                ManifestPresent: false,
                ChecksumsPresent: false,
                PassedChecks: 0,
                FailedChecks: 0,
                WarningCount: 0,
                Errors: []),
            Manifest: null,
            Retention: new UploadedZipArchiveRetentionCliSummary(
                CanDelete: false,
                DeleteBlockReason: null,
                RemovedAtUtc: null,
                RemovedBy: null),
            Warnings: [],
            Detail: detail);
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_authenticated)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_options.DeviceCredential))
        {
            if (!CliDeviceCredentialValidator.IsValid(_options.DeviceCredential))
            {
                throw new InstallerAuthenticationException(
                    InstallerAuthenticationFailureKind.MissingInstallerToken);
            }

            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    _options.DeviceCredential.Trim());

            _authenticated = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.InstallerToken))
        {
            throw new InstallerAuthenticationException(
                InstallerAuthenticationFailureKind.MissingInstallerToken);
        }

        using var unlockResponse = await _client.PostAsJsonAsync(
            "api/installer-auth/unlock",
            new InstallerUnlockRequest(_options.InstallerToken),
            CliOutput.JsonOptions());

        if (!unlockResponse.IsSuccessStatusCode)
        {
            throw new InstallerAuthenticationException(
                InstallerAuthenticationFailureKind.InstallerTokenRejected,
                (int)unlockResponse.StatusCode);
        }

        _authenticated = true;
    }

    /// <summary>
    /// Applies the short-lived legacy installer authentication cookie to a CLI client
    /// regardless of whether its inner transport is a real HTTP handler or a
    /// test handler. It deliberately carries only cookies; no Host Agent
    /// shared-secret header is added anywhere in the CLI transport.
    /// </summary>
    private sealed class CookieContainerHandler : DelegatingHandler
    {
        private readonly CookieContainer _cookies = new();

        public CookieContainerHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is { IsAbsoluteUri: true } requestUri)
            {
                var cookieHeader = _cookies.GetCookieHeader(requestUri);

                if (!string.IsNullOrWhiteSpace(cookieHeader) &&
                    !request.Headers.Contains("Cookie"))
                {
                    request.Headers.TryAddWithoutValidation(
                        "Cookie",
                        cookieHeader);
                }
            }

            var response = await base.SendAsync(
                request,
                cancellationToken);

            if (request.RequestUri is { IsAbsoluteUri: true } responseUri &&
                response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
            {
                foreach (var setCookieHeader in setCookieHeaders)
                {
                    _cookies.SetCookies(
                        responseUri,
                        setCookieHeader);
                }
            }

            return response;
        }
    }

    private static string? GetDownloadNameFromContentDisposition(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        const string utf8Prefix = "filename*=UTF-8''";
        var utf8Index = value.IndexOf(
            utf8Prefix,
            StringComparison.OrdinalIgnoreCase);

        if (utf8Index >= 0)
        {
            var start = utf8Index + utf8Prefix.Length;
            var end = value.IndexOf(';', start);
            var encoded = end >= 0
                ? value[start..end]
                : value[start..];

            return Uri.UnescapeDataString(encoded.Trim().Trim('"'));
        }

        const string normalPrefix = "filename=";
        var normalIndex = value.IndexOf(
            normalPrefix,
            StringComparison.OrdinalIgnoreCase);

        if (normalIndex >= 0)
        {
            var start = normalIndex + normalPrefix.Length;
            var end = value.IndexOf(';', start);
            var fileName = end >= 0
                ? value[start..end]
                : value[start..];

            return fileName.Trim().Trim('"');
        }

        return null;
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}