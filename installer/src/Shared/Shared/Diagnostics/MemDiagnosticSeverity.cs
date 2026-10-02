namespace Shared.Diagnostics;

public static class MemDiagnosticSeverities
{
    public const string Trace = "trace";
    public const string Debug = "debug";
    public const string Information = "information";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Critical = "critical";

    public static bool IsKnown(string? value) => value is
        Trace or Debug or Information or Warning or Error or Critical;

    public static bool RequiresImmediateFlush(string? value) => value is
        Warning or Error or Critical;
}
