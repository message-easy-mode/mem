using System.Net;
using System.Reflection;
using Seq.Api;
using Seq.Api.Model.Inputs;
using Seq.Api.Model.Shared;
using Seq.Api.Model.Security;

namespace Modules.Integrations.Seq.Services;

public sealed record SeqAdministrationApiKey(
    string Id,
    string Title,
    string? OwnerId,
    string? TokenPrefix,
    string? Token);

public interface ISeqAdministrationSession : IAsyncDisposable
{
    Task<IReadOnlyList<SeqAdministrationApiKey>> ListSharedApiKeysAsync(
        CancellationToken cancellationToken);

    Task<SeqAdministrationApiKey> CreateMemIngestionApiKeyAsync(
        CancellationToken cancellationToken);

    Task RemoveApiKeyAsync(
        string apiKeyId,
        CancellationToken cancellationToken);

    Task<string?> FindVerificationEventAsync(
        string verificationId,
        DateTimeOffset fromUtc,
        CancellationToken cancellationToken);
}

public interface ISeqAdministrationClientFactory
{
    Task<ISeqAdministrationSession> AuthenticateAsync(
        Uri serverUrl,
        string username,
        string password,
        CancellationToken cancellationToken);
}

/// <summary>
/// Approved-version adapter around Datalust's official Seq.Api client. The
/// authenticated connection exists only for the bounded connection operation;
/// cookies and CSRF state are disposed with the session.
/// </summary>
public sealed class SeqApiAdministrationClientFactory(
    SeqDiagnosticsOptions options) : ISeqAdministrationClientFactory
{
    public async Task<ISeqAdministrationSession> AuthenticateAsync(
        Uri serverUrl,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            throw new SeqOperationException(
                "seq_administrator_credentials_invalid",
                Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest,
                "The Seq administrator credentials are invalid.");
        }

        var connection = new SeqConnection(
            serverUrl.ToString(),
            createHttpMessageHandler: cookies => new HttpClientHandler
            {
                CookieContainer = cookies,
                UseCookies = true,
                AllowAutoRedirect = false
            });
        connection.Client.HttpClient.Timeout = TimeSpan.FromSeconds(
            Math.Clamp(options.ConnectionTimeoutSeconds, 5, 60));

        try
        {
            await connection.Users.LoginAsync(
                username.Trim(),
                password,
                cancellationToken);
            return new SeqApiAdministrationSession(connection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            connection.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            connection.Dispose();
            throw TranslateAuthenticationFailure(exception);
        }
    }

    internal static SeqOperationException TranslateAuthenticationFailure(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is TimeoutException or TaskCanceledException)
        {
            return new SeqOperationException(
                "seq_administration_timeout",
                Microsoft.AspNetCore.Http.StatusCodes.Status504GatewayTimeout,
                "MEM timed out while contacting the Seq administration API.");
        }

        var statusCode = TryGetHttpStatusCode(exception);
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return new SeqOperationException(
                "seq_administrator_credentials_rejected",
                Microsoft.AspNetCore.Http.StatusCodes.Status401Unauthorized,
                "Seq rejected the configured administrator username or supplied password.");
        }

        if (statusCode == HttpStatusCode.Forbidden)
        {
            return new SeqOperationException(
                "seq_administrator_access_denied",
                Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden,
                "Seq did not grant the configured account administrator access.");
        }

        if (statusCode == HttpStatusCode.NotFound ||
            statusCode == HttpStatusCode.MethodNotAllowed)
        {
            return ApiContractUnavailable();
        }

        if (statusCode is { } responseStatus && (int)responseStatus >= 500)
        {
            return new SeqOperationException(
                "seq_administration_unavailable",
                Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable,
                "Seq's administration API is currently unavailable.");
        }

        if (exception is HttpRequestException)
        {
            return new SeqOperationException(
                "seq_administration_unreachable",
                Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable,
                "MEM could not reach the Seq administration API.");
        }

        return ApiContractUnavailable();
    }

    private static SeqOperationException ApiContractUnavailable() => new(
        "seq_administration_api_incompatible",
        Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable,
        "The approved Seq administration API contract was not available. Review the safe Diagnostics evidence before retrying.");

    private static HttpStatusCode? TryGetHttpStatusCode(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException requestException &&
                requestException.StatusCode is { } requestStatus)
            {
                return requestStatus;
            }

            foreach (var propertyName in new[]
                     {
                         "StatusCode",
                         "ResponseStatusCode",
                         "HttpStatusCode"
                     })
            {
                PropertyInfo? property;
                try
                {
                    property = current.GetType().GetProperty(
                        propertyName,
                        BindingFlags.Instance | BindingFlags.Public);
                }
                catch
                {
                    continue;
                }

                if (property?.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(current);
                }
                catch
                {
                    continue;
                }

                if (value is HttpStatusCode status)
                {
                    return status;
                }

                if (value is int numeric && Enum.IsDefined(typeof(HttpStatusCode), numeric))
                {
                    return (HttpStatusCode)numeric;
                }
            }
        }

        return null;
    }

    private sealed class SeqApiAdministrationSession(
        SeqConnection connection) : ISeqAdministrationSession
    {
        private const string ApiKeyTitle = "MEM Control Plane";
        private readonly Dictionary<string, ApiKeyEntity> _knownKeys =
            new(StringComparer.Ordinal);
        private bool _disposed;

        public async Task<IReadOnlyList<SeqAdministrationApiKey>> ListSharedApiKeysAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var entities = await connection.ApiKeys.ListAsync(
                ownerId: null,
                shared: true,
                cancellationToken: cancellationToken);
            foreach (var entity in entities)
            {
                if (!string.IsNullOrWhiteSpace(entity.Id))
                {
                    _knownKeys[entity.Id] = entity;
                }
            }

            return entities
                .Select(ToProjection)
                .ToArray();
        }

        public async Task<SeqAdministrationApiKey> CreateMemIngestionApiKeyAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var entity = await connection.ApiKeys.TemplateAsync(cancellationToken);
            entity.Title = ApiKeyTitle;
            entity.OwnerId = null;
            entity.Token = null;
            entity.AssignedPermissions = [Permission.Ingest];
            entity.InputSettings ??= new InputSettingsPart();
            entity.InputSettings.AppliedProperties =
            [
                new EventPropertyPart("Source", ApiKeyTitle)
            ];

            var created = await connection.ApiKeys.AddAsync(
                entity,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(created.Id) ||
                string.IsNullOrWhiteSpace(created.Token))
            {
                throw new SeqOperationException(
                    "seq_ingestion_key_creation_failed",
                    Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable,
                    "Seq did not return a usable ingestion credential.");
            }

            _knownKeys[created.Id] = created;
            return ToProjection(created);
        }

        public async Task RemoveApiKeyAsync(
            string apiKeyId,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!_knownKeys.TryGetValue(apiKeyId, out var entity))
            {
                var entities = await connection.ApiKeys.ListAsync(
                    ownerId: null,
                    shared: true,
                    cancellationToken);
                entity = entities.SingleOrDefault(candidate =>
                    string.Equals(candidate.Id, apiKeyId, StringComparison.Ordinal));
            }

            if (entity is null)
            {
                return;
            }

            await connection.ApiKeys.RemoveAsync(entity, cancellationToken);
            _knownKeys.Remove(apiKeyId);
        }

        public async Task<string?> FindVerificationEventAsync(
            string verificationId,
            DateTimeOffset fromUtc,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var escaped = verificationId
                .Replace("'", "''", StringComparison.Ordinal);
            var events = await connection.Events.ListAsync(
                filter: $"VerificationId = '{escaped}'",
                count: 1,
                fromDateUtc: fromUtc.UtcDateTime,
                cancellationToken: cancellationToken);
            return events.FirstOrDefault()?.Id;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await connection.Users.LogoutAsync(timeout.Token);
            }
            catch
            {
                // Disposing the connection remains authoritative and clears the
                // operation-scoped cookie container and CSRF state.
            }
            finally
            {
                connection.Dispose();
                _knownKeys.Clear();
            }
        }

        private static SeqAdministrationApiKey ToProjection(ApiKeyEntity entity) =>
            new(
                entity.Id,
                entity.Title,
                entity.OwnerId,
                entity.TokenPrefix,
                entity.Token);

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
