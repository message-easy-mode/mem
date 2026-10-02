using System.Runtime.InteropServices;
using System.Text.Json;
using Mem.Cli.Clients;
using Mem.Cli.Commands;
using Mem.Cli.Config;
using Mem.Cli.Tests.TestSupport;
using Mem.Localization;

namespace Mem.Cli.Tests.Commands;

[Collection("Console output")]
public sealed class BackupCatalogLifecycleCommandsTests
{
    [Fact]
    public async Task Lifecycle_json_emits_safe_catalog_lifecycle_projection()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "catalogEntryId": "bkp_catalog_lifecycle_command_01",
                  "payloadState": "available",
                  "payloadPresent": true,
                  "hasActiveRestore": false,
                  "activeRestoreSessionId": null,
                  "canDelete": true,
                  "deleteBlockReason": null,
                  "originalArchive": {
                    "validationId": "20260702-120100Z-import-01",
                    "archiveState": "retained",
                    "originalFileName": "source.zip",
                    "archiveBytes": 1024,
                    "catalogEntryLinked": true,
                    "catalogEntryId": "bkp_catalog_lifecycle_command_01",
                    "detail": "Original ZIP transport archive is retained."
                  }
                }
                """));

        var options = JsonOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "lifecycle", "bkp_catalog_lifecycle_command_01", "--json"],
                "lifecycle",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.DoesNotContain("payloadDirectoryPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storedZipPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;

        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("lifecycle").GetProperty("canDelete").GetBoolean());
        Assert.Equal(
            "20260702-120100Z-import-01",
            root.GetProperty("lifecycle").GetProperty("originalArchive").GetProperty("validationId").GetString());
    }

    [Fact]
    public async Task Delete_without_yes_does_not_call_the_control_plane()
    {
        var handler = new RecordingHttpMessageHandler();
        var options = GermanHumanOptions();

        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "delete", "bkp_catalog_delete_no_yes"],
                "delete",
                options,
                client));

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains(
            "Ein Sicherungskatalogeintrag wird ohne --yes nicht dauerhaft gelöscht.",
            captured.StandardError);
        Assert.Contains(
            "Aktive Wiederherstellungsarbeitsbereiche verhindern die Löschung.",
            captured.StandardError);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Delete_legacy_stack_and_backup_shape_refuses_before_network_call()
    {
        var handler = new RecordingHttpMessageHandler();
        var options = HumanOptions();

        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "delete", "demo-stack", "legacy-backup-id", "--yes"],
                "delete",
                options,
                client));

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains(
            "Backups delete is now catalog-first and accepts one catalog entry id.",
            captured.StandardError);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Delete_json_blocks_without_delete_request_when_an_active_restore_exists()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "catalogEntryId": "bkp_catalog_delete_active_01",
                  "payloadState": "available",
                  "payloadPresent": true,
                  "hasActiveRestore": true,
                  "activeRestoreSessionId": "restore-active-01",
                  "canDelete": false,
                  "deleteBlockReason": "An active restore workspace references this Backup Catalog entry.",
                  "originalArchive": null
                }
                """));

        var options = JsonOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "delete", "bkp_catalog_delete_active_01", "--yes", "--json"],
                "delete",
                options,
                client));

        Assert.Equal(2, captured.ExitCode);
        Assert.Empty(captured.StandardError);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;

        Assert.Equal("blocked", root.GetProperty("status").GetString());
        Assert.Equal(
            "restore-active-01",
            root.GetProperty("activeRestoreSessionId").GetString());

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            "/internal/host-agent/backups/catalog/bkp_catalog_delete_active_01/lifecycle",
            request.PathAndQuery);
    }

    [Fact]
    public async Task Delete_json_preflights_then_permanently_deletes_by_catalog_identity()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "catalogEntryId": "bkp_catalog_delete_01",
                  "payloadState": "available",
                  "payloadPresent": true,
                  "hasActiveRestore": false,
                  "activeRestoreSessionId": null,
                  "canDelete": true,
                  "deleteBlockReason": null,
                  "originalArchive": null
                }
                """),
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

        var options = JsonOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "delete", "bkp_catalog_delete_01", "--yes", "--json"],
                "delete",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.DoesNotContain("payloadDirectoryPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;

        Assert.Equal("deleted", root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("detachedRestoreAttempts").GetInt32());

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("GET", handler.Requests[0].Method);
        Assert.Equal("DELETE", handler.Requests[1].Method);
        Assert.Equal(
            "/internal/host-agent/backups/catalog/bkp_catalog_delete_01",
            handler.Requests[1].PathAndQuery);

        using var body = JsonDocument.Parse(
            Assert.IsType<string>(handler.Requests[1].ContentBody));

        Assert.Equal("mem-cli", body.RootElement.GetProperty("operator").GetString());
    }

    [Fact]
    public async Task Export_json_generates_from_catalog_and_writes_only_the_operator_selected_path()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"mem-cli-catalog-export-{Guid.NewGuid():N}");

        var outputPath = Path.Combine(
            outputDirectory,
            "portable-export.zip");

        try
        {
            var handler = new RecordingHttpMessageHandler(
                RecordingHttpMessageHandler.Json(
                    """
                    {
                      "source": "control-plane",
                      "status": "created",
                      "catalogEntryId": "bkp_catalog_export_command_01",
                      "originKind": "imported-zip",
                      "sourceStackSlug": "demo-stack",
                      "exportId": "catalog-bkp_catalog_export_command_01",
                      "downloadName": "mem-stack-catalog-bkp_catalog_export_command_01.zip",
                      "downloadPath": "/internal/host-agent/backups/artifacts/portable-exports/catalog-bkp_catalog_export_command_01/download",
                      "sizeBytes": 4,
                      "warnings": [],
                      "detail": "A fresh portable MEM export was generated from the managed Backup Catalog payload."
                    }
                    """),
                RecordingHttpMessageHandler.Bytes(
                    [0x50, 0x4b, 0x03, 0x04],
                    "mem-stack-catalog-bkp_catalog_export_command_01.zip"));

            var options = JsonOptions();
            using var client = new HostAgentClient(options, handler);

            uint? originalUmask = null;

            try
            {
                if (OperatingSystem.IsLinux())
                {
                    originalUmask = Umask(0x0002);
                }

                var captured = await ConsoleOutputCapture.CaptureAsync(() =>
                    BackupCommands.RunAsync(
                        ["backups", "export", "bkp_catalog_export_command_01", "--out", outputPath, "--json"],
                        "export",
                        options,
                        client));

                Assert.Equal(0, captured.ExitCode);
                Assert.Empty(captured.StandardError);
                Assert.True(File.Exists(outputPath));
                Assert.Equal(new byte[] { 0x50, 0x4b, 0x03, 0x04 }, await File.ReadAllBytesAsync(outputPath));

                if (OperatingSystem.IsLinux())
                {
                    Assert.Equal(
                        UnixFileMode.UserRead | UnixFileMode.UserWrite,
                        File.GetUnixFileMode(outputPath));
                }

                Assert.DoesNotContain("downloadPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("exportPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);

                using var document = JsonDocument.Parse(captured.StandardOutput);
                var root = document.RootElement;

                Assert.Equal("created", root.GetProperty("status").GetString());
                Assert.Equal(
                    "bkp_catalog_export_command_01",
                    root.GetProperty("catalogEntryId").GetString());
                Assert.Equal(
                    Path.GetFullPath(outputPath),
                    root.GetProperty("outputPath").GetString());
                Assert.Equal(4, root.GetProperty("bytesWritten").GetInt64());

                Assert.Equal(2, handler.Requests.Count);
                Assert.Equal("POST", handler.Requests[0].Method);
                Assert.Equal(
                    "/internal/host-agent/backups/catalog/bkp_catalog_export_command_01/portable-export",
                    handler.Requests[0].PathAndQuery);
                Assert.Equal("GET", handler.Requests[1].Method);
                Assert.Equal(
                    "/internal/host-agent/backups/artifacts/portable-exports/catalog-bkp_catalog_export_command_01/download",
                    handler.Requests[1].PathAndQuery);
            }
            finally
            {
                if (originalUmask.HasValue)
                {
                    Umask(originalUmask.Value);
                }
            }
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Delete_human_output_localises_permanent_delete_and_preserves_server_detail_when_german_is_selected()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "catalogEntryId": "bkp_catalog_delete_de_01",
                  "payloadState": "available",
                  "payloadPresent": true,
                  "hasActiveRestore": false,
                  "activeRestoreSessionId": null,
                  "canDelete": true,
                  "deleteBlockReason": null,
                  "originalArchive": null
                }
                """),
            RecordingHttpMessageHandler.Json(
                """
                {
                  "source": "control-plane",
                  "status": "deleted",
                  "catalogEntryId": "bkp_catalog_delete_de_01",
                  "originKind": "imported-zip",
                  "deletedBy": "mem-cli",
                  "payloadDeleted": true,
                  "originalArchiveDeleted": true,
                  "portableExportsDeleted": 1,
                  "detachedRestoreAttempts": 2,
                  "detail": "Server deletion detail remains verbatim."
                }
                """));

        var options = GermanHumanOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "delete", "bkp_catalog_delete_de_01", "--yes"],
                "delete",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.Contains("Dauerhaftes Löschen des MEM-Sicherungskatalogs", captured.StandardOutput);
        Assert.Contains("Status: gelöscht", captured.StandardOutput);
        Assert.Contains("Herkunft: importierte ZIP-Datei", captured.StandardOutput);
        Assert.Contains("Nutzlast gelöscht: ja", captured.StandardOutput);
        Assert.Contains("Ursprüngliches Archiv gelöscht: ja", captured.StandardOutput);
        Assert.Contains("Gelöschte portable Exporte: 1", captured.StandardOutput);
        Assert.Contains("Getrennte Wiederherstellungsversuche: 2", captured.StandardOutput);
        Assert.Contains("Details: Server deletion detail remains verbatim.", captured.StandardOutput);
        Assert.Contains("Die Katalogquelle wurde dauerhaft gelöscht.", captured.StandardOutput);
        Assert.Contains(
            "Abgeschlossene Wiederherstellungsverläufe bleiben ohne Verknüpfung zur Katalogquelle verfügbar.",
            captured.StandardOutput);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("GET", handler.Requests[0].Method);
        Assert.Equal("DELETE", handler.Requests[1].Method);
    }

    [Fact]
    public async Task Export_human_output_localises_cli_owned_text_and_preserves_server_detail_when_german_is_selected()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"mem-cli-catalog-export-de-{Guid.NewGuid():N}");

        var outputPath = Path.Combine(
            outputDirectory,
            "portable-export.zip");

        try
        {
            var handler = new RecordingHttpMessageHandler(
                RecordingHttpMessageHandler.Json(
                    """
                    {
                      "source": "control-plane",
                      "status": "created",
                      "catalogEntryId": "bkp_catalog_export_de_01",
                      "originKind": "imported-zip",
                      "sourceStackSlug": "demo-stack",
                      "exportId": "catalog-bkp_catalog_export_de_01",
                      "downloadName": "mem-stack-catalog-bkp_catalog_export_de_01.zip",
                      "sizeBytes": 4,
                      "warnings": ["Server warning remains verbatim."],
                      "detail": "Server export detail remains verbatim."
                    }
                    """),
                RecordingHttpMessageHandler.Bytes(
                    [0x50, 0x4b, 0x03, 0x04],
                    "mem-stack-catalog-bkp_catalog_export_de_01.zip"));

            var options = GermanHumanOptions();
            using var client = new HostAgentClient(options, handler);

            var captured = await ConsoleOutputCapture.CaptureAsync(() =>
                BackupCommands.RunAsync(
                    ["backups", "export", "bkp_catalog_export_de_01", "--out", outputPath],
                    "export",
                    options,
                    client));

            Assert.Equal(0, captured.ExitCode);
            Assert.Empty(captured.StandardError);
            Assert.True(File.Exists(outputPath));
            Assert.Contains("MEM-Sicherungskatalogexport", captured.StandardOutput);
            Assert.Contains("Status: erstellt", captured.StandardOutput);
            Assert.Contains("Herkunft: importierte ZIP-Datei", captured.StandardOutput);
            Assert.Contains($"Ausgabepfad: {Path.GetFullPath(outputPath)}", captured.StandardOutput);
            Assert.Contains("Exportgröße: 4 B", captured.StandardOutput);
            Assert.Contains("Geschriebene Bytes: 4 B", captured.StandardOutput);
            Assert.Contains("Warnungen", captured.StandardOutput);
            Assert.Contains("Server warning remains verbatim.", captured.StandardOutput);
            Assert.Contains("Details: Server export detail remains verbatim.", captured.StandardOutput);

            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal("POST", handler.Requests[0].Method);
            Assert.Equal("GET", handler.Requests[1].Method);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static CliOptions JsonOptions() =>
        new(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: true);

    private static CliOptions HumanOptions() =>
        new(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: false);

    private static CliOptions GermanHumanOptions() =>
        new(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: false,
            Language: MemLanguage.German);

    [Fact]
    public async Task Lifecycle_human_output_localises_known_values_when_german_is_selected()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "catalogEntryId": "bkp_catalog_lifecycle_de_01",
                  "payloadState": "available",
                  "payloadPresent": true,
                  "hasActiveRestore": false,
                  "activeRestoreSessionId": null,
                  "canDelete": true,
                  "deleteBlockReason": null,
                  "originalArchive": {
                    "validationId": "20260702-120100Z-import-de-01",
                    "archiveState": "retained",
                    "originalFileName": "source.zip",
                    "archiveBytes": 1024,
                    "catalogEntryLinked": true,
                    "catalogEntryId": "bkp_catalog_lifecycle_de_01",
                    "detail": "Original ZIP transport archive is retained."
                  }
                }
                """));

        var options = new CliOptions(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: false,
            Language: MemLanguage.German);

        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "lifecycle", "bkp_catalog_lifecycle_de_01"],
                "lifecycle",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.Contains("Lebenszyklus des MEM-Sicherungskatalogs", captured.StandardOutput);
        Assert.Contains("Nutzlaststatus: verfügbar", captured.StandardOutput);
        Assert.Contains("Nutzlast vorhanden: ja", captured.StandardOutput);
        Assert.Contains("Aktive Wiederherstellung: nein", captured.StandardOutput);
        Assert.Contains("Darf dauerhaft gelöscht werden: ja", captured.StandardOutput);
        Assert.Contains("Ursprünglich hochgeladenes ZIP-Archiv", captured.StandardOutput);
        Assert.Contains("Validierungsreferenz: 20260702-120100Z-import-de-01 (nur Herkunftsnachweis)", captured.StandardOutput);
        Assert.Contains("Archivstatus: aufbewahrt", captured.StandardOutput);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            "/internal/host-agent/backups/catalog/bkp_catalog_lifecycle_de_01/lifecycle",
            request.PathAndQuery);
    }

    [DllImport("libc", EntryPoint = "umask")]
    private static extern uint Umask(
        uint mask);

}
