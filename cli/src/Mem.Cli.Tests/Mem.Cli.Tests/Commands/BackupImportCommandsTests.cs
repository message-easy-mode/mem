using System.Text.Json;
using Mem.Cli.Clients;
using Mem.Cli.Commands;
using Mem.Cli.Config;
using Mem.Cli.Tests.TestSupport;
using Mem.Localization;

namespace Mem.Cli.Tests.Commands;

[Collection("Console output")]
public sealed class BackupImportCommandsTests
{
    [Fact]
    public async Task Import_json_emits_catalog_identity_without_host_storage_path()
    {
        var zipPath = CreateTemporaryZipPath();

        try
        {
            var handler = new RecordingHttpMessageHandler(
                RecordingHttpMessageHandler.Json(
                    """
                    {
                      "source": "control-plane",
                      "status": "valid",
                      "validationId": "20260702-110000Z-import-01",
                      "uploadedFileName": "portable-export.zip",
                      "storedZipPath": "/host-only/imports/portable-export.zip",
                      "zipBytes": 1310720,
                      "zipEntryCount": 17,
                      "totalUncompressedBytes": 2621440,
                      "manifestPresent": true,
                      "checksumsPresent": true,
                      "manifest": {
                        "manifestVersion": 1,
                        "exportKind": "mem-stack-export",
                        "createdAtUtc": "2026-07-02T11:00:00Z",
                        "createdBy": "MEM",
                        "memVersion": "0.1.1",
                        "stack": {
                          "slug": "imported-stack",
                          "displayName": "Imported stack",
                          "matrixServerName": "matrix.imported.test",
                          "matrixPublicUrl": "https://matrix.imported.test",
                          "elementPublicUrl": "https://chat.imported.test"
                        },
                        "database": {
                          "engine": "postgres",
                          "dumpFile": "database/synapse.sql",
                          "databaseName": "hidden",
                          "username": "hidden",
                          "present": true
                        },
                        "matrix": {
                          "homeserverConfig": "matrix/homeserver.yaml",
                          "signingKey": "matrix/signing.key",
                          "mediaStore": "matrix/media_store",
                          "mediaBytes": 300,
                          "mediaFiles": 2,
                          "present": true
                        },
                        "element": {
                          "config": "element/config.json",
                          "present": true
                        }
                      },
                      "integrity": {
                        "checksumLines": 11,
                        "checkedFiles": 11,
                        "missingFiles": 0,
                        "failedFiles": 0,
                        "passedFiles": 11
                      },
                      "checks": [
                        {
                          "code": "checksums.verify",
                          "severity": "info",
                          "passed": true,
                          "message": "Checksums verified.",
                          "detail": null
                        }
                      ],
                      "warnings": [],
                      "errors": [],
                      "detail": "The ZIP was materialised into the Backup Catalog.",
                      "catalogEntryId": "bkp_20260702-110000Z-import-01",
                      "catalogPayloadState": "available",
                      "catalogMaterialisationAction": "created"
                    }
                    """));

            var options = new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: "mem_cli_test_token",
                Json: true);

            using var client = new HostAgentClient(options, handler);

            var captured = await ConsoleOutputCapture.CaptureAsync(() =>
                BackupCommands.RunAsync(
                    ["backups", "import", zipPath, "--json"],
                    "import",
                    options,
                    client));

            Assert.Equal(0, captured.ExitCode);
            Assert.Empty(captured.StandardError);
            Assert.DoesNotContain("storedZipPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/host-only", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("databaseName", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"hidden\"", captured.StandardOutput, StringComparison.Ordinal);

            using var document = JsonDocument.Parse(captured.StandardOutput);
            var root = document.RootElement;

            Assert.Equal("valid", root.GetProperty("status").GetString());
            Assert.Equal(
                "bkp_20260702-110000Z-import-01",
                root.GetProperty("catalogEntryId").GetString());
            Assert.Equal(
                "20260702-110000Z-import-01",
                root.GetProperty("validationId").GetString());
            Assert.Equal(
                "available",
                root.GetProperty("catalogPayloadState").GetString());
            Assert.Equal(
                "imported-stack",
                root.GetProperty("manifest").GetProperty("stack").GetProperty("slug").GetString());

            var request = Assert.Single(handler.Requests);
            Assert.Equal("POST", request.Method);
            Assert.Equal("/internal/host-agent/backups/artifacts/validated-imports", request.PathAndQuery);
            Assert.False(request.HasRetiredSharedSecretHeader);
            Assert.Equal("multipart/form-data", request.ContentType);
        }
        finally
        {
            DeleteTemporaryFile(zipPath);
        }
    }

