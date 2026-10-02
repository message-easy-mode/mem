using Shared.Diagnostics;

namespace Api.Diagnostics;

public sealed record MemExceptionClassification(
    int StatusCode,
    string Code,
    string Title,
    string Detail,
    bool CreateIncident,
    bool Retryable,
    string DiagnosticEventCode,
    string DiagnosticSeverity,
    string Feature,
    string? Stage = null,
    string? SuggestedAction = null,
    Guid? OperationId = null,
    MemDiagnosticResource? Resource = null,
    IReadOnlyDictionary<string, string?>? DiagnosticDetails = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
