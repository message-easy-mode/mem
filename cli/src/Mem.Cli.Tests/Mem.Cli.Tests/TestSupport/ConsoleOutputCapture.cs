namespace Mem.Cli.Tests.TestSupport;

[CollectionDefinition("Console output", DisableParallelization = true)]
public sealed class ConsoleOutputCollectionDefinition
{
}

public static class ConsoleOutputCapture
{
    public static async Task<CapturedConsoleOutput> CaptureAsync(
        Func<Task<int>> action)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;

        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();

        try
        {
            Console.SetOut(standardOutput);
            Console.SetError(standardError);

            var exitCode = await action();

            return new CapturedConsoleOutput(
                exitCode,
                standardOutput.ToString(),
                standardError.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}

public sealed record CapturedConsoleOutput(
    int ExitCode,
    string StandardOutput,
    string StandardError);
