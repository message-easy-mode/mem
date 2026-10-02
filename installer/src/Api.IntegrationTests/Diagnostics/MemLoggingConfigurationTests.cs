using System.Text.Json;
using Api.IntegrationTests.Runtime;
using Api.Logging;
using Microsoft.Extensions.Configuration;

namespace Api.IntegrationTests.Diagnostics;

public sealed class MemLoggingConfigurationTests
{
    [Fact]
    public void Default_logging_options_define_a_bounded_local_recorder()
    {
        var options = new MemLoggingOptions();

        Assert.True(options.PersistentFileEnabled);
        Assert.Equal(25, options.FileSizeLimitMiB);
        Assert.Equal(28, options.RetainedFileCountLimit);
        Assert.Equal(1000, options.FlushIntervalMilliseconds);
        Assert.Equal(1024, options.LowDiskWarningMiB);
        Assert.Equal(256, options.CriticalDiskWarningMiB);
        Assert.Equal("X-Correlation-ID", options.CorrelationHeaderName);
        Assert.Empty(MemLoggingOptionsValidator.Validate(options));
    }

    [Theory]
    [InlineData(0, 28, 1000)]
    [InlineData(513, 28, 1000)]
    [InlineData(25, 0, 1000)]
    [InlineData(25, 366, 1000)]
    [InlineData(25, 28, 99)]
    [InlineData(25, 28, 60001)]
    public void Unsafe_retention_and_flush_limits_are_rejected(
        int fileSizeMiB,
        int retainedFiles,
        int flushMilliseconds)
    {
        var options = new MemLoggingOptions
        {
            FileSizeLimitMiB = fileSizeMiB,
            RetainedFileCountLimit = retainedFiles,
            FlushIntervalMilliseconds = flushMilliseconds
        };

        Assert.NotEmpty(MemLoggingOptionsValidator.Validate(options));
    }


    [Fact]
    public void Invalid_disk_thresholds_and_non_clef_paths_are_rejected()
    {
        var options = new MemLoggingOptions
        {
            FilePath = "/data/logs/control-plane/mem-control-plane.log",
            LowDiskWarningMiB = 128,
            CriticalDiskWarningMiB = 256
        };

        var errors = MemLoggingOptionsValidator.Validate(options);

        Assert.Contains(
            errors,
            error => error.Contains("CriticalDiskWarningMiB", StringComparison.Ordinal));
        Assert.Contains(
            errors,
            error => error.Contains(".clef", StringComparison.Ordinal));
    }

    [Fact]
    public void Relative_log_paths_are_anchored_to_the_installer_root()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-diag-root-{Guid.NewGuid():N}");
        var contentRoot = Path.Combine(root, "src", "Api");
        Directory.CreateDirectory(contentRoot);

        try
        {
            var resolved = MemLoggingBootstrap.ResolveFilePath(
                "data/logs/control-plane/mem-control-plane-.clef",
                contentRoot);

            Assert.Equal(
                Path.GetFullPath(Path.Combine(
                    root,
                    "data/logs/control-plane/mem-control-plane-.clef")),
                resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Logger_writes_compact_structured_json_to_the_local_recorder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-diag-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var options = new MemLoggingOptions
            {
                FilePath = Path.Combine(root, "mem-control-plane-.clef"),
                FileSizeLimitMiB = 1,
                RetainedFileCountLimit = 2,
                FlushIntervalMilliseconds = 100
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Serilog:MinimumLevel:Default"] = "Information"
                })
                .Build();

            var runtimeContext = TestRuntimeContext.Create(root);
            var result = MemLoggingBootstrap.CreateLogger(
                configuration,
                options,
                environmentName: "Test",
                contentRootPath: root,
                runtimeContext);

            Assert.Null(result.PersistentRecorderWarning);
            Assert.NotNull(result.PersistentFilePath);

            result.Logger.Information(
                "Diagnostic foundation test {TestValue}",
                42);
            (result.Logger as IDisposable)?.Dispose();

            var file = Assert.Single(Directory.GetFiles(root, "*.clef"));
            var line = Assert.Single(File.ReadAllLines(file));
            using var json = JsonDocument.Parse(line);

            Assert.Equal(
                MemLoggingBootstrap.ApplicationName,
                json.RootElement.GetProperty("Application").GetString());
            Assert.Equal(
                MemLoggingBootstrap.ComponentName,
                json.RootElement.GetProperty("Component").GetString());
            Assert.Equal(42, json.RootElement.GetProperty("TestValue").GetInt32());
            Assert.Equal(
                runtimeContext.RuntimeMode,
                json.RootElement.GetProperty("RuntimeMode").GetString());
            Assert.Equal(
                runtimeContext.ControlPlaneInstanceId,
                json.RootElement.GetProperty("ControlPlaneInstanceId").GetGuid());
            Assert.Equal(
                runtimeContext.ApiProcessInstanceId,
                json.RootElement.GetProperty("ApiProcessInstanceId").GetGuid());
            Assert.Equal(
                runtimeContext.UiDeliveryMode,
                json.RootElement.GetProperty("UiDeliveryMode").GetString());
            Assert.Equal(
                runtimeContext.Commit,
                json.RootElement.GetProperty("Commit").GetString());
            Assert.True(json.RootElement.TryGetProperty("@t", out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Persistent_recorder_failure_falls_back_to_console_logging()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-diag-blocked-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var fileBlockingDirectoryCreation = Path.Combine(root, "not-a-directory");
        File.WriteAllText(fileBlockingDirectoryCreation, "block");

        try
        {
            var configuration = new ConfigurationBuilder().Build();
            var options = new MemLoggingOptions
            {
                FilePath = Path.Combine(
                    fileBlockingDirectoryCreation,
                    "mem-control-plane-.clef")
            };

            var result = MemLoggingBootstrap.CreateLogger(
                configuration,
                options,
                environmentName: "Test",
                contentRootPath: root);

            try
            {
                Assert.Null(result.PersistentFilePath);
                Assert.NotNull(result.PersistentRecorderWarning);
                result.Logger.Information("Console fallback remains usable");
            }
            finally
            {
                (result.Logger as IDisposable)?.Dispose();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
