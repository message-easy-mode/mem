using System.Text.Json;
using Mem.Cli.Clients;
using Mem.Cli.Commands;
using Mem.Cli.Config;
using Mem.Cli.Tests.TestSupport;
using Mem.Localization;

namespace Mem.Cli.Tests.Commands;

[Collection("Console output")]
public sealed class BackupCatalogCommandsTests
{
    [Fact]
    public async Task List_json_emits_stable_catalog_projection_when_german_is_selected()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "totalCount": 1,
                  "entries": [
                    {
                      "catalogEntryId": "catalog-20260702-local-01",
                      "originKind": "local-captured",
                      "displayName": "postgres-stack backup",
                      "sourceStackSlug": "postgres-stack",
                      "sourceBackupId": "20260702-093000Z",
                      "capturedAtUtc": "2026-07-02T09:30:00Z",
                      "payloadState": "available",
                      "integrityStatus": "valid",
                      "warningCount": 0,
                      "payloadBytes": 1310720,
                      "createdAtUtc": "2026-07-02T09:30:03Z",
                      "importedAtUtc": null,
                      "materialisedAtUtc": null,
                      "payloadRemovedAtUtc": null,
                      "advisoryCount": 0
                    }
                  ]
                }
                """));

        var options = new CliOptions(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: true,
            Language: MemLanguage.German);

        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "list", "--json"],
                "list",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.False(captured.StandardOutput.Contains("backupsRootPath", StringComparison.OrdinalIgnoreCase));
        Assert.False(captured.StandardOutput.Contains("backupRootPath", StringComparison.OrdinalIgnoreCase));
        Assert.False(captured.StandardOutput.Contains("storedZipPath", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Sicherungskatalog", captured.StandardOutput, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;

        Assert.Equal("control-plane", root.GetProperty("source").GetString());
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal(1, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            "catalog-20260702-local-01",
            root.GetProperty("entries")[0].GetProperty("catalogEntryId").GetString());
    }

    [Fact]
    public async Task Inspect_human_output_uses_catalog_identity_and_safe_provenance()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "catalogEntryId": "catalog-20260702-import-01",
                  "originKind": "imported-zip",
                  "displayName": "imported-postgres.zip",
                  "sourceStackSlug": "postgres-stack-2",
                  "sourceBackupId": null,
                  "validationId": "20260702-094500Z-12345678",
                  "manifestVersion": 1,
                  "memVersion": "0.1.1",
                  "matrixServerName": "matrix.example.test",
                  "matrixHost": "matrix.example.test",
                  "elementHost": "chat.example.test",
                  "capturedAtUtc": "2026-07-02T09:45:00Z",
                  "payloadState": "available",
                  "integrityStatus": "valid",
                  "integritySummary": "Catalog payload is available.",
                  "warningCount": 0,
                  "payloadBytes": 1153433,
                  "createdAtUtc": "2026-07-02T09:45:04Z",
                  "importedAtUtc": "2026-07-02T09:45:00Z",
                  "materialisedAtUtc": "2026-07-02T09:45:04Z",
                  "payloadRemovedAtUtc": null,
                  "payloadRemovedBy": null,
                  "advisoryCount": 0,
                  "advisories": []
                }
                """));

        var options = new CliOptions(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: false);

        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "inspect", "catalog-20260702-import-01"],
                "inspect",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.Contains("MEM Backup Catalog Entry", captured.StandardOutput);
        Assert.Contains("Catalog entry ID: catalog-20260702-import-01", captured.StandardOutput);
        Assert.Contains("Upload reference: 20260702-094500Z-12345678 (provenance only)", captured.StandardOutput);
        Assert.False(captured.StandardOutput.Contains("Backup root", StringComparison.OrdinalIgnoreCase));
        Assert.False(captured.StandardOutput.Contains("Stored ZIP path", StringComparison.OrdinalIgnoreCase));
        Assert.False(captured.StandardOutput.Contains("/home/master", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task List_human_output_localises_cli_owned_catalog_text_when_german_is_selected()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "totalCount": 1,
                  "entries": [
                    {
                      "catalogEntryId": "catalog-20260702-local-de-01",
                      "originKind": "local-captured",
                      "displayName": "postgres-stack backup",
                      "sourceStackSlug": "postgres-stack",
                      "sourceBackupId": "20260702-093000Z",
                      "capturedAtUtc": "2026-07-02T09:30:00Z",
                      "payloadState": "available",
                      "integrityStatus": "valid",
                      "warningCount": 0,
                      "payloadBytes": 1310720,
                      "createdAtUtc": "2026-07-02T09:30:03Z",
                      "importedAtUtc": null,
                      "materialisedAtUtc": null,
                      "payloadRemovedAtUtc": null,
                      "advisoryCount": 0
                    }
                  ]
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
                ["backups", "list"],
                "list",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.Contains("MEM-Sicherungskatalog", captured.StandardOutput);
        Assert.Contains("Quelle: control-plane", captured.StandardOutput);
        Assert.Contains("Status: OK", captured.StandardOutput);
        Assert.Contains("Einträge: 1", captured.StandardOutput);
        Assert.Contains("KATALOGEINTRAGS-ID", captured.StandardOutput);
        Assert.Contains("lokal erfasst", captured.StandardOutput);
        Assert.Contains("verfügbar", captured.StandardOutput);
        Assert.Contains("gültig", captured.StandardOutput);
        Assert.Contains("1,25 MB", captured.StandardOutput);
        Assert.DoesNotContain("MEM Backup Catalog", captured.StandardOutput, StringComparison.Ordinal);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/internal/host-agent/backups/catalog", request.PathAndQuery);
    }

    [Fact]
    public async Task List_human_output_renders_full_catalog_entry_id_that_can_be_inspected_unchanged()
    {
        const string catalogEntryId = "bkp_20260629-023108Z-586146ba9312474da3bcf43f68485c89";

        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                $$"""
                {
                  "totalCount": 1,
                  "entries": [
                    {
                      "catalogEntryId": "{{catalogEntryId}}",
                      "originKind": "local-captured",
                      "displayName": "cool-stack backup",
                      "sourceStackSlug": "cool-stack",
                      "sourceBackupId": "20260629-023108Z",
                      "capturedAtUtc": "2026-06-28T14:31:08Z",
                      "payloadState": "available",
                      "integrityStatus": "valid",
                      "warningCount": 0,
                      "payloadBytes": 196360,
                      "createdAtUtc": "2026-06-28T14:31:08Z",
                      "importedAtUtc": null,
                      "materialisedAtUtc": null,
                      "payloadRemovedAtUtc": null,
                      "advisoryCount": 0
                    }
                  ]
                }
                """),
            RecordingHttpMessageHandler.Json(
                $$"""
                {
                  "catalogEntryId": "{{catalogEntryId}}",
                  "originKind": "local-captured",
                  "displayName": "cool-stack backup",
                  "sourceStackSlug": "cool-stack",
                  "sourceBackupId": "20260629-023108Z",
                  "validationId": null,
                  "manifestVersion": null,
                  "memVersion": null,
                  "matrixServerName": null,
                  "matrixHost": "matrix-cool-stack.example.test",
                  "elementHost": "chat-cool-stack.example.test",
                  "capturedAtUtc": "2026-06-28T14:31:08Z",
                  "payloadState": "available",
                  "integrityStatus": "valid",
                  "integritySummary": "Required backup material is present.",
                  "warningCount": 0,
                  "payloadBytes": 196360,
                  "createdAtUtc": "2026-06-28T14:31:08Z",
                  "importedAtUtc": null,
                  "materialisedAtUtc": null,
                  "payloadRemovedAtUtc": null,
                  "payloadRemovedBy": null,
                  "advisoryCount": 0,
                  "advisories": []
                }
                """));

        var options = new CliOptions(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: false);

        using var client = new HostAgentClient(options, handler);

        var list = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "list"],
                "list",
                options,
                client));

        Assert.Equal(0, list.ExitCode);
        Assert.Contains(catalogEntryId, list.StandardOutput);

        var inspect = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "inspect", catalogEntryId],
                "inspect",
                options,
                client));

        Assert.Equal(0, inspect.ExitCode);
        Assert.Contains($"Catalog entry ID: {catalogEntryId}", inspect.StandardOutput);

        Assert.Collection(
            handler.Requests,
            request =>
            {
                Assert.Equal("GET", request.Method);
                Assert.Equal("/internal/host-agent/backups/catalog", request.PathAndQuery);
            },
            request =>
            {
                Assert.Equal("GET", request.Method);
                Assert.Equal(
                    $"/internal/host-agent/backups/catalog/{catalogEntryId}",
                    request.PathAndQuery);
            });
    }

    [Fact]
    public async Task Inspect_human_output_localises_cli_owned_text_and_preserves_server_advisory_when_german_is_selected()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "catalogEntryId": "catalog-20260702-import-de-01",
                  "originKind": "imported-zip",
                  "displayName": "imported-postgres.zip",
                  "sourceStackSlug": "postgres-stack-2",
                  "sourceBackupId": null,
                  "validationId": "20260702-094500Z-german-01",
                  "manifestVersion": 1,
                  "memVersion": "0.1.1",
                  "matrixServerName": "matrix.example.test",
                  "matrixHost": "matrix.example.test",
                  "elementHost": "chat.example.test",
                  "capturedAtUtc": "2026-07-02T09:45:00Z",
                  "payloadState": "available",
                  "integrityStatus": "valid",
                  "integritySummary": "Catalog payload is available.",
                  "warningCount": 0,
                  "payloadBytes": 1153433,
                  "createdAtUtc": "2026-07-02T09:45:04Z",
                  "importedAtUtc": "2026-07-02T09:45:00Z",
                  "materialisedAtUtc": "2026-07-02T09:45:04Z",
                  "payloadRemovedAtUtc": null,
                  "payloadRemovedBy": null,
                  "advisoryCount": 1,
                  "advisories": [
                    {
                      "category": "compatibility",
                      "title": "Legacy package",
                      "message": "Server-supplied advisory text remains verbatim."
                    }
                  ]
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
                ["backups", "inspect", "catalog-20260702-import-de-01"],
                "inspect",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.Contains("MEM-Sicherungskatalogeintrag", captured.StandardOutput);
        Assert.Contains("Katalogeintrags-ID: catalog-20260702-import-de-01", captured.StandardOutput);
        Assert.Contains("Herkunft: importierte ZIP-Datei", captured.StandardOutput);
        Assert.Contains("Upload-Referenz: 20260702-094500Z-german-01 (nur Herkunftsnachweis)", captured.StandardOutput);
        Assert.Contains("Matrix-Identität", captured.StandardOutput);
        Assert.Contains("Hinweise", captured.StandardOutput);
        Assert.Contains("Server-supplied advisory text remains verbatim.", captured.StandardOutput);
        Assert.DoesNotContain("Catalog entry ID:", captured.StandardOutput, StringComparison.Ordinal);
    }

}
