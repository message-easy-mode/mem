using System.Security.Cryptography;
using System.Text;
using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Output;
using Mem.Localization;

namespace Mem.Cli.Commands;

/// <summary>
/// Runs the interactive browser-approved named-device login. This command is
/// intentionally separate from <c>mem auth</c>: ordinary device login uses a
/// selected non-secret local profile and an OS secret store, while future
/// host-local recovery arming remains a narrowly privileged transport-free
/// boundary.
/// </summary>
public static class LoginCommands
{
    private const int OpaqueValueByteLength = 32;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public static async Task<int> RunAsync(
        string[] args,
        CliOptions options,
        CliOutput output,
        Func<CliOptions, ICliDeviceAuthorizationClient>
            deviceAuthorizationClientFactory,
        ICliDeviceCredentialStore credentialStore)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(deviceAuthorizationClientFactory);
        ArgumentNullException.ThrowIfNull(credentialStore);

        // The browser approval journey is interactive. A future automation
        // contract must be deliberately designed as a streaming format rather
        // than mixing multiple unrelated JSON documents on stdout.
        if (options.Json)
        {
            return WriteError(
                output,
                "cli_login_interactive_required",
                CliMessageKeys.LoginErrorInteractiveRequired);
        }

        if (!HasOption(args, "--device"))
        {
            return WriteError(
                output,
                "cli_login_device_required",
                CliMessageKeys.LoginErrorDeviceRequired);
        }

        if (string.IsNullOrWhiteSpace(options.ProfileName))
        {
            return WriteError(
                output,
                "cli_login_profile_required",
                CliMessageKeys.LoginErrorProfileRequired);
        }

        if (!CliDeviceCredentialKey.TryCreate(
                options.ProfileName,
                options.HostAgentUrl,
                out var credentialKey))
        {
            return WriteError(
                output,
                "cli_login_profile_invalid",
                CliMessageKeys.LoginErrorProfileInvalid);
        }

        // The secure-store round trip happens before any anonymous
        // authorization attempt is created. This avoids creating an approved
        // server session when the local operator cannot store it safely.
        CliDeviceCredentialStoreAvailability availability;

        try
        {
            availability = await credentialStore.CheckAvailableAsync();
        }
        catch (Exception)
        {
            availability = CliDeviceCredentialStoreAvailability.Unavailable();
        }

        if (!availability.Available)
        {
            return WriteError(
                output,
                "cli_login_secure_store_unavailable",
                CliMessageKeys.LoginErrorSecureStoreUnavailable);
        }

        CliDeviceCredentialReadResult existingCredential;

        try
        {
            existingCredential = await credentialStore.ReadAsync(
                credentialKey);
        }
        catch (Exception)
        {
            existingCredential =
                CliDeviceCredentialReadResult.Unavailable();
        }

        if (existingCredential.Succeeded)
        {
            output.WriteLocalizedLine(
                CliMessageKeys.LoginExistingSessionStored,
                new Dictionary<string, object?>
                {
                    ["profileName"] = credentialKey.ProfileName
                });
            output.WriteLocalizedLine(
                CliMessageKeys.LoginServerLine,
                new Dictionary<string, object?>
                {
                    ["serverUrl"] = credentialKey.ServerUrl
                });
            output.WriteLocalizedLine(
                CliMessageKeys.LoginExistingNoNewAuthorization);

            return 0;
        }

        if (existingCredential.Status !=
            CliDeviceCredentialStoreStatus.NotFound)
        {
            return WriteError(
                output,
                "cli_login_secure_store_unavailable",
                CliMessageKeys.LoginErrorSecureStoreReadUnavailable);
        }

        var verifier = CreateOpaqueValue();
        var verifierChallenge = CreateVerifierChallenge(
            verifier);

        CliDeviceAuthorizationStartResult started;

