using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Backups.StandardRecreate;

public sealed class StandardRecreateHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IConfiguration _configuration;

    public StandardRecreateHistoryService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SaveAsync(StandardRecreateResult result, CancellationToken ct)
    {
        var root = GetHistoryRootPath();
        Directory.CreateDirectory(root);

        var path = Path.Combine(root, $"{result.RecreateId}.json");
        var json = JsonSerializer.Serialize(result, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public async Task<StandardRecreateHistoryResponse> ListAsync(
        string? status,
        int? max,
        CancellationToken ct)
    {
        var root = GetHistoryRootPath();
        var warnings = new List<string>();

        if (!Directory.Exists(root))
        {
            return new StandardRecreateHistoryResponse(
                Source: "control-plane",
                Status: "ok",
                HistoryRootPath: root,
                TotalRecreates: 0,
                Recreates: [],
                Warnings: [],
                Detail: null);
        }

        var items = new List<StandardRecreateHistoryItem>();

        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(path, ct);
                var result = JsonSerializer.Deserialize<StandardRecreateResult>(json, JsonOptions);
                if (result is null)
                {
                    continue;
                }


                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(result.Status, status, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                items.Add(new StandardRecreateHistoryItem(
                    RecreateId: result.RecreateId,
                    SourceKind: result.SourceKind,
                    CatalogEntryId: result.CatalogEntryId,
                    RuntimeStackId: result.RuntimeStackId,
                    TargetStackSlug: result.TargetStackSlug,
                    MatrixHost: result.MatrixHost,
                    ElementHost: result.ElementHost,
                    Status: result.Status,
                    StartedAtUtc: result.StartedAtUtc,
                    FinishedAtUtc: result.FinishedAtUtc,
                    StackRegistered: result.Runtime.StackRegistered,
                    PublicReadinessPassed: result.Routes.PublicReadinessPassed,
                    WarningCount: result.Warnings.Count,
                    ErrorCount: result.Errors.Count,
                    Detail: result.Detail));
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not read production recreate history file '{path}': {ex.Message}");
            }
        }

        var ordered = items
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(Math.Clamp(max ?? 50, 1, 500))
            .ToArray();

        return new StandardRecreateHistoryResponse(
            Source: "control-plane",
            Status: "ok",
            HistoryRootPath: root,
            TotalRecreates: ordered.Length,
            Recreates: ordered,
            Warnings: warnings,
            Detail: null);
    }

    public async Task<StandardRecreateDetailResponse?> GetAsync(string recreateId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recreateId) || !IsSafePathSegment(recreateId))
        {
            throw new InvalidOperationException("Production recreate id is invalid.");
        }

        var root = GetHistoryRootPath();
        var path = Path.Combine(root, $"{recreateId}.json");

        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, ct);
        var result = JsonSerializer.Deserialize<StandardRecreateResult>(json, JsonOptions);
        if (result is null)
        {
            return null;
        }

        return new StandardRecreateDetailResponse(
            Source: "control-plane",
            Status: "ok",
            HistoryRootPath: root,
            Recreate: result,
            Warnings: [],
            Detail: null);
    }

    private string GetHistoryRootPath() =>
        Path.Combine(ResolveDataRoot(), "production-recreate", "runs");

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static bool IsSafePathSegment(string value) =>
        value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');
}
