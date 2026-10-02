using HostAgent.Runtime.Diagnostics;

namespace HostAgent.Tests.Runtime.Diagnostics;

public sealed class MemDiagnosticExceptionProjectorTests
{
    [Fact]
    public void Exception_projection_bounds_depth_messages_and_total_stack_text()
    {
        var options = new MemDiagnosticsOptions
        {
            MaximumExceptionDepth = 2,
            MaximumDetailCharacters = 120,
            MaximumStackTraceCharacters = 180
        };
        var redactor = new MemDiagnosticRedactor(options);
        var projector = new MemDiagnosticExceptionProjector(options, redactor);
        var exception = new SyntheticException(
            new string('m', 500),
            "   at Demo.Run() in /home/operator/Code/MEM/Secret/Runner.cs:line 42\n" +
            new string('s', 500),
            new SyntheticException(
                "password=hunter2",
                "   at Inner.Run() in C:\\Code\\MEM\\Inner.cs:line 12",
                new InvalidOperationException("third level")));

        var result = projector.Project(exception);

        Assert.NotNull(result.Exception);
        Assert.True(result.Exception!.Message.Length <= 120);
        Assert.DoesNotContain("/home/operator", result.Exception.StackTrace ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("Runner.cs:line 42", result.Exception.StackTrace ?? string.Empty, StringComparison.Ordinal);
        var inner = Assert.Single(result.Exception.InnerExceptions);
        Assert.DoesNotContain("hunter2", inner.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Code", inner.StackTrace ?? string.Empty, StringComparison.Ordinal);
        Assert.Empty(inner.InnerExceptions);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Aggregate_exception_is_bounded_without_serializing_exception_data()
    {
        var options = new MemDiagnosticsOptions
        {
            MaximumExceptionDepth = 3
        };
        var redactor = new MemDiagnosticRedactor(options);
        var projector = new MemDiagnosticExceptionProjector(options, redactor);
        var exception = new AggregateException(
            new InvalidOperationException("first"),
            new InvalidOperationException("second"));
        exception.Data["password"] = "must-not-appear";

        var result = projector.Project(exception);
        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Exception);

        Assert.NotNull(result.Exception);
        Assert.Equal(2, result.Exception!.InnerExceptions.Count);
        Assert.DoesNotContain("must-not-appear", serialized, StringComparison.Ordinal);
    }

    private sealed class SyntheticException(
        string message,
        string stackTrace,
        Exception? innerException = null) : Exception(message, innerException)
    {
        public override string StackTrace => stackTrace;
    }
}
