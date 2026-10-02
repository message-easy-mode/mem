using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Output;

namespace Mem.Cli.Commands;

public static class HostCommands
{
    public static async Task<int> RunAsync(
        string subcommand,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        if (subcommand == "status")
        {
            return await StatusAsync(
                options,
                hostAgentClient);
        }

        Console.Error.WriteLine("Unknown host command.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem host status [--json] [--profile <name>] [--server <url>]");

        return 1;
    }

    private static async Task<int> StatusAsync(
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var result = await hostAgentClient.GetHostStatusAsync();

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            Console.WriteLine("MEM Host Status");
            Console.WriteLine();
            Console.WriteLine($"Source:          {result.Source}");
            Console.WriteLine($"HostAgent:       {result.HostAgent}");
            Console.WriteLine($"Docker:          {(result.DockerReachable ? "reachable" : "not reachable")}");
            Console.WriteLine($"Gateway network: {result.RuntimeNetworkName ?? "unknown"}");
            Console.WriteLine($"NPM:             {(result.NpmReady ? "ready" : "not ready")}");
            Console.WriteLine($"Status:          {result.Status}");

            if (!string.IsNullOrWhiteSpace(result.Detail))
            {
                Console.WriteLine($"Detail:          {result.Detail}");
            }
        }

        return result.Status is "ready" ? 0 : 2;
    }
}