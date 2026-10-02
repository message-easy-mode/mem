using System.Reflection;
using Api.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;
using Serilog;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqDiagnosticsOptionsTests
{
    [Fact]
    public void Defaults_keep_seq_optional_and_pin_an_exact_release()
    {
        var options = new SeqDiagnosticsOptions();

        Assert.False(options.SinkEnabled);
        Assert.False(options.ManagementEnabled);
        Assert.False(options.AllowOperationalPull);
        Assert.Equal("datalust/seq:2026.1.17044", options.ApprovedImageReference);
        Assert.Equal("2026.1.17044", options.ExpectedVersion);
        Assert.Equal("http://seq:5341", options.IngestionUrl);
        Assert.Equal("http://seq:80", options.HealthUrl);
        Assert.True(options.AllowSetupPull);
        Assert.Equal("/data/secrets/seq", options.SecretRootPath);
        Assert.Equal("/data/secrets/seq/admin-password-hash", options.AdminPasswordHashFilePath);
        Assert.Equal("/data/secrets/seq/ingestion-api-key", options.ApiKeyFilePath);
        Assert.Equal("/data/diagnostics/seq-bootstrap.json", options.BootstrapStatePath);
        Assert.Equal("/data/diagnostics/seq-delivery.json", options.DeliveryStatePath);
        Assert.Equal(45, options.BootstrapHealthAttemptCount);
        Assert.Equal(2, options.BootstrapHealthPollIntervalSeconds);
        Assert.Empty(SeqDiagnosticsOptionsValidator.Validate(options));
    }

    [Fact]
    public void Unsafe_urls_latest_images_and_unaccepted_management_are_rejected()
    {
        var options = new SeqDiagnosticsOptions
        {
            SinkEnabled = true,
            ManagementEnabled = true,
            IngestionUrl = "http://user:password@seq:5341/?secret=value",
            HealthUrl = "file:///tmp/seq",
            UiUrl = "https://seq.example.test/#fragment",
            ApprovedImageReference = "datalust/seq:latest",
            ExpectedVersion = "latest",
            EulaAccepted = false,
            AllowOperationalPull = true,
            HostDataPath = Path.GetPathRoot(Path.GetFullPath("/"))!,
            DeliveryStatePath = "\0",
            BootstrapHealthAttemptCount = 0,
            BootstrapHealthPollIntervalSeconds = 31
        };

        var errors = SeqDiagnosticsOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.Contains("IngestionUrl", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("HealthUrl", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("UiUrl", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("exact datalust/seq patch tag", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("EulaAccepted", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("AllowOperationalPull", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("filesystem root", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DeliveryStatePath", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BootstrapHealthAttemptCount", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BootstrapHealthPollIntervalSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Development_relative_host_data_path_is_valid_for_setup()
    {
        var options = new SeqDiagnosticsOptions
        {
            HostDataPath = "../../data/seq"
        };

        var errors = SeqDiagnosticsOptionsValidator.ValidateForSetup(options);

        Assert.DoesNotContain(
            errors,
            error => error.Contains("HostDataPath", StringComparison.Ordinal));
    }

    [Fact]
    public void Api_key_and_admin_hash_resolve_from_environment_or_secret_file()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-seq-secret-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var apiVariable = $"MEM_SEQ_API_KEY_{Guid.NewGuid():N}";
        var adminFile = Path.Combine(root, "seq-admin-hash");
        File.WriteAllText(adminFile, "admin-hash-value\n");
        Environment.SetEnvironmentVariable(apiVariable, "api-key-value");

        try
        {
            var options = new SeqDiagnosticsOptions
            {
                ApiKeyEnvironmentVariableName = apiVariable,
                ApiKeyFilePath = null,
                AdminPasswordHashEnvironmentVariableName = string.Empty,
                AdminPasswordHashFilePath = adminFile
            };
            var resolver = new SeqSecretResolver(new TestHostEnvironment(root));

            var apiKey = resolver.ResolveApiKey(options);
            var adminHash = resolver.ResolveAdminPasswordHash(options);

            Assert.True(apiKey.Available);
            Assert.Equal("api-key-value", apiKey.Value);
            Assert.True(adminHash.Available);
            Assert.Equal("admin-hash-value", adminHash.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiVariable, null);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Browser_facing_seq_contracts_do_not_accept_plaintext_credentials()
    {
        var publicProperties = typeof(SeqDeployRequest)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(publicProperties, name => name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(publicProperties, name => name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(publicProperties, name => name.Contains("HostPath", StringComparison.OrdinalIgnoreCase));

        var deliveryProperties = typeof(DiagnosticsSeqDeliveryChangeRequest)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(["Enabled"], deliveryProperties);
        Assert.DoesNotContain(deliveryProperties, name => name.Contains("Url", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(deliveryProperties, name => name.Contains("Container", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(deliveryProperties, name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));

        var reviewProperties = typeof(DiagnosticsSeqBootstrapReviewRequest)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(["AcceptEula", "PrivateUiUrl", "EnableEventDelivery"], reviewProperties);
        Assert.DoesNotContain(reviewProperties, name => name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(reviewProperties, name => name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sink_configuration_fails_closed_when_api_key_is_unavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-seq-logger-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var missingVariable = $"MEM_SEQ_MISSING_{Guid.NewGuid():N}";

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Diagnostics:Seq:SinkEnabled"] = "true",
                    ["Diagnostics:Seq:IngestionUrl"] = "http://seq:5341",
                    ["Diagnostics:Seq:HealthUrl"] = "http://seq:80",
                    ["Diagnostics:Seq:ApiKeyEnvironmentVariableName"] = missingVariable,
                    ["Diagnostics:Seq:ApprovedImageReference"] = "datalust/seq:2026.1.17044",
                    ["Diagnostics:Seq:ExpectedVersion"] = "2026.1.17044"
                })
                .Build();
            var result = MemLoggingBootstrap.CreateLogger(
                configuration,
                new MemLoggingOptions { PersistentFileEnabled = false },
                "Test",
                root);

            try
            {
                Assert.False(result.SeqSinkConfigured);
                Assert.Equal("diagnostics.seq_api_key_unavailable", result.SeqSinkWarningCode);
                result.Logger.Information("Local logging remains available");
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

    [Fact]
    public void Disabled_sink_surfaces_a_bounded_startup_activation_warning()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-seq-startup-warning-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Diagnostics:Seq:SinkEnabled"] = "false",
                    ["Diagnostics:Seq:DeliveryStartupWarningCode"] =
                        "seq_delivery_startup_prerequisites_unavailable"
                })
                .Build();
            var result = MemLoggingBootstrap.CreateLogger(
                configuration,
                new MemLoggingOptions { PersistentFileEnabled = false },
                "Test",
                root);

            try
            {
                Assert.False(result.SeqSinkConfigured);
                Assert.Equal(
                    "seq_delivery_startup_prerequisites_unavailable",
                    result.SeqSinkWarningCode);
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

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
