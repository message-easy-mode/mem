using Shared.Diagnostics;

namespace Shared.Exceptions;

/// <summary>
/// Represents a deliberate, operator-safe API problem. The title and detail are
/// contracts intended for the browser; technical exception data belongs in the
/// diagnostic event written by the global request boundary.
/// </summary>
public sealed class MemProblemException : Exception
{
    public MemProblemException(
        int statusCode,
        string code,
        string title,
        string safeDetail,
        bool createIncident = false,
        bool retryable = false,
        string? suggestedAction = null,
        string? feature = null,
        string? stage = null,
        Guid? operationId = null,
        MemDiagnosticResource? resource = null,
        IReadOnlyDictionary<string, string?>? diagnosticDetails = null,
        Exception? innerException = null)
        : base(SanitizeText(safeDetail, 1500, nameof(safeDetail)), innerException)
    {
        if (statusCode is < 400 or > 599)
        {
            throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                "A MEM problem status code must be between 400 and 599.");
        }

        StatusCode = statusCode;
        Code = SanitizeCode(code);
        Title = SanitizeText(title, 200, nameof(title));
        SafeDetail = Message;
        CreateIncident = createIncident;
        Retryable = retryable;
        SuggestedAction = SanitizeOptionalText(suggestedAction, 1000);
        Feature = SanitizeOptionalIdentifier(feature, 100);
        Stage = SanitizeOptionalIdentifier(stage, 120);
        OperationId = operationId;
        Resource = resource;
        DiagnosticDetails = diagnosticDetails;
    }

    public int StatusCode { get; }

    public string Code { get; }

    public string Title { get; }

    public string SafeDetail { get; }

    public bool CreateIncident { get; }

    public bool Retryable { get; }

    public string? SuggestedAction { get; }

    public string? Feature { get; }

    public string? Stage { get; }

    public Guid? OperationId { get; }

    public MemDiagnosticResource? Resource { get; }

    public IReadOnlyDictionary<string, string?>? DiagnosticDetails { get; }

    private static string SanitizeCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", nameof(value));
        }

        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-')
            .Take(160)
            .ToArray());

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("The problem code contains no valid characters.", nameof(value));
        }

        return normalized;
    }

    private static string SanitizeText(
        string value,
        int maximumCharacters,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        var sanitized = new string(value
            .Where(character => !char.IsControl(character) || character is '\t' or '\n')
            .Take(maximumCharacters)
            .ToArray())
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return sanitized;
    }

    private static string? SanitizeOptionalText(string? value, int maximumCharacters) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : SanitizeText(value, maximumCharacters, nameof(value));

    private static string? SanitizeOptionalIdentifier(string? value, int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value
            .Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-' or ':')
            .Take(maximumCharacters)
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
