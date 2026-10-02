using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Output;

namespace Mem.Cli.Commands;

/// <summary>
/// Safe local account/session commands for browser-approved MEM CLI device
/// sessions. These commands operate on the selected non-secret profile and OS
/// secret store; they do not use installer-token cookies, browser cookies, or
/// Host Agent shared secrets.
/// </summary>
public static class AccountCommands
{
    public static async Task<int> RunAccountAsync(
        string[] args,
        string subcommand,
        CliOptions options,
        CliOutput output,
        Func<CliOptions, ICliDeviceSessionClient>
            deviceSessionClientFactory,
        ICliDeviceCredentialStore credentialStore)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(deviceSessionClientFactory);
        ArgumentNullException.ThrowIfNull(credentialStore);

        return subcommand switch
        {
            "show" => await ShowAsync(
                options,
                output,
                deviceSessionClientFactory,
                credentialStore),
            _ => WriteUsage(output)
        };
    }

    public static async Task<int> RunLogoutAsync(
        CliOptions options,
        CliOutput output,
        Func<CliOptions, ICliDeviceSessionClient>
            deviceSessionClientFactory,
        ICliDeviceCredentialStore credentialStore)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(deviceSessionClientFactory);
        ArgumentNullException.ThrowIfNull(credentialStore);

        if (!TryCreateCredentialKey(
                options,
                output,
                out var credentialKey))
        {
            return 1;
        }

        CliDeviceCredentialReadResult read;

        try
        {
            read = await credentialStore.ReadAsync(
                credentialKey);
        }
        catch (Exception)
        {
            read = CliDeviceCredentialReadResult.Unavailable();
        }

        if (read.Status == CliDeviceCredentialStoreStatus.NotFound)
        {
            if (output.IsJson)
            {
                output.WriteJson(new
                {
                    source = "mem-cli",
                    status = "signed_out",
                    profile = credentialKey.ProfileName,
                    server = credentialKey.ServerUrl,
                    removed = false,
                    serverRevocationStatus = "not_attempted"
                });
            }
            else
            {
                output.WriteHumanLine(
                    $"No MEM CLI device session is stored for profile: {credentialKey.ProfileName}");
                output.WriteHumanLine(
                    $"Server: {credentialKey.ServerUrl}");
            }

            return 0;
        }

        if (read.Status == CliDeviceCredentialStoreStatus.Unavailable)
        {
            return WriteError(
                output,
                "cli_account_secure_store_unavailable",
                "The OS secret store could not be read safely.");
        }

        var serverRevocationStatus = "not_attempted";
        DateTimeOffset? serverRevokedAtUtc = null;

        if (read.Status == CliDeviceCredentialStoreStatus.Success &&
            !string.IsNullOrWhiteSpace(read.Credential))
        {
            CliDeviceSessionRevocationResult revoked;

            try
            {
                using var deviceSessions = deviceSessionClientFactory(options);
                revoked = await deviceSessions.RevokeCurrentAsync(
                    read.Credential);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                revoked = new CliDeviceSessionRevocationResult(
                    "unavailable");
            }

            serverRevocationStatus = NormalizeRevocationStatus(
                revoked.Status);
            serverRevokedAtUtc = revoked.RevokedAtUtc;
        }

        CliDeviceCredentialDeleteResult deleted;

        try
        {
            deleted = await credentialStore.DeleteAsync(
                credentialKey);
        }
        catch (Exception)
        {
            deleted = CliDeviceCredentialDeleteResult.Unavailable();
        }

        if (!deleted.Succeeded)
        {
            return WriteError(
                output,
                "cli_account_secure_store_unavailable",
                "The OS secret store could not remove the local device session safely.");
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "signed_out",
                profile = credentialKey.ProfileName,
                server = credentialKey.ServerUrl,
                removed = true,
                serverRevocationStatus,
                serverRevokedAtUtc
            });
        }
        else
        {
            output.WriteHumanLine(
                $"Removed local MEM CLI device session for profile: {credentialKey.ProfileName}");
            output.WriteHumanLine(
                $"Server: {credentialKey.ServerUrl}");

            switch (serverRevocationStatus)
            {
                case "revoked":
                    output.WriteHumanLine(
                        "Server-side MEM CLI device session revoked.");
                    break;
                case "unauthenticated":
                    output.WriteHumanLine(
                        "Server-side MEM CLI device session was already rejected or expired.");
                    break;
                case "unavailable":
                    output.WriteHumanLine(
                        "Server-side session revocation could not be confirmed; local session was removed.");
                    break;
            }
        }

        return 0;
    }

    private static async Task<int> ShowAsync(
        CliOptions options,
        CliOutput output,
        Func<CliOptions, ICliDeviceSessionClient>
            deviceSessionClientFactory,
        ICliDeviceCredentialStore credentialStore)
    {
        if (!TryCreateCredentialKey(
                options,
                output,
                out var credentialKey))
        {
            return 1;
        }

        CliDeviceCredentialReadResult read;

        try
        {
            read = await credentialStore.ReadAsync(
                credentialKey);
        }
        catch (Exception)
        {
            read = CliDeviceCredentialReadResult.Unavailable();
        }

        switch (read.Status)
        {
            case CliDeviceCredentialStoreStatus.NotFound:
                return WriteSignedOut(
                    output,
                    credentialKey);

            case CliDeviceCredentialStoreStatus.InvalidCredential:
                return WriteError(
                    output,
                    "cli_account_invalid_local_session",
                    "The stored MEM CLI device session is invalid. Run: mem logout");

            case CliDeviceCredentialStoreStatus.Unavailable:
                return WriteError(
                    output,
                    "cli_account_secure_store_unavailable",
                    "The OS secret store could not be read safely.");

            case CliDeviceCredentialStoreStatus.Success:
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        CliDeviceSessionStatusResult session;

        try
        {
            using var deviceSessions = deviceSessionClientFactory(options);
            session = await deviceSessions.GetCurrentAsync(
                read.Credential!);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            session = new CliDeviceSessionStatusResult(
                "unavailable");
        }

        return session.Status switch
        {
            "authenticated" => WriteAuthenticated(
                output,
                credentialKey,
                session),
            "unauthenticated" => WriteUnauthenticated(
                output,
                credentialKey),
            _ => WriteError(
                output,
                "cli_account_unavailable",
                "The control plane could not verify the stored MEM CLI device session.")
        };
    }

    private static int WriteAuthenticated(
        CliOutput output,
        CliDeviceCredentialKey credentialKey,
        CliDeviceSessionStatusResult session)
    {
        var roles = session.Roles ?? [];

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "authenticated",
                profile = credentialKey.ProfileName,
                server = credentialKey.ServerUrl,
                displayName = session.DisplayName,
                roles,
                idleExpiresAtUtc = session.IdleExpiresAtUtc,
                absoluteExpiresAtUtc = session.AbsoluteExpiresAtUtc
            });
        }
        else
        {
            output.WriteHumanLine("MEM CLI account");
            output.WriteHumanLine(
                $"Profile: {credentialKey.ProfileName}");
            output.WriteHumanLine(
                $"Server: {credentialKey.ServerUrl}");
            output.WriteHumanLine("Status: authenticated");

            if (!string.IsNullOrWhiteSpace(session.DisplayName))
            {
                output.WriteHumanLine(
                    $"Operator: {session.DisplayName}");
            }

            if (roles.Count > 0)
            {
                output.WriteHumanLine(
                    $"Roles: {string.Join(", ", roles)}");
            }

            if (session.IdleExpiresAtUtc is { } idleExpiresAtUtc)
            {
                output.WriteHumanLine(
                    $"Idle expiry: {idleExpiresAtUtc:O}");
            }

            if (session.AbsoluteExpiresAtUtc is { } absoluteExpiresAtUtc)
            {
                output.WriteHumanLine(
                    $"Absolute expiry: {absoluteExpiresAtUtc:O}");
            }
        }

        return 0;
    }

    private static int WriteSignedOut(
        CliOutput output,
        CliDeviceCredentialKey credentialKey)
    {
        if (output.IsJson)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "signed_out",
                profile = credentialKey.ProfileName,
                server = credentialKey.ServerUrl
            });
        }
        else
        {
            output.WriteHumanLine(
                $"No MEM CLI device session is stored for profile: {credentialKey.ProfileName}");
            output.WriteHumanLine(
                $"Server: {credentialKey.ServerUrl}");
            output.WriteHumanLine(
                $"Run: mem login --device --profile {credentialKey.ProfileName}");
        }

        return 1;
    }

    private static int WriteUnauthenticated(
        CliOutput output,
        CliDeviceCredentialKey credentialKey)
    {
        if (output.IsJson)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "unauthenticated",
                profile = credentialKey.ProfileName,
                server = credentialKey.ServerUrl
            });
        }
        else
        {
            output.WriteHumanErrorLine(
                "The stored MEM CLI device session was rejected by the control plane.");
            output.WriteHumanErrorLine(
                $"Profile: {credentialKey.ProfileName}");
            output.WriteHumanErrorLine(
                $"Server: {credentialKey.ServerUrl}");
            output.WriteHumanErrorLine(
                "Run: mem logout");
        }

        return 1;
    }

    private static bool TryCreateCredentialKey(
        CliOptions options,
        CliOutput output,
        out CliDeviceCredentialKey credentialKey)
    {
        credentialKey = null!;

        if (string.IsNullOrWhiteSpace(options.ProfileName))
        {
            WriteError(
                output,
                "cli_account_profile_required",
                "Create or select a local MEM CLI profile first.");

            return false;
        }

        if (!CliDeviceCredentialKey.TryCreate(
                options.ProfileName,
                options.HostAgentUrl,
                out var createdKey) ||
            createdKey is null)
        {
            WriteError(
                output,
                "cli_account_profile_invalid",
                "The selected profile or server URL is not valid for a MEM CLI device session.");

            return false;
        }

        credentialKey = createdKey;
        return true;
    }

    private static string NormalizeRevocationStatus(string? status) =>
        status switch
        {
            "revoked" => "revoked",
            "unauthenticated" => "unauthenticated",
            "unavailable" => "unavailable",
            _ => "unavailable"
        };

    private static int WriteUsage(CliOutput output)
    {
        if (output.IsJson)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "error",
                error = "cli_account_usage"
            });
        }
        else
        {
            output.WriteHumanErrorLine(
                "Usage: mem account show [--profile <name>] [--json]");
        }

        return 1;
    }

    private static int WriteError(
        CliOutput output,
        string error,
        string message)
    {
        if (output.IsJson)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "error",
                error
            });
        }
        else
        {
            output.WriteHumanErrorLine(message);
        }

        return 1;
    }
}
