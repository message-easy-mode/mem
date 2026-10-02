using System.Text.Json;
using Mem.Cli.Clients;
using Mem.Cli.Commands;
using Mem.Cli.Config;
using Mem.Cli.Tests.TestSupport;
using Mem.Localization;

namespace Mem.Cli.Tests.Commands;

/// <summary>
/// Guards the boundary between localisation and the CLI's stable operator and
/// machine contracts. Language selection may change only human presentation;
/// it must not change HostAgent requests, JSON projections, result status, or
/// process exit codes.
/// </summary>
[Collection("Console output")]
public sealed class BackupLanguageContractInvarianceTests
{
    private const string CatalogEntryId = "bkp_catalog_language_contract_01";
    private const string TestDeviceCredential =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task List_human_output_changes_presentation_only_between_english_and_german()
    {
        var english = await RunListAsync(
            MemLanguage.English,
            json: false);

        var german = await RunListAsync(
            MemLanguage.German,
            json: false);

        Assert.Equal(0, english.Output.ExitCode);
        Assert.Equal(english.Output.ExitCode, german.Output.ExitCode);
        Assert.Empty(english.Output.StandardError);
        Assert.Empty(german.Output.StandardError);
        AssertRequestsEqual(english.Requests, german.Requests);

        var request = Assert.Single(english.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/internal/host-agent/backups/catalog", request.PathAndQuery);
        Assert.False(request.HasRetiredSharedSecretHeader);
        Assert.Null(request.ContentType);
        Assert.Null(request.ContentBody);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(TestDeviceCredential, request.AuthorizationParameter);

        Assert.Contains("MEM Backup Catalog", english.Output.StandardOutput);
        Assert.Contains("MEM-Sicherungskatalog", german.Output.StandardOutput);
        Assert.NotEqual(english.Output.StandardOutput, german.Output.StandardOutput);

        Assert.Contains(CatalogEntryId, english.Output.StandardOutput);
        Assert.Contains(CatalogEntryId, german.Output.StandardOutput);
        Assert.Contains("Server display", english.Output.StandardOutput);
        Assert.Contains("Server display", german.Output.StandardOutput);
    }

    [Fact]
    public async Task List_json_projection_is_byte_for_byte_invariant_between_english_and_german()
    {
        var english = await RunListAsync(
            MemLanguage.English,
            json: true);

        var german = await RunListAsync(
            MemLanguage.German,
            json: true);

        Assert.Equal(0, english.Output.ExitCode);
        Assert.Equal(english.Output.ExitCode, german.Output.ExitCode);
        Assert.Empty(english.Output.StandardError);
        Assert.Empty(german.Output.StandardError);
        AssertRequestsEqual(english.Requests, german.Requests);
        Assert.Equal(english.Output.StandardOutput, german.Output.StandardOutput);
        Assert.DoesNotContain("MEM Backup Catalog", english.Output.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("MEM-Sicherungskatalog", english.Output.StandardOutput, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(english.Output.StandardOutput);
        var root = document.RootElement;

        Assert.Equal("control-plane", root.GetProperty("source").GetString());
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal(CatalogEntryId, root.GetProperty("entries")[0].GetProperty("catalogEntryId").GetString());
    }

    [Fact]
    public async Task Permanent_delete_human_output_preserves_preflight_and_delete_contract_between_english_and_german()
    {
        var english = await RunPermanentDeleteAsync(
            MemLanguage.English,
            json: false,
            canDelete: true);

        var german = await RunPermanentDeleteAsync(
            MemLanguage.German,
            json: false,
            canDelete: true);

        Assert.Equal(0, english.Output.ExitCode);
        Assert.Equal(english.Output.ExitCode, german.Output.ExitCode);
        Assert.Empty(english.Output.StandardError);
        Assert.Empty(german.Output.StandardError);
        AssertRequestsEqual(english.Requests, german.Requests);

        Assert.Equal(2, english.Requests.Count);
        Assert.Equal("GET", english.Requests[0].Method);
        Assert.Equal(
            $"/internal/host-agent/backups/catalog/{CatalogEntryId}/lifecycle",
            english.Requests[0].PathAndQuery);
        Assert.Equal("DELETE", english.Requests[1].Method);
        Assert.Equal(
            $"/internal/host-agent/backups/catalog/{CatalogEntryId}",
            english.Requests[1].PathAndQuery);
        Assert.Equal("application/json", english.Requests[1].ContentType);

        using var deleteRequestBody = JsonDocument.Parse(
            Assert.IsType<string>(english.Requests[1].ContentBody));

        Assert.Equal(
            "mem-cli",
            deleteRequestBody.RootElement.GetProperty("operator").GetString());

        Assert.Contains("MEM Backup Catalog Permanent Delete", english.Output.StandardOutput);
        Assert.Contains("Dauerhaftes Löschen des MEM-Sicherungskatalogs", german.Output.StandardOutput);
        Assert.NotEqual(english.Output.StandardOutput, german.Output.StandardOutput);
        Assert.Contains("Server deletion detail remains verbatim.", english.Output.StandardOutput);
        Assert.Contains("Server deletion detail remains verbatim.", german.Output.StandardOutput);
    }

    [Fact]
    public async Task Blocked_permanent_delete_json_projection_preserves_status_exit_code_and_http_contract_between_english_and_german()
    {
        var english = await RunPermanentDeleteAsync(
            MemLanguage.English,
            json: true,
            canDelete: false);

        var german = await RunPermanentDeleteAsync(
            MemLanguage.German,
            json: true,
            canDelete: false);

        Assert.Equal(2, english.Output.ExitCode);
        Assert.Equal(english.Output.ExitCode, german.Output.ExitCode);
        Assert.Empty(english.Output.StandardError);
        Assert.Empty(german.Output.StandardError);
        AssertRequestsEqual(english.Requests, german.Requests);
        Assert.Equal(english.Output.StandardOutput, german.Output.StandardOutput);

        var request = Assert.Single(english.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            $"/internal/host-agent/backups/catalog/{CatalogEntryId}/lifecycle",
            request.PathAndQuery);

        using var document = JsonDocument.Parse(english.Output.StandardOutput);
        var root = document.RootElement;

        Assert.Equal("blocked", root.GetProperty("status").GetString());
        Assert.Equal(CatalogEntryId, root.GetProperty("catalogEntryId").GetString());
        Assert.Equal("restore-session-language-contract-01", root.GetProperty("activeRestoreSessionId").GetString());
        Assert.Equal(
            "An active restore workspace blocks permanent deletion.",
            root.GetProperty("detail").GetString());
    }

    private static async Task<CommandInvocation> RunListAsync(
        MemLanguage language,
        bool json)
    {
        var args = new List<string>
        {
            "backups",
            "list",
            "--language",
            language == MemLanguage.German ? "de" : "en"
        };

        if (json)
        {
            args.Add("--json");
        }

        var options = CreateOptions(args);
        using var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(CatalogListResponse));
        using var client = new HostAgentClient(options, handler);

        var output = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                args.ToArray(),
                "list",
                options,
                client));

        return new CommandInvocation(
            output,
            handler.Requests.ToArray());
    }

