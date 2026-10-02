using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Preview;

public sealed class RuntimeStackBackupPublicCutoverPreviewHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;

    public RuntimeStackBackupPublicCutoverPreviewHistoryService(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<RuntimeStackBackupPublicCutoverPreviewHistoryResponse> ListPreviewsAsync(
        string? validationId,
        string? candidateId,
        string? status,
        int? max,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        var historyRoot = ResolveHistoryRoot();

        if (!Directory.Exists(historyRoot))
        {
            return new RuntimeStackBackupPublicCutoverPreviewHistoryResponse(
                Source: "control-plane",
                Status: "empty",
                HistoryRootPath: historyRoot,
                TotalPreviews: 0,
                Previews: [],
                Warnings: [],
                Detail: "No Public Cutover preview history exists yet.");
        }

        var previews = new List<RuntimeStackBackupPublicCutoverPreviewSummary>();

        foreach (var previewDirectory in Directory.EnumerateDirectories(historyRoot))
        {
            ct.ThrowIfCancellationRequested();

            var resultPath = Path.Combine(
                previewDirectory,
                "public-cutover-preview.json");

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
                    warnings.Add($"Could not parse Public Cutover preview: {resultPath}");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(validationId) &&
                    !string.Equals(result.ValidationId, validationId.Trim(), StringComparison.OrdinalIgnoreCase))
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

                previews.Add(ToSummary(result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Could not read Public Cutover preview '{resultPath}': {ex.Message}");
            }
        }

        var ordered = previews
            .OrderByDescending(preview => preview.CreatedAtUtc)
            .Take(max is > 0 ? max.Value : 50)
            .ToList();

        return new RuntimeStackBackupPublicCutoverPreviewHistoryResponse(
            Source: "control-plane",
            Status: warnings.Count == 0 ? "ok" : "warning",
            HistoryRootPath: historyRoot,
            TotalPreviews: previews.Count,
            Previews: ordered,
            Warnings: warnings,
            Detail: ordered.Count == 0
                ? "No Public Cutover previews matched the requested filters."
                : null);
    }

    public async Task<RuntimeStackBackupPublicCutoverPreviewDetailResponse?> GetPreviewAsync(
        string previewId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(previewId))
        {
            throw new InvalidOperationException("Public Cutover preview id is required.");
        }

        if (!IsSafePathSegment(previewId))
        {
            throw new InvalidOperationException("Public Cutover preview id contains unsafe characters.");
        }

        var historyRoot = ResolveHistoryRoot();

        var resultPath = Path.Combine(
            historyRoot,
            previewId,
            "public-cutover-preview.json");

        if (!File.Exists(resultPath))
        {
            return null;
        }

        var result = await ReadResultFileAsync(
            resultPath,
            ct);

        return new RuntimeStackBackupPublicCutoverPreviewDetailResponse(
            Source: "control-plane",
            Status: result is null ? "error" : "ok",
            HistoryRootPath: historyRoot,
            Preview: result,
            Warnings: result is null ? [$"Could not parse Public Cutover preview '{resultPath}'."] : [],
            Detail: result is null
                ? "Public Cutover preview file exists but could not be parsed."
                : null);
    }

    internal async Task SavePreviewAsync(
        RuntimeStackBackupPublicCutoverPreviewResult result,
        CancellationToken ct)
    {
        var previewDirectory = Path.Combine(
            ResolveHistoryRoot(),
            result.PreviewId);

        Directory.CreateDirectory(previewDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(previewDirectory, "public-cutover-preview.json"),
            JsonSerializer.Serialize(result, JsonOptions),
            ct);
    }

    private async Task<RuntimeStackBackupPublicCutoverPreviewResult?> ReadResultFileAsync(
        string resultPath,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(resultPath);

        return await JsonSerializer.DeserializeAsync<RuntimeStackBackupPublicCutoverPreviewResult>(
            stream,
            JsonOptions,
            ct);
    }

    private static RuntimeStackBackupPublicCutoverPreviewSummary ToSummary(
        RuntimeStackBackupPublicCutoverPreviewResult result)
    {
        return new RuntimeStackBackupPublicCutoverPreviewSummary(
            PreviewId: result.PreviewId,
            ValidationId: result.ValidationId,
            CandidateId: result.CandidateId,
            PlanId: result.PlanId,
            RestoreMode: result.RestoreMode,
            Status: result.Status,
            CreatedAtUtc: result.CreatedAtUtc,
            ProductionExecutionLocked: result.ProductionExecutionLocked,
            TargetStackSlug: result.Candidate.TargetStackSlug,
            CandidateReady: result.Candidate.ReadyForMatrixCutoverPreview,
            ElementCandidateReady: result.Candidate.ReadyForElementCutoverPreview,
            MatrixHost: result.Routes.Matrix.Host,
            MatrixAction: result.Routes.Matrix.Action,
            MatrixRouteCurrentlyExists: result.Routes.Matrix.RouteCurrentlyExists,
            MatrixAvailableForFutureExecution: result.Routes.Matrix.AvailableForFutureExecution,
            ElementHost: result.Routes.Element.Host,
            ElementAction: result.Routes.Element.Action,
            ElementRouteCurrentlyExists: result.Routes.Element.RouteCurrentlyExists,
            ElementAvailableForFutureExecution: result.Routes.Element.AvailableForFutureExecution,
            BlockerCount: result.Blockers.Count,
            WarningCount: result.Warnings.Count,
            ErrorCount: result.Errors.Count,
            Detail: result.Detail,
            CatalogEntryId: result.CatalogEntryId,
            SourceKind: result.SourceKind,
            RestoreSessionId: result.RestoreSessionId);
    }

    private string ResolveHistoryRoot()
    {
        return Path.Combine(
            ResolveDataRoot(),
            "production-restore",
            "cutover-previews");
    }

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static bool IsSafePathSegment(
        string value)
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