        try
        {
            using var deviceAuthorizations =
                deviceAuthorizationClientFactory(options);

            started = await deviceAuthorizations.StartAsync(
                verifierChallenge,
                "MEM CLI");

            if (!TryValidateStarted(
                    started,
                    credentialKey.ServerUrl,
                    out var authorizationId,
                    out var userCode,
                    out var expiresAtUtc,
                    out var browserApprovalUrl))
            {
                return WriteStartFailure(
                    output,
                    started);
            }

            output.WriteLocalizedLine(CliMessageKeys.LoginTitle);
            output.WriteLocalizedLine(
                CliMessageKeys.LoginProfileLine,
                new Dictionary<string, object?>
                {
                    ["profileName"] = credentialKey.ProfileName
                });
            output.WriteLocalizedLine(
                CliMessageKeys.LoginServerLine,
                new Dictionary<string, object?>
                {
                    ["serverUrl"] = credentialKey.ServerUrl
                });
            output.WriteHumanLine();
            output.WriteLocalizedLine(CliMessageKeys.LoginOpenBrowser);
            output.WriteHumanLine(browserApprovalUrl);
            output.WriteHumanLine();
            output.WriteLocalizedLine(CliMessageKeys.LoginEnterDeviceCode);
            output.WriteHumanLine(userCode);
            output.WriteHumanLine();
            output.WriteLocalizedLine(CliMessageKeys.LoginWaitingApproval);

            return await PollUntilCompleteAsync(
                deviceAuthorizations,
                credentialStore,
                credentialKey,
                authorizationId,
                verifier,
                expiresAtUtc,
                output);
        }
        catch (OperationCanceledException)
        {
            return WriteError(
                output,
                "cli_login_cancelled",
                CliMessageKeys.LoginErrorCancelled);
        }
        catch (Exception)
        {
            return WriteError(
                output,
                "cli_login_unavailable",
                CliMessageKeys.LoginErrorUnavailable);
        }
    }

    private static async Task<int> PollUntilCompleteAsync(
        ICliDeviceAuthorizationClient deviceAuthorizations,
        ICliDeviceCredentialStore credentialStore,
        CliDeviceCredentialKey credentialKey,
        Guid authorizationId,
        string verifier,
        DateTimeOffset expiresAtUtc,
        CliOutput output)
    {
        while (DateTimeOffset.UtcNow < expiresAtUtc)
        {
            CliDeviceAuthorizationPollResult poll;

            try
            {
                poll = await deviceAuthorizations.PollAsync(
                    authorizationId,
                    verifier);
            }
            catch (OperationCanceledException)
            {
                return WriteError(
                    output,
                    "cli_login_cancelled",
                    CliMessageKeys.LoginErrorCancelled);
            }
            catch (Exception)
            {
                return WriteError(
                    output,
                    "cli_login_unreachable",
                    CliMessageKeys.LoginErrorUnreachableWaiting);
            }

            switch (poll.Status)
            {
                case "authorized":
                    return await StoreAuthorizedCredentialAsync(
                        poll,
                        credentialStore,
                        credentialKey,
                        output);

                case "authorization_pending":
                    if (!await WaitForNextPollAsync(
                            expiresAtUtc,
                            PollInterval))
                    {
                        break;
                    }

                    continue;

                case "rate_limited":
                    if (!await WaitForNextPollAsync(
                            expiresAtUtc,
                            poll.RetryAfter ?? PollInterval))
                    {
                        break;
                    }

                    continue;

                case "authorization_denied":
                    return WriteError(
                        output,
                        "cli_login_authorization_denied",
                        CliMessageKeys.LoginErrorAuthorizationDenied);

                case "authorization_expired":
                    return WriteError(
                        output,
                        "cli_login_authorization_expired",
                        CliMessageKeys.LoginErrorAuthorizationExpired);

                case "unreachable":
                    return WriteError(
                        output,
                        "cli_login_unreachable",
                        CliMessageKeys.LoginErrorUnreachableWaiting);

                default:
                    return WriteError(
                        output,
                        "cli_login_authorization_failed",
                        CliMessageKeys.LoginErrorAuthorizationFailed);
            }
        }

        return WriteError(
            output,
            "cli_login_authorization_expired",
            CliMessageKeys.LoginErrorAuthorizationExpired);
    }

    private static async Task<int> StoreAuthorizedCredentialAsync(
        CliDeviceAuthorizationPollResult poll,
        ICliDeviceCredentialStore credentialStore,
        CliDeviceCredentialKey credentialKey,
        CliOutput output)
    {
        if (!CliDeviceCredentialValidator.IsValid(
                poll.DeviceCredential))
        {
            return WriteError(
                output,
                "cli_login_invalid_credential",
                CliMessageKeys.LoginErrorInvalidCredential);
        }

        CliDeviceCredentialWriteResult stored;

        try
        {
            stored = await credentialStore.StoreAsync(
                credentialKey,
                poll.DeviceCredential!);
        }
        catch (Exception)
        {
            stored = CliDeviceCredentialWriteResult.Unavailable();
        }

        if (!stored.Succeeded)
        {
            return WriteError(
                output,
                "cli_login_secure_store_write_failed",
                CliMessageKeys.LoginErrorSecureStoreWriteFailed);
        }

        output.WriteLocalizedLine(
            CliMessageKeys.LoginApproved,
            new Dictionary<string, object?>
            {
                ["profileName"] = credentialKey.ProfileName
            });
        output.WriteLocalizedLine(
            CliMessageKeys.LoginCredentialStored);

        if (poll.IdleExpiresAtUtc is { } idleExpiresAtUtc)
        {
            output.WriteLocalizedLine(
                CliMessageKeys.LoginIdleExpiry,
                new Dictionary<string, object?>
                {
                    ["expiresAtUtc"] = idleExpiresAtUtc.ToString("O")
                });
        }

        if (poll.AbsoluteExpiresAtUtc is { } absoluteExpiresAtUtc)
        {
            output.WriteLocalizedLine(
                CliMessageKeys.LoginAbsoluteExpiry,
                new Dictionary<string, object?>
                {
                    ["expiresAtUtc"] = absoluteExpiresAtUtc.ToString("O")
                });
        }

        return 0;
    }

    private static int WriteStartFailure(
        CliOutput output,
        CliDeviceAuthorizationStartResult started)
    {
        return started.Status switch
        {
            "rate_limited" => WriteError(
                output,
                "cli_login_rate_limited",
                CliMessageKeys.LoginErrorRateLimited),
            "unreachable" => WriteError(
                output,
                "cli_login_unreachable",
                CliMessageKeys.LoginErrorUnreachableStart),
            _ => WriteError(
                output,
                "cli_login_authorization_unavailable",
                CliMessageKeys.LoginErrorAuthorizationUnavailable)
        };
    }

    private static bool TryValidateStarted(
        CliDeviceAuthorizationStartResult started,
        string serverUrl,
        out Guid authorizationId,
        out string userCode,
        out DateTimeOffset expiresAtUtc,
        out string browserApprovalUrl)
    {
        authorizationId = Guid.Empty;
        userCode = string.Empty;
        expiresAtUtc = default;
        browserApprovalUrl = string.Empty;

        if (started.Status != "authorization_started" ||
            started.AuthorizationId is not { } suppliedAuthorizationId ||
            suppliedAuthorizationId == Guid.Empty ||
            !TryValidateUserCode(started.UserCode, out var suppliedUserCode) ||
            started.ExpiresAtUtc is not { } suppliedExpiry ||
            suppliedExpiry <= DateTimeOffset.UtcNow ||
            !TryValidateBrowserApprovalUrl(
                started.BrowserApprovalUrl,
                serverUrl,
                out var suppliedBrowserApprovalUrl))
        {
            return false;
        }

        authorizationId = suppliedAuthorizationId;
        userCode = suppliedUserCode;
        expiresAtUtc = suppliedExpiry;
        browserApprovalUrl = suppliedBrowserApprovalUrl;

        return true;
    }

    private static bool TryValidateUserCode(
        string? value,
        out string userCode)
    {
        userCode = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();

        if (candidate.Length != 9 ||
            candidate[4] != '-')
        {
            return false;
        }

        for (var index = 0; index < candidate.Length; index++)
        {
            if (index == 4)
            {
                continue;
            }

            var character = candidate[index];

            var isUppercaseLetter = character is >= 'A' and <= 'Z';
            var isDigit = character is >= '0' and <= '9';

            if (!isUppercaseLetter && !isDigit)
            {
                return false;
            }
        }

        userCode = candidate;
        return true;
    }

    private static bool TryValidateBrowserApprovalUrl(
        string? value,
        string serverUrl,
        out string browserApprovalUrl)
    {
        browserApprovalUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(
                value.Trim(),
                UriKind.Absolute,
                out var uri) ||
            !Uri.TryCreate(
                serverUrl,
                UriKind.Absolute,
                out var serverUri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.Equals(
                uri.AbsolutePath,
                "/cli/authorize",
                StringComparison.Ordinal))
        {
            return false;
        }

        var isHttps = string.Equals(
            uri.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase);

        var isLoopbackHttp = string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) &&
            uri.IsLoopback;

        if (!isHttps && !isLoopbackHttp)
        {
            return false;
        }

        var approvalOrigin = uri.GetLeftPart(
            UriPartial.Authority);
        var selectedServerOrigin = serverUri.GetLeftPart(
            UriPartial.Authority);

        if (!string.Equals(
                approvalOrigin,
                selectedServerOrigin,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        browserApprovalUrl = uri.GetLeftPart(
            UriPartial.Path);

        return true;
    }

    private static async Task<bool> WaitForNextPollAsync(
        DateTimeOffset expiresAtUtc,
        TimeSpan requestedDelay)
    {
        var remaining = expiresAtUtc - DateTimeOffset.UtcNow;

        if (remaining <= TimeSpan.Zero)
        {
            return false;
        }

        var delay = requestedDelay <= TimeSpan.Zero
            ? TimeSpan.Zero
            : requestedDelay <= remaining
                ? requestedDelay
                : remaining;

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay);
        }

        return DateTimeOffset.UtcNow < expiresAtUtc;
    }

    private static string CreateOpaqueValue() =>
        Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(
                OpaqueValueByteLength))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string CreateVerifierChallenge(
        string verifier) =>
        Convert.ToBase64String(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(verifier)))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static bool HasOption(
        IEnumerable<string> args,
        string optionName) =>
        args.Any(argument => string.Equals(
            argument,
            optionName,
            StringComparison.OrdinalIgnoreCase));

    private static int WriteError(
        CliOutput output,
        string error,
        string messageKey)
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
            output.WriteLocalizedErrorLine(messageKey);
        }

        return 1;
    }
}
