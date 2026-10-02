using System.Net;
using System.Reflection;

namespace Mem.Migrate.Web.Hosting;

internal sealed record SourceAssistantCommandLineResult(
    SourceAssistantOptions? Options,
    bool ShouldExit,
    int ExitCode,
    string? Output,
    string? Error)
{
    public static SourceAssistantCommandLineResult Success(SourceAssistantOptions options) =>
        new(options, false, 0, null, null);

    public static SourceAssistantCommandLineResult Exit(string output, int exitCode = 0) =>
        new(null, true, exitCode, output, null);

    public static SourceAssistantCommandLineResult Failure(string error) =>
        new(null, true, 2, null, error);
}

internal static class SourceAssistantCommandLine
{
    internal const string DefaultListenAddress = "127.0.0.1";
    internal const int DefaultPort = 7391;
    internal const int DefaultSessionMinutes = 240;
    internal const int DefaultSessionAbsoluteMinutes = 1440;
    internal const string DefaultWorkspaceRoot = "/var/lib/mem-migrate/work";
    internal const string DefaultArtifactRoot = "/var/lib/mem-migrate/artifacts";

    public static SourceAssistantCommandLineResult Parse(
        IReadOnlyList<string> args,
        Func<string, string?>? readEnvironment = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;

        if (args.Any(arg => arg is "-h" or "--help"))
        {
            return SourceAssistantCommandLineResult.Exit(Usage());
        }

        if (args.Any(arg => arg == "--version"))
        {
            return SourceAssistantCommandLineResult.Exit(GetApplicationVersion());
        }

        var listenAddress = readEnvironment("MEM_MIGRATE_WEB_LISTEN_ADDRESS") ?? DefaultListenAddress;
        var portText = readEnvironment("MEM_MIGRATE_WEB_PORT") ?? DefaultPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var allowRemoteText = readEnvironment("MEM_MIGRATE_WEB_ALLOW_REMOTE") ?? "false";
        var workspaceRoot = readEnvironment("MEM_MIGRATE_WORKSPACE") ?? DefaultWorkspaceRoot;
        var artifactRoot = readEnvironment("MEM_MIGRATE_ARTIFACTS") ?? DefaultArtifactRoot;
        var sessionMinutesText = readEnvironment("MEM_MIGRATE_WEB_SESSION_MINUTES") ?? DefaultSessionMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sessionAbsoluteMinutesText = readEnvironment("MEM_MIGRATE_WEB_SESSION_ABSOLUTE_MINUTES") ?? DefaultSessionAbsoluteMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--listen-address":
                    if (!TryReadValue(args, ref index, argument, out listenAddress, out var listenError))
                    {
                        return SourceAssistantCommandLineResult.Failure(listenError!);
                    }
                    break;
                case "--port":
                    if (!TryReadValue(args, ref index, argument, out portText, out var portError))
                    {
                        return SourceAssistantCommandLineResult.Failure(portError!);
                    }
                    break;
                case "--allow-remote":
                    allowRemoteText = "true";
                    break;
                case "--workspace":
                    if (!TryReadValue(args, ref index, argument, out workspaceRoot, out var workspaceError))
                    {
                        return SourceAssistantCommandLineResult.Failure(workspaceError!);
                    }
                    break;
                case "--artifacts":
                    if (!TryReadValue(args, ref index, argument, out artifactRoot, out var artifactError))
                    {
                        return SourceAssistantCommandLineResult.Failure(artifactError!);
                    }
                    break;
                case "--session-minutes":
                    if (!TryReadValue(args, ref index, argument, out sessionMinutesText, out var sessionError))
                    {
                        return SourceAssistantCommandLineResult.Failure(sessionError!);
                    }
                    break;
                case "--session-absolute-minutes":
                    if (!TryReadValue(args, ref index, argument, out sessionAbsoluteMinutesText, out var sessionAbsoluteError))
                    {
                        return SourceAssistantCommandLineResult.Failure(sessionAbsoluteError!);
                    }
                    break;
                default:
                    return SourceAssistantCommandLineResult.Failure($"Unknown argument: {argument}");
            }
        }

        if (!IPAddress.TryParse(listenAddress, out var parsedAddress))
        {
            return SourceAssistantCommandLineResult.Failure("--listen-address must be an IPv4 or IPv6 address.");
        }

        if (parsedAddress.Equals(IPAddress.Any) || parsedAddress.Equals(IPAddress.IPv6Any))
        {
            return SourceAssistantCommandLineResult.Failure("Wildcard listener addresses are not supported. Bind to loopback, a trusted LAN address, or a VPN address explicitly.");
        }

        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
        {
            return SourceAssistantCommandLineResult.Failure("--port must be an integer from 1 to 65535.");
        }

        if (!bool.TryParse(allowRemoteText, out var allowRemote))
        {
            return SourceAssistantCommandLineResult.Failure("MEM_MIGRATE_WEB_ALLOW_REMOTE must be true or false.");
        }

        if (!IPAddress.IsLoopback(parsedAddress) && !allowRemote)
        {
            return SourceAssistantCommandLineResult.Failure("Non-loopback binding requires the explicit --allow-remote acknowledgement.");
        }

        if (!int.TryParse(sessionMinutesText, out var sessionMinutes) || sessionMinutes is < 5 or > 1440)
        {
            return SourceAssistantCommandLineResult.Failure("--session-minutes must be an integer from 5 to 1440.");
        }

        if (!int.TryParse(sessionAbsoluteMinutesText, out var sessionAbsoluteMinutes) || sessionAbsoluteMinutes is < 5 or > 1440)
        {
            return SourceAssistantCommandLineResult.Failure("--session-absolute-minutes must be an integer from 5 to 1440.");
        }

        if (sessionAbsoluteMinutes < sessionMinutes)
        {
            return SourceAssistantCommandLineResult.Failure("--session-absolute-minutes must be greater than or equal to --session-minutes.");
        }

        if (!TryNormalizePrivateRoot(workspaceRoot, "workspace", out var normalizedWorkspace, out var workspaceValidationError))
        {
            return SourceAssistantCommandLineResult.Failure(workspaceValidationError!);
        }

        if (!TryNormalizePrivateRoot(artifactRoot, "artifacts", out var normalizedArtifacts, out var artifactValidationError))
        {
            return SourceAssistantCommandLineResult.Failure(artifactValidationError!);
        }

        if (string.Equals(normalizedWorkspace, normalizedArtifacts, StringComparison.Ordinal))
        {
            return SourceAssistantCommandLineResult.Failure("Workspace and artifact roots must be different directories.");
        }

        if (IsDescendant(normalizedWorkspace!, normalizedArtifacts!) ||
            IsDescendant(normalizedArtifacts!, normalizedWorkspace!))
        {
            return SourceAssistantCommandLineResult.Failure("Workspace and artifact roots must not contain one another.");
        }

        return SourceAssistantCommandLineResult.Success(new SourceAssistantOptions(
            parsedAddress,
            port,
            allowRemote,
            normalizedWorkspace!,
            normalizedArtifacts!,
            TimeSpan.FromMinutes(sessionMinutes),
            TimeSpan.FromMinutes(sessionAbsoluteMinutes)));
    }


    internal static string GetApplicationVersion()
    {
        var assembly = typeof(SourceAssistantCommandLine).Assembly;
        return assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    public static string Usage() =>
        """
        Usage:
          mem-migrate-web [options]

        Options:
          --listen-address <ip>  Listener address. Default: 127.0.0.1
          --port <port>          Listener port. Default: 7391
          --allow-remote         Acknowledge an explicit trusted LAN or VPN bind.
          --workspace <path>     Private work root. Default: /var/lib/mem-migrate/work
          --artifacts <path>     Private artifact root. Default: /var/lib/mem-migrate/artifacts
          --session-minutes <n>  Idle session timeout from 5 to 1440 minutes. Default: 240
          --session-absolute-minutes <n>
                                 Maximum session lifetime from 5 to 1440 minutes. Default: 1440
          --version              Print the application version.
          -h, --help             Show this help.

        Safe default remote access:
          ssh -L 7391:127.0.0.1:7391 <operator>@<source-host>
        """;

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string option,
        out string value,
        out string? error)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = string.Empty;
            error = $"{option} requires a value.";
            return false;
        }

        index++;
        value = args[index];
        error = null;
        return true;
    }

    private static bool TryNormalizePrivateRoot(
        string value,
        string name,
        out string? normalized,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = null;
            error = $"The {name} root is required.";
            return false;
        }

        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalized = null;
            error = $"The {name} root is invalid.";
            return false;
        }

        var pathRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(normalized) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, pathRoot, StringComparison.Ordinal))
        {
            normalized = null;
            error = $"The {name} root cannot be a filesystem root.";
            return false;
        }

        error = null;
        return true;
    }
    private static bool IsDescendant(string candidate, string parent)
    {
        var parentPrefix = parent.EndsWith(Path.DirectorySeparatorChar)
            ? parent
            : parent + Path.DirectorySeparatorChar;
        return candidate.StartsWith(parentPrefix, StringComparison.Ordinal);
    }

}
