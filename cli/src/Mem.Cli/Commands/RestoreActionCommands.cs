using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Models;
using Mem.Cli.Output;

namespace Mem.Cli.Commands;

/// <summary>
/// Catalog-backed Restore Workspace actions. This class deliberately keeps
/// mutation parsing and acknowledgement checks separate from the read-only
/// RestoreCommands surface.
/// </summary>
public static class RestoreActionCommands
{
    private const string RecreateAcknowledgementUsage =
        "--execute-production-recreate " +
        "--acknowledge-creates-real-stack " +
        "--acknowledge-mutates-production-postgres " +
        "--acknowledge-mutates-npm-routes " +
        "--acknowledge-no-automatic-rollback";

    public static Task<int> RunAsync(
        string[] args,
        string subcommand,
        CliOptions options,
        HostAgentClient hostAgentClient) =>
        subcommand switch
        {
            "create" => CreateAsync(args, options, hostAgentClient),
            "private-test" => PrivateTestAsync(args, options, hostAgentClient),
            "recreate" => RecreateAsync(args, options, hostAgentClient),
            "cancel" => CancelAsync(args, options, hostAgentClient),
            "handover" => HandoverAsync(args, options, hostAgentClient),
            _ => Task.FromResult(PrintUsageAndReturn())
        };

    private static async Task<int> CreateAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var catalogEntryId = GetPositional(args, 2);
        if (string.IsNullOrWhiteSpace(catalogEntryId))
        {
            Console.Error.WriteLine("Missing catalog entry ID.");
            Console.Error.WriteLine();
            PrintCreateUsage();
            return 1;
        }

        if (HasUnexpectedPositional(args, 3))
        {
            Console.Error.WriteLine("Restores create accepts one catalog entry ID.");
            Console.Error.WriteLine();
            PrintCreateUsage();
            return 1;
        }

