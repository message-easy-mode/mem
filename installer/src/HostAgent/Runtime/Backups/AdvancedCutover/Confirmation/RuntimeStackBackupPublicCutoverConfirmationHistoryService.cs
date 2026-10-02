using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Confirmation;

public sealed class RuntimeStackBackupPublicCutoverConfirmationHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;

    public RuntimeStackBackupPublicCutoverConfirmationHistoryService(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<RuntimeStackBackupPublicCutoverConfirmationHistoryResponse> ListConfirmationsAsync(
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
            return new RuntimeStackBackupPublicCutoverConfirmationHistoryResponse(
                Source: "control-plane",
                Status: "empty",
                HistoryRootPath: historyRoot,
                TotalConfirmations: 0,
                Confirmations: [],
                Warnings: [],
                Detail: "No Public Cutover confirmation-gate history exists yet.");
        }

        var confirmations = new List<RuntimeStackBackupPublicCutoverConfirmationSummary>();

        foreach (var confirmationDirectory in Directory.EnumerateDirectories(historyRoot))
        {
            ct.ThrowIfCancellationRequested();

            var resultPath = Path.Combine(
                confirmationDirectory,
                "public-cutover-confirmation.json");

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
                    warnings.Add($"Could not parse Public Cutover confirmation: {resultPath}");
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

                confirmations.Add(ToSummary(result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Could not read Public Cutover confirmation '{resultPath}': {ex.Message}");
            }
        }

        var ordered = confirmations
            .OrderByDescending(confirmation => confirmation.CreatedAtUtc)
            .Take(max is > 0 ? max.Value : 50)
            .ToList();

        return new RuntimeStackBackupPublicCutoverConfirmationHistoryResponse(
            Source: "control-plane",
            Status: warnings.Count == 0 ? "ok" : "warning",
            HistoryRootPath: historyRoot,
            TotalConfirmations: confirmations.Count,
            Confirmations: ordered,
            Warnings: warnings,
            Detail: ordered.Count == 0
                ? "No Public Cutover confirmations matched the requested filters."
                : null);
    }

    public async Task<RuntimeStackBackupPublicCutoverConfirmationDetailResponse?> GetConfirmationAsync(
        string confirmationId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(confirmationId))
        {
            throw new InvalidOperationException("Public Cutover confirmation id is required.");
        }

        if (!IsSafePathSegment(confirmationId))
        {
            throw new InvalidOperationException("Public Cutover confirmation id contains unsafe characters.");
        }

        var historyRoot = ResolveHistoryRoot();

        var resultPath = Path.Combine(
            historyRoot,
            confirmationId,
            "public-cutover-confirmation.json");

        if (!File.Exists(resultPath))
        {
            return null;
        }

        var result = await ReadResultFileAsync(
            resultPath,
            ct);

        return new RuntimeStackBackupPublicCutoverConfirmationDetailResponse(
            Source: "control-plane",
            Status: result is null ? "error" : "ok",
            HistoryRootPath: historyRoot,
            Confirmation: result,
            Warnings: result is null ? [$"Could not parse Public Cutover confirmation '{resultPath}'."] : [],
            Detail: result is null
                ? "Public Cutover confirmation file exists but could not be parsed."
                : null);
    }

    internal async Task SaveConfirmationAsync(
        RuntimeStackBackupPublicCutoverConfirmationResult result,
        CancellationToken ct)
    {
        var confirmationDirectory = Path.Combine(
            ResolveHistoryRoot(),
            result.ConfirmationId);

        Directory.CreateDirectory(confirmationDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(confirmationDirectory, "public-cutover-confirmation.json"),
            JsonSerializer.Serialize(result, JsonOptions),
            ct);
    }

    private async Task<RuntimeStackBackupPublicCutoverConfirmationResult?> ReadResultFileAsync(
        string resultPath,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(resultPath);

        return await JsonSerializer.DeserializeAsync<RuntimeStackBackupPublicCutoverConfirmationResult>(
            stream,
            JsonOptions,
            ct);
    }

    private static RuntimeStackBackupPublicCutoverConfirmationSummary ToSummary(
        RuntimeStackBackupPublicCutoverConfirmationResult result)
    {
        var acknowledgementsPassed = result.Acknowledgements
            .Where(ack => ack.Required)
            .All(ack => ack.Acknowledged);

        return new RuntimeStackBackupPublicCutoverConfirmationSummary(
            ConfirmationId: result.ConfirmationId,
            PreviewId: result.PreviewId,
            ValidationId: result.ValidationId,
            CandidateId: result.CandidateId,
            FreshPlanId: result.FreshPlanId,
            RestoreMode: result.RestoreMode,
            Status: result.Status,
            CreatedAtUtc: result.CreatedAtUtc,
            Operator: result.Operator,
            ProductionExecutionLocked: result.ProductionExecutionLocked,
            ExecutionAvailable: result.ExecutionAvailable,
            CandidateReady: result.Candidate.PrivateOnly &&
                !result.Candidate.Destroyed &&
                result.Candidate.DatabaseImportSucceeded &&
                result.Candidate.SynapseHealthPassed,
            MatrixPassed: result.Routes.Matrix.Passed,
            ElementPassed: result.Routes.Element.Passed,
            AcknowledgementsPassed: acknowledgementsPassed,
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
            "cutover-confirmations");
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
