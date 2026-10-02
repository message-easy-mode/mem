using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Behaviors;

namespace Api.IntegrationTests.Diagnostics;

public sealed class LoggingBehaviorTests
{
    [Fact]
    public async Task Pipeline_failure_does_not_duplicate_full_error_exception_logging()
    {
        var logger = new CapturingLogger<LoggingBehavior<FailingRequest, string>>();
        var behavior = new LoggingBehavior<FailingRequest, string>(logger);
        var expected = new InvalidOperationException("technical failure");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new FailingRequest(),
                () => Task.FromException<string>(expected),
                CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.All(
            logger.Entries,
            entry => Assert.Null(entry.Exception));
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                     entry.Message.Contains(
                         nameof(InvalidOperationException),
                         StringComparison.Ordinal));
    }

    private sealed record FailingRequest : IRequest<string>;

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                exception));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception);
}
