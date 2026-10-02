using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

public sealed class PrivateStagingHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;

    public PrivateStagingHistoryService(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<PrivateStagingHistoryResponse> ListRunsAsync(
        string? validationId,
        string? status,
        int? max,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        var historyRoot = ResolveHistoryRoot();

        if (!Directory.Exists(historyRoot))
        {
            return new PrivateStagingHistoryResponse(
                Source: "control-plane",
                Status: "empty",
                HistoryRootPath: historyRoot,
                TotalRuns: 0,
                Runs: [],
                Warnings: [],
                Detail: "No Restore Staging history exists yet.");
        }

        var runs = new List<PrivateStagingRunSummary>();

        foreach (var runDirectory in Directory.EnumerateDirectories(historyRoot))
        {
            ct.ThrowIfCancellationRequested();

            var resultPath = Path.Combine(
                runDirectory,
                "restore-staging-result.json");

            if (!File.Exists(resultPath))
            {
                continue;
            }

            try
            {
                var result = await ReadResultFileAsync(
                    resultPath,
                    ct);

                if (result is null)
                {
                    warnings.Add($"Could not parse Restore Staging result: {resultPath}");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(validationId) &&
                    !string.Equals(
                        result.ValidationId,
                        validationId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(
                        result.Status,
                        status.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                runs.Add(ToSummary(result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Could not read Restore Staging result '{resultPath}': {ex.Message}");
            }
        }

        var ordered = runs
            .OrderByDescending(run => run.StartedAtUtc)
            .Take(max is > 0 ? max.Value : 50)
            .ToList();

        return new PrivateStagingHistoryResponse(
            Source: "control-plane",
            Status: warnings.Count == 0 ? "ok" : "warning",
            HistoryRootPath: historyRoot,
            TotalRuns: runs.Count,
            Runs: ordered,
            Warnings: warnings,
            Detail: ordered.Count == 0
                ? "No Restore Staging runs matched the requested filters."
                : null);
    }

    public async Task<PrivateStagingSafeHistoryResponse> ListSafeRunsAsync(
    string? validationId,
    string? status,
    int? max,
    CancellationToken ct)
    {
        var historyRoot = ResolveHistoryRoot();

        if (!Directory.Exists(historyRoot))
        {
            return new PrivateStagingSafeHistoryResponse(
                Status: "empty",
                TotalRuns: 0,
                Runs: [],
                Detail: "No private restore tests exist yet.");
        }

        var runs = new List<PrivateStagingSafeRunSummary>();

        foreach (var runDirectory in Directory.EnumerateDirectories(historyRoot))
        {
            ct.ThrowIfCancellationRequested();

            var resultPath = Path.Combine(
                runDirectory,
                "restore-staging-result.json");

            if (!File.Exists(resultPath))
            {
                continue;
            }

            try
            {
                var result = await ReadResultFileAsync(resultPath, ct);

                if (result is null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(validationId) &&
                    !string.Equals(
                        result.ValidationId,
                        validationId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(
                        result.Status,
                        status.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                runs.Add(ToSafeSummary(result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Do not leak filesystem paths or host-level parsing failures into
                // the browser-safe operator projection.
            }
        }

        var ordered = runs
            .OrderByDescending(run => run.StartedAtUtc)
            .Take(max is > 0 ? max.Value : 50)
            .ToList();

        return new PrivateStagingSafeHistoryResponse(
            Status: "ok",
            TotalRuns: runs.Count,
            Runs: ordered,
            Detail: ordered.Count == 0
                ? "No private restore tests matched the requested filters."
                : null);
    }

    public async Task<PrivateStagingSafeRunDetailResponse?> GetSafeRunAsync(
        string stagingId,
        CancellationToken ct)
    {
        var result = await LoadResultAsync(stagingId, ct);

        if (result is null)
        {
            return null;
        }

        return new PrivateStagingSafeRunDetailResponse(
            Status: "ok",
            Run: ToSafeSummary(result),
            Detail: null);
    }

    public async Task<PrivateStagingRunDetailResponse?> GetRunAsync(
        string stagingId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stagingId))
        {
            throw new InvalidOperationException("Staging id is required.");
        }

        if (!IsSafePathSegment(stagingId))
        {
            throw new InvalidOperationException("Staging id contains unsafe characters.");
        }

        var historyRoot = ResolveHistoryRoot();

        var resultPath = Path.Combine(
            historyRoot,
            stagingId,
            "restore-staging-result.json");

        if (!File.Exists(resultPath))
        {
            return null;
        }

        var result = await ReadResultFileAsync(
            resultPath,
            ct);

        return new PrivateStagingRunDetailResponse(
            Source: "control-plane",
            Status: result is null ? "error" : "ok",
            HistoryRootPath: historyRoot,
            Run: result,
            Warnings: result is null
                ? [$"Could not parse Restore Staging result '{resultPath}'."]
                : [],
            Detail: result is null
                ? "Restore Staging run file exists but could not be parsed."
                : null);
    }

    private static PrivateStagingSafeRunSummary ToSafeSummary(
    PrivateStagingRunResult result)
    {
        var destroyed =
            PrivateStagingService.IsDestroyComplete(result);

        var destroyAvailable =
            !destroyed &&
            result.Safety.RequiresExplicitDestroy;

        return new PrivateStagingSafeRunSummary(
            StagingId: result.StagingId,
            ValidationId: result.ValidationId,
            Status: result.Status,
            StartedAtUtc: result.StartedAtUtc,
            FinishedAtUtc: result.FinishedAtUtc,
            DestroyedAtUtc: destroyed ? result.Destroy?.DestroyedAtUtc : null,
            PrivateOnly: result.Safety.PrivateOnly,
            PublicRoutesCreated: result.Safety.PublicRoutesCreated,
            ProductionContainersTouched: result.Safety.ProductionContainersTouched,
            ProductionDatabasesTouched: result.Safety.ProductionDatabasesTouched,
            DatabaseImportSucceeded: result.Database.ImportSucceeded,
            SynapseHealthPassed: result.Runtime.SynapseHealthPassed,
            Destroyed: destroyed,
            DestroyAvailable: destroyAvailable,
            SafeSummary: BuildSafeSummary(result, destroyed),
            SourceKind: result.SourceKind,
            CatalogEntryId: result.CatalogEntryId,
            MatrixServerName: result.MatrixServerName);
    }

    private static string BuildSafeSummary(
        PrivateStagingRunResult result,
        bool destroyed)
    {
        if (destroyed)
        {
            return "This private restore test has been retired.";
        }

        if (string.Equals(
                result.Status,
                "destroy-needs-attention",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Private restore test retirement needs attention. One or more disposable staging resources remain; retry retirement after reviewing Diagnostics.";
        }

        if (string.Equals(result.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
            result.Database.ImportSucceeded &&
            result.Runtime.SynapseHealthPassed &&
            result.Safety.PrivateOnly &&
            !result.Safety.PublicRoutesCreated &&
            !result.Safety.ProductionContainersTouched &&
            !result.Safety.ProductionDatabasesTouched)
        {
            return "Private restore test is ready. The database was restored and Synapse passed its health check. No public routes or production resources were changed.";
        }

        if (string.Equals(result.Status, "running", StringComparison.OrdinalIgnoreCase))
        {
            return "Private restore test is still running.";
        }

        if (string.Equals(result.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return "Private restore test did not complete. Use the operator diagnostics area for safe next steps.";
        }

        return "Private restore test status is available.";
    }

    internal async Task<PrivateStagingRunResult?> LoadResultAsync(
        string stagingId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stagingId))
        {
            throw new InvalidOperationException("Staging id is required.");
        }

        if (!IsSafePathSegment(stagingId))
        {
            throw new InvalidOperationException("Staging id contains unsafe characters.");
        }

        var resultPath = Path.Combine(
            ResolveHistoryRoot(),
            stagingId,
            "restore-staging-result.json");

        return File.Exists(resultPath)
            ? await ReadResultFileAsync(resultPath, ct)
            : null;
    }

    internal async Task SaveResultAsync(
        PrivateStagingRunResult result,
        CancellationToken ct)
    {
        var runDirectory = Path.Combine(
            ResolveHistoryRoot(),
            result.StagingId);

        Directory.CreateDirectory(runDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(runDirectory, "restore-staging-result.json"),
            JsonSerializer.Serialize(result, JsonOptions),
            ct);
    }

    private async Task<PrivateStagingRunResult?> ReadResultFileAsync(
        string resultPath,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(resultPath);

        return await JsonSerializer.DeserializeAsync<PrivateStagingRunResult>(
            stream,
            JsonOptions,
            ct);
    }

    private PrivateStagingRunSummary ToSummary(
        PrivateStagingRunResult result)
    {
        return new PrivateStagingRunSummary(
            StagingId: result.StagingId,
            ValidationId: result.ValidationId,
            Mode: result.Mode,
            Status: result.Status,
            StartedAtUtc: result.StartedAtUtc,
            FinishedAtUtc: result.FinishedAtUtc,
            DurationSeconds: result.FinishedAtUtc is null
                ? null
                : (result.FinishedAtUtc.Value - result.StartedAtUtc).TotalSeconds,
            UploadedZipName: Path.GetFileName(result.UploadedZipPath),
            UploadedZipPath: result.UploadedZipPath,
            TargetStackSlug: result.TargetStackSlug,
            MatrixServerName: result.MatrixServerName,
            PostgresContainerName: result.PostgresContainerName,
            SynapseContainerName: result.SynapseContainerName,
            NetworkName: result.NetworkName,
            ImportSucceeded: result.Database.ImportSucceeded,
            SynapseHealthPassed: result.Runtime.SynapseHealthPassed,
            PublicTableCount: result.Database.PublicTableCount,
            SynapseKnownTableCount: result.Database.SynapseKnownTableCount,
            UsersCount: result.Database.UsersCount,
            EventsCount: result.Database.EventsCount,
            RoomsCount: result.Database.RoomsCount,
            Destroyed: result.Destroy is not null ||
                       string.Equals(
                           result.Status,
                           "destroyed",
                           StringComparison.OrdinalIgnoreCase),
            WarningCount: result.Warnings.Count,
            ErrorCount: result.Errors.Count,
            Detail: result.Detail,
            SourceKind: result.SourceKind,
            CatalogEntryId: result.CatalogEntryId);
    }

    private string ResolveHistoryRoot()
    {
        return Path.Combine(
            ResolveDataRoot(),
            "restore-staging",
            "history");
    }

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static bool IsSafePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Contains('/', StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal) ||
            value.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return value
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment != "." && segment != "..");
    }
}