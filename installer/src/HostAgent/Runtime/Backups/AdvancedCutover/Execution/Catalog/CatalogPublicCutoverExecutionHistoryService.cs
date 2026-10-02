using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Execution.Catalog;

public sealed class CatalogPublicCutoverExecutionHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;

    public CatalogPublicCutoverExecutionHistoryService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<CatalogPublicCutoverExecutionHistoryResponse> ListExecutionsAsync(
        string catalogEntryId,
        string? confirmationId,
        string? candidateId,
        string? status,
        int? max,
        CancellationToken ct)
    {
        ValidatePathSegment(catalogEntryId, "Backup Catalog entry id");

        var historyRoot = ResolveHistoryRoot();
        if (!Directory.Exists(historyRoot))
        {
            return new CatalogPublicCutoverExecutionHistoryResponse(
                Source: "control-plane",
                Status: "empty",
                HistoryRootPath: historyRoot,
                TotalExecutions: 0,
                Executions: [],
                Warnings: [],
                Detail: "No catalog-backed Public Cutover execution history exists yet.");
        }

        var warnings = new List<string>();
        var executions = new List<CatalogPublicCutoverExecutionSummary>();

        foreach (var directory in Directory.EnumerateDirectories(historyRoot))
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, "catalog-public-cutover-execution.json");
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var result = await ReadAsync(path, ct);
                if (result is null || !string.Equals(result.CatalogEntryId, catalogEntryId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(confirmationId) &&
                    !string.Equals(result.ConfirmationId, confirmationId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(candidateId) &&
                    !string.Equals(result.CandidateId, candidateId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(result.Status, status.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                executions.Add(ToSummary(result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Could not read catalog Public Cutover execution '{path}': {ex.Message}");
            }
        }

        var ordered = executions
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(max is > 0 ? max.Value : 50)
            .ToArray();

        return new CatalogPublicCutoverExecutionHistoryResponse(
            Source: "control-plane",
            Status: warnings.Count == 0 ? "ok" : "warning",
            HistoryRootPath: historyRoot,
            TotalExecutions: executions.Count,
            Executions: ordered,
            Warnings: warnings,
            Detail: ordered.Length == 0 ? "No catalog-backed Public Cutover executions matched the requested filters." : null);
    }

    public async Task<CatalogPublicCutoverExecutionDetailResponse?> GetExecutionAsync(
        string catalogEntryId,
        string executionId,
        CancellationToken ct)
    {
        ValidatePathSegment(catalogEntryId, "Backup Catalog entry id");
        ValidatePathSegment(executionId, "Catalog Public Cutover execution id");

        var root = ResolveHistoryRoot();
        var path = Path.Combine(root, executionId, "catalog-public-cutover-execution.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var result = await ReadAsync(path, ct);
        if (result is null || !string.Equals(result.CatalogEntryId, catalogEntryId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new CatalogPublicCutoverExecutionDetailResponse(
            Source: "control-plane",
            Status: "ok",
            HistoryRootPath: root,
            Execution: result,
            Warnings: [],
            Detail: null);
    }

    public async Task<CatalogPublicCutoverExecutionResult?> FindLatestSuccessfulAsync(
        string catalogEntryId,
        string confirmationId,
        string candidateId,
        CancellationToken ct)
    {
        var history = await ListExecutionsAsync(catalogEntryId, confirmationId, candidateId, null, 100, ct);
        var match = history.Executions.FirstOrDefault(x =>
            string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
            x.AnyRouteMutated &&
            x.PublicVerificationPassed &&
            !x.RollbackAttempted);

        if (match is null)
        {
            return null;
        }

        var detail = await GetExecutionAsync(catalogEntryId, match.ExecutionId, ct);
        return detail?.Execution;
    }

    internal async Task SaveAsync(CatalogPublicCutoverExecutionResult result, CancellationToken ct)
    {
        var directory = Path.Combine(ResolveHistoryRoot(), result.ExecutionId);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "catalog-public-cutover-execution.json"),
            JsonSerializer.Serialize(result, JsonOptions),
            ct);
    }

    private async Task<CatalogPublicCutoverExecutionResult?> ReadAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CatalogPublicCutoverExecutionResult>(stream, JsonOptions, ct);
    }

    private static CatalogPublicCutoverExecutionSummary ToSummary(CatalogPublicCutoverExecutionResult result) =>
        new(
            result.ExecutionId,
            result.CatalogEntryId,
            result.SourceKind,
            result.RestoreSessionId,
            result.ConfirmationId,
            result.PreviewId,
            result.CandidateId,
            result.OldRuntimeStackSlug,
            result.Status,
            result.StartedAtUtc,
            result.FinishedAtUtc,
            result.Operator,
            result.ExecutionRequested,
            result.FinalBackup.Captured,
            result.OldRuntime.RetainedForRollback,
            result.Routes.AnyRouteMutated,
            result.PublicVerification.Passed,
            result.Rollback.Attempted,
            result.Rollback.Completed,
            result.Blockers.Count,
            result.Warnings.Count,
            result.Errors.Count,
            result.Detail);

    private string ResolveHistoryRoot() => Path.Combine(ResolveDataRoot(), "production-restore", "catalog-cutover-executions");

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static void ValidatePathSegment(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('/') || value.Contains('\\') || value.Contains(':') || value.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label} contains unsafe characters.");
        }
    }
}
