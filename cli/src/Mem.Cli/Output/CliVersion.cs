using System.Reflection;

namespace Mem.Cli.Output;

public static class CliVersion
{
    public const string ProductName = "Message Easy Mode CLI";

    public static string InformationalVersion =>
        typeof(CliVersion)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "dev";

    public static void Write(
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine(ProductName);
        output.WriteLine($"Version: {InformationalVersion}");
        output.WriteLine("Command: mem");
    }
}
