using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Candidate;

public sealed class RuntimeStackBackupProductionCandidateHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;

    public RuntimeStackBackupProductionCandidateHistoryService(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<RuntimeStackBackupProductionCandidateHistoryResponse> ListCandidatesAsync(
        string? validationId,
        string? status,
        int? max,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        var historyRoot = ResolveHistoryRoot();

        if (!Directory.Exists(historyRoot))
        {
            return new RuntimeStackBackupProductionCandidateHistoryResponse(
                Source: "control-plane",
                Status: "empty",
                HistoryRootPath: historyRoot,
                TotalCandidates: 0,
                Candidates: [],
                Warnings: [],
                Detail: "No Production Restore candidates exist yet.");
        }

        var candidates = new List<RuntimeStackBackupProductionCandidateSummary>();

        foreach (var candidateDirectory in Directory.EnumerateDirectories(historyRoot))
        {
            ct.ThrowIfCancellationRequested();

            var resultPath = Path.Combine(
                candidateDirectory,
                "production-candidate-result.json");

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
                    warnings.Add($"Could not parse Production Restore candidate: {resultPath}");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(validationId) &&
                    !string.Equals(result.ValidationId, validationId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(result.Status, status.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add(ToSummary(result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Could not read Production Restore candidate '{resultPath}': {ex.Message}");
            }
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.StartedAtUtc)
            .Take(max is > 0 ? max.Value : 50)
            .ToList();

        return new RuntimeStackBackupProductionCandidateHistoryResponse(
            Source: "control-plane",
            Status: warnings.Count == 0 ? "ok" : "warning",
            HistoryRootPath: historyRoot,
            TotalCandidates: candidates.Count,
            Candidates: ordered,
            Warnings: warnings,
            Detail: ordered.Count == 0
                ? "No Production Restore candidates matched the requested filters."
                : null);
    }

    public async Task<RuntimeStackBackupProductionCandidateDetailResponse?> GetCandidateAsync(
        string candidateId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            throw new InvalidOperationException("Production Restore candidate id is required.");
        }

        if (!IsSafePathSegment(candidateId))
        {
            throw new InvalidOperationException("Production Restore candidate id contains unsafe characters.");
        }

        var historyRoot = ResolveHistoryRoot();

        var resultPath = Path.Combine(
            historyRoot,
            candidateId,
            "production-candidate-result.json");

        if (!File.Exists(resultPath))
        {
            return null;
        }

        var result = await ReadResultFileAsync(
            resultPath,
            ct);

        return new RuntimeStackBackupProductionCandidateDetailResponse(
            Source: "control-plane",
            Status: result is null ? "error" : "ok",
            HistoryRootPath: historyRoot,
            Candidate: result,
            Warnings: result is null ? [$"Could not parse Production Restore candidate '{resultPath}'."] : [],
            Detail: result is null
                ? "Production Restore candidate file exists but could not be parsed."
                : null);
    }

    /// <summary>
    /// Returns the newest still-active private candidate for the exact
    /// catalog entry and replacement target. This keeps repeated catalog
    /// candidate requests idempotent without treating a legacy validation
    /// receipt as the source identity.
    /// </summary>
    public async Task<RuntimeStackBackupProductionCandidateResult?> FindActiveCatalogCandidateAsync(
        string catalogEntryId,
        string targetStackSlug,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(catalogEntryId))
        {
            throw new InvalidOperationException("Backup Catalog entry id is required.");
        }

        var historyRoot = ResolveHistoryRoot();

        if (!Directory.Exists(historyRoot))
        {
            return null;
        }

        var matches = new List<RuntimeStackBackupProductionCandidateResult>();

        foreach (var candidateDirectory in Directory.EnumerateDirectories(historyRoot))
        {
            ct.ThrowIfCancellationRequested();

            var resultPath = Path.Combine(
                candidateDirectory,
                "production-candidate-result.json");

            if (!File.Exists(resultPath))
            {
                continue;
            }

            try
            {
                var candidate = await ReadResultFileAsync(
                    resultPath,
                    ct);

                if (candidate is null ||
                    candidate.Destroy is not null ||
                    !string.Equals(
                        candidate.Status,
                        "private_candidate_ready",
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        candidate.SourceKind,
                        "backup-catalog",
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        candidate.CatalogEntryId,
                        catalogEntryId.Trim(),
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        candidate.TargetStackSlug,
                        targetStackSlug.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matches.Add(candidate);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _ = ex;
                // History is durable but best-effort for resume discovery.
                // A malformed unrelated result must not prevent creation of a
                // fresh candidate for this catalog source.
            }
        }

        return matches
            .OrderByDescending(candidate => candidate.StartedAtUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Returns the newest still-active private candidate for a migration-owned
    /// source and target stack. Migration identity is retained in the legacy-
    /// shaped ValidationId field so existing durable preview/confirmation/
    /// execution history can be reused without creating a Backup Catalog item
    /// or Restore Session.
    /// </summary>
    public async Task<RuntimeStackBackupProductionCandidateResult?> FindActiveMigrationCandidateAsync(
        string migrationId,
        string? targetStackSlug,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            throw new InvalidOperationException("Migration id is required.");
        }

        var historyRoot = ResolveHistoryRoot();

        if (!Directory.Exists(historyRoot))
        {
            return null;
        }

        var matches = new List<RuntimeStackBackupProductionCandidateResult>();

        foreach (var candidateDirectory in Directory.EnumerateDirectories(historyRoot))
        {
            ct.ThrowIfCancellationRequested();

            var resultPath = Path.Combine(
                candidateDirectory,
                "production-candidate-result.json");

            if (!File.Exists(resultPath))
            {
                continue;
            }

            try
            {
                var candidate = await ReadResultFileAsync(resultPath, ct);

                if (candidate is null ||
                    candidate.Destroy is not null ||
                    !string.Equals(candidate.Status, "private_candidate_ready", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(candidate.SourceKind, "migration-session", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(candidate.ValidationId, migrationId.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(targetStackSlug) &&
                     !string.Equals(candidate.TargetStackSlug, targetStackSlug.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                matches.Add(candidate);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _ = ex;
            }
        }

        return matches
            .OrderByDescending(candidate => candidate.StartedAtUtc)
            .FirstOrDefault();
    }

    internal async Task SaveCandidateAsync(
        RuntimeStackBackupProductionCandidateResult result,
        CancellationToken ct)
    {
        var candidateDirectory = Path.Combine(
            ResolveHistoryRoot(),
            result.CandidateId);

        Directory.CreateDirectory(candidateDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(candidateDirectory, "production-candidate-result.json"),
            JsonSerializer.Serialize(result, JsonOptions),
            ct);
    }

    private async Task<RuntimeStackBackupProductionCandidateResult?> ReadResultFileAsync(
        string resultPath,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(resultPath);

        return await JsonSerializer.DeserializeAsync<RuntimeStackBackupProductionCandidateResult>(
            stream,
            JsonOptions,
            ct);
    }

    private static RuntimeStackBackupProductionCandidateSummary ToSummary(
        RuntimeStackBackupProductionCandidateResult result)
    {
        var destroyed = result.Destroy is not null ||
            string.Equals(result.Status, "destroyed", StringComparison.OrdinalIgnoreCase);

        return new RuntimeStackBackupProductionCandidateSummary(
            CandidateId: result.CandidateId,
            ValidationId: result.ValidationId,
            RestoreMode: result.RestoreMode,
            Status: result.Status,
            StartedAtUtc: result.StartedAtUtc,
            FinishedAtUtc: result.FinishedAtUtc,
            DurationSeconds: result.FinishedAtUtc is null
                ? null
                : (result.FinishedAtUtc.Value - result.StartedAtUtc).TotalSeconds,
            TargetStackSlug: result.TargetStackSlug,
            MatrixServerName: result.MatrixServerName,
            PrivateRuntimeId: result.PrivateRuntimeId,
            PrivateRuntimeStatus: result.PrivateRuntimeStatus,
            PostgresContainerName: result.PostgresContainerName,
            SynapseContainerName: result.SynapseContainerName,
            ElementContainerName: result.ElementContainerName,
            NetworkName: result.NetworkName,
            ImportSucceeded: result.Database.ImportSucceeded,
            SynapseHealthPassed: result.Runtime.SynapseHealthPassed,
            ElementHealthPassed: result.Runtime.ElementHealthPassed,
            PublicTableCount: result.Database.PublicTableCount,
            SynapseKnownTableCount: result.Database.SynapseKnownTableCount,
            UsersCount: result.Database.UsersCount,
            EventsCount: result.Database.EventsCount,
            RoomsCount: result.Database.RoomsCount,
            Destroyed: destroyed,
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
            "candidates");
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