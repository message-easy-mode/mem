using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Preflight;

public sealed class RuntimeStackBackupProductionRestorePlanHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;

    public RuntimeStackBackupProductionRestorePlanHistoryService(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<RuntimeStackBackupProductionRestorePlanHistoryResponse> ListPlansAsync(
        string? validationId,
        string? stagingId,
        string? status,
        int? max,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        var historyRoot = ResolveHistoryRoot();

        if (!Directory.Exists(historyRoot))
        {
            return new RuntimeStackBackupProductionRestorePlanHistoryResponse(
                Source: "control-plane",
                Status: "empty",
                HistoryRootPath: historyRoot,
                TotalPlans: 0,
                Plans: [],
                Warnings: [],
                Detail: "No Production Restore plan history exists yet.");
        }

        var plans = new List<RuntimeStackBackupProductionRestorePlanSummary>();

        foreach (var planDirectory in Directory.EnumerateDirectories(historyRoot))
        {
            ct.ThrowIfCancellationRequested();

            var resultPath = Path.Combine(
                planDirectory,
                "production-restore-plan.json");

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
                    warnings.Add($"Could not parse Production Restore plan: {resultPath}");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(validationId) &&
                    !string.Equals(result.ValidationId, validationId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(stagingId) &&
                    !string.Equals(result.StagingId, stagingId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(result.Status, status.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                plans.Add(ToSummary(result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Could not read Production Restore plan '{resultPath}': {ex.Message}");
            }
        }

        var ordered = plans
            .OrderByDescending(plan => plan.CreatedAtUtc)
            .Take(max is > 0 ? max.Value : 50)
            .ToList();

        return new RuntimeStackBackupProductionRestorePlanHistoryResponse(
            Source: "control-plane",
            Status: warnings.Count == 0 ? "ok" : "warning",
            HistoryRootPath: historyRoot,
            TotalPlans: plans.Count,
            Plans: ordered,
            Warnings: warnings,
            Detail: ordered.Count == 0
                ? "No Production Restore plans matched the requested filters."
                : null);
    }

    public async Task<RuntimeStackBackupProductionRestorePlanDetailResponse?> GetPlanAsync(
        string planId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            throw new InvalidOperationException("Production Restore plan id is required.");
        }

        if (!IsSafePathSegment(planId))
        {
            throw new InvalidOperationException("Production Restore plan id contains unsafe characters.");
        }

        var historyRoot = ResolveHistoryRoot();

        var resultPath = Path.Combine(
            historyRoot,
            planId,
            "production-restore-plan.json");

        if (!File.Exists(resultPath))
        {
            return null;
        }

        var result = await ReadResultFileAsync(
            resultPath,
            ct);

        return new RuntimeStackBackupProductionRestorePlanDetailResponse(
            Source: "control-plane",
            Status: result is null ? "error" : "ok",
            HistoryRootPath: historyRoot,
            Plan: result,
            Warnings: result is null ? [$"Could not parse Production Restore plan '{resultPath}'."] : [],
            Detail: result is null
                ? "Production Restore plan file exists but could not be parsed."
                : null);
    }

    internal async Task SavePlanAsync(
        RuntimeStackBackupProductionRestorePlanResult result,
        CancellationToken ct)
    {
        var planDirectory = Path.Combine(
            ResolveHistoryRoot(),
            result.PlanId);

        Directory.CreateDirectory(planDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(planDirectory, "production-restore-plan.json"),
            JsonSerializer.Serialize(result, JsonOptions),
            ct);
    }

    private async Task<RuntimeStackBackupProductionRestorePlanResult?> ReadResultFileAsync(
        string resultPath,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(resultPath);

        return await JsonSerializer.DeserializeAsync<RuntimeStackBackupProductionRestorePlanResult>(
            stream,
            JsonOptions,
            ct);
    }

    private static RuntimeStackBackupProductionRestorePlanSummary ToSummary(
        RuntimeStackBackupProductionRestorePlanResult result)
    {
        return new RuntimeStackBackupProductionRestorePlanSummary(
            PlanId: result.PlanId,
            ValidationId: result.ValidationId,
            StagingId: result.StagingId,
            RestoreMode: result.RestoreMode,
            Status: result.Status,
            CreatedAtUtc: result.CreatedAtUtc,
            ProductionExecutionLocked: result.ProductionExecutionLocked,
            SourceStackSlug: result.BackupSource.SourceStackSlug,
            SourceMatrixServerName: result.BackupSource.SourceMatrixServerName,
            TargetStackSlug: result.Target.TargetStackSlug,
            TargetStackFound: result.Target.StackFound,
            StagingEvidenceAccepted: result.StagingEvidence.AcceptedAsEvidence,
            MatrixRouteDiscovered: result.Npm.MatrixRouteDiscovered,
            ElementRouteDiscovered: result.Npm.ElementRouteDiscovered,
            MatrixCertificateAvailable: result.Certificates.MatrixCertificateAvailable,
            ElementCertificateAvailable: result.Certificates.ElementCertificateAvailable,
            BlockerCount: result.Blockers.Count,
            WarningCount: result.Warnings.Count,
            ErrorCount: result.Errors.Count,
            Detail: result.Detail,
            CatalogEntryId: result.CatalogEntryId,
            SourceKind: result.SourceKind);
    }

    private string ResolveHistoryRoot()
    {
        return Path.Combine(
            ResolveDataRoot(),
            "production-restore",
            "plans");
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