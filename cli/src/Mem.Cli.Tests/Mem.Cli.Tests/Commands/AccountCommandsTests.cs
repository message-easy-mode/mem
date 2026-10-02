using System.Text.Json;
using Mem.Cli.Clients;
using Mem.Cli.Commands;
using Mem.Cli.Config;
using Mem.Cli.Output;

namespace Mem.Cli.Tests.Commands;

public sealed class AccountCommandsTests
{
    private const string DeviceCredential =
        "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";

    [Fact]
    public async Task Account_show_requires_a_selected_profile_before_touching_secret_store_or_network()
    {
        var store = new RecordingCredentialStore();
        var clientFactoryCalled = false;
        var output = CreateOutput(out var standardOutput, out var standardError);

        var exitCode = await AccountCommands.RunAccountAsync(
            ["account", "show"],
            "show",
            CreateOptions(profileName: null),
            output,
            _ =>
            {
                clientFactoryCalled = true;
                return new RecordingDeviceSessionClient();
            },
            store);

        Assert.Equal(1, exitCode);
        Assert.False(clientFactoryCalled);
        Assert.Equal(0, store.ReadCalls);
        Assert.Contains("Create or select", standardError.ToString());
        Assert.Empty(standardOutput.ToString());
    }

    [Fact]
    public async Task Account_show_reports_signed_out_when_no_credential_is_stored_without_network()
    {
        var store = new RecordingCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.NotFound()
        };
        var clientFactoryCalled = false;
        var output = CreateOutput(out var standardOutput, out var standardError);

        var exitCode = await AccountCommands.RunAccountAsync(
            ["account", "show"],
            "show",
            CreateOptions(),
            output,
            _ =>
            {
                clientFactoryCalled = true;
                return new RecordingDeviceSessionClient();
            },
            store);

