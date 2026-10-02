using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Models;
using Mem.Cli.Output;

namespace Mem.Cli.Commands;

/// <summary>
/// Catalog-first Restore Attempt and Restore Workspace commands. Read-only
/// inspection remains here while mutating workspace actions are delegated to
/// RestoreActionCommands so their acknowledgement rules stay explicit.
/// </summary>
public static class RestoreCommands
{
    public static async Task<int> RunAsync(
        string[] args,
        string subcommand,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        return subcommand switch
        {
            "list" => await ListAsync(args, options, hostAgentClient),
            "inspect" => await InspectAsync(args, options, hostAgentClient),
            "evidence" => await EvidenceAsync(args, options, hostAgentClient),
            "logs" => await LogsAsync(args, options, hostAgentClient),
            "support-report" => await SupportReportAsync(args, options, hostAgentClient),
            "create" or "private-test" or "recreate" or "cancel" or "handover" =>
                await RestoreActionCommands.RunAsync(args, subcommand, options, hostAgentClient),
            _ => PrintUsageAndReturn()
        };
    }

    private static async Task<int> ListAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        if (!TryBuildListRequest(args, out var request, out var error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            PrintListUsage();
            return 1;
        }

        var result = await hostAgentClient.GetRestoreAttemptsAsync(request);

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            WriteList(result);
        }

