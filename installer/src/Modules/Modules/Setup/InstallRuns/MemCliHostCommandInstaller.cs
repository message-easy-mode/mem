using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Modules.Setup.InstallRuns;

public sealed class MemCliHostCommandInstaller
{
    private readonly ICommandRunner _commandRunner;
    private readonly IOptions<MemCliHostCommandOptions> _options;
    private readonly ILogger<MemCliHostCommandInstaller> _logger;

    public MemCliHostCommandInstaller(
        ICommandRunner commandRunner,
        IOptions<MemCliHostCommandOptions> options,
        ILogger<MemCliHostCommandInstaller> logger)
    {
        _commandRunner = commandRunner;
        _options = options;
        _logger = logger;
    }

    public async Task<InstallStepResult> InstallAsync(CancellationToken cancellationToken)
    {
        var options = Normalize(_options.Value);

        if (!options.Enabled)
        {
            return new InstallStepResult(
                Succeeded: true,
                Message: "MEM CLI host command installation is not enabled for this installer package.");
        }

        var validationError = Validate(options);
        if (validationError is not null)
        {
            return new InstallStepResult(
                Succeeded: false,
                Message: "MEM CLI host command installation is not configured.",
                ErrorMessage: validationError);
        }

        _logger.LogInformation(
            "Installing MEM CLI host command using script {InstallScriptPath} and binary {BinaryPath}",
            options.InstallScriptPath,
            options.BinaryPath);

        var result = await _commandRunner.RunAsync(
            options.InstallScriptPath,
            [
                "--binary", options.BinaryPath,
                "--version", options.Version,
                "--install-root", options.InstallRoot,
                "--link-path", options.LinkPath
            ],
            cancellationToken);

        if (!result.Succeeded)
        {
            return new InstallStepResult(
                Succeeded: false,
                Message: "MEM CLI host command installation failed.",
                ErrorMessage: string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError);
        }

        return new InstallStepResult(
            Succeeded: true,
            Message: "MEM CLI host command installed. The 'mem' command is available on the host.");
    }

    private static MemCliHostCommandOptions Normalize(MemCliHostCommandOptions options)
    {
        return new MemCliHostCommandOptions
        {
            Enabled = options.Enabled,
            InstallScriptPath = NormalizePath(options.InstallScriptPath),
            BinaryPath = NormalizePath(options.BinaryPath),
            Version = string.IsNullOrWhiteSpace(options.Version)
                ? "dev"
                : options.Version.Trim(),
            InstallRoot = NormalizePath(options.InstallRoot),
            LinkPath = NormalizePath(options.LinkPath)
        };
    }

    private static string NormalizePath(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static string? Validate(MemCliHostCommandOptions options)
    {
        var errors = new List<string>();

        RequireAbsolutePath(options.InstallScriptPath, "MemCliHostCommand:InstallScriptPath", errors);
        RequireAbsolutePath(options.BinaryPath, "MemCliHostCommand:BinaryPath", errors);
        RequireAbsolutePath(options.InstallRoot, "MemCliHostCommand:InstallRoot", errors);
        RequireAbsolutePath(options.LinkPath, "MemCliHostCommand:LinkPath", errors);

        if (!IsSafeVersion(options.Version))
        {
            errors.Add("MemCliHostCommand:Version must be 1-64 letters, numbers, dots, underscores, or hyphens.");
        }

        return errors.Count == 0
            ? null
            : string.Join(Environment.NewLine, errors);
    }

    private static void RequireAbsolutePath(
        string value,
        string key,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key} is required.");
            return;
        }

        if (!Path.IsPathRooted(value))
        {
            errors.Add($"{key} must be an absolute path.");
        }
    }

    private static bool IsSafeVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-')
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
