using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Modules.Integrations.Seq.Contracts;
using Shared.ControlPlane.Runtime;

namespace Modules.Integrations.Seq.Services;

public sealed record SeqConnectionProvisioningResult(
    string ApiKeyId,
    string VerificationId,
    string EventId,
    DateTimeOffset VerifiedAtUtc,
    bool ReusedCredential);

public interface ISeqConnectionProvisioner
{
    Task<SeqConnectionProvisioningResult> ProvisionAndVerifyAsync(
        string administratorPassword,
        Guid operationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Creates one narrowly-scoped MEM ingestion credential, stores the token only
/// in the protected server-side secret file, sends a harmless CLEF event, and
/// proves that the exact event can be read back through the authenticated Seq
/// administration session. It never enables MEM delivery.
/// </summary>
public sealed class SeqConnectionProvisioner(
    SeqDiagnosticsOptions options,
    SeqEffectiveConfigurationProvider effectiveConfigurationProvider,
    ISeqRuntimeStatusReader runtimeStatusReader,
    ISeqRuntimeHealthVerifier healthVerifier,
    ISeqAdministrationClientFactory administrationClientFactory,
    IMemManagedServiceAuthorityResolver authorityResolver,
    IHttpClientFactory httpClientFactory,
    SeqSecretResolver secretResolver,
    SeqSecretFileWriter secretWriter,
    ISeqBootstrapStateStore bootstrapStateStore,
    MemControlPlaneRuntimeContext runtimeContext,
    TimeProvider timeProvider,
    SeqRuntimeContextProfile? runtimeProfile = null) : ISeqConnectionProvisioner
{
    public const string ApiKeyTitle = "MEM Control Plane";
    public const string VerificationEventCode =
        "diagnostics.seq_connection_verification";

    public async Task<SeqConnectionProvisioningResult> ProvisionAndVerifyAsync(
        string administratorPassword,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(administratorPassword) ||
            administratorPassword.Length > options.MaximumAdministratorPasswordLength)
        {
            throw new SeqOperationException(
                "seq_administrator_password_invalid",
                StatusCodes.Status400BadRequest,
                "Enter the current Seq administrator password.");
        }

        var effectiveOptions = effectiveConfigurationProvider.CreateEffectiveOptions();
        var runtime = await runtimeStatusReader.GetStatusAsync(cancellationToken);
        if (!runtime.Managed || !runtime.Running || !runtime.UsesApprovedRuntime)
        {
            throw new SeqOperationException(
                runtime.WarningCode ?? "seq_runtime_not_ready_for_connection",
                StatusCodes.Status409Conflict,
                "A running MEM-managed Seq runtime using the approved image is required before MEM can connect event delivery.");
        }

        var health = await healthVerifier.VerifyAsync(cancellationToken);
        if (!health.Passed)
        {
            throw new SeqOperationException(
                health.WarningCode ?? "seq_health_verification_failed",
                StatusCodes.Status503ServiceUnavailable,
                "Seq did not pass the bounded health verification required before credential provisioning.");
        }

        var stateRead = bootstrapStateStore.Read();
        var state = stateRead.State;
        if (state is null ||
            state.ManagementEnabled != true ||
            state.RuntimeVerifiedAtUtc is null)
        {
            throw new SeqOperationException(
                stateRead.WarningCode ?? "seq_bootstrap_state_not_verified",
                StatusCodes.Status409Conflict,
                "The Seq runtime is not backed by verified MEM bootstrap state.");
        }

        Uri administrationAuthority;
        Uri ingestionAuthority;
        try
        {
            var authoritySource = SeqManagedServiceAuthoritySourceFactory.Create(
                effectiveOptions,
                runtime,
                runtimeProfile ?? SeqRuntimeContextProfile.Create(
                    runtimeContext,
                    effectiveOptions));
            administrationAuthority = authorityResolver.Resolve(
                MemManagedServicePurposes.Administration,
                authoritySource).Authority;
            ingestionAuthority = authorityResolver.Resolve(
                MemManagedServicePurposes.Ingestion,
                authoritySource).Authority;
        }
        catch (MemManagedServiceAuthorityException)
        {
            throw new SeqOperationException(
                "seq_runtime_authority_unavailable",
                StatusCodes.Status409Conflict,
                "The active Control Plane runtime could not resolve a server-owned Seq administration authority.");
        }

        var localSecret = secretResolver.ResolveApiKey(effectiveOptions);
        await using var session = await administrationClientFactory.AuthenticateAsync(
            administrationAuthority,
            effectiveOptions.AdministratorUserName,
            administratorPassword,
            cancellationToken);

        var matchingKeys = (await session.ListSharedApiKeysAsync(cancellationToken))
            .Where(key => string.IsNullOrWhiteSpace(key.OwnerId) &&
                          string.Equals(
                              key.Title,
                              ApiKeyTitle,
                              StringComparison.Ordinal))
            .ToArray();
        if (matchingKeys.Length > 1)
        {
            throw new SeqOperationException(
                "seq_ingestion_key_duplicate",
                StatusCodes.Status409Conflict,
                "More than one MEM Control Plane ingestion key exists in Seq. Resolve the conflict in Seq before retrying.");
        }

        if (state.DeliveryVerifiedAtUtc is not null &&
            state.IngestionCredentialState == "available" &&
            localSecret.Available &&
            matchingKeys.Length == 1 &&
            TokenMatchesPrefix(localSecret.Value, matchingKeys[0].TokenPrefix) &&
            string.Equals(
                state.IngestionCredentialId,
                matchingKeys[0].Id,
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(state.LastDeliveryVerificationEventId))
        {
            return new SeqConnectionProvisioningResult(
                matchingKeys[0].Id,
                state.LastDeliveryVerificationId ?? "already-verified",
                state.LastDeliveryVerificationEventId,
                state.DeliveryVerifiedAtUtc.Value,
                ReusedCredential: true);
        }

        SeqAdministrationApiKey key;
        var created = false;
        if (matchingKeys.Length == 1)
        {
            if (!localSecret.Available)
            {
                throw new SeqOperationException(
                    "seq_ingestion_key_token_missing",
                    StatusCodes.Status409Conflict,
                    "The MEM ingestion key exists in Seq, but its server-side token is unavailable. MEM will not create a duplicate key.");
            }

            key = matchingKeys[0];
            if (!TokenMatchesPrefix(localSecret.Value, key.TokenPrefix))
            {
                throw new SeqOperationException(
                    "seq_ingestion_key_identity_mismatch",
                    StatusCodes.Status409Conflict,
                    "The stored MEM ingestion token does not match the key registered in Seq.");
            }
        }
        else
        {
            if (localSecret.Available)
            {
                throw new SeqOperationException(
                    "seq_ingestion_key_record_missing",
                    StatusCodes.Status409Conflict,
                    "A server-side MEM ingestion token exists, but the corresponding Seq key is missing. MEM will not overwrite the secret or create an ambiguous replacement.");
            }

            key = await session.CreateMemIngestionApiKeyAsync(cancellationToken);
            created = true;
            try
            {
                await secretWriter.WriteIngestionApiKeyAsync(
                    key.Token ?? string.Empty,
                    cancellationToken);
            }
            catch
            {
                try
                {
                    await session.RemoveApiKeyAsync(
                        key.Id,
                        CancellationToken.None);
                }
                catch
                {
                    throw new SeqOperationException(
                        "seq_ingestion_key_cleanup_unproven",
                        StatusCodes.Status503ServiceUnavailable,
                        "Seq created the MEM ingestion key, but MEM could not persist the token or prove compensating key removal. Review Seq before retrying.");
                }

                throw;
            }

            localSecret = secretResolver.ResolveApiKey(effectiveOptions);
            if (!localSecret.Available ||
                !string.Equals(localSecret.Value, key.Token, StringComparison.Ordinal))
            {
                throw new SeqOperationException(
                    "seq_ingestion_key_secret_unavailable",
                    StatusCodes.Status503ServiceUnavailable,
                    "MEM wrote the ingestion credential but could not resolve the same token from the protected effective secret source.");
            }
        }

        var verificationId = $"seq-connect-{Guid.NewGuid():N}";
        var sentAt = timeProvider.GetUtcNow();
        await SendVerificationEventAsync(
            ingestionAuthority,
            localSecret.Value!,
            verificationId,
            operationId,
            sentAt,
            cancellationToken);

        string? eventId = null;
        var verificationAttempts = Math.Clamp(
            options.ConnectionVerificationAttemptCount,
            1,
            60);
        for (var attempt = 1; attempt <= verificationAttempts; attempt++)
        {
            eventId = await session.FindVerificationEventAsync(
                verificationId,
                sentAt.AddMinutes(-1),
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(eventId))
            {
                break;
            }

            if (attempt < verificationAttempts)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Clamp(
                        options.ConnectionVerificationPollIntervalSeconds,
                        1,
                        10)),
                    cancellationToken);
            }
        }

        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new SeqOperationException(
                "seq_verification_event_not_found",
                StatusCodes.Status503ServiceUnavailable,
                "Seq accepted the MEM verification event, but MEM could not read the exact event back before the bounded timeout.");
        }

        var verifiedAt = timeProvider.GetUtcNow();
        await bootstrapStateStore.WriteAsync(
            state with
            {
                SetupStage = "connected",
                IngestionCredentialState = "available",
                IngestionCredentialId = key.Id,
                DeliveryVerifiedAtUtc = verifiedAt,
                LastDeliveryVerificationId = verificationId,
                LastDeliveryVerificationEventId = eventId,
                LastOperationId = operationId
            },
            cancellationToken);

        return new SeqConnectionProvisioningResult(
            key.Id,
            verificationId,
            eventId,
            verifiedAt,
            ReusedCredential: !created);
    }

    private async Task SendVerificationEventAsync(
        Uri ingestionBaseUrl,
        string apiKey,
        string verificationId,
        Guid operationId,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        var target = new Uri(
            ingestionBaseUrl.ToString().TrimEnd('/') + "/ingest/clef",
            UriKind.Absolute);
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@t"] = timestamp.UtcDateTime.ToString("O"),
            ["@mt"] = "MEM verified dedicated Seq ingestion for {VerificationId}",
            ["@l"] = "Information",
            ["VerificationId"] = verificationId,
            ["OperationId"] = operationId,
            ["EventCode"] = VerificationEventCode,
            ["Feature"] = "diagnostics",
            ["Source"] = ApiKeyTitle,
            ["RuntimeMode"] = runtimeContext.RuntimeMode,
            ["ControlPlaneInstanceId"] = runtimeContext.ControlPlaneInstanceId,
            ["ApiProcessInstanceId"] = runtimeContext.ApiProcessInstanceId,
            ["Environment"] = runtimeContext.EnvironmentName,
            ["Version"] = runtimeContext.Version,
            ["Commit"] = runtimeContext.Commit
        }) + "\n";

        using var request = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = new StringContent(
                payload,
                Encoding.UTF8,
                "application/vnd.serilog.clef")
        };
        request.Headers.TryAddWithoutValidation("X-Seq-ApiKey", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var client = httpClientFactory.CreateClient("seq-ingestion-verification");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SeqOperationException(
                "seq_verification_event_rejected",
                StatusCodes.Status503ServiceUnavailable,
                "Seq rejected the authenticated MEM verification event.");
        }
    }

    private static bool TokenMatchesPrefix(string? token, string? tokenPrefix)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(tokenPrefix) &&
               token.StartsWith(tokenPrefix, StringComparison.Ordinal);
    }
}
