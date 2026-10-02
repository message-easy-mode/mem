using System.Net;
using System.Text;
using System.Text.Json;
using Api.IntegrationTests.Runtime;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modules.Auth.Services.Identity;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;
using Modules.Operator.Npm;
using Modules.Setup.InstallRuns;
using Modules.Setup.Secrets;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Setup;

public sealed class NpmInstalledCredentialManagementTests
{
    [Fact]
    public void STARTUP_NPM_BOOTSTRAP_01D_safe_projection_has_no_password_field()
    {
        Assert.DoesNotContain(
            typeof(OperatorNpmSettingsProjection).GetProperties(),
            property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task STARTUP_NPM_BOOTSTRAP_01D_reveal_returns_protected_password_and_writes_safe_audit()
    {
        var fixture = await Fixture.CreateAsync(HttpStatusCode.OK);
        await using (fixture)
        {
            var actorId = Guid.NewGuid();
            var revealed = await fixture.Service.RevealAsync(
                actorId,
                "npm-reveal-correlation",
                CancellationToken.None);

            Assert.Equal("admin@deltabox.dev", revealed.AdministratorEmail);
            Assert.Equal("known-good-password", revealed.Password);

            var audit = Assert.Single(fixture.Audit.Events);
            Assert.Equal("operator.npm.credential.revealed", audit.EventType);
            Assert.Equal("succeeded", audit.Outcome);
            Assert.Equal(actorId, audit.ActorOperatorId);
            Assert.Equal("step_up_verified", audit.ReasonCode);
            Assert.Null(audit.SubjectOperatorId);
        }
    }

    [Fact]
    public async Task STARTUP_NPM_BOOTSTRAP_01D_rejected_replacement_keeps_previous_verified_credential()
    {
        var fixture = await Fixture.CreateAsync(HttpStatusCode.BadRequest);
        await using (fixture)
        {
            var actorId = Guid.NewGuid();
            var exception = await Assert.ThrowsAsync<OperatorNpmCredentialException>(
                () => fixture.Service.UpdateAsync(
                    new UpdateOperatorNpmCredentialRequest(
                        "replacement@deltabox.dev",
                        "replacement-password"),
                    actorId,
                    "npm-update-rejected",
                    CancellationToken.None));

            Assert.Equal("npm_credentials_rejected", exception.Code);
            Assert.Equal(1, fixture.Handler.LoginCalls);
            Assert.Equal("replacement@deltabox.dev", fixture.Handler.Identity);
            Assert.Equal("replacement-password", fixture.Handler.Secret);
            Assert.Equal(0, fixture.TokenProvider.InvalidateCalls);

            var retained = await fixture.CredentialService.ResolveAsync(
                fixture.InstallationId,
                CancellationToken.None);
            Assert.NotNull(retained);
            Assert.Equal("admin@deltabox.dev", retained!.Email);
            Assert.Equal("known-good-password", retained.Password);
            Assert.Equal(fixture.InitialVerifiedAtUtc, retained.VerifiedAtUtc);

            var audit = Assert.Single(fixture.Audit.Events);
            Assert.Equal("operator.npm.credential.verification_failed", audit.EventType);
            Assert.Equal("failed", audit.Outcome);
            Assert.Equal(actorId, audit.ActorOperatorId);
            Assert.Equal("npm_credentials_rejected", audit.ReasonCode);
        }
    }

    [Fact]
    public async Task STARTUP_NPM_BOOTSTRAP_01D_verified_replacement_is_persisted_only_after_login_succeeds()
    {
        var fixture = await Fixture.CreateAsync(HttpStatusCode.OK);
        await using (fixture)
        {
            var actorId = Guid.NewGuid();
            var result = await fixture.Service.UpdateAsync(
                new UpdateOperatorNpmCredentialRequest(
                    "replacement@deltabox.dev",
                    "replacement-password"),
                actorId,
                "npm-update-success",
                CancellationToken.None);

            Assert.True(result.CredentialStored);
            Assert.Equal("Verified", result.Status);
            Assert.Equal("replacement@deltabox.dev", result.AdministratorEmail);
            Assert.NotNull(result.VerifiedAtUtc);
            Assert.Equal(1, fixture.Handler.LoginCalls);
            Assert.Equal(1, fixture.TokenProvider.InvalidateCalls);

            var stored = await fixture.CredentialService.ResolveAsync(
                fixture.InstallationId,
                CancellationToken.None);
            Assert.NotNull(stored);
            Assert.Equal("replacement@deltabox.dev", stored!.Email);
            Assert.Equal("replacement-password", stored.Password);
            Assert.NotNull(stored.VerifiedAtUtc);
            Assert.True(stored.VerifiedAtUtc > fixture.InitialVerifiedAtUtc);

            var audit = Assert.Single(fixture.Audit.Events);
            Assert.Equal("operator.npm.credential.updated", audit.EventType);
            Assert.Equal("succeeded", audit.Outcome);
            Assert.Equal(actorId, audit.ActorOperatorId);
            Assert.Equal("verified_before_replace", audit.ReasonCode);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly MemDbContext _db;

        private Fixture(
            string root,
            MemDbContext db,
            Guid installationId,
            DateTime initialVerifiedAtUtc,
            NpmAdminCredentialService credentialService,
            NpmCredentialManagementService service,
            RecordingLoginHandler handler,
            RecordingTokenProvider tokenProvider,
            RecordingAuditService audit)
        {
            _root = root;
            _db = db;
            InstallationId = installationId;
            InitialVerifiedAtUtc = initialVerifiedAtUtc;
            CredentialService = credentialService;
            Service = service;
            Handler = handler;
            TokenProvider = tokenProvider;
            Audit = audit;
        }

        public Guid InstallationId { get; }
        public DateTime InitialVerifiedAtUtc { get; }
        public NpmAdminCredentialService CredentialService { get; }
        public NpmCredentialManagementService Service { get; }
        public RecordingLoginHandler Handler { get; }
        public RecordingTokenProvider TokenProvider { get; }
        public RecordingAuditService Audit { get; }

        public static async Task<Fixture> CreateAsync(HttpStatusCode loginStatus)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"mem-npm-installed-credential-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var databasePath = Path.Combine(root, "control-plane.db");
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var installationId = Guid.NewGuid();
            db.Installations.Add(new InstallationEntity
            {
                Id = installationId,
                Status = InstallationStatuses.Succeeded,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
                UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                CompletedAtUtc = DateTime.UtcNow.AddMinutes(-1)
            });
            await db.SaveChangesAsync();

            var secretStore = new RecordingSecretStore();
            var credentialService = new NpmAdminCredentialService(db, secretStore);
            await secretStore.SetProtectedAsync(
                installationId,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.NpmAdminEmail,
                "admin@deltabox.dev",
                null,
                CancellationToken.None);
            await secretStore.SetProtectedAsync(
                installationId,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.NpmAdminPassword,
                "known-good-password",
                null,
                CancellationToken.None);

            var initialVerifiedAtUtc = new DateTime(2026, 8, 12, 10, 49, 25, DateTimeKind.Utc);
            await credentialService.MarkVerifiedAsync(
                installationId,
                initialVerifiedAtUtc,
                CancellationToken.None);

            var handler = new RecordingLoginHandler(loginStatus);
            var apiClient = new NpmApiClient(new HttpClient(handler));
            var runtimeContext = TestRuntimeContext.Create(root);
            var authorityResolver = new MemManagedServiceAuthorityResolver(runtimeContext);
            var apiBaseUrlResolver = new NpmApiBaseUrlResolver(
                db,
                Options.Create(new NpmApiOptions
                {
                    BaseUrl = "http://npm.test:81/api"
                }),
                NullLogger<NpmApiBaseUrlResolver>.Instance,
                authorityResolver);
            var tokenProvider = new RecordingTokenProvider();
            var audit = new RecordingAuditService();

            var service = new NpmCredentialManagementService(
                db,
                credentialService,
                runtimeService: null!,
                apiBaseUrlResolver,
                apiClient,
                tokenProvider,
                authorityResolver,
                audit,
                TimeProvider.System);

            return new Fixture(
                root,
                db,
                installationId,
                initialVerifiedAtUtc,
                credentialService,
                service,
                handler,
                tokenProvider,
                audit);
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Test cleanup only.
            }
        }
    }

    private sealed class RecordingLoginHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int LoginCalls { get; private set; }
        public string? Identity { get; private set; }
        public string? Secret { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/tokens", request.RequestUri?.AbsolutePath);

            LoginCalls++;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            Identity = json.RootElement.GetProperty("identity").GetString();
            Secret = json.RootElement.GetProperty("secret").GetString();

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    statusCode == HttpStatusCode.OK
                        ? "{\"token\":\"replacement-verified-token\"}"
                        : "{\"error\":{\"message\":\"Invalid credentials\"}}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class RecordingTokenProvider : INpmTokenProvider
    {
        public int InvalidateCalls { get; private set; }

        public Task<string> GetTokenAsync(string baseUrl, CancellationToken ct) =>
            Task.FromResult("unused");

        public void Invalidate() => InvalidateCalls++;
    }

    private sealed class RecordingAuditService : IMemOperatorAuditService
    {
        public List<MemOperatorAuditEventWrite> Events { get; } = [];

        public Task WriteAsync(
            MemOperatorAuditEventWrite auditEvent,
            CancellationToken ct = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSecretStore : IInstallationSecretStore
    {
        private readonly Dictionary<(Guid InstallationId, string Category, string Key), string> _values = [];

        public Task SetProtectedAsync(
            Guid installationId,
            string category,
            string key,
            string secret,
            string? description,
            CancellationToken cancellationToken)
        {
            _values[(installationId, category, key)] = secret;
            return Task.CompletedTask;
        }

        public Task<string?> ResolveProtectedAsync(
            Guid installationId,
            string category,
            string key,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                _values.TryGetValue((installationId, category, key), out var value)
                    ? value
                    : null);

        public Task<bool> ExistsAsync(
            Guid installationId,
            string category,
            string key,
            CancellationToken cancellationToken) =>
            Task.FromResult(_values.ContainsKey((installationId, category, key)));

        public Task DeleteAsync(
            Guid installationId,
            string category,
            string key,
            CancellationToken cancellationToken)
        {
            _values.Remove((installationId, category, key));
            return Task.CompletedTask;
        }
    }
}
