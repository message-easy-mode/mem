using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Models;
using Mem.Cli.Output;

namespace Mem.Cli.Commands;

public static class MaintenanceCommands
{
    private const string RequiredConfirmationText = "DELETE ORPHANED NPM HOSTS";

    public static async Task<int> RunAsync(
        string[] args,
        string subcommand,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var output = CliOutput.Create(options);

        if (subcommand is "reconciliation" or "reconcile")
        {
            return await ReconciliationAsync(options, hostAgentClient, output);
        }

        if (subcommand == "cleanup")
        {
            var cleanupTarget = GetPositional(args, 2);

            if (cleanupTarget is "npm-hosts" or "npm-proxy-hosts")
            {
                return await CleanupNpmHostsAsync(args, options, hostAgentClient, output);
            }
        }

        PrintUsage(output);
        return 1;
    }

    private static async Task<int> ReconciliationAsync(
        CliOptions options,
        HostAgentClient hostAgentClient,
        CliOutput output)
    {
        var result = await hostAgentClient.GetRuntimeReconciliationReportAsync();

        if (options.Json)
        {
            output.WriteJson(result);
        }
        else
        {
            output.WriteHumanLine("Runtime reconciliation");
            output.WriteHumanLine();
            output.WriteHumanLine($"Status:              {result.Status}");
            output.WriteHumanLine($"Checked:             {result.CheckedAtUtc:G}");
            output.WriteHumanLine($"Active stacks:       {result.Summary.ActiveStackCount}");
            output.WriteHumanLine($"Destroyed history:   {result.Summary.DestroyedStackHistoryCount}");
            output.WriteHumanLine($"Active routes:       {result.Summary.ActiveRouteCount}");
            output.WriteHumanLine($"MEM-like NPM hosts:  {result.Summary.MemManagedNpmProxyHostCount}");
            output.WriteHumanLine($"Orphaned NPM hosts:  {result.Summary.OrphanedNpmProxyHostCount}");

            if (!string.IsNullOrWhiteSpace(result.Detail))
            {
                output.WriteHumanLine($"Detail:              {result.Detail}");
            }

            if (result.OrphanedNpmProxyHosts.Count > 0)
            {
                output.WriteHumanLine();
                output.WriteHumanLine("Orphaned NPM proxy hosts:");

                foreach (var host in result.OrphanedNpmProxyHosts)
                {
                    output.WriteHumanLine(
                        $"  {host.ProxyHostId}: {string.Join(", ", host.DomainNames)} -> {host.ForwardHost ?? "unknown"}:{host.ForwardPort?.ToString() ?? "?"}");
                }
            }
        }

        return result.Status is "ok" or "needs_attention" or "degraded" ? 0 : 2;
    }

    private static async Task<int> CleanupNpmHostsAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient,
        CliOutput output)
    {
        var ids = GetRepeatedIntOptionValues(args, "--proxy-host-id");
        var confirmation = CliOptions.GetOptionValue(args, "--confirm");

        if (ids.Count == 0)
        {
            output.WriteHumanErrorLine("Select at least one orphaned NPM proxy host with --proxy-host-id <id>.");
            output.WriteHumanErrorLine("Use: mem maintenance reconciliation");
            return 1;
        }

        if (!string.Equals(confirmation, RequiredConfirmationText, StringComparison.Ordinal))
        {
            output.WriteHumanErrorLine("Refusing to delete orphaned NPM proxy hosts without explicit confirmation.");
            output.WriteHumanErrorLine($"Use: --confirm "{RequiredConfirmationText}"");
            return 1;
        }

        var result = await hostAgentClient.DeleteOrphanedNpmProxyHostsAsync(
            new RuntimeReconciliationNpmProxyHostCleanupRequest(ids, confirmation));

        if (options.Json)
        {
            output.WriteJson(result);
        }
        else
        {
            output.WriteHumanLine("Orphaned NPM proxy host cleanup");
            output.WriteHumanLine();
            output.WriteHumanLine($"Status:          {result.Status}");
            output.WriteHumanLine($"Deleted:         {result.DeletedCount}");
            output.WriteHumanLine($"Already missing: {result.AlreadyMissingCount}");
            output.WriteHumanLine($"Skipped:         {result.SkippedCount}");
            output.WriteHumanLine($"Failed:          {result.FailedCount}");

            if (!string.IsNullOrWhiteSpace(result.Detail))
            {
                output.WriteHumanLine($"Detail:          {result.Detail}");
            }

            foreach (var item in result.Results)
            {
                output.WriteHumanLine(
                    $"  {item.ProxyHostId}: {item.Status} {string.Join(", ", item.DomainNames)}");

                if (!string.IsNullOrWhiteSpace(item.Reason))
                {
                    output.WriteHumanLine($"      {item.Reason}");
                }
            }
        }

        return result.Status is "completed" or "skipped" ? 0 : 2;
    }

    private static IReadOnlyList<int> GetRepeatedIntOptionValues(
        string[] args,
        string optionName)
    {
        var values = new List<int>();

        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out var value) || value <= 0)
            {
                continue;
            }

            values.Add(value);
            i++;
        }

        return values.Distinct().OrderBy(x => x).ToArray();
    }

    private static string? GetPositional(string[] args, int index)
    {
        var positional = args
            .Skip(1)
            .Where(arg => !arg.StartsWith("--", StringComparison.Ordinal))
            .ToArray();

        var offset = index - 1;
        return offset >= 0 && offset < positional.Length
            ? positional[offset]
            : null;
    }

    private static void PrintUsage(CliOutput output)
    {
        output.WriteHumanErrorLine("Unknown maintenance command.");
        output.WriteHumanErrorLine();
        output.WriteHumanErrorLine("Usage:");
        output.WriteHumanErrorLine("  mem maintenance reconciliation [--json] [--profile <name>] [--server <url>]");
        output.WriteHumanErrorLine("  mem maintenance cleanup npm-hosts --proxy-host-id <id> [--proxy-host-id <id>] --confirm "DELETE ORPHANED NPM HOSTS" [--json] [--profile <name>] [--server <url>]");
    }
}
