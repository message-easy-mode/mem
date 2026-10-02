using System.Text.Json;
using Mem.Cli.Clients;
using Mem.Cli.Commands;
using Mem.Cli.Config;
using Mem.Cli.Tests.TestSupport;
using Mem.Localization;

namespace Mem.Cli.Tests.Commands;

[Collection("Console output")]
public sealed class BackupUploadCommandsTests
{
    [Fact]
    public async Task Inspect_json_emits_safe_uploaded_zip_provenance()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "source": "control-plane",
                  "status": "ok",
                  "validationId": "20260702-111500Z-upload-01",
                  "sourceKind": "uploaded-zip",
                  "uploadedFileName": "portable-export.zip",
                  "recordedAtUtc": "2026-07-02T11:15:00Z",
                  "archiveBytes": 1310720,
                  "archiveState": "retained",
                  "validation": {
                    "status": "valid",
                    "summary": "All required checks passed.",
                    "zipEntryCount": 17,
                    "totalUncompressedBytes": 2621440,
                    "manifestPresent": true,
                    "checksumsPresent": true,
                    "passedChecks": 12,
                    "failedChecks": 0,
                    "warningCount": 0,
                    "errors": []
                  },
                  "manifest": {
                    "manifestVersion": 1,
                    "memVersion": "0.1.1",
                    "sourceStackSlug": "imported-stack",
                    "sourceStackDisplayName": "Imported stack",
                    "matrixServerName": "matrix.imported.test",
                    "includedFileCount": 17
                  },
                  "retention": {
                    "canDelete": true,
                    "deleteBlockReason": null,
                    "removedAtUtc": null,
                    "removedBy": null
                  },
                  "warnings": [],
                  "detail": "Uploaded ZIP metadata loaded."
                }
                """));

        var options = new CliOptions(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: true);

        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "uploads", "inspect", "20260702-111500Z-upload-01", "--json"],
                "uploads",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.DoesNotContain("storedZipPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("backupRootPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;

        Assert.Equal("20260702-111500Z-upload-01", root.GetProperty("validationId").GetString());
        Assert.Equal("retained", root.GetProperty("archiveState").GetString());
        Assert.Equal(
            "imported-stack",
            root.GetProperty("manifest").GetProperty("sourceStackSlug").GetString());

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            "/internal/host-agent/backups/artifacts/validated-imports/20260702-111500Z-upload-01",
            request.PathAndQuery);
    }

    [Fact]
    public async Task Delete_without_yes_does_not_call_host_agent()
    {
        var handler = new RecordingHttpMessageHandler();

        var options = new CliOptions(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: false,
            Language: MemLanguage.German);

        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "uploads", "delete", "20260702-111500Z-upload-02"],
                "uploads",
                options,
                client));

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains(
            "Ein hochgeladenes ZIP-Archiv wird ohne --yes nicht gelöscht.",
            captured.StandardError);
        Assert.Contains(
            "Dadurch wird nur das aufbewahrte Upload-Archiv entfernt.",
            captured.StandardError);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Delete_json_uses_archive_provenance_route_with_acknowledgement()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "source": "control-plane",
                  "status": "deleted",
                  "validationId": "20260702-111500Z-upload-03",
                  "archiveState": "removed",
                  "deletedBytes": 1310720,
                  "deletedAtUtc": "2026-07-02T11:18:00Z",
                  "warnings": [],
                  "detail": "The retained uploaded ZIP was removed."
                }
                """));

        var options = new CliOptions(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: true);

        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "uploads", "delete", "20260702-111500Z-upload-03", "--yes", "--json"],
                "uploads",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;

        Assert.Equal("deleted", root.GetProperty("status").GetString());
        Assert.Equal("removed", root.GetProperty("archiveState").GetString());
        Assert.Equal(1310720, root.GetProperty("deletedBytes").GetInt64());

        var request = Assert.Single(handler.Requests);
        Assert.Equal("DELETE", request.Method);
        Assert.Equal(
            "/internal/host-agent/backups/artifacts/validated-imports/20260702-111500Z-upload-03?acknowledgeDelete=true",
            request.PathAndQuery);
    }

    [Fact]
    public async Task Delete_human_output_localises_cli_owned_text_and_preserves_server_detail_when_german_is_selected()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "source": "control-plane",
                  "status": "deleted",
                  "validationId": "20260702-111500Z-upload-de-02",
                  "archiveState": "removed",
                  "deletedBytes": 1310720,
                  "deletedAtUtc": "2026-07-02T11:18:00Z",
                  "warnings": ["Server warning remains verbatim."],
                  "detail": "Server delete detail remains verbatim."
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
                ["backups", "uploads", "delete", "20260702-111500Z-upload-de-02", "--yes"],
                "uploads",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.Contains("Löschen des hochgeladenen MEM-ZIP-Archivs", captured.StandardOutput);
        Assert.Contains("Status: gelöscht", captured.StandardOutput);
        Assert.Contains(
            "Validierungsreferenz: 20260702-111500Z-upload-de-02 (nur Herkunftsnachweis)",
            captured.StandardOutput);
        Assert.Contains("Archivstatus: entfernt", captured.StandardOutput);
        Assert.Contains("Gelöschte Bytes: 1,25 MB", captured.StandardOutput);
        Assert.Contains("Gelöscht (UTC): 2026-07-02 11:18:00 UTC", captured.StandardOutput);
        Assert.Contains("Warnungen", captured.StandardOutput);
        Assert.Contains("Server warning remains verbatim.", captured.StandardOutput);
        Assert.Contains("Details: Server delete detail remains verbatim.", captured.StandardOutput);
        Assert.Contains(
            "Das aufbewahrte hochgeladene ZIP-Archiv wurde entfernt.",
            captured.StandardOutput);
        Assert.Contains(
            "Die materialisierte Sicherungskatalognutzlast und dauerhafte Wiederherstellungsverläufe bleiben getrennt erhalten.",
            captured.StandardOutput);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("DELETE", request.Method);
        Assert.Equal(
            "/internal/host-agent/backups/artifacts/validated-imports/20260702-111500Z-upload-de-02?acknowledgeDelete=true",
            request.PathAndQuery);
    }

    [Fact]
    public async Task Inspect_human_output_localises_cli_owned_text_and_preserves_validation_details_when_german_is_selected()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "source": "control-plane",
                  "status": "ok",
                  "validationId": "20260702-111500Z-upload-de-01",
                  "sourceKind": "uploaded-zip",
                  "uploadedFileName": "portable-export.zip",
                  "recordedAtUtc": "2026-07-02T11:15:00Z",
                  "archiveBytes": 1310720,
                  "archiveState": "retained",
                  "validation": {
                    "status": "valid",
                    "summary": "All required checks passed.",
                    "zipEntryCount": 17,
                    "totalUncompressedBytes": 2621440,
                    "manifestPresent": true,
                    "checksumsPresent": true,
                    "passedChecks": 12,
                    "failedChecks": 1,
                    "warningCount": 1,
                    "errors": ["Server validation error remains verbatim."]
                  },
                  "manifest": {
                    "manifestVersion": 1,
                    "memVersion": "0.1.1",
                    "sourceStackSlug": "imported-stack",
                    "sourceStackDisplayName": "Imported stack",
                    "matrixServerName": "matrix.imported.test",
                    "includedFileCount": 17
                  },
                  "retention": {
                    "canDelete": true,
                    "deleteBlockReason": null,
                    "removedAtUtc": null,
                    "removedBy": null
                  },
                  "warnings": ["Server warning remains verbatim."],
                  "detail": "Uploaded ZIP metadata loaded."
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
                ["backups", "uploads", "inspect", "20260702-111500Z-upload-de-01"],
                "uploads",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.Contains("MEM-Herkunft der hochgeladenen ZIP-Datei", captured.StandardOutput);
        Assert.Contains("Quelltyp: hochgeladene ZIP-Datei", captured.StandardOutput);
        Assert.Contains("Archivstatus: aufbewahrt", captured.StandardOutput);
        Assert.Contains("Archivgröße: 1,25 MB", captured.StandardOutput);
        Assert.Contains("Validierung", captured.StandardOutput);
        Assert.Contains("Zusammenfassung: All required checks passed.", captured.StandardOutput);
        Assert.Contains("Validierungsfehler", captured.StandardOutput);
        Assert.Contains("Server validation error remains verbatim.", captured.StandardOutput);
        Assert.Contains("Aufbewahrung", captured.StandardOutput);
        Assert.Contains("Kann gelöscht werden: ja", captured.StandardOutput);
        Assert.Contains("Warnungen", captured.StandardOutput);
        Assert.Contains("Server warning remains verbatim.", captured.StandardOutput);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            "/internal/host-agent/backups/artifacts/validated-imports/20260702-111500Z-upload-de-01",
            request.PathAndQuery);
    }

}