        return IsOk(result.Status) ? 0 : 2;
    }

    private static async Task<int> InspectAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var restoreSessionId = GetPositional(args, 2);
        if (string.IsNullOrWhiteSpace(restoreSessionId))
        {
            Console.Error.WriteLine("Missing restore session ID.");
            Console.Error.WriteLine();
            PrintInspectUsage();
            return 1;
        }

        var result = await hostAgentClient.GetRestoreWorkspaceAsync(restoreSessionId);

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            WriteWorkspace(result);
        }

        return IsOk(result.Status) && result.Workspace is not null ? 0 : 2;
    }

    private static async Task<int> EvidenceAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var restoreSessionId = GetPositional(args, 2);
        if (string.IsNullOrWhiteSpace(restoreSessionId))
        {
            Console.Error.WriteLine("Missing restore session ID.");
            Console.Error.WriteLine();
            PrintEvidenceUsage();
            return 1;
        }

        var workspaceResult = await hostAgentClient.GetRestoreWorkspaceAsync(restoreSessionId);
        var result = ToEvidenceResult(workspaceResult);

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            WriteEvidence(result);
        }

        return IsOk(result.Status) && result.Evidence is not null ? 0 : 2;
    }

    private static async Task<int> LogsAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var restoreSessionId = GetPositional(args, 2);
        if (string.IsNullOrWhiteSpace(restoreSessionId))
        {
            Console.Error.WriteLine("Missing restore session ID.");
            Console.Error.WriteLine();
            PrintLogsUsage();
            return 1;
        }

        if (!TryBuildLogRequest(args, out var request, out var error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            PrintLogsUsage();
            return 1;
        }

        var result = await hostAgentClient.GetRestoreLogsAsync(
            restoreSessionId,
            request);

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            WriteLogs(result);
        }

        return IsOk(result.Status) && result.Logs is not null ? 0 : 2;
    }

    private static async Task<int> SupportReportAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var restoreSessionId = GetPositional(args, 2);
        if (string.IsNullOrWhiteSpace(restoreSessionId))
        {
            Console.Error.WriteLine("Missing restore session ID.");
            Console.Error.WriteLine();
            PrintSupportReportUsage();
            return 1;
        }

        var result = await hostAgentClient.GetRestoreSupportReportAsync(
            restoreSessionId);

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            WriteSupportReport(result);
        }

        return IsOk(result.Status) && result.Report is not null ? 0 : 2;
    }

    private static void WriteList(RestoreAttemptListCliResult result)
    {
        Console.WriteLine("MEM Restore Sessions");
        Console.WriteLine();
        Console.WriteLine($"Source:  {result.Source}");
        Console.WriteLine($"Status:  {result.Status}");

        if (!IsOk(result.Status))
        {
            WriteDetail(result.Detail);
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Summary");
        Console.WriteLine($"  Restore sessions:     {result.Summary.TotalSessions}");
        Console.WriteLine($"  Production recreates: {result.Summary.ProductionRecreateCount}");
        Console.WriteLine($"  Publicly verified:    {result.Summary.PubliclyVerifiedCount}");
        Console.WriteLine($"  Needs action:         {result.Summary.NeedsActionCount}");
        Console.WriteLine();
        Console.WriteLine($"Page: {result.Page}/{Math.Max(result.TotalPages, 1)}  Page size: {result.PageSize}  Total: {result.TotalSessions}");

        if (result.Sessions.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("No restore sessions matched the supplied filters.");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine(
                $"{Pad("RESTORE SESSION ID", 34)} " +
                $"{Pad("SOURCE", 28)} " +
                $"{Pad("TARGET STACK", 20)} " +
                $"{Pad("STATUS", 18)} " +
                $"{Pad("STAGE", 18)} " +
                $"{Pad("PROGRESS", 10)} " +
                $"{Pad("UPDATED", 20)} " +
                "NEXT ACTION");

            foreach (var session in result.Sessions)
            {
                var source = session.SourceDeleted
                    ? "Deleted backup"
                    : session.SourceLabel;
                var progress = session.ProgressPercent.HasValue
                    ? $"{session.ProgressPercent.Value}%"
                    : "not reported";

                Console.WriteLine(
                    $"{Pad(session.RestoreSessionId, 34)} " +
                    $"{Pad(source, 28)} " +
                    $"{Pad(session.TargetStackSlug ?? "not selected", 20)} " +
                    $"{Pad(session.StatusLabel, 18)} " +
                    $"{Pad(session.CurrentStageLabel, 18)} " +
                    $"{Pad(progress, 10)} " +
                    $"{Pad(FormatDate(session.LastUpdatedAtUtc), 20)} " +
                    (string.IsNullOrWhiteSpace(session.NextActionTitle)
                        ? "none"
                        : session.NextActionTitle));
            }
        }

        if (result.TargetStacks.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Target stack choices: {string.Join(", ", result.TargetStacks)}");
        }

        WriteWarnings(result.Warnings);
        WriteDetail(result.Detail);
    }

    private static void WriteWorkspace(RestoreWorkspaceCliResult result)
    {
        Console.WriteLine("MEM Restore Workspace");
        Console.WriteLine();
        Console.WriteLine($"Source: {result.Source}");
        Console.WriteLine($"Status: {result.Status}");

        var workspace = result.Workspace;
        if (!IsOk(result.Status) || workspace is null)
        {
            WriteDetail(result.Detail);
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"Restore session ID: {workspace.RestoreSessionId}");
        Console.WriteLine($"Schema version:     {workspace.SchemaVersion}");

        Console.WriteLine();
        Console.WriteLine("Overall status");
        Console.WriteLine($"  Title:       {workspace.OverallStatus.Title}");
        Console.WriteLine($"  Severity:    {workspace.OverallStatus.Severity}");
        Console.WriteLine($"  Description: {workspace.OverallStatus.Description}");
        if (workspace.OverallStatus.NextAction is not null)
        {
            Console.WriteLine($"  Next action: {workspace.OverallStatus.NextAction.Title} ({workspace.OverallStatus.NextAction.Code})");
            Console.WriteLine($"  Enabled:     {YesNo(workspace.OverallStatus.NextAction.Enabled)}");
        }

        Console.WriteLine();
        Console.WriteLine("Attempt");
        Console.WriteLine($"  Status:      {workspace.Attempt.Status}");
        Console.WriteLine($"  Stage:       {workspace.Attempt.CurrentStage}");
        Console.WriteLine($"  Created UTC: {FormatDate(workspace.Attempt.CreatedAtUtc)}");
        Console.WriteLine($"  Updated UTC: {FormatDate(workspace.Attempt.UpdatedAtUtc)}");
        Console.WriteLine($"  Terminal UTC: {FormatNullableDate(workspace.Attempt.TerminalAtUtc)}");
        Console.WriteLine($"  Warnings:    {workspace.Attempt.WarningCount}");
        Console.WriteLine($"  Errors:      {workspace.Attempt.ErrorCount}");
        if (!string.IsNullOrWhiteSpace(workspace.Attempt.LastErrorSummary))
        {
            Console.WriteLine($"  Last error:  {workspace.Attempt.LastErrorSummary}");
        }

        Console.WriteLine();
        Console.WriteLine("Source backup");
        Console.WriteLine($"  Display name: {SourceDisplayName(workspace.Source.SourceDeleted, workspace.Source.SourceDisplayName)}");
        Console.WriteLine($"  Origin:       {workspace.Source.SourceOriginKind}");
        Console.WriteLine($"  Catalog ID:   {workspace.Source.CatalogEntryId ?? "not available (source deleted)"}");
        Console.WriteLine($"  Source stack: {workspace.Source.StackSlug ?? "not recorded"}");
        Console.WriteLine($"  Matrix host:  {workspace.Source.MatrixHost ?? "not recorded"}");
        Console.WriteLine($"  Element host: {workspace.Source.ElementHost ?? "not recorded"}");
        Console.WriteLine($"  Integrity:    {workspace.Source.ValidationStatus}");
        Console.WriteLine($"  Summary:      {workspace.Source.ValidationSummary}");

        Console.WriteLine();
        Console.WriteLine("Target");
        Console.WriteLine($"  Stack:        {workspace.Target.StackSlug ?? "not selected"}");
        Console.WriteLine($"  Matrix host:  {workspace.Target.MatrixHost ?? "not selected"}");
        Console.WriteLine($"  Element host: {workspace.Target.ElementHost ?? "not selected"}");
        Console.WriteLine($"  Availability: {workspace.Target.Availability}");
        Console.WriteLine($"  Detail:       {workspace.Target.Detail}");

        if (workspace.Target.Claims.Count > 0)
        {
            Console.WriteLine("  Claims:");
            foreach (var claim in workspace.Target.Claims)
            {
                Console.WriteLine($"    - {claim.ResourceType}: {claim.ResourceValue} ({claim.Status})");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Standard stages");
        foreach (var stage in workspace.StandardStages)
        {
            Console.WriteLine($"  [{stage.State}] {stage.Title} ({stage.Code})");
            Console.WriteLine($"    {stage.Summary}");
            if (stage.Blockers.Count > 0)
            {
                foreach (var blocker in stage.Blockers)
                {
                    Console.WriteLine($"    Blocker: {blocker}");
                }
            }
            if (stage.OperationSummary is not null)
            {
                Console.WriteLine($"    Operation: {stage.OperationSummary.Operation} ({stage.OperationSummary.Status})");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Verification");
        Console.WriteLine($"  Status:      {workspace.Verification.Status}");
        Console.WriteLine($"  Has run:     {YesNo(workspace.Verification.HasRun)}");
        Console.WriteLine($"  All passed:  {YesNo(workspace.Verification.AllPassed)}");
        Console.WriteLine($"  Checked UTC: {FormatNullableDate(workspace.Verification.CheckedAtUtc)}");
        Console.WriteLine($"  Summary:     {workspace.Verification.Summary}");
        foreach (var check in workspace.Verification.Checks)
        {
            Console.WriteLine($"    - [{check.Status}] {check.Title} ({check.Code})");
        }

        Console.WriteLine();
        Console.WriteLine("Evidence and logs");
        Console.WriteLine($"  Evidence categories: {workspace.Evidence.Categories.Count}");
        Console.WriteLine($"  Log events:          {workspace.Logs.TotalEvents}");
        Console.WriteLine($"  Log warnings:        {workspace.Logs.WarningCount}");
        Console.WriteLine($"  Log errors:          {workspace.Logs.ErrorCount}");
        Console.WriteLine($"  Support report:      {YesNo(workspace.Logs.SupportReportAvailable)}");

        if (workspace.Cancellation is not null)
        {
            Console.WriteLine();
            Console.WriteLine("Cancellation");
            Console.WriteLine($"  Available: {YesNo(workspace.Cancellation.CanCancel)}");
            Console.WriteLine($"  Summary:   {workspace.Cancellation.Summary}");
            if (!string.IsNullOrWhiteSpace(workspace.Cancellation.ReasonUnavailable))
            {
                Console.WriteLine($"  Reason:    {workspace.Cancellation.ReasonUnavailable}");
            }
        }

        WriteWarnings(workspace.Warnings);
        WriteDetail(result.Detail);
    }

    private static RestoreWorkspaceEvidenceCliResult ToEvidenceResult(
        RestoreWorkspaceCliResult workspaceResult)
    {
        var workspace = workspaceResult.Workspace;
        return new RestoreWorkspaceEvidenceCliResult(
            Source: workspaceResult.Source,
            Status: workspaceResult.Status,
            RestoreSessionId: workspace?.RestoreSessionId,
            WorkspaceSource: workspace?.Source,
            Evidence: workspace?.Evidence,
            Warnings: workspace?.Warnings ?? [],
            Detail: workspaceResult.Detail);
    }

    private static void WriteEvidence(RestoreWorkspaceEvidenceCliResult result)
    {
        Console.WriteLine("MEM Restore Workspace Evidence");
        Console.WriteLine();
        Console.WriteLine($"Source: {result.Source}");
        Console.WriteLine($"Status: {result.Status}");

        if (!IsOk(result.Status) || result.Evidence is null)
        {
            WriteDetail(result.Detail);
            return;
        }

        Console.WriteLine($"Restore session ID: {result.RestoreSessionId}");
        Console.WriteLine($"Source backup:      {SourceDisplayName(result.WorkspaceSource?.SourceDeleted == true, result.WorkspaceSource?.SourceDisplayName)}");
        Console.WriteLine($"Catalog ID:         {result.WorkspaceSource?.CatalogEntryId ?? "not available (source deleted)"}");

        if (result.Evidence.Categories.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("No curated evidence has been recorded yet.");
        }
        else
        {
            foreach (var category in result.Evidence.Categories)
            {
                Console.WriteLine();
                Console.WriteLine($"{category.Title} ({category.Code})");
                Console.WriteLine($"  Status: {category.Status}");
                Console.WriteLine($"  Items:  {category.ItemCount}");
                Console.WriteLine($"  Latest: {FormatNullableDate(category.LatestOccurredAtUtc)}");

                foreach (var item in category.Items)
                {
                    Console.WriteLine($"  - [{item.Status}] {item.Title} ({item.Code})");
                    Console.WriteLine($"    {item.Description}");
                }
            }
        }

        WriteEvidenceHighlight("Latest failure", result.Evidence.LatestFailure);
        WriteEvidenceHighlight("Latest success", result.Evidence.LatestSuccess);
        WriteWarnings(result.Warnings);
        WriteDetail(result.Detail);
    }

    private static void WriteEvidenceHighlight(
        string title,
        RestoreWorkspaceEvidenceItem? item)
    {
        if (item is null)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"{title}: [{item.Status}] {item.Title} ({item.Code})");
        Console.WriteLine($"  Occurred UTC: {FormatDate(item.OccurredAtUtc)}");
        Console.WriteLine($"  {item.Description}");
    }

    private static void WriteLogs(RestoreLogPageCliResult result)
    {
        Console.WriteLine("MEM Restore Workspace Logs");
        Console.WriteLine();
        Console.WriteLine($"Source: {result.Source}");
        Console.WriteLine($"Status: {result.Status}");

        var logs = result.Logs;
        if (!IsOk(result.Status) || logs is null)
        {
            WriteDetail(result.Detail);
            return;
        }

        Console.WriteLine($"Restore session ID: {logs.RestoreSessionId}");
        Console.WriteLine($"Page: {logs.Page}/{Math.Max(logs.TotalPages, 1)}  Page size: {logs.PageSize}  Total: {logs.TotalEvents}");
        Console.WriteLine($"Warnings: {logs.Summary.WarningCount}  Errors: {logs.Summary.ErrorCount}");

        if (logs.Events.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("No structured log events matched the supplied filters.");
        }
        else
        {
            foreach (var log in logs.Events)
            {
                Console.WriteLine();
                Console.WriteLine($"{FormatDate(log.TimestampUtc)} [{log.Severity}] {log.Stage} ({log.EventCode})");
                Console.WriteLine($"  {log.Message}");
            }
        }

        WriteWarnings(logs.Warnings);
        WriteDetail(result.Detail);
    }

    private static void WriteSupportReport(RestoreSupportReportCliResult result)
    {
        Console.WriteLine("MEM Restore Support Report");
        Console.WriteLine();
        Console.WriteLine($"Source: {result.Source}");
        Console.WriteLine($"Status: {result.Status}");

        var report = result.Report;
        if (!IsOk(result.Status) || report is null)
        {
            WriteDetail(result.Detail);
            return;
        }

        Console.WriteLine($"Restore session ID: {report.RestoreSessionId}");
        Console.WriteLine($"Generated UTC:      {FormatDate(report.GeneratedAtUtc)}");
        Console.WriteLine($"MEM version:        {report.MemVersion}");

        Console.WriteLine();
        Console.WriteLine("Attempt");
        Console.WriteLine($"  Status: {report.Attempt.Status}");
        Console.WriteLine($"  Stage:  {report.Attempt.CurrentStage}");
        Console.WriteLine($"  Errors: {report.Attempt.ErrorCount}");
        Console.WriteLine($"  Warnings: {report.Attempt.WarningCount}");

        Console.WriteLine();
        Console.WriteLine("Source backup");
        Console.WriteLine($"  Display name: {SourceDisplayName(report.Source.SourceDeleted, report.Source.SourceDisplayName)}");
        Console.WriteLine($"  Origin:       {report.Source.SourceOriginKind}");
        Console.WriteLine($"  Catalog ID:   {report.Source.CatalogEntryId ?? "not available (source deleted)"}");

        Console.WriteLine();
        Console.WriteLine("Target");
        Console.WriteLine($"  Stack:        {report.Target.TargetStackSlug ?? "not selected"}");
        Console.WriteLine($"  Matrix host:  {report.Target.MatrixHost ?? "not selected"}");
        Console.WriteLine($"  Element host: {report.Target.ElementHost ?? "not selected"}");

        Console.WriteLine();
        Console.WriteLine("Operations");
        if (report.Operations.Count == 0)
        {
            Console.WriteLine("  No operations recorded.");
        }
        else
        {
            foreach (var operation in report.Operations)
            {
                Console.WriteLine($"  - {operation.Operation}: {operation.Status}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Recent events");
        if (report.RecentEvents.Count == 0)
        {
            Console.WriteLine("  No recent events recorded.");
        }
        else
        {
            foreach (var log in report.RecentEvents)
            {
                Console.WriteLine($"  - {FormatDate(log.TimestampUtc)} [{log.Severity}] {log.Stage}: {log.Message}");
            }
        }

        WriteWarnings(report.Warnings);
        WriteDetail(result.Detail);
    }

    private static bool TryBuildListRequest(
        string[] args,
        out RestoreAttemptListRequest request,
        out string? error)
    {
        request = new RestoreAttemptListRequest();
        error = null;

        if (!TryGetPositiveIntOption(args, "--page", out var page, out error) ||
            !TryGetPositiveIntOption(args, "--page-size", out var pageSize, out error))
        {
            return false;
        }

        var sortDirection = ReadFilter(args, "--sort-direction");
        if (!string.IsNullOrWhiteSpace(sortDirection) &&
            !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase))
        {
            error = "--sort-direction must be asc or desc.";
            return false;
        }

        request = new RestoreAttemptListRequest(
            Page: page,
            PageSize: pageSize,
            Search: ReadFilter(args, "--search"),
            Status: ReadFilter(args, "--status"),
            TargetStack: ReadFilter(args, "--target-stack"),
            SortBy: ReadFilter(args, "--sort-by"),
            SortDirection: sortDirection?.ToLowerInvariant());

        return true;
    }

    private static bool TryBuildLogRequest(
        string[] args,
        out RestoreLogListRequest request,
        out string? error)
    {
        request = new RestoreLogListRequest();
        error = null;

        if (!TryGetPositiveIntOption(args, "--page", out var page, out error) ||
            !TryGetPositiveIntOption(args, "--page-size", out var pageSize, out error))
        {
            return false;
        }

        request = new RestoreLogListRequest(
            Page: page,
            PageSize: pageSize,
            Severity: ReadFilter(args, "--severity"),
            Stage: ReadFilter(args, "--stage"),
            Search: ReadFilter(args, "--search"));

        return true;
    }

    private static bool TryGetPositiveIntOption(
        string[] args,
        string option,
        out int? value,
        out string? error)
    {
        value = null;
        error = null;

        var index = Array.FindIndex(
            args,
            argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return true;
        }

        if (index == args.Length - 1 || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            error = $"{option} requires a positive integer value.";
            return false;
        }

        if (!int.TryParse(args[index + 1], out var parsed) || parsed <= 0)
        {
            error = $"{option} requires a positive integer value.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static string? ReadFilter(
        string[] args,
        string option)
    {
        var value = CliOptions.GetOptionValue(args, option);
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? GetPositional(
        string[] args,
        int index)
    {
        return args.Length > index && !args[index].StartsWith("--", StringComparison.Ordinal)
            ? args[index]
            : null;
    }

    private static bool IsOk(string status) =>
        string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase);


    private static string SourceDisplayName(
        bool sourceDeleted,
        string? sourceDisplayName)
    {
        if (sourceDeleted)
        {
            return "Deleted backup";
        }

        return string.IsNullOrWhiteSpace(sourceDisplayName)
            ? "not recorded"
            : sourceDisplayName;
    }

    private static string YesNo(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        _ => "not reported"
    };

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

    private static string FormatNullableDate(DateTimeOffset? value) =>
        value.HasValue ? FormatDate(value.Value) : "not recorded";

    private static string Pad(string? value, int width)
    {
        var source = value ?? string.Empty;
        var rendered = source.Length <= width
            ? source
            : source[..Math.Max(width - 1, 0)] + "…";

        return rendered.PadRight(width);
    }

    private static void WriteWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Warnings");
        foreach (var warning in warnings)
        {
            Console.WriteLine($"  - {warning}");
        }
    }

    private static void WriteDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"Detail: {detail}");
    }

    private static int PrintUsageAndReturn()
    {
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Unknown restores command.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem restores list [--page <n>] [--page-size <n>] [--search <text>] [--status <status>] [--target-stack <slug>] [--sort-by <field>] [--sort-direction asc|desc] [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores inspect <restore-session-id> [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores evidence <restore-session-id> [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores logs <restore-session-id> [--page <n>] [--page-size <n>] [--severity <severity>] [--stage <stage>] [--search <text>] [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores support-report <restore-session-id> [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores create <catalog-entry-id> [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores private-test <restore-session-id> [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores private-test destroy <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores recreate preflight <restore-session-id> --target-stack <slug> --element-host <host> [--matrix-host <host>] [--requested-domain-id <id>] [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores recreate execute <restore-session-id> --target-stack <slug> --element-host <host> --execute-production-recreate --acknowledge-creates-real-stack --acknowledge-mutates-production-postgres --acknowledge-mutates-npm-routes --acknowledge-no-automatic-rollback [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores cancel <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores handover complete <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintListUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem restores list [--page <n>] [--page-size <n>] [--search <text>] [--status <status>] [--target-stack <slug>] [--sort-by <field>] [--sort-direction asc|desc] [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintInspectUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem restores inspect <restore-session-id> [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintEvidenceUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem restores evidence <restore-session-id> [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintLogsUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem restores logs <restore-session-id> [--page <n>] [--page-size <n>] [--severity <severity>] [--stage <stage>] [--search <text>] [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintSupportReportUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem restores support-report <restore-session-id> [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores create <catalog-entry-id> [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores private-test <restore-session-id> [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores private-test destroy <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores recreate preflight <restore-session-id> --target-stack <slug> --element-host <host> [--matrix-host <host>] [--requested-domain-id <id>] [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores recreate execute <restore-session-id> --target-stack <slug> --element-host <host> --execute-production-recreate --acknowledge-creates-real-stack --acknowledge-mutates-production-postgres --acknowledge-mutates-npm-routes --acknowledge-no-automatic-rollback [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores cancel <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores handover complete <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]");
    }
}
