namespace Shared.Diagnostics;

public sealed record MemDiagnosticContext(
    string? TraceId = null,
    string? SpanId = null,
    string? RequestId = null,
    string? CorrelationId = null);

public sealed record MemDiagnosticResource(
    string Kind,
    string Id,
    string? DisplayName = null,
    string? StackId = null,
    string? StackSlug = null,
    string? Service = null,
    string? WorkspacePath = null);

public sealed record MemDiagnosticException(
    string Type,
    string Message,
    string? StackTrace,
    IReadOnlyList<MemDiagnosticException> InnerExceptions);

public sealed record MemDiagnosticEvent(
    int SchemaVersion,
    string EventId,
    DateTimeOffset TimestampUtc,
    string Severity,
    string EventCode,
    string Source,
    string Feature,
    string? Stage,
    string Message,
    string? IncidentId,
    string? TraceId,
    string? SpanId,
    string? RequestId,
    string? CorrelationId,
    Guid? OperationId,
    MemDiagnosticResource? Resource,
    IReadOnlyDictionary<string, string>? Expected,
    IReadOnlyDictionary<string, string>? Observed,
    IReadOnlyDictionary<string, string>? Details,
    MemDiagnosticException? Exception,
    string? SuggestedAction,
    bool Retryable,
    bool RedactionsApplied,
    bool Truncated);

public sealed record MemDiagnosticWriteRequest(
    string Severity,
    string EventCode,
    string Source,
    string Feature,
    string Message,
    string? Stage = null,
    string? IncidentId = null,
    bool CreateIncident = false,
    Guid? OperationId = null,
    MemDiagnosticResource? Resource = null,
    IReadOnlyDictionary<string, string?>? Expected = null,
    IReadOnlyDictionary<string, string?>? Observed = null,
    IReadOnlyDictionary<string, string?>? Details = null,
    Exception? Exception = null,
    string? SuggestedAction = null,
    bool Retryable = false,
    MemDiagnosticContext? Context = null,
    IReadOnlyCollection<string>? ExactSecrets = null);

public sealed record MemDiagnosticWriteResult(
    bool Stored,
    string EventId,
    string? IncidentId,
    string? WarningCode,
    DateTimeOffset? TimestampUtc = null);

public sealed record MemDiagnosticQuery(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? UntilUtc = null,
    string? Severity = null,
    string? Source = null,
    string? Feature = null,
    string? Stage = null,
    string? EventCode = null,
    string? IncidentId = null,
    string? TraceId = null,
    Guid? OperationId = null,
    string? StackId = null,
    string? EventId = null,
    string? ResourceKind = null,
    string? Search = null,
    string? Cursor = null,
    int? PageSize = null,
    bool RequireIncidentId = false);

public sealed record MemDiagnosticEventPage(
    DateTimeOffset FromUtc,
    DateTimeOffset UntilUtc,
    int PageSize,
    bool WindowClamped,
    IReadOnlyList<MemDiagnosticEvent> Events,
    string? NextCursor,
    IReadOnlyList<string> Warnings);

public sealed record MemDiagnosticStoreHealth(
    bool Enabled,
    string Status,
    DateTimeOffset? LastWriteAtUtc,
    DateTimeOffset? LastReadAtUtc,
    long StoredEventCount,
    long DroppedEventCount,
    long MalformedLineCount,
    string? LastWriteErrorCode,
    string? LastReadWarningCode,
    DateTimeOffset? LastRetentionRunAtUtc,
    int LastRetentionDeletedFileCount,
    long LastRetentionDeletedBytes,
    string? LastRetentionErrorCode,
    MemStorageCapacityHealth? Storage = null,
    bool HasEverRecordedEvent = false);

public sealed class MemDiagnosticCursorException : Exception
{
    public MemDiagnosticCursorException()
        : base("The diagnostic cursor is invalid or expired.")
    {
    }

    public MemDiagnosticCursorException(Exception innerException)
        : base("The diagnostic cursor is invalid or expired.", innerException)
    {
    }
}