        var result = await hostAgentClient.CreateOrResumeRestoreSessionAsync(
            catalogEntryId);

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            WriteCreate(result);
        }

        return IsOk(result.Status) && !string.IsNullOrWhiteSpace(result.RestoreSessionId)
            ? 0
            : 2;
    }

    private static async Task<int> PrivateTestAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var action = GetPositional(args, 2);
        if (string.Equals(action, "destroy", StringComparison.OrdinalIgnoreCase))
        {
            return await DestroyPrivateTestAsync(args, options, hostAgentClient);
        }

        var restoreSessionId = action;
        if (string.IsNullOrWhiteSpace(restoreSessionId))
        {
            Console.Error.WriteLine("Missing restore session ID.");
            Console.Error.WriteLine();
            PrintPrivateTestUsage();
            return 1;
        }

        if (HasUnexpectedPositional(args, 3))
        {
            Console.Error.WriteLine("Restores private-test accepts one restore session ID.");
            Console.Error.WriteLine();
            PrintPrivateTestUsage();
            return 1;
        }

        var result = await hostAgentClient.RunRestorePrivateTestAsync(
            restoreSessionId);

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            WritePrivateTest(result);
        }

        return string.Equals(result.Status, "ready", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 2;
    }

    private static async Task<int> DestroyPrivateTestAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var restoreSessionId = GetPositional(args, 3);
        if (string.IsNullOrWhiteSpace(restoreSessionId))
        {
            Console.Error.WriteLine("Missing restore session ID.");
            Console.Error.WriteLine();
            PrintPrivateTestDestroyUsage();
            return 1;
        }

        if (HasUnexpectedPositional(args, 4))
        {
            Console.Error.WriteLine("Private-test destroy accepts one restore session ID.");
            Console.Error.WriteLine();
            PrintPrivateTestDestroyUsage();
            return 1;
        }

        if (!HasFlag(args, "--yes"))
        {
            Console.Error.WriteLine("Refusing to destroy a retained private test runtime without --yes.");
            Console.Error.WriteLine("This removes only the isolated private staging containers, network, and workspace.");
            Console.Error.WriteLine("It does not alter the Backup Catalog source, production stack, or durable restore evidence.");
            Console.Error.WriteLine();
            PrintPrivateTestDestroyUsage();
            return 1;
        }

        var workspaceResult = await hostAgentClient.GetRestoreWorkspaceAsync(
            restoreSessionId);

        if (!IsOk(workspaceResult.Status) || workspaceResult.Workspace is null)
        {
            return WritePrivateTestDestroy(
                PrivateTestDestroyError(
                    restoreSessionId,
                    stagingId: null,
                    workspaceResult.Detail ?? "The Restore Workspace could not be read before private-test cleanup.",
                    status: workspaceResult.Status),
                options);
        }

        var evidence = FindPrivateTestEvidence(workspaceResult.Workspace);
        if (evidence is null || string.IsNullOrWhiteSpace(evidence.StagingId))
        {
            return WritePrivateTestDestroy(
                PrivateTestDestroyError(
                    restoreSessionId,
                    stagingId: null,
                    "This Restore Workspace has no retained private-test staging runtime to destroy."),
                options);
        }

        if (evidence.StagingRuntimeDestroyed == true)
        {
            return WritePrivateTestDestroy(
                new RestorePrivateTestDestroyCliResult(
                    Source: "control-plane",
                    Status: "already-destroyed",
                    RestoreSessionId: restoreSessionId,
                    StagingId: evidence.StagingId,
                    DestroyedAtUtc: evidence.DestroyedAtUtc,
                    SynapseContainerRemoved: null,
                    PostgresContainerRemoved: null,
                    NetworkRemoved: null,
                    WorkspaceRemoved: null,
                    Warnings: [],
                    Detail: "The private test staging runtime was already destroyed."),
                options);
        }

        if (evidence.RequiresExplicitDestroy != true ||
            evidence.DestroyAvailable != true)
        {
            return WritePrivateTestDestroy(
                PrivateTestDestroyError(
                    restoreSessionId,
                    evidence.StagingId,
                    "The private test staging runtime is not currently available for explicit destruction."),
                options);
        }

        var result = await hostAgentClient.DestroyPrivateTestStagingAsync(
            restoreSessionId,
            evidence.StagingId);

        return WritePrivateTestDestroy(result, options);
    }

    private static Task<int> RecreateAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var action = GetPositional(args, 2);

        return action?.ToLowerInvariant() switch
        {
            "preflight" => RecreatePreflightAsync(args, options, hostAgentClient),
            "execute" => RecreateExecuteAsync(args, options, hostAgentClient),
            _ => Task.FromResult(PrintRecreateUsageAndReturn())
        };
    }

    private static async Task<int> RecreatePreflightAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var restoreSessionId = GetPositional(args, 3);
        if (string.IsNullOrWhiteSpace(restoreSessionId))
        {
            Console.Error.WriteLine("Missing restore session ID.");
            Console.Error.WriteLine();
            PrintRecreatePreflightUsage();
            return 1;
        }

        if (!TryBuildPreflightRequest(args, out var request, out var error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            PrintRecreatePreflightUsage();
            return 1;
        }

        var result = await hostAgentClient.PreflightRestoreStandardRecreateAsync(
            restoreSessionId,
            request);

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            WritePreflight(result);
        }

        return result.CanCreate &&
               string.Equals(result.Status, "ready", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 2;
    }

    private static async Task<int> RecreateExecuteAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var restoreSessionId = GetPositional(args, 3);
        if (string.IsNullOrWhiteSpace(restoreSessionId))
        {
            Console.Error.WriteLine("Missing restore session ID.");
            Console.Error.WriteLine();
            PrintRecreateExecuteUsage();
            return 1;
        }

        if (!TryBuildRecreateRequest(args, out var request, out var error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            PrintRecreateExecuteUsage();
            return 1;
        }

        var result = await hostAgentClient.ExecuteRestoreStandardRecreateAsync(
            restoreSessionId,
            request);

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            WriteRecreate(result);
        }

        return string.Equals(
            result.Status,
            "production_recreate_verified",
            StringComparison.OrdinalIgnoreCase)
            ? 0
            : 2;
    }

    private static async Task<int> CancelAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var restoreSessionId = GetPositional(args, 2);
        if (string.IsNullOrWhiteSpace(restoreSessionId))
        {
            Console.Error.WriteLine("Missing restore session ID.");
            Console.Error.WriteLine();
            PrintCancelUsage();
            return 1;
        }

        if (!HasFlag(args, "--yes"))
        {
            Console.Error.WriteLine("Refusing to cancel a Restore Workspace without --yes.");
            Console.Error.WriteLine("Cancellation releases temporary claims only when no queued or running operation could be left half-mutated.");
            Console.Error.WriteLine("The Backup Catalog source and durable audit history are retained.");
            Console.Error.WriteLine();
            PrintCancelUsage();
            return 1;
        }

        var result = await hostAgentClient.CancelRestoreAsync(
            restoreSessionId);

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            WriteSessionAction("MEM Restore Cancellation", result);
        }

        return IsOk(result.Status) &&
               string.Equals(result.AttemptStatus, "cancelled", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 2;
    }

    private static async Task<int> HandoverAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var action = GetPositional(args, 2);
        var restoreSessionId = GetPositional(args, 3);

        if (!string.Equals(action, "complete", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(restoreSessionId))
        {
            Console.Error.WriteLine("Use handover complete with a restore session ID.");
            Console.Error.WriteLine();
            PrintHandoverUsage();
            return 1;
        }

        if (!HasFlag(args, "--yes"))
        {
            Console.Error.WriteLine("Refusing to complete restore handover without --yes.");
            Console.Error.WriteLine("This marks the Restore Workspace complete after the server's public verification checks. It does not delete the audit record.");
            Console.Error.WriteLine();
            PrintHandoverUsage();
            return 1;
        }

        var result = await hostAgentClient.CompleteRestoreHandoverAsync(
            restoreSessionId);

        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            WriteSessionAction("MEM Restore Handover Completion", result);
        }

        return IsOk(result.Status) &&
               string.Equals(result.AttemptStatus, "completed", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 2;
    }

    private static bool TryBuildPreflightRequest(
        string[] args,
        out RestoreStandardRecreatePreflightCliRequest request,
        out string? error)
    {
        request = null!;

        if (!TryReadRequiredOption(args, "--target-stack", out var targetStackSlug, out error) ||
            !TryReadRequiredOption(args, "--element-host", out var elementHost, out error))
        {
            return false;
        }

        if (!TryReadOptionalOption(args, "--requested-domain-id", out var requestedDomainId, out error) ||
            !TryReadOptionalOption(args, "--matrix-host", out var matrixHost, out error))
        {
            return false;
        }

        request = new RestoreStandardRecreatePreflightCliRequest(
            targetStackSlug!,
            elementHost!,
            requestedDomainId,
            matrixHost);
        return true;
    }

    private static bool TryBuildRecreateRequest(
        string[] args,
        out RestoreStandardRecreateCliRequest request,
        out string? error)
    {
        request = null!;

        if (!TryBuildPreflightRequest(
                args,
                out var preflight,
                out error))
        {
            return false;
        }

        if (!HasFlag(args, "--execute-production-recreate"))
        {
            error = "Standard Recreate requires --execute-production-recreate.";
            return false;
        }

        var missingAcknowledgements = new[]
        {
            "--acknowledge-creates-real-stack",
            "--acknowledge-mutates-production-postgres",
            "--acknowledge-mutates-npm-routes",
            "--acknowledge-no-automatic-rollback"
        }
        .Where(flag => !HasFlag(args, flag))
        .ToArray();

        if (missingAcknowledgements.Length > 0)
        {
            error = "Standard Recreate requires explicit acknowledgement flags: " +
                    string.Join(", ", missingAcknowledgements) + ".";
            return false;
        }

        if (!TryReadOptionalOption(args, "--matrix-image", out var matrixImage, out error) ||
            !TryReadOptionalOption(args, "--element-image", out var elementImage, out error) ||
            !TryReadOptionalOption(args, "--operator", out var operatorName, out error) ||
            !TryReadOptionalOption(args, "--note", out var note, out error))
        {
            return false;
        }

        request = new RestoreStandardRecreateCliRequest(
            preflight.TargetStackSlug,
            preflight.ElementHost,
            preflight.RequestedDomainId,
            preflight.MatrixHost,
            matrixImage,
            elementImage,
            operatorName,
            note,
            ExecuteProductionRecreate: true,
            AcknowledgeCreatesRealStack: true,
            AcknowledgeMutatesProductionPostgres: true,
            AcknowledgeMutatesNpmRoutes: true,
            AcknowledgeNoAutomaticRollback: true);
        return true;
    }

    private static bool TryReadRequiredOption(
        string[] args,
        string option,
        out string? value,
        out string? error)
    {
        if (!TryReadOptionalOption(args, option, out value, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{option} is required.";
            return false;
        }

        return true;
    }

    private static bool TryReadOptionalOption(
        string[] args,
        string option,
        out string? value,
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

        if (index == args.Length - 1 ||
            args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            error = $"{option} requires a value.";
            return false;
        }

        var candidate = args[index + 1].Trim();
        if (candidate.Length == 0)
        {
            error = $"{option} requires a non-empty value.";
            return false;
        }

        value = candidate;
        return true;
    }

    private static RestoreWorkspacePrivateTestEvidence? FindPrivateTestEvidence(
        RestoreWorkspaceResponse workspace) =>
        workspace.StandardStages
            .FirstOrDefault(stage =>
                string.Equals(
                    stage.Code,
                    "private-test",
                    StringComparison.OrdinalIgnoreCase))
            ?.PrivateTestEvidence;

    private static RestorePrivateTestDestroyCliResult PrivateTestDestroyError(
        string restoreSessionId,
        string? stagingId,
        string detail,
        string status = "error") =>
        new(
            Source: "control-plane",
            Status: status,
            RestoreSessionId: restoreSessionId,
            StagingId: stagingId,
            DestroyedAtUtc: null,
            SynapseContainerRemoved: null,
            PostgresContainerRemoved: null,
            NetworkRemoved: null,
            WorkspaceRemoved: null,
            Warnings: [],
            Detail: detail);

    private static int WritePrivateTestDestroy(
        RestorePrivateTestDestroyCliResult result,
        CliOptions options)
    {
        if (options.Json)
        {
            CliOutput.Create(options).WriteJson(result);
        }
        else
        {
            Console.WriteLine("MEM Restore Private Test Cleanup");
            Console.WriteLine();
            Console.WriteLine($"Source:             {result.Source}");
            Console.WriteLine($"Status:             {result.Status}");
            Console.WriteLine($"Restore session ID: {result.RestoreSessionId}");
            Console.WriteLine($"Staging ID:         {result.StagingId ?? "not available"}");
            Console.WriteLine($"Destroyed UTC:      {FormatDate(result.DestroyedAtUtc)}");
            Console.WriteLine($"Synapse removed:    {YesNo(result.SynapseContainerRemoved)}");
            Console.WriteLine($"Postgres removed:   {YesNo(result.PostgresContainerRemoved)}");
            Console.WriteLine($"Network removed:    {YesNo(result.NetworkRemoved)}");
            Console.WriteLine($"Workspace removed:  {YesNo(result.WorkspaceRemoved)}");
            WriteLines("Warnings", result.Warnings);
            WriteDetail(result.Detail);
        }

        return result.Status is "destroyed" or "already-destroyed"
            ? 0
            : 2;
    }

    private static void WriteCreate(CatalogRestoreSessionCliResult result)
    {
        Console.WriteLine("MEM Restore Workspace");
        Console.WriteLine();
        Console.WriteLine($"Source:          {result.Source}");
        Console.WriteLine($"Status:          {result.Status}");
        Console.WriteLine($"Catalog entry ID:  {result.CatalogEntryId ?? "not available"}");
        Console.WriteLine($"Restore session ID: {result.RestoreSessionId ?? "not available"}");
        Console.WriteLine($"Workspace created: {YesNo(result.RestoreAttemptCreated)}");
        Console.WriteLine($"Workspace resumed: {YesNo(result.RestoreAttemptResumed)}");
        Console.WriteLine($"Payload state:   {result.PayloadState ?? "not reported"}");
        Console.WriteLine($"Integrity:       {result.IntegrityStatus ?? "not reported"}");
        Console.WriteLine($"Warnings:        {result.WarningCount}");
        WriteDetail(result.Detail);

        if (IsOk(result.Status) && !string.IsNullOrWhiteSpace(result.RestoreSessionId))
        {
            Console.WriteLine();
            Console.WriteLine($"Inspect this workspace: mem restores inspect {result.RestoreSessionId}");
        }
    }

    private static void WritePrivateTest(RestorePrivateTestCliResult result)
    {
        Console.WriteLine("MEM Restore Private Test");
        Console.WriteLine();
        Console.WriteLine($"Source:             {result.Source}");
        Console.WriteLine($"Status:             {result.Status}");
        Console.WriteLine($"Restore session ID: {result.RestoreSessionId ?? "not available"}");
        Console.WriteLine($"Operation ID:       {result.OperationId?.ToString() ?? "not available"}");
        Console.WriteLine($"Catalog entry ID:   {result.CatalogEntryId ?? "not available"}");
        Console.WriteLine($"Staging ID:         {result.StagingId ?? "not recorded"}");
        Console.WriteLine($"Private only:       {YesNo(result.PrivateOnly)}");
        Console.WriteLine($"Database imported:  {YesNo(result.DatabaseImportSucceeded)}");
        Console.WriteLine($"Synapse healthy:    {YesNo(result.SynapseHealthPassed)}");
        Console.WriteLine($"Explicit destroy:   {YesNo(result.RequiresExplicitDestroy)}");
        WriteDetail(result.Detail);

        if (string.Equals(result.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(result.RestoreSessionId))
        {
            Console.WriteLine();
            Console.WriteLine($"Inspect private-test evidence: mem restores evidence {result.RestoreSessionId}");
        }
    }

    private static void WritePreflight(RestoreStandardRecreatePreflightCliResult result)
    {
        Console.WriteLine("MEM Restore Standard Recreate Preflight");
        Console.WriteLine();
        Console.WriteLine($"Source:             {result.Source}");
        Console.WriteLine($"Status:             {result.Status}");
        Console.WriteLine($"Restore session ID: {result.RestoreSessionId ?? "not available"}");
        Console.WriteLine($"Checked UTC:        {FormatDate(result.CheckedAtUtc)}");
        Console.WriteLine($"Can create:         {YesNo(result.CanCreate)}");

        if (result.Targets is not null)
        {
            Console.WriteLine();
            Console.WriteLine("Targets");
            Console.WriteLine($"  Stack:        {result.Targets.TargetStackSlug ?? "not selected"}");
            Console.WriteLine($"  Matrix host:  {result.Targets.MatrixHost ?? "not selected"}");
            Console.WriteLine($"  Element host: {result.Targets.ElementHost ?? "not selected"}");
        }

        WritePreflightChecks(result.Checks);
        WriteLines("Blockers", result.Blockers);
        WriteLines("Warnings", result.Warnings);
        WriteDetail(result.Detail);
    }

    private static void WriteRecreate(RestoreStandardRecreateCliResult result)
    {
        Console.WriteLine("MEM Restore Standard Recreate");
        Console.WriteLine();
        Console.WriteLine($"Source:             {result.Source}");
        Console.WriteLine($"Status:             {result.Status}");
        Console.WriteLine($"Restore session ID: {result.RestoreSessionId ?? "not available"}");
        Console.WriteLine($"Catalog entry ID:   {result.CatalogEntryId ?? "not available"}");
        Console.WriteLine($"Recreate ID:        {result.RecreateId ?? "not available"}");
        Console.WriteLine($"Started UTC:        {FormatDate(result.StartedAtUtc)}");
        Console.WriteLine($"Finished UTC:       {FormatDate(result.FinishedAtUtc)}");
        Console.WriteLine($"Operator:           {result.Operator ?? "not recorded"}");
        Console.WriteLine($"Note:               {result.Note ?? "not recorded"}");

        Console.WriteLine();
        Console.WriteLine("Target");
        Console.WriteLine($"  Stack:        {result.TargetStackSlug ?? "not recorded"}");
        Console.WriteLine($"  Matrix host:  {result.MatrixHost ?? "not recorded"}");
        Console.WriteLine($"  Element host: {result.ElementHost ?? "not recorded"}");

        if (result.Database is not null)
        {
            Console.WriteLine();
            Console.WriteLine("Database evidence");
            Console.WriteLine($"  Provisioned:      {YesNo(result.Database.Provisioned)}");
            Console.WriteLine($"  Import succeeded: {YesNo(result.Database.ImportSucceeded)}");
            Console.WriteLine($"  Public tables:    {result.Database.PublicTableCount}");
            Console.WriteLine($"  Synapse tables:   {result.Database.SynapseKnownTableCount}");
            Console.WriteLine($"  Users:            {FormatCount(result.Database.UsersCount)}");
            Console.WriteLine($"  Events:           {FormatCount(result.Database.EventsCount)}");
            Console.WriteLine($"  Rooms:            {FormatCount(result.Database.RoomsCount)}");
            Console.WriteLine($"  State events:     {FormatCount(result.Database.StateEventsCount)}");
        }

        if (result.Runtime is not null)
        {
            Console.WriteLine();
            Console.WriteLine("Runtime evidence");
            Console.WriteLine($"  Matrix started:  {YesNo(result.Runtime.MatrixStarted)}");
            Console.WriteLine($"  Matrix healthy:  {YesNo(result.Runtime.MatrixHealthPassed)}");
            Console.WriteLine($"  Element started: {YesNo(result.Runtime.ElementStarted)}");
            Console.WriteLine($"  Element healthy: {YesNo(result.Runtime.ElementHealthPassed)}");
            Console.WriteLine($"  Stack registered:{YesNo(result.Runtime.StackRegistered)}");
        }

        if (result.Routes is not null)
        {
            Console.WriteLine();
            Console.WriteLine("Public routes");
            Console.WriteLine($"  Matrix host:      {result.Routes.MatrixPublicHost ?? "not recorded"}");
            Console.WriteLine($"  Matrix route:     {YesNo(result.Routes.MatrixRouteReady)}");
            Console.WriteLine($"  Element host:     {result.Routes.ElementPublicHost ?? "not recorded"}");
            Console.WriteLine($"  Element route:    {YesNo(result.Routes.ElementRouteReady)}");
            Console.WriteLine($"  Public readiness: {YesNo(result.Routes.PublicReadinessPassed)}");
        }

        if (result.Mutations is not null)
        {
            Console.WriteLine();
            Console.WriteLine("Recorded mutations");
            Console.WriteLine($"  Runtime stack created: {YesNo(result.Mutations.RuntimeStackCreated)}");
            Console.WriteLine($"  Production Postgres:   {YesNo(result.Mutations.ProductionPostgresMutated)}");
            Console.WriteLine($"  Production containers: {YesNo(result.Mutations.ProductionContainersTouched)}");
            Console.WriteLine($"  NPM routes:            {YesNo(result.Mutations.NpmRoutesChanged)}");
            Console.WriteLine($"  DNS changed:           {YesNo(result.Mutations.DnsChanged)}");
            Console.WriteLine($"  Certificates changed:  {YesNo(result.Mutations.CertificatesChanged)}");
            Console.WriteLine($"  Old stacks deleted:    {YesNo(result.Mutations.OldStacksDeleted)}");
        }

        if (result.Checks.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Checks");
            foreach (var check in result.Checks)
            {
                var state = check.Passed ? "PASS" : "FAIL";
                Console.WriteLine($"  [{state}] {check.Code} ({check.Severity}): {check.Message}");
            }
        }

        WriteLines("Warnings", result.Warnings);
        WriteLines("Errors", result.Errors);
        WriteDetail(result.Detail);
    }

    private static void WriteSessionAction(
        string title,
        RestoreSessionActionCliResult result)
    {
        Console.WriteLine(title);
        Console.WriteLine();
        Console.WriteLine($"Source:             {result.Source}");
        Console.WriteLine($"Status:             {result.Status}");
        Console.WriteLine($"Action:             {result.Action}");
        Console.WriteLine($"Restore session ID: {result.RestoreSessionId ?? "not available"}");
        Console.WriteLine($"Attempt status:     {result.AttemptStatus ?? "not reported"}");
        Console.WriteLine($"Current stage:      {result.CurrentStage ?? "not reported"}");
        Console.WriteLine($"Updated UTC:        {FormatDate(result.UpdatedAtUtc)}");
        Console.WriteLine($"Terminal UTC:       {FormatDate(result.TerminalAtUtc)}");
        Console.WriteLine($"Warnings:           {result.WarningCount}");
        Console.WriteLine($"Errors:             {result.ErrorCount}");

        if (!string.IsNullOrWhiteSpace(result.LastErrorSummary))
        {
            Console.WriteLine($"Last error:         {result.LastErrorSummary}");
        }

        WriteDetail(result.Detail);
    }

    private static void WritePreflightChecks(
        IReadOnlyList<RestoreStandardRecreatePreflightCheck> checks)
    {
        if (checks.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Checks");
        foreach (var check in checks)
        {
            Console.WriteLine($"  [{check.State}] {check.Title} ({check.Code})");
            Console.WriteLine($"    {check.Message}");
        }
    }

    private static void WriteLines(
        string title,
        IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine(title);
        foreach (var value in values)
        {
            Console.WriteLine($"  - {value}");
        }
    }

    private static string? GetPositional(
        string[] args,
        int index) =>
        args.Length > index && !args[index].StartsWith("--", StringComparison.Ordinal)
            ? args[index]
            : null;

    private static bool HasUnexpectedPositional(
        string[] args,
        int expectedEndExclusive)
    {
        for (var index = expectedEndExclusive; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) &&
                (index == 0 || !args[index - 1].StartsWith("--", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasFlag(
        IEnumerable<string> args,
        string flag) =>
        args.Any(argument => string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase));

    private static bool IsOk(string status) =>
        string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase);

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string YesNo(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        _ => "not reported"
    };

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "not recorded";

    private static string FormatCount(long? value) =>
        value?.ToString() ?? "not reported";

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

    private static int PrintRecreateUsageAndReturn()
    {
        PrintRecreateUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Unknown restore action.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        PrintActionUsageLines();
    }

    private static void PrintCreateUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem restores create <catalog-entry-id> [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintPrivateTestUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem restores private-test <restore-session-id> [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores private-test destroy <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintPrivateTestDestroyUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem restores private-test destroy <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintRecreateUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem restores recreate preflight <restore-session-id> --target-stack <slug> --element-host <host> [--matrix-host <host>] [--requested-domain-id <id>] [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine($"  mem restores recreate execute <restore-session-id> --target-stack <slug> --element-host <host> [--matrix-host <host>] [--requested-domain-id <id>] [--matrix-image <image>] [--element-image <image>] [--operator <name>] [--note <text>] {RecreateAcknowledgementUsage} [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintRecreatePreflightUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem restores recreate preflight <restore-session-id> --target-stack <slug> --element-host <host> [--matrix-host <host>] [--requested-domain-id <id>] [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintRecreateExecuteUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine($"  mem restores recreate execute <restore-session-id> --target-stack <slug> --element-host <host> [--matrix-host <host>] [--requested-domain-id <id>] [--matrix-image <image>] [--element-image <image>] [--operator <name>] [--note <text>] {RecreateAcknowledgementUsage} [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintCancelUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem restores cancel <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintHandoverUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  mem restores handover complete <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintActionUsageLines()
    {
        Console.Error.WriteLine("  mem restores create <catalog-entry-id> [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores private-test <restore-session-id> [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores private-test destroy <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores recreate preflight <restore-session-id> --target-stack <slug> --element-host <host> [--matrix-host <host>] [--requested-domain-id <id>] [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine($"  mem restores recreate execute <restore-session-id> --target-stack <slug> --element-host <host> [--matrix-host <host>] [--requested-domain-id <id>] [--matrix-image <image>] [--element-image <image>] [--operator <name>] [--note <text>] {RecreateAcknowledgementUsage} [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores cancel <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]");
        Console.Error.WriteLine("  mem restores handover complete <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]");
    }
}