    [Fact]
    public async Task Import_human_output_directs_operator_to_catalog_not_validation_restore()
    {
        var zipPath = CreateTemporaryZipPath();

        try
        {
            var handler = new RecordingHttpMessageHandler(
                RecordingHttpMessageHandler.Json(
                    """
                    {
                      "source": "control-plane",
                      "status": "valid",
                      "validationId": "20260702-110000Z-import-02",
                      "uploadedFileName": "portable-export.zip",
                      "storedZipPath": "/host-only/imports/portable-export.zip",
                      "zipBytes": 512,
                      "zipEntryCount": 2,
                      "totalUncompressedBytes": 1024,
                      "manifestPresent": false,
                      "checksumsPresent": false,
                      "manifest": null,
                      "integrity": {
                        "checksumLines": 0,
                        "checkedFiles": 0,
                        "missingFiles": 0,
                        "failedFiles": 0,
                        "passedFiles": 0
                      },
                      "checks": [],
                      "warnings": [],
                      "errors": [],
                      "detail": "The ZIP was materialised into the Backup Catalog.",
                      "catalogEntryId": "bkp_20260702-110000Z-import-02",
                      "catalogPayloadState": "available",
                      "catalogMaterialisationAction": "created"
                    }
                    """));

            var options = new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: "mem_cli_test_token",
                Json: false);

            using var client = new HostAgentClient(options, handler);

            var captured = await ConsoleOutputCapture.CaptureAsync(() =>
                BackupCommands.RunAsync(
                    ["backups", "import", zipPath],
                    "import",
                    options,
                    client));

            Assert.Equal(0, captured.ExitCode);
            Assert.Empty(captured.StandardError);
            Assert.Contains("MEM Backup Catalog Import", captured.StandardOutput);
            Assert.Contains("Catalog entry ID: bkp_20260702-110000Z-import-02", captured.StandardOutput);
            Assert.Contains(
                "Inspect this backup: mem backups inspect bkp_20260702-110000Z-import-02",
                captured.StandardOutput);
            Assert.Contains(
                "Inspect retained ZIP provenance: mem backups uploads inspect 20260702-110000Z-import-02",
                captured.StandardOutput);
            Assert.Contains(
                "Start restore work only from the Backup Catalog entry.",
                captured.StandardOutput);
            Assert.DoesNotContain("Stored ZIP path", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/host-only", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("restore-plan", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryFile(zipPath);
        }
    }

