using System.Text.Json;
using Mem.Cli.Clients;
using Mem.Cli.Commands;
using Mem.Cli.Config;
using Mem.Cli.Output;
using Mem.Localization;

namespace Mem.Cli.Tests.Commands;

public sealed class LoginCommandsTests
{
    private const string DeviceCredential =
        "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";

    [Fact]
    public async Task Device_login_requires_a_selected_profile_before_touching_secret_store_or_network()
    {
        var store = new RecordingCredentialStore();
        var clientFactoryCalled = false;
        var output = CreateOutput(out var standardOutput, out var standardError);

        var exitCode = await LoginCommands.RunAsync(
            ["login", "--device"],
            CreateOptions(profileName: null),
            output,
            _ =>
            {
                clientFactoryCalled = true;
                return new RecordingDeviceAuthorizationClient();
            },
            store);

        Assert.Equal(1, exitCode);
        Assert.False(clientFactoryCalled);
        Assert.Equal(0, store.AvailabilityChecks);
        Assert.Contains("Create or select", standardError.ToString());
        Assert.Empty(standardOutput.ToString());
    }

    [Fact]
    public async Task Device_login_fails_closed_when_the_secret_store_is_unavailable_without_starting_authorization()
    {
        var store = new RecordingCredentialStore
        {
            Availability = CliDeviceCredentialStoreAvailability.Unavailable()
        };
        var clientFactoryCalled = false;
        var output = CreateOutput(out var standardOutput, out var standardError);

        var exitCode = await LoginCommands.RunAsync(
            ["login", "--device"],
            CreateOptions(),
            output,
            _ =>
            {
                clientFactoryCalled = true;
                return new RecordingDeviceAuthorizationClient();
            },
            store);

        Assert.Equal(1, exitCode);
        Assert.Equal(1, store.AvailabilityChecks);
        Assert.False(clientFactoryCalled);
        Assert.Contains("No device authorization was started", standardError.ToString());
        Assert.Empty(standardOutput.ToString());
    }

    [Fact]
    public async Task Device_login_is_idempotent_when_a_valid_credential_is_already_stored()
    {
        var store = new RecordingCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Found(
                DeviceCredential)
        };
        var clientFactoryCalled = false;
        var output = CreateOutput(out var standardOutput, out var standardError);

