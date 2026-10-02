using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Confirmation.Catalog;

/// <summary>
/// Durable audit history for catalog-native confirmation evaluations. Stored
/// separately from legacy upload-validation confirmation history so a catalog
/// entry can never be mistaken for a validation receipt.
/// </summary>
public sealed class CatalogPublicCutoverConfirmationHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;

    public CatalogPublicCutoverConfirmationHistoryService(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<CatalogPublicCutoverConfirmationHistoryResponse> ListConfirmationsAsync(
        string catalogEntryId,
        string? previewId,
        string? candidateId,
        string? status,
        int? max,
        CancellationToken ct)
    {
        ValidatePathSegment(catalogEntryId, "Backup Catalog entry id");

        var warnings = new List<string>();
        var historyRoot = ResolveHistoryRoot();

        if (!Directory.Exists(historyRoot))
        {
            return new CatalogPublicCutoverConfirmationHistoryResponse(
                Source: "control-plane",
                Status: "empty",
                HistoryRootPath: historyRoot,
                TotalConfirmations: 0,
                Confirmations: [],
                Warnings: [],
                Detail: "No catalog-native Public Cutover confirmation history exists yet.");
        }

        var confirmations = new List<CatalogPublicCutoverConfirmationSummary>();

        foreach (var confirmationDirectory in Directory.EnumerateDirectories(historyRoot))
        {
            ct.ThrowIfCancellationRequested();

            var resultPath = Path.Combine(
                confirmationDirectory,
                "catalog-public-cutover-confirmation.json");

            if (!File.Exists(resultPath))
            {
                continue;
            }

            try
            {
                var result = await ReadResultFileAsync(resultPath, ct);

                if (result is null)
                {
                    warnings.Add($"Could not parse catalog-native Public Cutover confirmation: {resultPath}");
                    continue;
                }

                if (!string.Equals(
                        result.CatalogEntryId,
                        catalogEntryId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(previewId) &&
                    !string.Equals(result.PreviewId, previewId.Trim(), StringComparison.OrdinalIgnoreCase))
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
                warnings.Add($"Could not read catalog-native Public Cutover confirmation '{resultPath}': {ex.Message}");
            }
        }

        var ordered = confirmations
            .OrderByDescending(confirmation => confirmation.CreatedAtUtc)
            .Take(max is > 0 ? max.Value : 50)
            .ToList();

        return new CatalogPublicCutoverConfirmationHistoryResponse(
            Source: "control-plane",
            Status: warnings.Count == 0 ? "ok" : "warning",
            HistoryRootPath: historyRoot,
            TotalConfirmations: confirmations.Count,
            Confirmations: ordered,
            Warnings: warnings,
            Detail: ordered.Count == 0
                ? "No catalog-native Public Cutover confirmations matched the requested filters."
                : null);
    }

    public async Task<CatalogPublicCutoverConfirmationDetailResponse?> GetConfirmationAsync(
        string catalogEntryId,
        string confirmationId,
        CancellationToken ct)
    {
        ValidatePathSegment(catalogEntryId, "Backup Catalog entry id");
        ValidatePathSegment(confirmationId, "Catalog-native Public Cutover confirmation id");

        var historyRoot = ResolveHistoryRoot();
        var resultPath = Path.Combine(
            historyRoot,
            confirmationId,
            "catalog-public-cutover-confirmation.json");

        if (!File.Exists(resultPath))
        {
            return null;
        }

        var result = await ReadResultFileAsync(resultPath, ct);

        if (result is not null &&
            !string.Equals(result.CatalogEntryId, catalogEntryId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new CatalogPublicCutoverConfirmationDetailResponse(
            Source: "control-plane",
            Status: result is null ? "error" : "ok",
            HistoryRootPath: historyRoot,
            Confirmation: result,
            Warnings: result is null
                ? [$"Could not parse catalog-native Public Cutover confirmation '{resultPath}'."]
                : [],
            Detail: result is null
                ? "Catalog-native Public Cutover confirmation file exists but could not be parsed."
                : null);
    }

    internal async Task SaveConfirmationAsync(
        CatalogPublicCutoverConfirmationResult result,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidatePathSegment(result.ConfirmationId, "Catalog-native Public Cutover confirmation id");

        var confirmationDirectory = Path.Combine(
            ResolveHistoryRoot(),
            result.ConfirmationId);

        Directory.CreateDirectory(confirmationDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(confirmationDirectory, "catalog-public-cutover-confirmation.json"),
            JsonSerializer.Serialize(result, JsonOptions),
            ct);
    }

    private async Task<CatalogPublicCutoverConfirmationResult?> ReadResultFileAsync(
        string resultPath,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(resultPath);

        return await JsonSerializer.DeserializeAsync<CatalogPublicCutoverConfirmationResult>(
            stream,
            JsonOptions,
            ct);
    }

    private static CatalogPublicCutoverConfirmationSummary ToSummary(
        CatalogPublicCutoverConfirmationResult result)
    {
        var acknowledgementsPassed = result.Acknowledgements
            .Where(acknowledgement => acknowledgement.Required)
            .All(acknowledgement => acknowledgement.Acknowledged);

        return new CatalogPublicCutoverConfirmationSummary(
            ConfirmationId: result.ConfirmationId,
            CatalogEntryId: result.CatalogEntryId,
            SourceKind: result.SourceKind,
            RestoreSessionId: result.RestoreSessionId,
            PreviewId: result.PreviewId,
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
                result.Candidate.SynapseHealthPassed &&
                result.Candidate.ElementHealthPassed,
            MatrixPassed: result.Routes.Matrix.Passed,
            ElementPassed: result.Routes.Element.Passed,
            CertificatesPassed: result.Certificates.AllRequiredCertificatesReady,
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
            "catalog-cutover-confirmations");
    }

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static void ValidatePathSegment(
        string value,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }

        if (value.Contains('/', StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal) ||
            value.Contains(':', StringComparison.Ordinal) ||
            value.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label} contains unsafe characters.");
        }
    }
}
