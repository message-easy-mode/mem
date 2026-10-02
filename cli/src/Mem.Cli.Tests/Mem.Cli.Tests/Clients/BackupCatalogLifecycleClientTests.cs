using System.Text.Json;
using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Tests.TestSupport;

namespace Mem.Cli.Tests.Clients;

public sealed class BackupCatalogLifecycleClientTests
{
    [Fact]
    public async Task GetBackupCatalogLifecycleAsync_uses_canonical_lifecycle_route_and_projects_active_restore()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "catalogEntryId": "bkp_catalog_lifecycle_01",
                  "payloadState": "available",
                  "payloadPresent": true,
                  "hasActiveRestore": true,
                  "activeRestoreSessionId": "restore-active-01",
                  "canDelete": false,
                  "deleteBlockReason": "An active restore workspace references this Backup Catalog entry.",
                  "originalArchive": {
                    "validationId": "20260702-120000Z-import-01",
                    "archiveState": "retained",
                    "originalFileName": "source.zip",
                    "archiveBytes": 1310720,
                    "catalogEntryLinked": true,
                    "catalogEntryId": "bkp_catalog_lifecycle_01",
                    "detail": "Original ZIP transport archive is retained."
                  }
                }
                """));

        using var client = CreateClient(handler);

        var result = await client.GetBackupCatalogLifecycleAsync(
            "bkp_catalog_lifecycle_01");

        Assert.Equal("ok", result.Status);

        var lifecycle = Assert.IsType<Mem.Cli.Models.BackupCatalogLifecycleApiResponse>(
            result.Lifecycle);

        Assert.Equal("bkp_catalog_lifecycle_01", lifecycle.CatalogEntryId);
        Assert.True(lifecycle.HasActiveRestore);
        Assert.Equal("restore-active-01", lifecycle.ActiveRestoreSessionId);
        Assert.False(lifecycle.CanDelete);
        Assert.Equal("retained", lifecycle.OriginalArchive?.ArchiveState);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            "/internal/host-agent/backups/catalog/bkp_catalog_lifecycle_01/lifecycle",
            request.PathAndQuery);
        Assert.False(request.HasRetiredSharedSecretHeader);
        Assert.Null(request.ContentBody);
    }

    [Fact]
    public async Task DeleteBackupCatalogAsync_uses_catalog_identity_and_auditable_operator_body()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "source": "control-plane",
                  "status": "deleted",
                  "catalogEntryId": "bkp_catalog_delete_01",
                  "originKind": "imported-zip",
                  "deletedBy": "mem-cli",
                  "payloadDeleted": true,
                  "originalArchiveDeleted": true,
                  "portableExportsDeleted": 1,
                  "detachedRestoreAttempts": 2,
                  "detail": "The Backup Catalog record was permanently deleted."
                }
                """));

        using var client = CreateClient(handler);

        var result = await client.DeleteBackupCatalogAsync(
            "bkp_catalog_delete_01");

        Assert.Equal("deleted", result.Status);
        Assert.Equal("bkp_catalog_delete_01", result.CatalogEntryId);
        Assert.True(result.PayloadDeleted);
        Assert.True(result.OriginalArchiveDeleted);
        Assert.Equal(2, result.DetachedRestoreAttempts);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("DELETE", request.Method);
        Assert.Equal(
            "/internal/host-agent/backups/catalog/bkp_catalog_delete_01",
            request.PathAndQuery);
        Assert.Equal("application/json", request.ContentType);

        using var body = JsonDocument.Parse(
            Assert.IsType<string>(request.ContentBody));

        Assert.Equal(
            "mem-cli",
            body.RootElement.GetProperty("operator").GetString());
    }

    [Fact]
    public async Task Portable_export_create_and_download_use_catalog_routes_without_control_plane_paths()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "source": "control-plane",
                  "status": "created",
                  "catalogEntryId": "bkp_catalog_export_01",
                  "originKind": "local-captured",
                  "sourceStackSlug": "demo-stack",
                  "exportId": "catalog-bkp_catalog_export_01",
                  "downloadName": "mem-stack-catalog-bkp_catalog_export_01.zip",
                  "downloadPath": "/internal/host-agent/backups/artifacts/portable-exports/catalog-bkp_catalog_export_01/download",
                  "sizeBytes": 1024,
                  "warnings": [
                    "This export contains Matrix signing identity material. Store it securely."
                  ],
                  "detail": "A fresh portable MEM export was generated from the managed Backup Catalog payload."
                }
                """),
            RecordingHttpMessageHandler.Bytes(
                [0x50, 0x4b, 0x03, 0x04],
                "mem-stack-catalog-bkp_catalog_export_01.zip"));

        using var client = CreateClient(handler);

        var created = await client.CreateBackupCatalogPortableExportAsync(
            "bkp_catalog_export_01");

        var downloaded = await client.DownloadPortableExportAsync(
            Assert.IsType<string>(created.ExportId));

        Assert.Equal("created", created.Status);
        Assert.Equal("bkp_catalog_export_01", created.CatalogEntryId);
        Assert.Equal("catalog-bkp_catalog_export_01", created.ExportId);
        Assert.Equal("downloaded", downloaded.Status);
        Assert.Equal(4, downloaded.Bytes.Length);

        Assert.Equal(2, handler.Requests.Count);

        Assert.Equal("POST", handler.Requests[0].Method);
        Assert.Equal(
            "/internal/host-agent/backups/catalog/bkp_catalog_export_01/portable-export",
            handler.Requests[0].PathAndQuery);

        Assert.Equal("GET", handler.Requests[1].Method);
        Assert.Equal(
            "/internal/host-agent/backups/artifacts/portable-exports/catalog-bkp_catalog_export_01/download",
            handler.Requests[1].PathAndQuery);

        Assert.DoesNotContain(
            "downloadPath",
            JsonSerializer.Serialize(created),
            StringComparison.OrdinalIgnoreCase);
    }

    private static HostAgentClient CreateClient(
        RecordingHttpMessageHandler handler) =>
        new(
            new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: "mem_cli_test_token",
                Json: false),
            handler);
}
