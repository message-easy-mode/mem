using System.Text.Json;
using System.Text.Json.Serialization;
using HostAgent.Runtime.Manifests;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostAgent.Runtime.Readiness;

public sealed class RuntimeReadinessReportStore
{
    public const int DefaultHistoryPageSize = 10;
    public const int MaximumHistoryPageSize = 50;

    private readonly MemDbContext _db;

    public RuntimeReadinessReportStore(MemDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> SaveAsync(
        RuntimeStackManifest manifest,
        RuntimeReadinessVerificationResult verification,
        string reportKind,
        string triggeredBy,
        Guid? operationId,
        CancellationToken ct)
    {
        var runtimeStackExists = await _db.RuntimeStacks
            .AnyAsync(x => x.Id == manifest.StackId, ct);

        if (!runtimeStackExists)
        {
            throw new InvalidOperationException(
                $"RuntimeStack '{manifest.StackId}' was not found. Run create/verify first so the control-plane stack record exists.");
        }

        var now = DateTimeOffset.UtcNow;

        var failedCheckCount = verification.Checks.Count(x => !x.Success);

        var reportId = Guid.NewGuid();

        var reportPayload = new RuntimeReadinessReportPayload(
            ReportId: reportId,
            StackId: manifest.StackId,
            Slug: manifest.Slug,
            ReportKind: reportKind,
            Status: verification.AllPassed ? "passed" : "failed",
            AllPassed: verification.AllPassed,
            CreatedAtUtc: now,
            LastVerifiedStatus: manifest.LastVerifiedStatus,
            LastVerifiedAtUtc: manifest.LastVerifiedAtUtc,
            OperationId: operationId,
            Checks: verification.Checks
                .Select(RuntimeReadinessReportCheckPayload.FromCheck)
                .ToArray());

        var entity = new RuntimeReadinessReportEntity
        {
            Id = reportId,
            RuntimeStackId = manifest.StackId,
            OperationId = operationId,

            ReportKind = reportKind,
            Status = verification.AllPassed ? "passed" : "failed",
            AllPassed = verification.AllPassed,

            CreatedAtUtc = now.UtcDateTime,

            Summary = verification.AllPassed
                ? "Runtime readiness checks passed."
                : $"Runtime readiness checks failed. Failed checks: {failedCheckCount}.",

            ReportJson = JsonSerializer.Serialize(
                reportPayload,
                JsonOptions()),

            EvidenceJson = JsonSerializer.Serialize(
                reportPayload.Checks,
                JsonOptions()),

            FailedCheckCount = failedCheckCount,
            WarningCount = 0,

            TriggeredBy = triggeredBy,
            TriggeredByUserId = null
        };

        _db.RuntimeReadinessReports.Add(entity);

        await _db.SaveChangesAsync(ct);

        return reportId;
    }

    public async Task<RuntimeReadinessReportSnapshot?> GetLatestAsync(
        Guid runtimeStackId,
        string reportKind,
        CancellationToken ct)
    {
        ValidateReportKind(reportKind);

        var entity = await BaseQuery(runtimeStackId, reportKind)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : ToSnapshot(entity);
    }

    public async Task<RuntimeReadinessReportSnapshot?> GetByIdAsync(
        Guid runtimeStackId,
        string reportKind,
        Guid reportId,
        CancellationToken ct)
    {
        ValidateReportKind(reportKind);

        var entity = await BaseQuery(runtimeStackId, reportKind)
            .FirstOrDefaultAsync(x => x.Id == reportId, ct);

        return entity is null ? null : ToSnapshot(entity);
    }

    public async Task<RuntimeReadinessReportHistoryPage> ListPreviousAsync(
        Guid runtimeStackId,
        string reportKind,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        ValidateReportKind(reportKind);

        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "History page must be at least 1.");
        }