    private static async Task<CommandInvocation> RunPermanentDeleteAsync(
        MemLanguage language,
        bool json,
        bool canDelete)
    {
        var args = new List<string>
        {
            "backups",
            "delete",
            CatalogEntryId,
            "--yes",
            "--language",
            language == MemLanguage.German ? "de" : "en"
        };

        if (json)
        {
            args.Add("--json");
        }

        var options = CreateOptions(args);
        using var handler = canDelete
            ? CreateAllowedPermanentDeleteHandler()
            : CreateBlockedPermanentDeleteHandler();
        using var client = new HostAgentClient(options, handler);

        var output = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                args.ToArray(),
                "delete",
                options,
                client));

        return new CommandInvocation(
            output,
            handler.Requests.ToArray());
    }

    private static void AssertRequestsEqual(
        IReadOnlyList<RecordedHttpRequest> expected,
        IReadOnlyList<RecordedHttpRequest> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index], actual[index]);
        }
    }

    private static CliOptions CreateOptions(
        IReadOnlyList<string> commandArgs)
    {
        var args = new List<string>
        {
            "--server",
            "http://mem.test"
        };

        args.AddRange(commandArgs);

        return CliOptions.FromArgs(args.ToArray()) with
        {
            ProfileName = "language-contract-profile",
            DeviceCredential = TestDeviceCredential
        };
    }

    private static RecordingHttpMessageHandler CreateAllowedPermanentDeleteHandler()
    {
        return new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                $$"""
                {
                  "catalogEntryId": "{{CatalogEntryId}}",
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
                $$"""
                {
                  "source": "control-plane",
                  "status": "deleted",
                  "catalogEntryId": "{{CatalogEntryId}}",
                  "originKind": "imported-zip",
                  "deletedBy": "mem-cli",
                  "payloadDeleted": true,
                  "originalArchiveDeleted": true,
                  "portableExportsDeleted": 1,
                  "detachedRestoreAttempts": 2,
                  "detail": "Server deletion detail remains verbatim."
                }
                """));
    }

    private static RecordingHttpMessageHandler CreateBlockedPermanentDeleteHandler()
    {
        return new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                $$"""
                {
                  "catalogEntryId": "{{CatalogEntryId}}",
                  "payloadState": "available",
                  "payloadPresent": true,
                  "hasActiveRestore": true,
                  "activeRestoreSessionId": "restore-session-language-contract-01",
                  "canDelete": false,
                  "deleteBlockReason": "An active restore workspace blocks permanent deletion.",
                  "originalArchive": null
                }
                """));
    }

    private static readonly string CatalogListResponse = $$"""
        {
          "totalCount": 1,
          "entries": [
            {
              "catalogEntryId": "{{CatalogEntryId}}",
              "originKind": "local-captured",
              "displayName": "Server display",
              "sourceStackSlug": "language-contract-stack",
              "sourceBackupId": "20260703-060000Z",
              "capturedAtUtc": "2026-07-03T06:00:00Z",
              "payloadState": "available",
              "integrityStatus": "valid",
              "warningCount": 0,
              "payloadBytes": 1310720,
              "createdAtUtc": "2026-07-03T06:00:03Z",
              "importedAtUtc": null,
              "materialisedAtUtc": null,
              "payloadRemovedAtUtc": null,
              "advisoryCount": 0
            }
          ]
        }
        """;

    private sealed record CommandInvocation(
        CapturedConsoleOutput Output,
        IReadOnlyList<RecordedHttpRequest> Requests);
}
