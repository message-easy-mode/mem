using System;

namespace Infrastructure.Data.Entities;

public sealed class RuntimeReadinessReportEntity
{
    public Guid Id { get; set; }

    public Guid RuntimeStackId { get; set; }
    public RuntimeStackEntity RuntimeStack { get; set; } = default!;

    public Guid? OperationId { get; set; }

    public string ReportKind { get; set; } = default!;
    public string Status { get; set; } = default!;

    public bool AllPassed { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? Summary { get; set; }

    public string? ReportJson { get; set; }
    public string? EvidenceJson { get; set; }

    public int FailedCheckCount { get; set; }
    public int WarningCount { get; set; }

    public string? TriggeredBy { get; set; }
    public Guid? TriggeredByUserId { get; set; }
}