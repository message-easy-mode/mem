using Modules.Integrations.Seq.Services;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Shared.ControlPlane;
using Shared.ControlPlane.Runtime;

namespace Api.Logging;

public sealed record MemLoggerBuildResult(
    Serilog.ILogger Logger,
    string? PersistentFilePath,
    string? PersistentRecorderWarning,
    bool SeqSinkConfigured,
    string? SeqSinkWarningCode);

public static class MemLoggingBootstrap
{
    public const string ApplicationName = MemControlPlaneIdentity.CanonicalContainerName;
    public const string ComponentName = "api-host-agent";

    public static Serilog.ILogger CreateBootstrapLogger() =>
        BaseConfiguration(environmentName: "Bootstrap")
            .WriteTo.Console(new CompactJsonFormatter())
            .CreateLogger();

    public static MemLoggerBuildResult CreateLogger(
        IConfiguration configuration,
        MemLoggingOptions options,
        string environmentName,
        string contentRootPath,
        MemControlPlaneRuntimeContext? runtimeContext = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        MemLoggingOptionsValidator.ThrowIfInvalid(options);

        var loggerConfiguration = BaseConfiguration(environmentName, runtimeContext)
            .ReadFrom.Configuration(configuration)
            .WriteTo.Console(new CompactJsonFormatter());

        var seqResult = ConfigureSeq(
            loggerConfiguration,
            configuration,
            contentRootPath);

        if (!options.PersistentFileEnabled)
        {
            return new MemLoggerBuildResult(
                loggerConfiguration.CreateLogger(),
                PersistentFilePath: null,
                PersistentRecorderWarning: null,
                seqResult.Configured,
                seqResult.WarningCode);
        }

        var resolvedPath = ResolveFilePath(options.FilePath, contentRootPath);
        var preparationError = TryPrepareRecorderDirectory(resolvedPath);

        if (preparationError is not null)
        {
            return new MemLoggerBuildResult(
                loggerConfiguration.CreateLogger(),
                PersistentFilePath: null,
                PersistentRecorderWarning: preparationError,
                seqResult.Configured,
                seqResult.WarningCode);
        }

        try
        {
            loggerConfiguration.WriteTo.File(
                formatter: new CompactJsonFormatter(),
                path: resolvedPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: options.FileSizeLimitMiB * 1024L * 1024L,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: options.RetainedFileCountLimit,
                buffered: true,
                shared: false,
                flushToDiskInterval: TimeSpan.FromMilliseconds(options.FlushIntervalMilliseconds));

            return new MemLoggerBuildResult(
                loggerConfiguration.CreateLogger(),
                PersistentFilePath: resolvedPath,
                PersistentRecorderWarning: null,
                seqResult.Configured,
                seqResult.WarningCode);
        }
        catch (Exception ex)
        {
            var fallback = BaseConfiguration(environmentName, runtimeContext)
                .ReadFrom.Configuration(configuration)
                .WriteTo.Console(new CompactJsonFormatter());
            ConfigureSeq(fallback, configuration, contentRootPath);

            return new MemLoggerBuildResult(
                fallback.CreateLogger(),
                PersistentFilePath: null,
                PersistentRecorderWarning:
                    $"Persistent logging could not open '{resolvedPath}': {ex.GetType().Name}: {ex.Message}",
                seqResult.Configured,
                seqResult.WarningCode);
        }
    }

    public static string ResolveFilePath(string configuredPath, string contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var possibleSrcDirectory = Directory.GetParent(contentRootPath);
        var possibleInstallerRoot = possibleSrcDirectory?.Parent?.FullName;

        var basePath = possibleSrcDirectory?.Name.Equals(
                "src",
                StringComparison.OrdinalIgnoreCase) == true
            ? possibleInstallerRoot ?? contentRootPath
            : contentRootPath;

        return Path.GetFullPath(Path.Combine(basePath, configuredPath));
    }

    private static SeqSinkBuildResult ConfigureSeq(
        LoggerConfiguration loggerConfiguration,
        IConfiguration configuration,
        string contentRootPath)
    {
        var options = new SeqDiagnosticsOptions();
        configuration
            .GetSection(SeqDiagnosticsOptions.SectionName)
            .Bind(options);

        if (!options.SinkEnabled)
        {
            return new SeqSinkBuildResult(
                false,
                options.DeliveryStartupWarningCode);
        }

        var errors = SeqDiagnosticsOptionsValidator.Validate(options);
        if (errors.Count > 0 ||
            !SeqDiagnosticsOptionsValidator.TryNormalizeUrl(
                options.IngestionUrl,
                out var ingestionUri) ||
            ingestionUri is null)
        {
            return new SeqSinkBuildResult(
                false,
                "diagnostics.seq_configuration_invalid");
        }

        var apiKey = SeqSecretResolver.ResolveApiKey(
            options,
            contentRootPath);
        if (!apiKey.Available)
        {
            return new SeqSinkBuildResult(false, apiKey.WarningCode);
        }

        try
        {
            loggerConfiguration.WriteTo.Seq(
                ingestionUri.ToString(),
                restrictedToMinimumLevel: LogEventLevel.Information,
                batchPostingLimit: 1000,
                period: TimeSpan.FromSeconds(2),
                apiKey: apiKey.Value);
            return new SeqSinkBuildResult(true, null);
        }
        catch
        {
            return new SeqSinkBuildResult(
                false,
                "diagnostics.seq_sink_configuration_failed");
        }
    }

    private static LoggerConfiguration BaseConfiguration(
        string environmentName,
        MemControlPlaneRuntimeContext? runtimeContext = null)
    {
        var configuration = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", ApplicationName)
            .Enrich.WithProperty("Component", ComponentName)
            .Enrich.WithProperty("Environment", environmentName)
            .Enrich.WithProperty("MachineName", Environment.MachineName)
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .Enrich.WithProperty(
                "Version",
                runtimeContext?.Version ??
                typeof(MemLoggingBootstrap).Assembly.GetName().Version?.ToString() ??
                "unknown");

        if (runtimeContext is null)
        {
            return configuration;
        }

        configuration
            .Enrich.WithProperty("RuntimeMode", runtimeContext.RuntimeMode)
            .Enrich.WithProperty(
                "ControlPlaneInstanceId",
                runtimeContext.ControlPlaneInstanceId)
            .Enrich.WithProperty(
                "ApiProcessInstanceId",
                runtimeContext.ApiProcessInstanceId)
            .Enrich.WithProperty("UiDeliveryMode", runtimeContext.UiDeliveryMode)
            .Enrich.WithProperty("StateRootKind", runtimeContext.StateRootKind);

        if (!string.IsNullOrWhiteSpace(runtimeContext.Commit))
        {
            configuration.Enrich.WithProperty("Commit", runtimeContext.Commit);
        }

        return configuration;
    }

    private static string? TryPrepareRecorderDirectory(string resolvedPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(resolvedPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return $"Persistent log path '{resolvedPath}' has no parent directory.";
            }

            Directory.CreateDirectory(directory);

            var probePath = Path.Combine(
                directory,
                $".mem-log-write-probe-{Guid.NewGuid():N}");

            using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
            {
            }

            return null;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return $"Persistent logging is unavailable for '{resolvedPath}': {ex.GetType().Name}: {ex.Message}";
        }
    }

    private sealed record SeqSinkBuildResult(
        bool Configured,
        string? WarningCode);
}
