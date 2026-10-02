using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HostAgent.Options;

namespace HostAgent.Matrix.Users;

public interface ISynapseAdminUserClient
{
    Task<MatrixAdminAuthorityIdentity> ValidateAuthorityAsync(
        string homeserverBaseUrl,
        string accessToken,
        CancellationToken ct);

    Task<MatrixAdminAuthorityLoginResult> LoginAndValidateAuthorityAsync(
        string homeserverBaseUrl,
        string matrixUserId,
        string password,
        CancellationToken ct);

    Task ResetPasswordAsync(
        string homeserverBaseUrl,
        string matrixUserId,
        string newPassword,
        string accessToken,
        bool logoutDevices,
        CancellationToken ct);

    Task DeactivateUserAsync(
        string homeserverBaseUrl,
        string matrixUserId,
        string accessToken,
        bool erase,
        CancellationToken ct);

    Task ReactivateUserAsync(
        string homeserverBaseUrl,
        string matrixUserId,
        string newPassword,
        string accessToken,
        CancellationToken ct);
}

public sealed class SynapseAdminUserClient(
    RuntimeConnectivityContext connectivityContext)
    : ISynapseAdminUserClient
{
    public async Task<MatrixAdminAuthorityIdentity> ValidateAuthorityAsync(
        string homeserverBaseUrl,
        string accessToken,
        CancellationToken ct)
    {
        var baseUri = ValidateInputs(homeserverBaseUrl, accessToken);

        try
        {
            using var client = SynapseControlPlaneHttpClientFactory.Create(baseUri, connectivityContext);
            using var whoAmIRequest = CreateBearerRequest(
                HttpMethod.Get,
                new Uri(baseUri, "/_matrix/client/v3/account/whoami"),
                accessToken);
            using var whoAmIResponse = await client.SendAsync(whoAmIRequest, ct);
            var whoAmIBody = await whoAmIResponse.Content.ReadAsStringAsync(ct);

            ThrowForAuthorityFailure(
                whoAmIResponse.StatusCode,
                "Matrix administrator authority validation",
                whoAmIBody);

            var whoAmI = JsonSerializer.Deserialize<SynapseWhoAmIResponse>(
                whoAmIBody,
                JsonOptions());

            if (string.IsNullOrWhiteSpace(whoAmI?.UserId))
            {
                throw new MatrixAdminAuthorityException(
                    "matrix_admin_authority_identity_invalid",
                    "The Matrix administrator token did not resolve to a user identity.",
                    MatrixAdminAuthorityFailureKind.Rejected);
            }

            var encodedUserId = Uri.EscapeDataString(whoAmI.UserId);
            using var adminRequest = CreateBearerRequest(
                HttpMethod.Get,
                new Uri(baseUri, $"/_synapse/admin/v2/users/{encodedUserId}"),
                accessToken);
            using var adminResponse = await client.SendAsync(adminRequest, ct);
            var adminBody = await adminResponse.Content.ReadAsStringAsync(ct);

            ThrowForAuthorityFailure(
                adminResponse.StatusCode,
                "Synapse administrator authority validation",
                adminBody);

            var adminUser = JsonSerializer.Deserialize<SynapseAdminUserResponse>(
                adminBody,
                JsonOptions());

            if (adminUser?.Admin != true)
            {
                throw new MatrixAdminAuthorityException(
                    "matrix_admin_authority_not_admin",
                    "The supplied Matrix token does not belong to a Synapse server administrator.",
                    MatrixAdminAuthorityFailureKind.Rejected);
            }

            if (adminUser.Deactivated)
            {
                throw new MatrixAdminAuthorityException(
                    "matrix_admin_authority_deactivated",
                    "The supplied Matrix administrator account is deactivated.",
                    MatrixAdminAuthorityFailureKind.Rejected);
            }

            return new MatrixAdminAuthorityIdentity(
                MatrixUserId: whoAmI.UserId,
                IsAdmin: true,
                IsDeactivated: false);
        }
        catch (MatrixAdminAuthorityException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw Unavailable("Matrix administrator authority validation", ex);
        }
    }

    public async Task<MatrixAdminAuthorityLoginResult> LoginAndValidateAuthorityAsync(
        string homeserverBaseUrl,
        string matrixUserId,
        string password,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(homeserverBaseUrl))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_homeserver_url_required",
                "The Matrix homeserver base URL is required.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        if (string.IsNullOrWhiteSpace(matrixUserId))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_admin_authority_identity_required",
                "A Matrix administrator user ID is required.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_admin_authority_password_required",
                "The Matrix administrator password is required.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        var baseUri = new Uri(homeserverBaseUrl.TrimEnd('/'));

        try
        {
            var payload = new SynapsePasswordLoginRequest(
                Type: "m.login.password",
                Identifier: new SynapsePasswordLoginIdentifier(
                    Type: "m.id.user",
                    User: matrixUserId.Trim()),
                Password: password);

            using var client = SynapseControlPlaneHttpClientFactory.Create(baseUri, connectivityContext);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(baseUri, "/_matrix/client/v3/login"));
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions()),
                Encoding.UTF8,
                "application/json");

            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var failureKind = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? MatrixAdminAuthorityFailureKind.Rejected
                    : MatrixAdminAuthorityFailureKind.Unavailable;
                var code = failureKind == MatrixAdminAuthorityFailureKind.Rejected
                    ? "matrix_admin_authority_login_rejected"
                    : "matrix_admin_api_unavailable";
                var detail = failureKind == MatrixAdminAuthorityFailureKind.Rejected
                    ? "The Matrix administrator credentials were rejected by Synapse."
                    : "Matrix administrator login failed because the Synapse API was unavailable.";

                throw new MatrixAdminAuthorityException(
                    code,
                    detail,
                    failureKind,
                    new InvalidOperationException(
                        $"Synapse login returned HTTP {(int)response.StatusCode}: {body}"));
            }

            var login = JsonSerializer.Deserialize<SynapsePasswordLoginResponse>(
                body,
                JsonOptions());

            if (string.IsNullOrWhiteSpace(login?.AccessToken) ||
                string.IsNullOrWhiteSpace(login.UserId))
            {
                throw new MatrixAdminAuthorityException(
                    "matrix_admin_authority_login_invalid",
                    "Synapse login did not return a usable administrator identity and access token.",
                    MatrixAdminAuthorityFailureKind.Unavailable);
            }

            if (!string.Equals(login.UserId, matrixUserId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new MatrixAdminAuthorityException(
                    "matrix_admin_authority_login_identity_mismatch",
                    "Synapse login returned a different Matrix identity than requested.",
                    MatrixAdminAuthorityFailureKind.Rejected);
            }

            var identity = await ValidateAuthorityAsync(
                homeserverBaseUrl,
                login.AccessToken,
                ct);

            if (!string.Equals(identity.MatrixUserId, login.UserId, StringComparison.OrdinalIgnoreCase))
            {
                throw new MatrixAdminAuthorityException(
                    "matrix_admin_authority_login_identity_mismatch",
                    "The validated Matrix administrator identity did not match the login identity.",
                    MatrixAdminAuthorityFailureKind.Rejected);
            }

            return new MatrixAdminAuthorityLoginResult(
                AccessToken: login.AccessToken,
                Identity: identity);
        }
        catch (MatrixAdminAuthorityException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw Unavailable("Matrix administrator login", ex);
        }
    }

    public async Task ResetPasswordAsync(
        string homeserverBaseUrl,
        string matrixUserId,
        string newPassword,
        string accessToken,
        bool logoutDevices,
        CancellationToken ct)
    {
        var baseUri = ValidateInputs(homeserverBaseUrl, accessToken);

        if (string.IsNullOrWhiteSpace(matrixUserId))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_password_reset_user_required",
                "A Matrix user ID is required for password reset.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            throw new MatrixAdminAuthorityException(
                "matrix_password_reset_password_invalid",
                "The new Matrix password must be at least 8 characters.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        try
        {
            var encodedUserId = Uri.EscapeDataString(matrixUserId.Trim());
            var payload = new SynapseAdminUserPasswordRequest(
                Password: newPassword,
                LogoutDevices: logoutDevices);

            using var client = SynapseControlPlaneHttpClientFactory.Create(baseUri, connectivityContext);
            var userUri = new Uri(baseUri, $"/_synapse/admin/v2/users/{encodedUserId}");

            using var preflightRequest = CreateBearerRequest(
                HttpMethod.Get,
                userUri,
                accessToken);
            using var preflightResponse = await client.SendAsync(preflightRequest, ct);
            var preflightBody = await preflightResponse.Content.ReadAsStringAsync(ct);
            ThrowForAuthorityFailure(
                preflightResponse.StatusCode,
                "Synapse password reset preflight",
                preflightBody);

            var target = JsonSerializer.Deserialize<SynapseAdminUserResponse>(
                preflightBody,
                JsonOptions());
            if (!string.Equals(target?.Name, matrixUserId, StringComparison.OrdinalIgnoreCase))
            {
                throw new MatrixAdminAuthorityException(
                    "matrix_password_reset_target_mismatch",
                    "Synapse did not confirm the requested Matrix account before password reset.",
                    MatrixAdminAuthorityFailureKind.InvalidRequest);
            }
            if (target.Deactivated)
            {
                throw new MatrixAdminAuthorityException(
                    "matrix_password_reset_user_inactive",
                    "Password reset is available only for active Matrix users.",
                    MatrixAdminAuthorityFailureKind.InvalidRequest);
            }

            using var request = CreateBearerRequest(
                HttpMethod.Put,
                userUri,
                accessToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions()),
                Encoding.UTF8,
                "application/json");

            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            ThrowForAuthorityFailure(
                response.StatusCode,
                "Synapse password reset",
                body);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                throw new MatrixAdminAuthorityException(
                    "matrix_password_reset_unexpected_create",
                    "Synapse reported account creation instead of an existing-account password reset.",
                    MatrixAdminAuthorityFailureKind.Unavailable);
            }
        }
        catch (MatrixAdminAuthorityException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw Unavailable("Synapse password reset", ex);
        }
    }

    public async Task DeactivateUserAsync(
        string homeserverBaseUrl,
        string matrixUserId,
        string accessToken,
        bool erase,
        CancellationToken ct)
    {
        var baseUri = ValidateInputs(homeserverBaseUrl, accessToken);

        try
        {
            using var client = SynapseControlPlaneHttpClientFactory.Create(baseUri, connectivityContext);
            var target = await ReadTargetAsync(
                client,
                baseUri,
                matrixUserId,
                accessToken,
                "Synapse account-deactivation preflight",
                ct);

            if (target.Deactivated)
            {
                throw new MatrixAdminAuthorityException(
                    "matrix_user_already_deactivated",
                    "The Matrix account is already deactivated.",
                    MatrixAdminAuthorityFailureKind.InvalidRequest);
            }

            var encodedUserId = Uri.EscapeDataString(matrixUserId);
            using var request = CreateBearerRequest(
                HttpMethod.Post,
                new Uri(baseUri, $"/_synapse/admin/v1/deactivate/{encodedUserId}"),
                accessToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new SynapseDeactivateUserRequest(erase), JsonOptions()),
                Encoding.UTF8,
                "application/json");

            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            ThrowForAuthorityFailure(
                response.StatusCode,
                "Synapse account deactivation",
                body);
        }
        catch (MatrixAdminAuthorityException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw Unavailable("Synapse account deactivation", ex);
        }
    }

    public async Task ReactivateUserAsync(
        string homeserverBaseUrl,
        string matrixUserId,
        string newPassword,
        string accessToken,
        CancellationToken ct)
    {
        var baseUri = ValidateInputs(homeserverBaseUrl, accessToken);

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_user_reactivation_password_required",
                "A new Matrix password is required to reactivate the account.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        try
        {
            using var client = SynapseControlPlaneHttpClientFactory.Create(baseUri, connectivityContext);
            var target = await ReadTargetAsync(
                client,
                baseUri,
                matrixUserId,
                accessToken,
                "Synapse account-reactivation preflight",
                ct);

            if (!target.Deactivated)
            {
                throw new MatrixAdminAuthorityException(
                    "matrix_user_not_deactivated",
                    "The Matrix account is already active.",
                    MatrixAdminAuthorityFailureKind.InvalidRequest);
            }

            var encodedUserId = Uri.EscapeDataString(matrixUserId);
            var userUri = new Uri(baseUri, $"/_synapse/admin/v2/users/{encodedUserId}");
            using var request = CreateBearerRequest(HttpMethod.Put, userUri, accessToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(
                    new SynapseReactivateUserRequest(
                        Password: newPassword,
                        LogoutDevices: true,
                        Admin: target.Admin,
                        Deactivated: false),
                    JsonOptions()),
                Encoding.UTF8,
                "application/json");

            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            ThrowForAuthorityFailure(
                response.StatusCode,
                "Synapse account reactivation",
                body);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                throw new MatrixAdminAuthorityException(
                    "matrix_user_reactivation_unexpected_create",
                    "Synapse reported account creation instead of reactivating the existing account.",
                    MatrixAdminAuthorityFailureKind.Unavailable);
            }
        }
        catch (MatrixAdminAuthorityException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw Unavailable("Synapse account reactivation", ex);
        }
    }

    private static async Task<SynapseAdminUserResponse> ReadTargetAsync(
        HttpClient client,
        Uri baseUri,
        string matrixUserId,
        string accessToken,
        string operation,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(matrixUserId))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_admin_target_required",
                "A Matrix user ID is required.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        var encodedUserId = Uri.EscapeDataString(matrixUserId);
        using var request = CreateBearerRequest(
            HttpMethod.Get,
            new Uri(baseUri, $"/_synapse/admin/v2/users/{encodedUserId}"),
            accessToken);
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        ThrowForAuthorityFailure(response.StatusCode, operation, body);

        var target = JsonSerializer.Deserialize<SynapseAdminUserResponse>(body, JsonOptions());
        if (!string.Equals(target?.Name, matrixUserId, StringComparison.OrdinalIgnoreCase))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_admin_target_mismatch",
                "Synapse did not confirm the requested Matrix account.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        return target!;
    }

    private static Uri ValidateInputs(
        string homeserverBaseUrl,
        string accessToken)
    {
        if (string.IsNullOrWhiteSpace(homeserverBaseUrl))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_homeserver_url_required",
                "The Matrix homeserver base URL is required.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_admin_authority_required",
                "Matrix administrator authority is required before resetting passwords.",
                MatrixAdminAuthorityFailureKind.Required);
        }

        return new Uri(homeserverBaseUrl.TrimEnd('/'));
    }

    private static HttpRequestMessage CreateBearerRequest(
        HttpMethod method,
        Uri uri,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken.Trim());
        return request;
    }

    private static void ThrowForAuthorityFailure(
        HttpStatusCode statusCode,
        string operation,
        string responseBody)
    {
        if ((int)statusCode is >= 200 and < 300)
        {
            return;
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new MatrixAdminAuthorityException(
                "matrix_admin_authority_rejected",
                "The stored Matrix administrator authority was rejected by Synapse.",
                MatrixAdminAuthorityFailureKind.Rejected);
        }

        if (statusCode == HttpStatusCode.NotFound)
        {
            throw new MatrixAdminAuthorityException(
                "matrix_admin_target_not_found",
                "The requested Matrix account was not found by Synapse.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        throw new MatrixAdminAuthorityException(
            "matrix_admin_api_unavailable",
            $"{operation} failed because the Synapse Admin API was unavailable.",
            MatrixAdminAuthorityFailureKind.Unavailable,
            new InvalidOperationException(
                $"Synapse Admin API returned HTTP {(int)statusCode}: {responseBody}"));
    }

    private static MatrixAdminAuthorityException Unavailable(
        string operation,
        Exception innerException) =>
        new(
            "matrix_admin_api_unavailable",
            $"{operation} failed because the Synapse Admin API was unavailable.",
            MatrixAdminAuthorityFailureKind.Unavailable,
            innerException);

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record SynapseWhoAmIResponse(
    [property: JsonPropertyName("user_id")]
    string UserId);

public sealed record SynapseAdminUserResponse(
    [property: JsonPropertyName("name")]
    string? Name,

    [property: JsonPropertyName("admin")]
    bool Admin,

    [property: JsonPropertyName("deactivated")]
    bool Deactivated);

public sealed record SynapseAdminUserPasswordRequest(
    string Password,
    bool LogoutDevices);

public sealed record SynapseDeactivateUserRequest(
    bool Erase);

public sealed record SynapseReactivateUserRequest(
    string Password,
    bool LogoutDevices,
    bool Admin,
    bool Deactivated);

public sealed record SynapsePasswordLoginIdentifier(
    string Type,
    string User);

public sealed record SynapsePasswordLoginRequest(
    string Type,
    SynapsePasswordLoginIdentifier Identifier,
    string Password);

public sealed record SynapsePasswordLoginResponse(
    [property: JsonPropertyName("user_id")]
    string UserId,

    [property: JsonPropertyName("access_token")]
    string AccessToken);