        Assert.Equal(1, exitCode);
        Assert.False(clientFactoryCalled);
        Assert.Equal(1, store.ReadCalls);
        Assert.Contains("No MEM CLI device session", standardOutput.ToString());
        Assert.Contains("mem login --device --profile home", standardOutput.ToString());
        Assert.Empty(standardError.ToString());
    }

    [Fact]
    public async Task Account_show_uses_stored_credential_to_display_safe_session_status()
    {
        var idleExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(8);
        var absoluteExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7);
        var store = new RecordingCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Found(
                DeviceCredential)
        };
        var client = new RecordingDeviceSessionClient
        {
            Result = new CliDeviceSessionStatusResult(
                Status: "authenticated",
                DisplayName: "admin",
                Roles: ["platform_owner", "operator"],
                IdleExpiresAtUtc: idleExpiresAtUtc,
                AbsoluteExpiresAtUtc: absoluteExpiresAtUtc)
        };
        var output = CreateOutput(out var standardOutput, out var standardError);

        var exitCode = await AccountCommands.RunAccountAsync(
            ["account", "show"],
            "show",
            CreateOptions(),
            output,
            _ => client,
            store);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, store.ReadCalls);
        Assert.Equal(1, client.GetCurrentCalls);
        Assert.Equal(DeviceCredential, client.LastCredential);

        var rendered = standardOutput.ToString();

        Assert.Contains("MEM CLI account", rendered);
        Assert.Contains("Profile: home", rendered);
        Assert.Contains("Server: https://mem.example.internal", rendered);
        Assert.Contains("Status: authenticated", rendered);
        Assert.Contains("Operator: admin", rendered);
        Assert.Contains("platform_owner", rendered);
        Assert.DoesNotContain(DeviceCredential, rendered);
        Assert.Empty(standardError.ToString());
    }

    [Fact]
    public async Task Account_show_json_never_prints_the_stored_credential()
    {
        var store = new RecordingCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Found(
                DeviceCredential)
        };
        var client = new RecordingDeviceSessionClient
        {
            Result = new CliDeviceSessionStatusResult(
                Status: "authenticated",
                DisplayName: "admin",
                Roles: ["platform_owner"])
        };
        var output = CreateOutput(
            out var standardOutput,
            out var standardError,
            json: true);

        var exitCode = await AccountCommands.RunAccountAsync(
            ["account", "show", "--json"],
            "show",
            CreateOptions(json: true),
            output,
            _ => client,
            store);

        Assert.Equal(0, exitCode);
        Assert.Empty(standardError.ToString());

        var rendered = standardOutput.ToString();

        Assert.DoesNotContain(DeviceCredential, rendered);

        using var response = JsonDocument.Parse(rendered);

        Assert.Equal(
            "authenticated",
            response.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "home",
            response.RootElement.GetProperty("profile").GetString());
        Assert.Equal(
            "admin",
            response.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Account_show_reports_rejected_stored_session_without_deleting_it()
    {
        var store = new RecordingCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Found(
                DeviceCredential)
        };
        var client = new RecordingDeviceSessionClient
        {
            Result = new CliDeviceSessionStatusResult(
                Status: "unauthenticated")
        };
        var output = CreateOutput(out var standardOutput, out var standardError);

        var exitCode = await AccountCommands.RunAccountAsync(
            ["account", "show"],
            "show",
            CreateOptions(),
            output,
            _ => client,
            store);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, store.DeleteCalls);
        Assert.Contains("rejected by the control plane", standardError.ToString());
        Assert.DoesNotContain(DeviceCredential, standardOutput.ToString());
    }

    [Fact]
    public async Task Logout_removes_the_local_secret_store_entry()
    {
        var store = new RecordingCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Found(
                DeviceCredential)
        };
        var output = CreateOutput(out var standardOutput, out var standardError);

        var client = new RecordingDeviceSessionClient
        {
            RevocationResult = new CliDeviceSessionRevocationResult(
                "revoked",
                DateTimeOffset.UtcNow)
        };

        var exitCode = await AccountCommands.RunLogoutAsync(
            CreateOptions(),
            output,
            _ => client,
            store);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, store.ReadCalls);
        Assert.Equal(1, store.DeleteCalls);
        Assert.Contains("Removed local MEM CLI device session", standardOutput.ToString());
        Assert.DoesNotContain(DeviceCredential, standardOutput.ToString());
        Assert.Empty(standardError.ToString());
    }

    [Fact]
    public async Task Logout_is_idempotent_when_no_local_session_exists()
    {
        var store = new RecordingCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.NotFound()
        };
        var output = CreateOutput(out var standardOutput, out var standardError);

        var client = new RecordingDeviceSessionClient();

        var exitCode = await AccountCommands.RunLogoutAsync(
            CreateOptions(),
            output,
            _ => client,
            store);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, store.ReadCalls);
        Assert.Equal(0, store.DeleteCalls);
        Assert.Contains("No MEM CLI device session", standardOutput.ToString());
        Assert.Empty(standardError.ToString());
    }

    [Fact]
    public async Task Logout_revokes_the_server_session_before_removing_the_local_secret_store_entry()
    {
        var store = new RecordingCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Found(
                DeviceCredential)
        };
        var client = new RecordingDeviceSessionClient
        {
            RevocationResult = new CliDeviceSessionRevocationResult(
                "revoked",
                DateTimeOffset.UtcNow)
        };
        var output = CreateOutput(out var standardOutput, out var standardError);

        var exitCode = await AccountCommands.RunLogoutAsync(
            CreateOptions(),
            output,
            _ => client,
            store);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, store.ReadCalls);
        Assert.Equal(1, client.RevokeCurrentCalls);
        Assert.Equal(DeviceCredential, client.LastRevokedCredential);
        Assert.Equal(1, store.DeleteCalls);
        Assert.Contains("Server-side MEM CLI device session revoked", standardOutput.ToString());
        Assert.DoesNotContain(DeviceCredential, standardOutput.ToString());
        Assert.Empty(standardError.ToString());
    }

    [Fact]
    public async Task Logout_still_removes_the_local_secret_when_server_revocation_cannot_be_confirmed()
    {
        var store = new RecordingCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Found(
                DeviceCredential)
        };
        var client = new RecordingDeviceSessionClient
        {
            RevocationResult = new CliDeviceSessionRevocationResult(
                "unavailable")
        };
        var output = CreateOutput(out var standardOutput, out var standardError);

        var exitCode = await AccountCommands.RunLogoutAsync(
            CreateOptions(),
            output,
            _ => client,
            store);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, client.RevokeCurrentCalls);
        Assert.Equal(1, store.DeleteCalls);
        Assert.Contains("could not be confirmed", standardOutput.ToString());
        Assert.Empty(standardError.ToString());
    }

    [Fact]
    public async Task Logout_json_reports_local_removal_and_server_revocation_status_without_printing_the_credential()
    {
        var store = new RecordingCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Found(
                DeviceCredential)
        };
        var client = new RecordingDeviceSessionClient
        {
            RevocationResult = new CliDeviceSessionRevocationResult(
                "revoked",
                DateTimeOffset.UtcNow)
        };
        var output = CreateOutput(
            out var standardOutput,
            out var standardError,
            json: true);

        var exitCode = await AccountCommands.RunLogoutAsync(
            CreateOptions(json: true),
            output,
            _ => client,
            store);

        Assert.Equal(0, exitCode);
        Assert.Empty(standardError.ToString());

        var rendered = standardOutput.ToString();
        Assert.DoesNotContain(DeviceCredential, rendered);

        using var response = JsonDocument.Parse(rendered);
        Assert.Equal("signed_out", response.RootElement.GetProperty("status").GetString());
        Assert.True(response.RootElement.GetProperty("removed").GetBoolean());
        Assert.Equal(
            "revoked",
            response.RootElement.GetProperty("serverRevocationStatus").GetString());
    }

    private static CliOptions CreateOptions(
        string? profileName = "home",
        bool json = false) =>
        new(
            HostAgentUrl: "https://mem.example.internal",
            InstallerToken: null,
            Json: json,
            ProfileName: profileName);

    private static CliOutput CreateOutput(
        out StringWriter standardOutput,
        out StringWriter standardError,
        bool json = false)
    {
        standardOutput = new StringWriter();
        standardError = new StringWriter();

        return new CliOutput(
            json,
            () => throw new InvalidOperationException(
                "This test does not use localisation."),
            standardOutput,
            standardError);
    }

    private sealed class RecordingDeviceSessionClient :
        ICliDeviceSessionClient
    {
        public int GetCurrentCalls { get; private set; }

        public string? LastCredential { get; private set; }

        public CliDeviceSessionStatusResult Result { get; set; } =
            new("unavailable");

        public CliDeviceSessionRevocationResult RevocationResult { get; set; } =
            new("unavailable");

        public int RevokeCurrentCalls { get; private set; }

        public string? LastRevokedCredential { get; private set; }

        public Task<CliDeviceSessionStatusResult> GetCurrentAsync(
            string deviceCredential,
            CancellationToken ct = default)
        {
            GetCurrentCalls++;
            LastCredential = deviceCredential;

            return Task.FromResult(Result);
        }

        public Task<CliDeviceSessionRevocationResult> RevokeCurrentAsync(
            string deviceCredential,
            CancellationToken ct = default)
        {
            RevokeCurrentCalls++;
            LastRevokedCredential = deviceCredential;

            return Task.FromResult(RevocationResult);
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingCredentialStore :
        ICliDeviceCredentialStore
    {
        public CliDeviceCredentialReadResult ReadResult { get; set; } =
            CliDeviceCredentialReadResult.NotFound();

        public CliDeviceCredentialDeleteResult DeleteResult { get; set; } =
            CliDeviceCredentialDeleteResult.Success();

        public int AvailabilityChecks { get; private set; }

        public int ReadCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public Task<CliDeviceCredentialStoreAvailability> CheckAvailableAsync(
            CancellationToken ct = default)
        {
            AvailabilityChecks++;
            return Task.FromResult(
                CliDeviceCredentialStoreAvailability.AvailableResult());
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
            CancellationToken ct = default) =>
            Task.FromResult(
                CliDeviceCredentialWriteResult.Success());

        public Task<CliDeviceCredentialDeleteResult> DeleteAsync(
            CliDeviceCredentialKey key,
            CancellationToken ct = default)
        {
            DeleteCalls++;
            return Task.FromResult(DeleteResult);
        }
    }
}
