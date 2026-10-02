using System.Text.Json;
using Mem.Migrate.Core.Target;
using Mem.Migrate.Infrastructure.Target;

namespace Mem.Migrate.IntegrationTests;

public sealed class MemCliTargetProfileCredentialResolverTests
{
    [Fact]
    public async Task Reads_mem_cli_profile_and_exact_secret_service_key()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-migrate-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "config.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 2,
                    language = "en",
                    defaultProfile = "target-server",
                    profiles = new[]
                    {
                        new
                        {
                            name = "target-server",
                            serverUrl = "http://127.0.0.1:7105",
                            language = "en"
                        }
                    }
                }));

            var credential = CreateDeviceCredential();
            var runner = new RecordingSecretToolRunner(
                new TargetSecretToolProcessResult(
                    Started: true,
                    TimedOut: false,
                    ExitCode: 0,
                    StandardOutput: credential + Environment.NewLine));
            var resolver = new MemCliTargetProfileCredentialResolver(
                root,
                runner);

            var result = await resolver.ResolveAsync(
                "Target-Server",
                CancellationToken.None);

            Assert.Equal("target-server", result.ProfileName);
            Assert.Equal("http://127.0.0.1:7105", result.TargetBaseUrl);
            Assert.Equal(credential, result.DeviceCredential);
            Assert.Equal(
                new[]
                {
                    "lookup",
                    "application",
                    "matrix-easy-mode",
                    "kind",
                    "cli-device-session",
                    "profile",
                    "target-server",
                    "server",
                    "http://127.0.0.1:7105"
                },
                runner.Arguments);
            Assert.DoesNotContain(credential, runner.Arguments);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Missing_secret_service_item_returns_safe_login_guidance()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-migrate-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "config.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 2,
                    profiles = new[]
                    {
                        new
                        {
                            name = "target-server",
                            serverUrl = "https://mem.example.test"
                        }
                    }
                }));

            var resolver = new MemCliTargetProfileCredentialResolver(
                root,
                new RecordingSecretToolRunner(
                    new TargetSecretToolProcessResult(
                        Started: true,
                        TimedOut: false,
                        ExitCode: 1,
                        StandardOutput: string.Empty)));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync(
                    "target-server",
                    CancellationToken.None));

            Assert.Contains(
                "mem login --device --profile target-server",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Profile_server_must_match_mem_cli_transport_rules()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-migrate-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "config.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 2,
                    profiles = new[]
                    {
                        new
                        {
                            name = "target-server",
                            serverUrl = "http://remote.example.test"
                        }
                    }
                }));

            var resolver = new MemCliTargetProfileCredentialResolver(
                root,
                new RecordingSecretToolRunner(
                    new TargetSecretToolProcessResult(
                        Started: true,
                        TimedOut: false,
                        ExitCode: 0,
                        StandardOutput: CreateDeviceCredential())));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync(
                    "target-server",
                    CancellationToken.None));

            Assert.Contains(
                "profile 'target-server' is invalid",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CreateDeviceCredential() =>
        Convert.ToBase64String(new byte[32])
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class RecordingSecretToolRunner(
        TargetSecretToolProcessResult result) :
        ITargetSecretToolProcessRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<TargetSecretToolProcessResult> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Arguments = arguments.ToArray();
            return Task.FromResult(result);
        }
    }
}
