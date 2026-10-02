using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Execution;

public sealed class RuntimeStackBackupPublicCutoverExecutionHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;

    public RuntimeStackBackupPublicCutoverExecutionHistoryService(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<RuntimeStackBackupPublicCutoverExecutionHistoryResponse> ListExecutionsAsync(
        string? confirmationId,
        string? previewId,
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
            return new RuntimeStackBackupPublicCutoverExecutionHistoryResponse(
                Source: "control-plane",
                Status: "empty",
                HistoryRootPath: historyRoot,
                TotalExecutions: 0,
                Executions: [],
                Warnings: [],
                Detail: "No Public Cutover execution history exists yet.");
        }

        var executions = new List<RuntimeStackBackupPublicCutoverExecutionSummary>();

        foreach (var executionDirectory in Directory.EnumerateDirectories(historyRoot))
        {
            ct.ThrowIfCancellationRequested();

            var resultPath = Path.Combine(
                executionDirectory,
                "public-cutover-execution.json");

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
                    warnings.Add($"Could not parse Public Cutover execution: {resultPath}");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(confirmationId) &&
                    !string.Equals(result.ConfirmationId, confirmationId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(previewId) &&
                    !string.Equals(result.PreviewId, previewId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
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

                executions.Add(ToSummary(result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Could not read Public Cutover execution '{resultPath}': {ex.Message}");
            }
        }

        var ordered = executions
            .OrderByDescending(execution => execution.CreatedAtUtc)
            .Take(max is > 0 ? max.Value : 50)
            .ToList();

        return new RuntimeStackBackupPublicCutoverExecutionHistoryResponse(
            Source: "control-plane",
            Status: warnings.Count == 0 ? "ok" : "warning",
            HistoryRootPath: historyRoot,
            TotalExecutions: executions.Count,
            Executions: ordered,
            Warnings: warnings,
            Detail: ordered.Count == 0
                ? "No Public Cutover executions matched the requested filters."
                : null);
    }

    public async Task<RuntimeStackBackupPublicCutoverExecutionDetailResponse?> GetExecutionAsync(
        string executionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(executionId))
        {
            throw new InvalidOperationException("Public Cutover execution id is required.");
        }

        if (!IsSafePathSegment(executionId))
        {
            throw new InvalidOperationException("Public Cutover execution id contains unsafe characters.");
        }

        var historyRoot = ResolveHistoryRoot();

        var resultPath = Path.Combine(
            historyRoot,
            executionId,
            "public-cutover-execution.json");

        if (!File.Exists(resultPath))
        {
            return null;
        }

        var result = await ReadResultFileAsync(
            resultPath,
            ct);

        return new RuntimeStackBackupPublicCutoverExecutionDetailResponse(
            Source: "control-plane",
            Status: result is null ? "error" : "ok",
            HistoryRootPath: historyRoot,
            Execution: result,
            Warnings: result is null ? [$"Could not parse Public Cutover execution '{resultPath}'."] : [],
            Detail: result is null
                ? "Public Cutover execution file exists but could not be parsed."
                : null);
    }

    internal async Task SaveExecutionAsync(
        RuntimeStackBackupPublicCutoverExecutionResult result,
        CancellationToken ct)
    {
        var executionDirectory = Path.Combine(
            ResolveHistoryRoot(),
            result.ExecutionId);

        Directory.CreateDirectory(executionDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(executionDirectory, "public-cutover-execution.json"),
            JsonSerializer.Serialize(result, JsonOptions),
            ct);
    }

    private async Task<RuntimeStackBackupPublicCutoverExecutionResult?> ReadResultFileAsync(
        string resultPath,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(resultPath);

        return await JsonSerializer.DeserializeAsync<RuntimeStackBackupPublicCutoverExecutionResult>(
            stream,
            JsonOptions,
            ct);
    }

    private static RuntimeStackBackupPublicCutoverExecutionSummary ToSummary(
        RuntimeStackBackupPublicCutoverExecutionResult result)
    {
        return new RuntimeStackBackupPublicCutoverExecutionSummary(
            ExecutionId: result.ExecutionId,
            ConfirmationId: result.ConfirmationId,
            PreviewId: result.PreviewId,
            ValidationId: result.ValidationId,
            CandidateId: result.CandidateId,
            FreshPlanId: result.FreshPlanId,
            RestoreMode: result.RestoreMode,
            Status: result.Status,
            CreatedAtUtc: result.CreatedAtUtc,
            FinishedAtUtc: result.FinishedAtUtc,
            Operator: result.Operator,
            NpmRouteExecutionPerformed: result.NpmRouteExecutionPerformed,
            CutoverIngressNetworkExecutionPerformed: result.CutoverIngressNetworkExecutionPerformed,
            RuntimePromotionLocked: result.RuntimePromotionLocked,
            DnsMutationLocked: result.DnsMutationLocked,
            CertificateMutationLocked: result.CertificateMutationLocked,
            MatrixSucceeded: result.Routes.Matrix.Succeeded,
            ElementSucceeded: result.Routes.Element.Succeeded,
            AnyRouteMutated: result.Routes.AnyRouteMutated,
            CutoverIngressReady: result.CutoverIngress.Ready,
            DockerNetworksChanged: result.CutoverIngress.DockerNetworksChanged,
            BlockerCount: result.Blockers.Count,
            WarningCount: result.Warnings.Count,
            ErrorCount: result.Errors.Count,
            Detail: result.Detail);
    }

    private string ResolveHistoryRoot()
    {
        return Path.Combine(
            ResolveDataRoot(),
            "production-restore",
            "cutover-executions");
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

        return value.All(ch =>
            char.IsLetterOrDigit(ch) ||
            ch is '-' or '_' or '.');
    }
}