        if (pageSize < 1 || pageSize > MaximumHistoryPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                $"History page size must be between 1 and {MaximumHistoryPageSize}.");
        }

        var query = BaseQuery(runtimeStackId, reportKind);
        var latestReportId = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);

        if (!latestReportId.HasValue)
        {
            return RuntimeReadinessReportHistoryPage.Empty(pageSize);
        }

        var previousQuery = query.Where(x => x.Id != latestReportId.Value);
        var totalCount = await previousQuery.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var effectivePage = Math.Min(page, totalPages);

        var entities = await previousQuery
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((effectivePage - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var reports = entities
            .Select(ToSummary)
            .ToArray();

        return new RuntimeReadinessReportHistoryPage(
            Reports: reports,
            TotalCount: totalCount,
            Page: effectivePage,
            PageSize: pageSize,
            TotalPages: totalPages,
            HasPreviousPage: effectivePage > 1,
            HasNextPage: effectivePage < totalPages);
    }

    private IQueryable<RuntimeReadinessReportEntity> BaseQuery(
        Guid runtimeStackId,
        string reportKind)
    {
        return _db.RuntimeReadinessReports
            .AsNoTracking()
            .Where(x => x.RuntimeStackId == runtimeStackId && x.ReportKind == reportKind);
    }

    private static RuntimeReadinessReportSnapshot ToSnapshot(RuntimeReadinessReportEntity entity)
    {
        var checks = ReadChecks(entity);

        return new RuntimeReadinessReportSnapshot(
            ReportId: entity.Id,
            RuntimeStackId: entity.RuntimeStackId,
            OperationId: entity.OperationId,
            ReportKind: entity.ReportKind,
            Status: entity.Status,
            AllPassed: entity.AllPassed,
            CreatedAtUtc: AsUtc(entity.CreatedAtUtc),
            Summary: entity.Summary,
            FailedCheckCount: entity.FailedCheckCount,
            WarningCount: entity.WarningCount,
            TriggeredBy: entity.TriggeredBy,
            Checks: checks);
    }

    private static RuntimeReadinessReportSummarySnapshot ToSummary(RuntimeReadinessReportEntity entity)
    {
        return new RuntimeReadinessReportSummarySnapshot(
            ReportId: entity.Id,
            OperationId: entity.OperationId,
            Status: entity.Status,
            AllPassed: entity.AllPassed,
            CreatedAtUtc: AsUtc(entity.CreatedAtUtc),
            Summary: entity.Summary,
            CheckCount: ReadChecks(entity).Count,
            FailedCheckCount: entity.FailedCheckCount,
            WarningCount: entity.WarningCount);
    }

    private static IReadOnlyList<RuntimeReadinessReportCheckPayload> ReadChecks(
        RuntimeReadinessReportEntity entity)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(entity.EvidenceJson))
            {
                return JsonSerializer.Deserialize<RuntimeReadinessReportCheckPayload[]>(
                           entity.EvidenceJson,
                           JsonOptions())
                       ?? [];
            }

            if (!string.IsNullOrWhiteSpace(entity.ReportJson))
            {
                return JsonSerializer.Deserialize<RuntimeReadinessReportPayload>(
                           entity.ReportJson,
                           JsonOptions())
                       ?.Checks
                       ?? [];
            }

            return [];
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Runtime readiness report '{entity.Id}' contains invalid persisted JSON evidence.",
                ex);
        }
    }

    private static void ValidateReportKind(string reportKind)
    {
        if (string.IsNullOrWhiteSpace(reportKind))
        {
            throw new ArgumentException("Report kind is required.", nameof(reportKind));
        }
    }

    private static DateTimeOffset AsUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        return new DateTimeOffset(utc);
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}

public sealed record RuntimeReadinessReportPayload(
    Guid ReportId,
    Guid StackId,
    string Slug,
    string ReportKind,
    string Status,
    bool AllPassed,
    DateTimeOffset CreatedAtUtc,
    string LastVerifiedStatus,
    DateTimeOffset LastVerifiedAtUtc,
    Guid? OperationId,
    IReadOnlyList<RuntimeReadinessReportCheckPayload> Checks);

public sealed record RuntimeReadinessReportCheckPayload(
    string Code,
    string Name,
    string Url,
    bool Success,
    int? StatusCode,
    string? Detail,
    string? BodyPreview)
{
    public static RuntimeReadinessReportCheckPayload FromCheck(
        RuntimeReadinessCheckResult check)
    {
        return new RuntimeReadinessReportCheckPayload(
            Code: check.Code,
            Name: check.Name,
            Url: check.Url,
            Success: check.Success,
            StatusCode: check.StatusCode,
            Detail: check.Detail,
            BodyPreview: check.BodyPreview);
    }
}

public sealed record RuntimeReadinessReportSnapshot(
    Guid ReportId,
    Guid RuntimeStackId,
    Guid? OperationId,
    string ReportKind,
    string Status,
    bool AllPassed,
    DateTimeOffset CreatedAtUtc,
    string? Summary,
    int FailedCheckCount,
    int WarningCount,
    string? TriggeredBy,
    IReadOnlyList<RuntimeReadinessReportCheckPayload> Checks);

public sealed record RuntimeReadinessReportSummarySnapshot(
    Guid ReportId,
    Guid? OperationId,
    string Status,
    bool AllPassed,
    DateTimeOffset CreatedAtUtc,
    string? Summary,
    int CheckCount,
    int FailedCheckCount,
    int WarningCount);

public sealed record RuntimeReadinessReportHistoryPage(
    IReadOnlyList<RuntimeReadinessReportSummarySnapshot> Reports,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage)
{
    public static RuntimeReadinessReportHistoryPage Empty(int pageSize) =>
        new(
            Reports: [],
            TotalCount: 0,
            Page: 1,
            PageSize: pageSize,
            TotalPages: 1,
            HasPreviousPage: false,
            HasNextPage: false);
}
