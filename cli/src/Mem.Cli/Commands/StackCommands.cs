using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Models;
using Mem.Cli.Output;

namespace Mem.Cli.Commands;

public static class StackCommands
{
    public static async Task<int> RunAsync(
        string[] args,
        string subcommand,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        if (subcommand == "list")
        {
            return await ListAsync(
                options,
                hostAgentClient);
        }

        if (subcommand == "inspect")
        {
            var slugOrId = GetSlugOrId(args);

            if (string.IsNullOrWhiteSpace(slugOrId))
            {
                Console.Error.WriteLine("Missing stack slug or id.");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage:");
                Console.Error.WriteLine("  mem stack inspect <slug-or-id> [--json] [--profile <name>] [--server <url>]");
                return 1;
            }

            return await InspectAsync(
                slugOrId,
                options,
                hostAgentClient);
        }

        if (subcommand == "doctor")
        {
            var slugOrId = GetSlugOrId(args);

            if (string.IsNullOrWhiteSpace(slugOrId))
            {
                Console.Error.WriteLine("Missing stack slug or id.");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage:");
                Console.Error.WriteLine("  mem stack doctor <slug-or-id> [--json] [--profile <name>] [--server <url>]");
                return 1;
            }

            return await DoctorAsync(
                slugOrId,
                options,
                hostAgentClient);
        }

        if (subcommand == "operations")
        {
            var slugOrId = GetSlugOrId(args);

            if (string.IsNullOrWhiteSpace(slugOrId))
            {
                Console.Error.WriteLine("Missing stack slug or id.");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage:");
                Console.Error.WriteLine("  mem stack operations <slug-or-id> [--json] [--profile <name>] [--server <url>]");
                return 1;
            }

            return await OperationsAsync(
                slugOrId,
                options,
                hostAgentClient);
        }

        Console.Error.WriteLine("Unknown stack command.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem stack list [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem stack inspect <slug-or-id> [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem stack doctor <slug-or-id> [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem stack operations <slug-or-id> [--json] [--profile <name>] [--server <url>]");

        return 1;
    }

    private static async Task<int> ListAsync(
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var result = await hostAgentClient.GetRuntimeStackListAsync();

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            Console.WriteLine("MEM Runtime Stacks");
            Console.WriteLine();
            Console.WriteLine($"Source: {result.Source}");
            Console.WriteLine($"Status: {result.Status}");

            if (!string.IsNullOrWhiteSpace(result.Detail))
            {
                Console.WriteLine($"Detail: {result.Detail}");
            }

            Console.WriteLine();

            if (result.Stacks.Count == 0)
            {
                Console.WriteLine("No runtime stacks found.");
            }
            else
            {
                foreach (var stack in result.Stacks)
                {
                    Console.WriteLine($"{stack.Slug}");
                    Console.WriteLine($"  Stack ID:             {stack.StackId}");
                    Console.WriteLine($"  Last verified status: {stack.LastVerifiedStatus}");
                    Console.WriteLine($"  Last verified at UTC: {stack.LastVerifiedAtUtc:O}");
                    Console.WriteLine($"  Matrix URL:           {stack.MatrixPublicBaseUrl ?? "unknown"}");
                    Console.WriteLine($"  Element URL:          {stack.ElementPublicBaseUrl ?? "unknown"}");
                    Console.WriteLine();
                }
            }
        }

