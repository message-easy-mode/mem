using Mem.Cli.Config;

namespace Mem.Cli.Tests.Config;

public sealed class SecretToolCliDeviceCredentialStoreTests
{
    private const string Credential =
        "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";

    [Fact]
    public async Task Store_writes_an_opaque_credential_through_stdin_not_command_arguments()
    {
        var runner = new RecordingSecretToolProcessRunner();
        runner.Enqueue(new SecretToolProcessResult(
            Started: true,
            TimedOut: false,
            ExitCode: 0,
            StandardOutput: string.Empty));

        var store = new SecretToolCliDeviceCredentialStore(runner);
        var key = CreateKey();

        var result = await store.StoreAsync(key, Credential);

        Assert.True(result.Succeeded);

        var request = Assert.Single(runner.Requests);

        Assert.Equal("store", request.Arguments[0]);
        Assert.Contains(
            "--label=MEM CLI device session (home)",
            request.Arguments);
        Assert.DoesNotContain(
            Credential,
            request.Arguments);
        Assert.Equal(
            Credential + Environment.NewLine,
            request.StandardInput);
        Assert.Contains("profile", request.Arguments);
        Assert.Contains("home", request.Arguments);
        Assert.Contains("server", request.Arguments);
        Assert.Contains("https://mem.example.internal", request.Arguments);
    }

    [Fact]
    public async Task Read_returns_a_valid_opaque_credential_without_process_error_detail()
    {
        var runner = new RecordingSecretToolProcessRunner();
        runner.Enqueue(new SecretToolProcessResult(
            Started: true,
            TimedOut: false,
            ExitCode: 0,
            StandardOutput: Credential + Environment.NewLine));

        var store = new SecretToolCliDeviceCredentialStore(runner);

        var result = await store.ReadAsync(CreateKey());

        Assert.True(result.Succeeded);
        Assert.Equal(Credential, result.Credential);

        var request = Assert.Single(runner.Requests);
        Assert.Equal("lookup", request.Arguments[0]);
        Assert.Null(request.StandardInput);
        Assert.DoesNotContain(Credential, request.Arguments);
    }

    [Fact]
    public async Task Read_rejects_invalid_secret_service_output_without_returning_it()
    {
        const string invalidSecret = "not-a-mem-device-credential";

        var runner = new RecordingSecretToolProcessRunner();
        runner.Enqueue(new SecretToolProcessResult(
            Started: true,
            TimedOut: false,
            ExitCode: 0,
            StandardOutput: invalidSecret));

        var store = new SecretToolCliDeviceCredentialStore(runner);

        var result = await store.ReadAsync(CreateKey());

        Assert.Equal(
            CliDeviceCredentialStoreStatus.InvalidCredential,
            result.Status);
        Assert.Null(result.Credential);
    }

    [Fact]
    public async Task CheckAvailable_requires_a_full_store_read_delete_round_trip()
    {
        var runner = new RecordingSecretToolProcessRunner();
        runner.OnRequest = request =>
        {
            if (request.Arguments[0] == "lookup")
            {
                var storedSecret = runner.Requests
                    .Single(candidate => candidate.Arguments[0] == "store")
                    .StandardInput!
                    .Trim();

                return new SecretToolProcessResult(
                    Started: true,
                    TimedOut: false,
                    ExitCode: 0,
                    StandardOutput: storedSecret + Environment.NewLine);
            }

            return new SecretToolProcessResult(
                Started: true,
                TimedOut: false,
                ExitCode: 0,
                StandardOutput: string.Empty);
        };

        var store = new SecretToolCliDeviceCredentialStore(runner);

        var availability = await store.CheckAvailableAsync();

        Assert.True(availability.Available);
        Assert.Equal(
            ["store", "lookup", "clear"],
            runner.Requests
                .Select(request => request.Arguments[0])
                .ToArray());

        var storedProbe = runner.Requests[0].StandardInput!.Trim();
        Assert.True(CliDeviceCredentialValidator.IsValid(storedProbe));
        Assert.DoesNotContain(
            storedProbe,
            runner.Requests[0].Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-credential")]
    [InlineData("abc")]
    public void Credential_validator_rejects_non_opaque_values(string? value)
    {
        Assert.False(CliDeviceCredentialValidator.IsValid(value));
    }

    private static CliDeviceCredentialKey CreateKey()
    {
        Assert.True(
            CliDeviceCredentialKey.TryCreate(
                "home",
                "https://mem.example.internal/",
                out var key));

        return key!;
    }

    private sealed class RecordingSecretToolProcessRunner :
        ISecretToolProcessRunner
    {
        private readonly Queue<SecretToolProcessResult> _results = [];

        public RecordingSecretToolProcessRunner(
            Func<SecretToolProcessRequest, SecretToolProcessResult>? onRequest = null)
        {
            OnRequest = onRequest;
        }

        public List<SecretToolProcessRequest> Requests { get; } = [];

        public Func<SecretToolProcessRequest, SecretToolProcessResult>? OnRequest { get; set; }

        public void Enqueue(SecretToolProcessResult result) =>
            _results.Enqueue(result);

        public Task<SecretToolProcessResult> RunAsync(
            SecretToolProcessRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);

            if (OnRequest is not null)
            {
                return Task.FromResult(OnRequest(request));
            }

            return Task.FromResult(_results.Dequeue());
        }
    }
}
