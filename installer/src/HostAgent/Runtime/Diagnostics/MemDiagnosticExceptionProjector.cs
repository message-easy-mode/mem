using Shared.Diagnostics;

namespace HostAgent.Runtime.Diagnostics;

public sealed class MemDiagnosticExceptionProjector
{
    private readonly MemDiagnosticsOptions _options;
    private readonly MemDiagnosticRedactor _redactor;

    public MemDiagnosticExceptionProjector(
        MemDiagnosticsOptions options,
        MemDiagnosticRedactor redactor)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
    }

    internal MemDiagnosticExceptionProjectionResult Project(
        Exception? exception,
        IReadOnlyCollection<string>? exactSecrets = null)
    {
        if (exception is null)
        {
            return new MemDiagnosticExceptionProjectionResult(null, false, false);
        }

        var stackBudget = new StackBudget(_options.MaximumStackTraceCharacters);
        var result = ProjectCore(exception, depth: 0, stackBudget, exactSecrets);
        return new MemDiagnosticExceptionProjectionResult(
            result.Exception,
            result.Redacted,
            result.Truncated);
    }

    private ProjectionNode ProjectCore(
        Exception exception,
        int depth,
        StackBudget stackBudget,
        IReadOnlyCollection<string>? exactSecrets)
    {
        var message = _redactor.RedactText(
            exception.Message,
            _options.MaximumDetailCharacters,
            exactSecrets);
        var stackTrace = _redactor.RedactText(
            exception.StackTrace,
            stackBudget.Remaining,
            exactSecrets,
            sanitizeSourcePaths: true);
        stackBudget.Remaining = Math.Max(0, stackBudget.Remaining - stackTrace.Value.Length);

        var redacted = message.Changed || stackTrace.Changed;
        var truncated = message.Truncated || stackTrace.Truncated;
        var innerExceptions = new List<MemDiagnosticException>();

        if (depth + 1 < _options.MaximumExceptionDepth)
        {
            foreach (var inner in EnumerateImmediateInnerExceptions(exception))
            {
                if (innerExceptions.Count >= _options.MaximumExceptionDepth - depth - 1)
                {
                    truncated = true;
                    break;
                }

                var projected = ProjectCore(inner, depth + 1, stackBudget, exactSecrets);
                innerExceptions.Add(projected.Exception);
                redacted |= projected.Redacted;
                truncated |= projected.Truncated;
            }
        }
        else if (EnumerateImmediateInnerExceptions(exception).Any())
        {
            truncated = true;
        }

        return new ProjectionNode(
            new MemDiagnosticException(
                Type: MemDiagnosticRedactor.NormalizeIdentifier(
                    exception.GetType().FullName ?? exception.GetType().Name,
                    300),
                Message: message.Value,
                StackTrace: string.IsNullOrWhiteSpace(stackTrace.Value)
                    ? null
                    : stackTrace.Value,
                InnerExceptions: innerExceptions),
            redacted,
            truncated);
    }

    private static IEnumerable<Exception> EnumerateImmediateInnerExceptions(
        Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            return aggregate.InnerExceptions;
        }

        return exception.InnerException is null
            ? Array.Empty<Exception>()
            : [exception.InnerException];
    }

    private sealed class StackBudget(int remaining)
    {
        public int Remaining { get; set; } = remaining;
    }

    private sealed record ProjectionNode(
        MemDiagnosticException Exception,
        bool Redacted,
        bool Truncated);
}

internal sealed record MemDiagnosticExceptionProjectionResult(
    MemDiagnosticException? Exception,
    bool Redacted,
    bool Truncated);