        return result.Status is "ok" ? 0 : 2;
    }

    private static async Task<int> InspectAsync(
        string slugOrId,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var result = await hostAgentClient.GetRuntimeStackInspectAsync(
            slugOrId);
        var safeResult = ToCliSafeResult(result);

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(safeResult);
        }
        else
        {
            Console.WriteLine("MEM Stack Inspect");
            Console.WriteLine();
            Console.WriteLine($"Source:   {safeResult.Source}");
            Console.WriteLine($"Status:   {safeResult.Status}");
            Console.WriteLine($"Stack ID: {safeResult.StackId?.ToString() ?? "unknown"}");
            Console.WriteLine($"Slug:     {safeResult.Slug}");

            WritePublicServiceDetails("Matrix", safeResult.Matrix);
            WritePublicServiceDetails("Element", safeResult.Element);

            if (!string.IsNullOrWhiteSpace(safeResult.Detail))
            {
                Console.WriteLine();
                Console.WriteLine($"Detail: {safeResult.Detail}");
            }
        }

        return safeResult.Status is "not_found" or "unauthorized" or "error"
            ? 2
            : 0;
    }

    private static RuntimeStackInspectCliResult ToCliSafeResult(
        RuntimeStackInspectResponse result) =>
        new(
            Source: result.Source,
            Status: result.Status,
            StackId: result.StackId,
            Slug: result.Slug,
            Matrix: ToPublicServiceProjection(result.Matrix),
            Element: ToPublicServiceProjection(result.Element),
            LastVerifiedAtUtc: result.LastVerifiedAtUtc,
            Detail: result.Detail);

    private static RuntimeStackServicePublicCliProjection? ToPublicServiceProjection(
        RuntimeStackServiceInspectResponse? service) =>
        service is null
            ? null
            : new RuntimeStackServicePublicCliProjection(
                ServiceKey: service.ServiceKey,
                PublicHost: service.PublicHost,
                PublicBaseUrl: service.PublicBaseUrl);

    private static void WritePublicServiceDetails(
        string title,
        RuntimeStackServicePublicCliProjection? service)
    {
        if (service is null)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine(title);
        Console.WriteLine($"  Public host:     {service.PublicHost ?? "unknown"}");
        Console.WriteLine($"  Public base URL: {service.PublicBaseUrl ?? "unknown"}");
    }

    private static async Task<int> DoctorAsync(
        string slugOrId,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var result = await hostAgentClient.GetRuntimeStackDoctorAsync(
            slugOrId);

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            Console.WriteLine("MEM Stack Doctor");
            Console.WriteLine();
            Console.WriteLine($"Source:               {result.Source}");
            Console.WriteLine($"Status:               {result.Status}");
            Console.WriteLine($"Stack ID:             {result.StackId?.ToString() ?? "unknown"}");
            Console.WriteLine($"Slug:                 {result.Slug}");
            Console.WriteLine($"Last verified status: {result.LastVerifiedStatus ?? "unknown"}");
            Console.WriteLine($"Last verified at UTC: {result.LastVerifiedAtUtc?.ToString("O") ?? "unknown"}");
            Console.WriteLine($"Checked at UTC:       {result.CheckedAtUtc:O}");
            Console.WriteLine($"All passed:           {result.AllPassed}");

            if (result.OperationId.HasValue)
            {
                Console.WriteLine($"Operation ID:         {result.OperationId}");
            }

            if (result.ReportId.HasValue)
            {
                Console.WriteLine($"Report ID:            {result.ReportId}");
            }

            if (!string.IsNullOrWhiteSpace(result.Detail))
            {
                Console.WriteLine($"Detail:               {result.Detail}");
            }

            Console.WriteLine();

            if (result.Checks.Count == 0)
            {
                Console.WriteLine("No doctor checks returned.");
            }
            else
            {
                foreach (var check in result.Checks)
                {
                    Console.WriteLine($"{(check.Success ? "OK" : "FAIL")} {check.Code}");
                    Console.WriteLine($"  Name:   {check.Name}");
                    Console.WriteLine($"  URL:    {check.Url}");
                    Console.WriteLine($"  Status: {check.StatusCode?.ToString() ?? "n/a"}");

                    if (!string.IsNullOrWhiteSpace(check.Detail))
                    {
                        Console.WriteLine($"  Detail: {check.Detail}");
                    }

                    Console.WriteLine();
                }
            }
        }

        return result.AllPassed ? 0 : 2;
    }

    private static async Task<int> OperationsAsync(
        string slugOrId,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var result = await hostAgentClient.GetRuntimeStackOperationsAsync(
            slugOrId);

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            Console.WriteLine("MEM Stack Operations");
            Console.WriteLine();
            Console.WriteLine($"Source:   {result.Source}");
            Console.WriteLine($"Status:   {result.Status}");
            Console.WriteLine($"Stack ID: {result.StackId?.ToString() ?? "unknown"}");
            Console.WriteLine($"Slug:     {result.Slug}");

            if (!string.IsNullOrWhiteSpace(result.Detail))
            {
                Console.WriteLine($"Detail:   {result.Detail}");
            }

            Console.WriteLine();

            if (result.Operations.Count == 0)
            {
                Console.WriteLine("No operations found.");
            }
            else
            {
                foreach (var operation in result.Operations)
                {
                    Console.WriteLine($"{operation.Operation} [{operation.Status}]");
                    Console.WriteLine($"  ID:                  {operation.Id}");
                    Console.WriteLine($"  Requested by:        {operation.RequestedBy ?? "unknown"}");
                    Console.WriteLine($"  Mutation level:      {operation.HostMutationLevel ?? "unknown"}");
                    Console.WriteLine($"  Current step:        {operation.CurrentStep ?? "unknown"}");
                    Console.WriteLine($"  Idempotency key:     {operation.IdempotencyKey ?? "none"}");
                    Console.WriteLine($"  Requested at UTC:    {operation.RequestedAtUtc:O}");
                    Console.WriteLine($"  Started at UTC:      {operation.StartedAtUtc?.ToString("O") ?? "unknown"}");
                    Console.WriteLine($"  Completed at UTC:    {operation.CompletedAtUtc?.ToString("O") ?? "unknown"}");

                    if (!string.IsNullOrWhiteSpace(operation.LastError))
                    {
                        Console.WriteLine($"  Last error:          {operation.LastError}");
                    }

                    Console.WriteLine();
                }
            }
        }

        return result.Status is "ok" ? 0 : 2;
    }

    private static string? GetSlugOrId(string[] args)
    {
        return args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal)
            ? args[2]
            : null;
    }
}