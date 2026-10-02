namespace Shared.Diagnostics;

/// <summary>
/// Shared safe-text projection used when an existing workflow record must be
/// included in browser or support evidence. The implementation applies the
/// same redaction policy used by MEM diagnostic event storage.
/// </summary>
public interface IMemDiagnosticTextRedactor
{
    MemDiagnosticSafeText Project(
        string? value,
        int maximumCharacters,
        bool sanitizeSourcePaths = false);
}

public sealed record MemDiagnosticSafeText(
    string Value,
    bool RedactionsApplied,
    bool Truncated);
