namespace Mem.Migrate.Application.Assessment;

public sealed class SourcePreflightService
{
    private readonly ISourceAssessmentApplicationService _assessments;
    private readonly string _workspaceRoot;
    private readonly string _artifactRoot;
    private readonly string _dockerCommand;
    private readonly string _ageCommand;
    private readonly Func<string, string?> _resolveExecutable;
    private readonly Func<bool> _isSupportedOperatingSystem;

    public SourcePreflightService(
        ISourceAssessmentApplicationService assessments,
        string workspaceRoot,
        string artifactRoot,
        string dockerCommand = "docker",
        string ageCommand = "age",
        Func<string, string?>? resolveExecutable = null,
        Func<bool>? isSupportedOperatingSystem = null)
    {
        _assessments = assessments;
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _artifactRoot = Path.GetFullPath(artifactRoot);
        _dockerCommand = dockerCommand;
        _ageCommand = ageCommand;
        _resolveExecutable = resolveExecutable ?? ResolveExecutable;
        _isSupportedOperatingSystem = isSupportedOperatingSystem ?? (() => OperatingSystem.IsLinux());
    }

    public async Task<SourcePreflightResult> RunAsync(
        bool sourceOperationRunning,
        CancellationToken cancellationToken)
    {
        var checks = new List<SourcePreflightCheck>();

        checks.Add(_isSupportedOperatingSystem()
            ? new SourcePreflightCheck("operating-system", "ready", "A supported Linux source host was detected.")
            : new SourcePreflightCheck("operating-system", "blocked", "The Source Assistant requires a supported Linux source host."));

        checks.Add(_resolveExecutable(_dockerCommand) is { } dockerPath
            ? new SourcePreflightCheck("docker-command", "ready", "The Docker command is available.", Path.GetFileName(dockerPath))
            : new SourcePreflightCheck("docker-command", "blocked", "The Docker command could not be found.", "Install Docker or place the docker command on PATH."));

        checks.Add(CheckPrivateRoot("workspace-root", "Private workspace", _workspaceRoot));
        checks.Add(CheckPrivateRoot("artifact-root", "Private artifact", _artifactRoot));

        checks.Add(_resolveExecutable(_ageCommand) is not null
            ? new SourcePreflightCheck("age-command", "ready", "The age encryption command is available.")
            : new SourcePreflightCheck("age-command", "warning", "The age command is not available yet.", "Assessment can run, but package encryption in MM-WEB-01C will require age."));

        if (sourceOperationRunning)
        {
            checks.Add(new SourcePreflightCheck("source-operation", "running", "A protected source operation is currently running.", "Wait for the active assessment, capture, or packaging operation to finish."));
        }
        else
        {
            var latest = await _assessments.GetLatestAsync(cancellationToken);
            checks.Add(latest is null
                ? new SourcePreflightCheck("source-assessment", "not-run", "No source assessment has been recorded yet.")
                : new SourcePreflightCheck("source-assessment", "ready", $"The latest source assessment completed at {latest.CompletedAtUtc:O}."));
        }

        var blocked = checks.Any(check => string.Equals(check.Status, "blocked", StringComparison.Ordinal));
        var warning = checks.Any(check => check.Status is "warning" or "configured" or "not-run" or "running");

        return new SourcePreflightResult(
            SchemaVersion: 1,
            Status: blocked ? "blocked" : warning ? "ready-with-warnings" : "ready",
            CanRunAssessment: !blocked && !sourceOperationRunning,
            Checks: checks);
    }

    private static SourcePreflightCheck CheckPrivateRoot(string code, string label, string path)
    {
        if (File.Exists(path))
        {
            return new SourcePreflightCheck(code, "blocked", $"{label} root is occupied by a file.");
        }

        if (Directory.Exists(path))
        {
            return new SourcePreflightCheck(code, "ready", $"{label} root is available.");
        }

        var parent = Directory.GetParent(path);
        if (parent is null || (!parent.Exists && parent.Parent is null))
        {
            return new SourcePreflightCheck(code, "blocked", $"{label} root cannot be created.");
        }

        return new SourcePreflightCheck(code, "configured", $"{label} root will be created when assessment starts.");
    }

    private static string? ResolveExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        if (Path.IsPathRooted(command))
        {
            return File.Exists(command) ? Path.GetFullPath(command) : null;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, command);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