        var exitCode = await LoginCommands.RunAsync(
            ["login", "--device"],
            CreateOptions(),
            output,
            _ =>
            {
                clientFactoryCalled = true;
                return new RecordingDeviceAuthorizationClient();
            },
            store);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, store.AvailabilityChecks);
        Assert.Equal(1, store.ReadCalls);
        Assert.Equal(0, store.StoreCalls);
        Assert.False(clientFactoryCalled);
        Assert.Contains("already stored for profile: home", standardOutput.ToString());
        Assert.DoesNotContain(DeviceCredential, standardOutput.ToString());
        Assert.Empty(standardError.ToString());
    }

    [Fact]
    public async Task Device_login_stores_an_approved_credential_without_printing_the_credential_or_verifier()
    {
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5);
        var idleExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(8);
        var absoluteExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7);

        var deviceAuthorizations = new RecordingDeviceAuthorizationClient
        {
            StartResult = new CliDeviceAuthorizationStartResult(
                Status: "authorization_started",
                AuthorizationId: Guid.NewGuid(),
                UserCode: "ABCD-EFGH",
                ExpiresAtUtc: expiresAtUtc,
                BrowserApprovalUrl: "https://mem.example.internal/cli/authorize"),
            PollResults =
            [
                new CliDeviceAuthorizationPollResult(
                    Status: "authorized",
                    DeviceCredential: DeviceCredential,
                    IdleExpiresAtUtc: idleExpiresAtUtc,
                    AbsoluteExpiresAtUtc: absoluteExpiresAtUtc)
            ]
        };
        var store = new RecordingCredentialStore();
        var output = CreateOutput(out var standardOutput, out var standardError);

        var exitCode = await LoginCommands.RunAsync(
            ["login", "--device"],
            CreateOptions(),
            output,
            _ => deviceAuthorizations,
            store);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, deviceAuthorizations.StartCalls);
        Assert.Equal(1, deviceAuthorizations.PollCalls);
        Assert.Equal(1, store.StoreCalls);
        Assert.Equal(DeviceCredential, store.StoredCredential);
        Assert.NotNull(deviceAuthorizations.LastVerifier);
        Assert.NotNull(deviceAuthorizations.LastVerifierChallenge);
        Assert.NotEqual(
            deviceAuthorizations.LastVerifier,
            deviceAuthorizations.LastVerifierChallenge);
        Assert.Equal(43, deviceAuthorizations.LastVerifier!.Length);

        var rendered = standardOutput.ToString();

        Assert.Contains("https://mem.example.internal/cli/authorize", rendered);
        Assert.Contains("ABCD-EFGH", rendered);
        Assert.Contains("stored securely", rendered);
        Assert.DoesNotContain(DeviceCredential, rendered);
        Assert.DoesNotContain(
            deviceAuthorizations.LastVerifier,
            rendered);
        Assert.Empty(standardError.ToString());
    }

    [Fact]
    public async Task Device_login_uses_german_human_prompts_without_localising_contract_values()
    {
        var deviceAuthorizations = new RecordingDeviceAuthorizationClient
        {
            StartResult = new CliDeviceAuthorizationStartResult(
                Status: "authorization_started",
                AuthorizationId: Guid.NewGuid(),
                UserCode: "ABCD-EFGH",
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(5),
                BrowserApprovalUrl: "https://mem.example.internal/cli/authorize"),
            PollResults =
            [
                new CliDeviceAuthorizationPollResult(
                    Status: "authorized",
                    DeviceCredential: DeviceCredential)
            ]
        };
        var store = new RecordingCredentialStore();
        var output = CreateOutput(
            out var standardOutput,
            out var standardError,
            language: MemLanguage.German);

        var exitCode = await LoginCommands.RunAsync(
            ["login", "--device", "--language", "de"],
            CreateOptions(language: MemLanguage.German),
            output,
            _ => deviceAuthorizations,
            store);

        Assert.Equal(0, exitCode);

        var rendered = standardOutput.ToString();

        Assert.Contains("MEM CLI-Geräteanmeldung", rendered);
        Assert.Contains("Profil: home", rendered);
        Assert.Contains("Server: https://mem.example.internal", rendered);
        Assert.Contains("Öffnen Sie diese Seite in einem Browser:", rendered);
        Assert.Contains("https://mem.example.internal/cli/authorize", rendered);
        Assert.Contains("Geben Sie diesen Gerätecode ein:", rendered);
        Assert.Contains("ABCD-EFGH", rendered);
        Assert.Contains("Warte auf Browserfreigabe...", rendered);
        Assert.Contains("Geräteanmeldung für Profil genehmigt: home", rendered);
        Assert.DoesNotContain("Open this page in a browser", rendered);
        Assert.DoesNotContain("Enter this device code", rendered);
        Assert.DoesNotContain(DeviceCredential, rendered);
        Assert.Empty(standardError.ToString());
    }

    [Fact]
    public async Task Device_login_json_error_keeps_stable_english_machine_contract_under_german()
    {
        var store = new RecordingCredentialStore();
        var output = CreateOutput(
            out var standardOutput,
            out var standardError,
            json: true,
            language: MemLanguage.German,
            throwOnLocalization: true);

        var exitCode = await LoginCommands.RunAsync(
            ["login", "--device", "--json", "--language", "de"],
            CreateOptions(
                json: true,
                language: MemLanguage.German),
            output,
            _ => new RecordingDeviceAuthorizationClient(),
            store);

        Assert.Equal(1, exitCode);
        Assert.Empty(standardError.ToString());

        using var response = JsonDocument.Parse(
            standardOutput.ToString());

        Assert.Equal(
            "mem-cli",
            response.RootElement.GetProperty("source").GetString());
        Assert.Equal(
            "error",
            response.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "cli_login_interactive_required",
            response.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Device_login_rejects_a_missing_server_browser_url_without_displaying_the_user_code_or_polling()
    {
        var deviceAuthorizations = new RecordingDeviceAuthorizationClient
        {
            StartResult = new CliDeviceAuthorizationStartResult(
                Status: "authorization_started",
                AuthorizationId: Guid.NewGuid(),
                UserCode: "ABCD-EFGH",
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(5),
                BrowserApprovalUrl: null)
        };
        var store = new RecordingCredentialStore();
        var output = CreateOutput(out var standardOutput, out var standardError);

        var exitCode = await LoginCommands.RunAsync(
            ["login", "--device"],
            CreateOptions(),
            output,
            _ => deviceAuthorizations,
            store);

        Assert.Equal(1, exitCode);
        Assert.Equal(1, deviceAuthorizations.StartCalls);
        Assert.Equal(0, deviceAuthorizations.PollCalls);
        Assert.Equal(0, store.StoreCalls);
        Assert.DoesNotContain("ABCD-EFGH", standardOutput.ToString());
        Assert.Contains("could not be started safely", standardError.ToString());
    }

    [Fact]
    public async Task Device_login_rejects_a_foreign_origin_browser_url_without_displaying_the_user_code_or_polling()
    {
        var deviceAuthorizations = new RecordingDeviceAuthorizationClient
        {
            StartResult = new CliDeviceAuthorizationStartResult(
                Status: "authorization_started",
                AuthorizationId: Guid.NewGuid(),
                UserCode: "ABCD-EFGH",
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(5),
                BrowserApprovalUrl: "https://attacker.example/cli/authorize")
        };
        var store = new RecordingCredentialStore();
        var output = CreateOutput(out var standardOutput, out var standardError);

        var exitCode = await LoginCommands.RunAsync(
            ["login", "--device"],
            CreateOptions(),
            output,
            _ => deviceAuthorizations,
            store);

        Assert.Equal(1, exitCode);
        Assert.Equal(1, deviceAuthorizations.StartCalls);
        Assert.Equal(0, deviceAuthorizations.PollCalls);
        Assert.Equal(0, store.StoreCalls);
        Assert.DoesNotContain("ABCD-EFGH", standardOutput.ToString());
        Assert.DoesNotContain("attacker.example", standardOutput.ToString());
        Assert.Contains("could not be started safely", standardError.ToString());
    }

    [Fact]
    public async Task Device_login_rejects_an_invalid_server_browser_url_without_displaying_the_user_code_or_polling()
    {
        var deviceAuthorizations = new RecordingDeviceAuthorizationClient
        {
            StartResult = new CliDeviceAuthorizationStartResult(
                Status: "authorization_started",
                AuthorizationId: Guid.NewGuid(),
                UserCode: "ABCD-EFGH",
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(5),
                BrowserApprovalUrl: "https://mem.example.internal/cli/authorize?unsafe=1")
        };
        var store = new RecordingCredentialStore();
        var output = CreateOutput(out var standardOutput, out var standardError);

        var exitCode = await LoginCommands.RunAsync(
            ["login", "--device"],
            CreateOptions(),
            output,
            _ => deviceAuthorizations,
            store);

        Assert.Equal(1, exitCode);
        Assert.Equal(1, deviceAuthorizations.StartCalls);
        Assert.Equal(0, deviceAuthorizations.PollCalls);
        Assert.Equal(0, store.StoreCalls);
        Assert.DoesNotContain("ABCD-EFGH", standardOutput.ToString());
        Assert.DoesNotContain("unsafe=1", standardOutput.ToString());
        Assert.Contains("could not be started safely", standardError.ToString());
    }

    [Fact]
    public async Task Device_login_handles_browser_denial_without_storing_a_credential()
    {
        var deviceAuthorizations = new RecordingDeviceAuthorizationClient
        {
            StartResult = CreateStarted(),
            PollResults =
            [
                new CliDeviceAuthorizationPollResult(
                    "authorization_denied")
            ]
        };
        var store = new RecordingCredentialStore();
        var output = CreateOutput(out var standardOutput, out var standardError);

        var exitCode = await LoginCommands.RunAsync(
            ["login", "--device"],
            CreateOptions(),
            output,
            _ => deviceAuthorizations,
            store);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, store.StoreCalls);
        Assert.Contains("denied in the browser", standardError.ToString());
        Assert.DoesNotContain(DeviceCredential, standardOutput.ToString());
    }

    [Fact]
    public async Task Device_login_rejects_json_before_touching_secret_store_or_network()
    {
        var store = new RecordingCredentialStore();
        var clientFactoryCalled = false;
        var output = CreateOutput(
            out var standardOutput,
            out var standardError,
            json: true);

        var exitCode = await LoginCommands.RunAsync(
            ["login", "--device", "--json"],
            CreateOptions(json: true),
            output,
            _ =>
            {
                clientFactoryCalled = true;
                return new RecordingDeviceAuthorizationClient();
            },
            store);

        Assert.Equal(1, exitCode);
        Assert.False(clientFactoryCalled);
        Assert.Equal(0, store.AvailabilityChecks);
        Assert.Empty(standardError.ToString());

        using var response = JsonDocument.Parse(
            standardOutput.ToString());

        Assert.Equal(
            "cli_login_interactive_required",
            response.RootElement.GetProperty("error").GetString());
    }

    private static CliDeviceAuthorizationStartResult CreateStarted() =>
        new(
            Status: "authorization_started",
            AuthorizationId: Guid.NewGuid(),
            UserCode: "ABCD-EFGH",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(5),
            BrowserApprovalUrl: "https://mem.example.internal/cli/authorize");

    private static CliOptions CreateOptions(
        string? profileName = "home",
        bool json = false,
        MemLanguage language = MemLanguage.English) =>
        new(
            HostAgentUrl: "https://mem.example.internal",
            InstallerToken: null,
            Json: json,
            Language: language,
            ProfileName: profileName);

    private static CliOutput CreateOutput(
        out StringWriter standardOutput,
        out StringWriter standardError,
        bool json = false,
        MemLanguage language = MemLanguage.English,
        bool throwOnLocalization = false)
    {
        standardOutput = new StringWriter();
        standardError = new StringWriter();

        return new CliOutput(
            json,
            () => throwOnLocalization
                ? throw new InvalidOperationException(
                    "JSON output must not initialise localisation.")
                : new MemLocalizer(language),
            standardOutput,
            standardError);
    }

    private sealed class RecordingDeviceAuthorizationClient :
        ICliDeviceAuthorizationClient
    {
        private Queue<CliDeviceAuthorizationPollResult> _pollResults = [];

        public int StartCalls { get; private set; }

        public int PollCalls { get; private set; }

        public string? LastVerifierChallenge { get; private set; }

        public string? LastVerifier { get; private set; }

        public CliDeviceAuthorizationStartResult StartResult { get; set; } =
            CreateStarted();

        public IReadOnlyList<CliDeviceAuthorizationPollResult> PollResults
        {
            set => _pollResults = new Queue<CliDeviceAuthorizationPollResult>(
                value);
        }

        public Task<CliDeviceAuthorizationStartResult> StartAsync(
            string verifierChallenge,
            string deviceLabel,
            CancellationToken ct = default)
        {
            StartCalls++;
            LastVerifierChallenge = verifierChallenge;

            Assert.Equal("MEM CLI", deviceLabel);

            return Task.FromResult(StartResult);
        }

        public Task<CliDeviceAuthorizationPollResult> PollAsync(
            Guid authorizationId,
            string verifier,
            CancellationToken ct = default)
        {
            PollCalls++;
            LastVerifier = verifier;

            if (_pollResults.Count == 0)
            {
                throw new InvalidOperationException(
                    "No scripted poll response was configured.");
            }

            return Task.FromResult(_pollResults.Dequeue());
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingCredentialStore :
        ICliDeviceCredentialStore
    {
        public CliDeviceCredentialStoreAvailability Availability { get; set; } =
            CliDeviceCredentialStoreAvailability.AvailableResult();

        public CliDeviceCredentialReadResult ReadResult { get; set; } =
            CliDeviceCredentialReadResult.NotFound();

        public CliDeviceCredentialWriteResult WriteResult { get; set; } =
            CliDeviceCredentialWriteResult.Success();

        public int AvailabilityChecks { get; private set; }

        public int ReadCalls { get; private set; }

        public int StoreCalls { get; private set; }

        public string? StoredCredential { get; private set; }

        public Task<CliDeviceCredentialStoreAvailability> CheckAvailableAsync(
            CancellationToken ct = default)
        {
            AvailabilityChecks++;
            return Task.FromResult(Availability);
        }

        public Task<CliDeviceCredentialReadResult> ReadAsync(
            CliDeviceCredentialKey key,
            CancellationToken ct = default)
        {
            ReadCalls++;
            return Task.FromResult(ReadResult);
        }

        public Task<CliDeviceCredentialWriteResult> StoreAsync(
            CliDeviceCredentialKey key,
            string credential,
            CancellationToken ct = default)
        {
            StoreCalls++;
            StoredCredential = credential;
            return Task.FromResult(WriteResult);
        }

        public Task<CliDeviceCredentialDeleteResult> DeleteAsync(
            CliDeviceCredentialKey key,
            CancellationToken ct = default) =>
            Task.FromResult(
                CliDeviceCredentialDeleteResult.Success());
    }
}
