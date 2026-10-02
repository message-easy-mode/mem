using System.Net;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Services;
using Api.IntegrationTests.Runtime;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqConnectionProvisionerTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 5, 6, 30, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"mem-seq-connection-{Guid.NewGuid():N}");

    [Fact]
    public async Task Provision_creates_one_ingest_key_persists_only_the_token_and_reads_back_the_exact_event()
    {
        var fixture = await CreateFixtureAsync();
        const string administratorPassword = "correct-horse-battery-staple";

        var result = await fixture.Provisioner.ProvisionAndVerifyAsync(
            administratorPassword,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CancellationToken.None);

        Assert.Equal("api-key-mem", result.ApiKeyId);
        Assert.Equal("event-verification-1", result.EventId);
        Assert.False(result.ReusedCredential);
        Assert.Equal(1, fixture.Administration.CreateCount);
        Assert.Equal("127.0.0.1", fixture.Administration.LastServerUrl?.Host);
        Assert.Equal(25341, fixture.Administration.LastServerUrl?.Port);
        Assert.Equal(1, fixture.Ingestion.CallCount);
        Assert.Equal(
            "http://127.0.0.1:25341/ingest/clef",
            fixture.Ingestion.RequestUri?.ToString());
        Assert.Equal(
            fixture.Administration.Token,
            fixture.Ingestion.ApiKey);
        Assert.Contains(
            SeqConnectionProvisioner.VerificationEventCode,
            fixture.Ingestion.Body,
            StringComparison.Ordinal);
        using (var verificationJson = JsonDocument.Parse(fixture.Ingestion.Body))
        {
            var root = verificationJson.RootElement;
            Assert.Equal(
                fixture.RuntimeContext.RuntimeMode,
                root.GetProperty("RuntimeMode").GetString());
            Assert.Equal(
                fixture.RuntimeContext.ControlPlaneInstanceId,
                root.GetProperty("ControlPlaneInstanceId").GetGuid());
            Assert.Equal(
                fixture.RuntimeContext.ApiProcessInstanceId,
                root.GetProperty("ApiProcessInstanceId").GetGuid());
            Assert.Equal(
                fixture.RuntimeContext.EnvironmentName,
                root.GetProperty("Environment").GetString());
            Assert.Equal(
                fixture.RuntimeContext.Version,
                root.GetProperty("Version").GetString());
            Assert.Equal(
                fixture.RuntimeContext.Commit,
                root.GetProperty("Commit").GetString());
        }
        Assert.DoesNotContain(
            administratorPassword,
            fixture.Ingestion.Body,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            fixture.Administration.Token,
            fixture.Ingestion.Body,
            StringComparison.Ordinal);

        var persistedToken = await File.ReadAllTextAsync(
            Path.Combine(_root, "data", "secrets", "seq", "ingestion-api-key"));
        Assert.Equal(fixture.Administration.Token, persistedToken);

        var state = Assert.IsType<SeqBootstrapState>(fixture.StateStore.Read().State);
        Assert.Equal("connected", state.SetupStage);
        Assert.Equal("available", state.IngestionCredentialState);
        Assert.Equal("api-key-mem", state.IngestionCredentialId);
        Assert.Equal("event-verification-1", state.LastDeliveryVerificationEventId);
        Assert.NotNull(state.DeliveryVerifiedAtUtc);

        var stateJson = await File.ReadAllTextAsync(
            Path.Combine(_root, "data", "diagnostics", "seq-bootstrap.json"));
        Assert.DoesNotContain(
            administratorPassword,
            stateJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            fixture.Administration.Token,
            stateJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verified_retry_reuses_the_same_key_without_duplicate_creation_or_event()
    {
        var fixture = await CreateFixtureAsync();

        await fixture.Provisioner.ProvisionAndVerifyAsync(
            "first-administrator-password",
            Guid.NewGuid(),
            CancellationToken.None);
        var retry = await fixture.Provisioner.ProvisionAndVerifyAsync(
            "second-administrator-password",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(retry.ReusedCredential);
        Assert.Equal("api-key-mem", retry.ApiKeyId);
        Assert.Equal(1, fixture.Administration.CreateCount);
        Assert.Equal(1, fixture.Ingestion.CallCount);
    }

    [Theory]
    [InlineData(MemRuntimeModes.ContainerizedDevelopment, "seq-dev")]
    [InlineData(MemRuntimeModes.ContainerizedProduction, "seq")]
    public async Task Containerized_runtime_uses_the_context_owned_Docker_network_authorities_for_administration_and_ingestion(
        string runtimeMode,
        string expectedHost)
    {
        var fixture = await CreateFixtureAsync(runtimeMode: runtimeMode);

        await fixture.Provisioner.ProvisionAndVerifyAsync(
            "administrator-password",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(expectedHost, fixture.Administration.LastServerUrl?.Host);
        Assert.Equal(80, fixture.Administration.LastServerUrl?.Port);
        Assert.Equal(expectedHost, fixture.Ingestion.RequestUri?.Host);
        Assert.Equal(5341, fixture.Ingestion.RequestUri?.Port);
        Assert.Equal("/ingest/clef", fixture.Ingestion.RequestUri?.AbsolutePath);
        Assert.Equal(runtimeMode, fixture.RuntimeContext.RuntimeMode);
    }

    [Fact]
    public async Task Missing_server_owned_published_port_fails_before_authentication_or_ingestion()
    {
        var administration = new FakeAdministrationState();
        var fixture = await CreateFixtureAsync(
            administration,
            ManagedStatus() with { UiHostPort = null });

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Provisioner.ProvisionAndVerifyAsync(
                "administrator-password",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("seq_runtime_authority_unavailable", exception.Code);
        Assert.Null(administration.LastServerUrl);
        Assert.Equal(0, administration.CreateCount);
        Assert.Equal(0, fixture.Ingestion.CallCount);
    }

    [Fact]
    public async Task Existing_key_without_local_token_fails_closed_and_never_creates_a_duplicate()
    {
        var administration = new FakeAdministrationState();
        administration.AddExistingMemKey();
        var fixture = await CreateFixtureAsync(administration);

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Provisioner.ProvisionAndVerifyAsync(
                "administrator-password",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("seq_ingestion_key_token_missing", exception.Code);
        Assert.Equal(0, administration.CreateCount);
        Assert.Equal(0, fixture.Ingestion.CallCount);
    }

    private async Task<Fixture> CreateFixtureAsync(
        FakeAdministrationState? administration = null,
        SeqStatusResponse? runtimeStatus = null,
        string runtimeMode = MemRuntimeModes.LocalDevelopment)
    {
        Directory.CreateDirectory(Path.Combine(_root, "data", "secrets", "seq"));
        Directory.CreateDirectory(Path.Combine(_root, "data", "diagnostics"));

        var options = new SeqDiagnosticsOptions
        {
            SinkEnabled = false,
            ManagementEnabled = true,
            AdministratorUserName = "admin",
            ApiKeyEnvironmentVariableName = string.Empty,
            ApiKeyFilePath = "data/secrets/seq/ingestion-api-key",
            AdminPasswordHashEnvironmentVariableName = string.Empty,
            AdminPasswordHashFilePath = "data/secrets/seq/admin-password-hash",
            SecretRootPath = "data/secrets/seq",
            BootstrapStatePath = "data/diagnostics/seq-bootstrap.json",
            ConnectionVerificationAttemptCount = 2,
            ConnectionVerificationPollIntervalSeconds = 1
        };
        var environment = new TestHostEnvironment(_root);
        var stateStore = new SeqBootstrapStateStore(options, environment);
        await stateStore.WriteAsync(
            new SeqBootstrapState(
                ManagementEnabled: true,
                EulaAccepted: true,
                SelectedHostPort: 25341,
                SetupStage: "completed",
                RuntimeVerifiedAtUtc: Now.AddMinutes(-2)),
            CancellationToken.None);

        administration ??= new FakeAdministrationState();
        var ingestion = new RecordingIngestionHandler();
        var containerized = MemRuntimeModes.IsContainerized(runtimeMode);
        var runtimeContext = TestRuntimeContext.Create(
            _root,
            runtimeMode,
            runningInContainer: containerized,
            containerName: runtimeMode switch
            {
                MemRuntimeModes.ContainerizedDevelopment =>
                    Shared.ControlPlane.MemControlPlaneIdentity.DevelopmentContainerName,
                MemRuntimeModes.ContainerizedProduction =>
                    Shared.ControlPlane.MemControlPlaneIdentity.CanonicalContainerName,
                _ => null
            },
            uiDeliveryMode: containerized
                ? MemUiDeliveryModes.EmbeddedSpa
                : MemUiDeliveryModes.Vite);
        var provisioner = new SeqConnectionProvisioner(
            options,
            new SeqEffectiveConfigurationProvider(options, stateStore),
            new FixedRuntimeStatusReader(runtimeStatus ?? ManagedStatus()),
            new PassingHealthVerifier(),
            new FakeAdministrationClientFactory(administration),
            new MemManagedServiceAuthorityResolver(runtimeContext),
            new SingleClientFactory(new HttpClient(ingestion)),
            new SeqSecretResolver(environment),
            new SeqSecretFileWriter(options, environment),
            stateStore,
            runtimeContext,
            new FixedTimeProvider(Now),
            SeqRuntimeContextProfile.Create(runtimeContext, options));

        return new Fixture(
            provisioner,
            stateStore,
            administration,
            ingestion,
            runtimeContext);
    }

    private static SeqStatusResponse ManagedStatus() => new(
        ServiceName: "seq",
        ContainerName: "mem-seq",
        ExpectedVersion: "2026.1.17044",
        HostDataPath: "/data/seq",
        UiHostPort: 25341,
        Exists: true,
        Running: true,
        State: "running",
        Image: "sha256:approved-seq",
        UsesApprovedRuntime: true,
        Warnings: [],
        Managed: true,
        OwnershipState: "managed",
        WarningCode: null);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed record Fixture(
        SeqConnectionProvisioner Provisioner,
        ISeqBootstrapStateStore StateStore,
        FakeAdministrationState Administration,
        RecordingIngestionHandler Ingestion,
        MemControlPlaneRuntimeContext RuntimeContext);

    private sealed class FixedRuntimeStatusReader(SeqStatusResponse status)
        : ISeqRuntimeStatusReader
    {
        public Task<SeqStatusResponse> GetStatusAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(status);
    }

    private sealed class PassingHealthVerifier : ISeqRuntimeHealthVerifier
    {
        public Task<SeqRuntimeHealthVerification> VerifyAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new SeqRuntimeHealthVerification(
                Passed: true,
                Status: "healthy",
                ObservedAtUtc: Now,
                WarningCode: null));
    }

    private sealed class FakeAdministrationClientFactory(
        FakeAdministrationState state) : ISeqAdministrationClientFactory
    {
        public Task<ISeqAdministrationSession> AuthenticateAsync(
            Uri serverUrl,
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            state.LastServerUrl = serverUrl;
            state.LastUsername = username;
            state.LastPassword = password;
            return Task.FromResult<ISeqAdministrationSession>(
                new FakeAdministrationSession(state));
        }
    }

    private sealed class FakeAdministrationSession(
        FakeAdministrationState state) : ISeqAdministrationSession
    {
        public Task<IReadOnlyList<SeqAdministrationApiKey>> ListSharedApiKeysAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SeqAdministrationApiKey>>(
                state.Keys.ToArray());

        public Task<SeqAdministrationApiKey> CreateMemIngestionApiKeyAsync(
            CancellationToken cancellationToken)
        {
            state.CreateCount++;
            var created = new SeqAdministrationApiKey(
                "api-key-mem",
                SeqConnectionProvisioner.ApiKeyTitle,
                OwnerId: null,
                TokenPrefix: state.Token[..6],
                Token: state.Token);
            state.Keys.Clear();
            state.Keys.Add(created with { Token = null });
            return Task.FromResult(created);
        }

        public Task RemoveApiKeyAsync(
            string apiKeyId,
            CancellationToken cancellationToken)
        {
            state.RemoveCount++;
            state.Keys.RemoveAll(key =>
                string.Equals(key.Id, apiKeyId, StringComparison.Ordinal));
            return Task.CompletedTask;
        }

        public Task<string?> FindVerificationEventAsync(
            string verificationId,
            DateTimeOffset fromUtc,
            CancellationToken cancellationToken)
        {
            state.LastVerificationId = verificationId;
            return Task.FromResult<string?>("event-verification-1");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAdministrationState
    {
        public string Token { get; } = "mem-token-123456789";
        public List<SeqAdministrationApiKey> Keys { get; } = [];
        public int CreateCount { get; set; }
        public int RemoveCount { get; set; }
        public Uri? LastServerUrl { get; set; }
        public string? LastUsername { get; set; }
        public string? LastPassword { get; set; }
        public string? LastVerificationId { get; set; }

        public void AddExistingMemKey()
        {
            Keys.Add(new SeqAdministrationApiKey(
                "api-key-mem",
                SeqConnectionProvisioner.ApiKeyTitle,
                OwnerId: null,
                TokenPrefix: Token[..6],
                Token: null));
        }
    }

    private sealed class RecordingIngestionHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.TryGetValues("X-Seq-ApiKey", out var values)
                ? values.Single()
                : null;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestHostEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