    [Fact]
    public async Task Import_valid_response_without_catalog_identity_is_not_reported_as_ready()
    {
        var zipPath = CreateTemporaryZipPath();

        try
        {
            var handler = new RecordingHttpMessageHandler(
                RecordingHttpMessageHandler.Json(
                    """
                    {
                      "source": "control-plane",
                      "status": "valid",
                      "validationId": "20260702-110000Z-import-03",
                      "uploadedFileName": "portable-export.zip",
                      "zipBytes": 512,
                      "zipEntryCount": 2,
                      "totalUncompressedBytes": 1024,
                      "manifestPresent": false,
                      "checksumsPresent": false,
                      "manifest": null,
                      "integrity": {
                        "checksumLines": 0,
                        "checkedFiles": 0,
                        "missingFiles": 0,
                        "failedFiles": 0,
                        "passedFiles": 0
                      },
                      "checks": [],
                      "warnings": [],
                      "errors": [],
                      "detail": "Validation passed but catalog materialisation did not return an identity.",
                      "catalogEntryId": null,
                      "catalogPayloadState": null,
                      "catalogMaterialisationAction": null
                    }
                    """));

            var options = new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: "mem_cli_test_token",
                Json: true);

            using var client = new HostAgentClient(options, handler);

            var captured = await ConsoleOutputCapture.CaptureAsync(() =>
                BackupCommands.RunAsync(
                    ["backups", "import", zipPath, "--json"],
                    "import",
                    options,
                    client));

            Assert.Equal(2, captured.ExitCode);

            using var document = JsonDocument.Parse(captured.StandardOutput);
            Assert.Equal("valid", document.RootElement.GetProperty("status").GetString());
            Assert.False(document.RootElement.TryGetProperty("catalogEntryId", out _));
        }
        finally
        {
            DeleteTemporaryFile(zipPath);
        }
    }

    [Fact]
    public async Task Import_human_output_localises_cli_owned_text_and_preserves_server_evidence_when_german_is_selected()
    {
        var zipPath = CreateTemporaryZipPath();

        try
        {
            var handler = new RecordingHttpMessageHandler(
                RecordingHttpMessageHandler.Json(
                    """
                    {
                      "source": "control-plane",
                      "status": "valid",
                      "validationId": "20260702-110000Z-import-de-01",
                      "uploadedFileName": "portable-export.zip",
                      "zipBytes": 1310720,
                      "zipEntryCount": 17,
                      "totalUncompressedBytes": 2621440,
                      "manifestPresent": false,
                      "checksumsPresent": true,
                      "manifest": null,
                      "integrity": {
                        "checksumLines": 11,
                        "checkedFiles": 11,
                        "missingFiles": 0,
                        "failedFiles": 0,
                        "passedFiles": 11
                      },
                      "checks": [
                        {
                          "code": "checksums.verify",
                          "severity": "info",
                          "passed": true,
                          "message": "Checksums verified by the server.",
                          "detail": "Server check detail remains verbatim."
                        }
                      ],
                      "warnings": ["Server warning remains verbatim."],
                      "errors": [],
                      "detail": "The ZIP was materialised into the Backup Catalog.",
                      "catalogEntryId": "bkp_20260702-110000Z-import-de-01",
                      "catalogPayloadState": "available",
                      "catalogMaterialisationAction": "created"
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
                    ["backups", "import", zipPath],
                    "import",
                    options,
                    client));

            Assert.Equal(0, captured.ExitCode);
            Assert.Empty(captured.StandardError);
            Assert.Contains("MEM-Sicherungskatalogimport", captured.StandardOutput);
            Assert.Contains("Status: gültig", captured.StandardOutput);
            Assert.Contains(
                "Validierungsreferenz: 20260702-110000Z-import-de-01 (nur Herkunftsnachweis)",
                captured.StandardOutput);
            Assert.Contains("Katalogeintrags-ID: bkp_20260702-110000Z-import-de-01", captured.StandardOutput);
            Assert.Contains("Nutzlaststatus: verfügbar", captured.StandardOutput);
            Assert.Contains("Materialisierungsaktion: erstellt", captured.StandardOutput);
            Assert.Contains("ZIP-Größe: 1,25 MB", captured.StandardOutput);
            Assert.Contains("Integrität", captured.StandardOutput);
            Assert.Contains("Prüfungen", captured.StandardOutput);
            Assert.Contains("[BESTANDEN] checksums.verify: Checksums verified by the server.", captured.StandardOutput);
            Assert.Contains("Server check detail remains verbatim.", captured.StandardOutput);
            Assert.Contains("Warnungen", captured.StandardOutput);
            Assert.Contains("Server warning remains verbatim.", captured.StandardOutput);
            Assert.Contains("Nächste Schritte (katalogorientiert)", captured.StandardOutput);
            Assert.Contains(
                "Diese Sicherung prüfen: mem backups inspect bkp_20260702-110000Z-import-de-01",
                captured.StandardOutput);
            Assert.Contains(
                "Aufbewahrte ZIP-Herkunft prüfen: mem backups uploads inspect 20260702-110000Z-import-de-01",
                captured.StandardOutput);
            Assert.Contains(
                "Wiederherstellungsarbeiten nur über den Sicherungskatalogeintrag beginnen.",
                captured.StandardOutput);
            Assert.Contains(
                "Details: The ZIP was materialised into the Backup Catalog.",
                captured.StandardOutput);
            Assert.DoesNotContain("/host-only", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);

            var request = Assert.Single(handler.Requests);
            Assert.Equal("POST", request.Method);
            Assert.Equal("/internal/host-agent/backups/artifacts/validated-imports", request.PathAndQuery);
        }
        finally
        {
            DeleteTemporaryFile(zipPath);
        }
    }

    [Fact]
    public async Task Legacy_validate_command_is_rejected_without_calling_control_plane()
    {
        var handler = new RecordingHttpMessageHandler();

        var options = new CliOptions(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: false);

        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", "validate", "portable-export.zip"],
                "validate",
                options,
                client));

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains("Unknown backups command.", captured.StandardError);
        Assert.Empty(handler.Requests);
    }

    private static string CreateTemporaryZipPath()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"mem-cli-import-{Guid.NewGuid():N}.zip");

        File.WriteAllBytes(
            path,
            [0x50, 0x4B, 0x03, 0x04]);

        return path;
    }

    private static void DeleteTemporaryFile(
        string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
