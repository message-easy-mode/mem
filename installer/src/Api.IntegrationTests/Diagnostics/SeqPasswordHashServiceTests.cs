using Infrastructure.Docker;
using Modules.Integrations.Seq.Services;
using Modules.Shared.RuntimeImages;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqPasswordHashServiceTests
{
    [Fact]
    public async Task Password_is_sent_only_through_standard_input_to_an_isolated_one_shot_container()
    {
        const string password = "Correct Horse Battery Staple 42!";
        var runner = new RecordingRunner(new DockerIsolatedStandardInputResult(
            0,
            "$PH$generated-hash",
            string.Empty,
            TimedOut: false));
        var service = CreateService(runner, LocalImage());

        var hash = await service.HashAsync(password.AsMemory(), CancellationToken.None);

        Assert.Equal("$PH$generated-hash", hash);
        Assert.Equal(password, runner.StandardInput);
        Assert.Equal($"sha256:{new string('a', 64)}", runner.Image);
        Assert.StartsWith(
            "mem-seq-password-hash-",
            runner.ContainerName,
            StringComparison.Ordinal);
        Assert.Equal(["config", "hash"], runner.Command);
        Assert.DoesNotContain(runner.Command, value =>
            value.Contains(password, StringComparison.Ordinal));
        Assert.DoesNotContain(password, runner.Image ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(password, runner.ContainerName ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_image_is_prepared_only_inside_setup_hashing()
    {
        var inspector = new PullingInspector();
        var resolver = new SeqRuntimeImageResolver(Options(), inspector);
        var runner = new RecordingRunner(new DockerIsolatedStandardInputResult(
            0,
            "$PH$generated-hash",
            string.Empty,
            TimedOut: false));
        var service = new SeqPasswordHashService(Options(), resolver, runner);

        await service.HashAsync("valid password 123!".AsMemory(), CancellationToken.None);

        Assert.Equal(1, inspector.PullCount);
        Assert.Equal("datalust/seq:2026.1.17044", inspector.PulledReference);
    }

    [Fact]
    public async Task Timeout_is_safe_and_does_not_expose_the_password()
    {
        const string password = "never expose this password";
        var service = CreateService(
            new RecordingRunner(new DockerIsolatedStandardInputResult(
                -1,
                string.Empty,
                "Docker one-shot command timed out.",
                TimedOut: true)),
            LocalImage());

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            service.HashAsync(password.AsMemory(), CancellationToken.None));

        Assert.Equal("seq_password_hash_timeout", exception.Code);
        Assert.DoesNotContain(password, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_hash_output_fails_closed()
    {
        var service = CreateService(
            new RecordingRunner(new DockerIsolatedStandardInputResult(
                0,
                "not a single hash",
                string.Empty,
                TimedOut: false)),
            LocalImage());

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            service.HashAsync("valid password 123!".AsMemory(), CancellationToken.None));

        Assert.Equal("seq_password_hash_invalid", exception.Code);
    }

    [Fact]
    public void Seq_hashing_uses_the_Docker_API_isolated_runner_and_not_the_Docker_CLI()
    {
        var repositoryRoot = FindRepositoryRoot();
        var serviceSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "installer",
            "src",
            "Modules",
            "Modules",
            "Integrations",
            "Seq",
            "Services",
            "SeqPasswordHashService.cs"));
        var runnerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "installer",
            "src",
            "Infrastructure",
            "Infrastructure",
            "Docker",
            "DockerIsolatedStandardInputRunner.cs"));

        Assert.DoesNotContain("IStandardInputCommandRunner", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"docker\"", serviceSource, StringComparison.Ordinal);
        Assert.Contains("IDockerIsolatedStandardInputRunner", serviceSource, StringComparison.Ordinal);

        Assert.Contains("NetworkMode = \"none\"", runnerSource, StringComparison.Ordinal);
        Assert.Contains("AutoRemove = true", runnerSource, StringComparison.Ordinal);
        Assert.Contains("AttachStdin = true", runnerSource, StringComparison.Ordinal);
        Assert.Contains("OpenStdin = true", runnerSource, StringComparison.Ordinal);
        Assert.Contains("StdinOnce = true", runnerSource, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.ZeroMemory", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Binds =", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Mounts =", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Env =", runnerSource, StringComparison.Ordinal);
    }

    private static SeqPasswordHashService CreateService(
        IDockerIsolatedStandardInputRunner runner,
        RuntimeImageInspection? image)
    {
        var options = Options();
        return new SeqPasswordHashService(
            options,
            new SeqRuntimeImageResolver(options, new FixedInspector(image)),
            runner);
    }

    private static SeqDiagnosticsOptions Options() => new()
    {
        AllowSetupPull = true,
        PasswordHashTimeoutSeconds = 30
    };

    private static RuntimeImageInspection LocalImage() => new(
        $"sha256:{new string('a', 64)}",
        ["datalust/seq@sha256:" + new string('b', 64)],
        []);

    private sealed class RecordingRunner(DockerIsolatedStandardInputResult result)
        : IDockerIsolatedStandardInputRunner
    {
        public string? Image { get; private set; }
        public string? ContainerName { get; private set; }
        public IReadOnlyList<string> Command { get; private set; } = [];
        public string? StandardInput { get; private set; }

        public Task<DockerIsolatedStandardInputResult> RunAsync(
            string image,
            string containerName,
            IReadOnlyList<string> command,
            ReadOnlyMemory<char> standardInput,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Image = image;
            ContainerName = containerName;
            Command = command.ToArray();
            StandardInput = standardInput.ToString();
            return Task.FromResult(result);
        }
    }

    private sealed class FixedInspector(RuntimeImageInspection? image)
        : IRuntimeImageInspector
    {
        public Task<RuntimeImageInspection?> InspectAsync(
            string immutableReference,
            CancellationToken cancellationToken) => Task.FromResult(image);

        public Task PullAsync(
            string immutableReference,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class PullingInspector : IRuntimeImageInspector
    {
        private RuntimeImageInspection? _image;
        public int PullCount { get; private set; }
        public string? PulledReference { get; private set; }

        public Task<RuntimeImageInspection?> InspectAsync(
            string immutableReference,
            CancellationToken cancellationToken) => Task.FromResult(_image);

        public Task PullAsync(
            string immutableReference,
            CancellationToken cancellationToken)
        {
            PullCount++;
            PulledReference = immutableReference;
            _image = LocalImage();
            return Task.CompletedTask;
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "installer", "src", "MemInstaller.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate repository root containing installer/src/MemInstaller.sln.");
    }
}
