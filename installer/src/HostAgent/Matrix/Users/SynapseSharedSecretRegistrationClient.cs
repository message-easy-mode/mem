using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HostAgent.Options;

namespace HostAgent.Matrix.Users;

public interface ISynapseSharedSecretRegistrationClient
{
    Task<SynapseSharedSecretRegistrationResult> RegisterAsync(
        string homeserverBaseUrl,
        string username,
        string password,
        bool admin,
        string sharedSecret,
        CancellationToken ct);
}

public sealed class SynapseSharedSecretRegistrationClient(
    RuntimeConnectivityContext connectivityContext)
    : ISynapseSharedSecretRegistrationClient
{
    public async Task<SynapseSharedSecretRegistrationResult> RegisterAsync(
        string homeserverBaseUrl,
        string username,
        string password,
        bool admin,
        string sharedSecret,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(homeserverBaseUrl))
        {
            throw new InvalidOperationException("Matrix homeserver base URL is required.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Matrix username is required.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Matrix password is required.");
        }

        if (string.IsNullOrWhiteSpace(sharedSecret))
        {
            throw new InvalidOperationException("Matrix shared registration secret is required.");
        }

        var baseUri = new Uri(homeserverBaseUrl.TrimEnd('/'));

        using var client = SynapseControlPlaneHttpClientFactory.Create(baseUri, connectivityContext);

        var nonceResponse = await client.GetAsync(
            new Uri(baseUri, "/_synapse/admin/v1/register"),
            ct);

        var nonceBody = await nonceResponse.Content.ReadAsStringAsync(ct);

        if (!nonceResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Synapse registration nonce request failed with HTTP {(int)nonceResponse.StatusCode}: {nonceBody}");
        }

        var noncePayload = JsonSerializer.Deserialize<SynapseRegistrationNonceResponse>(
            nonceBody,
            JsonOptions());

        var nonce = noncePayload?.Nonce;

        if (string.IsNullOrWhiteSpace(nonce))
        {
            throw new InvalidOperationException("Synapse registration nonce response did not include a nonce.");
        }

        var mac = CreateMac(
            nonce,
            username,
            password,
            admin,
            sharedSecret);

        var request = new SynapseSharedSecretRegistrationRequest(
            Nonce: nonce,
            Username: username,
            Password: password,
            Admin: admin,
            Mac: mac);

        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions()),
            Encoding.UTF8,
            "application/json");

        var registerResponse = await client.PostAsync(
            new Uri(baseUri, "/_synapse/admin/v1/register"),
            content,
            ct);

        var registerBody = await registerResponse.Content.ReadAsStringAsync(ct);

        if (!registerResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Synapse shared-secret registration failed with HTTP {(int)registerResponse.StatusCode}: {registerBody}");
        }

        var result = JsonSerializer.Deserialize<SynapseSharedSecretRegistrationResponse>(
            registerBody,
            JsonOptions());

        if (string.IsNullOrWhiteSpace(result?.UserId))
        {
            throw new InvalidOperationException("Synapse registration response did not include a user_id.");
        }

        return new SynapseSharedSecretRegistrationResult(
            UserId: result.UserId,
            AccessToken: result.AccessToken,
            HomeServer: result.HomeServer);
    }

    private static string CreateMac(
        string nonce,
        string username,
        string password,
        bool admin,
        string sharedSecret)
    {
        var adminValue = admin ? "admin" : "notadmin";
        var payload = string.Join('\0', nonce, username, password, adminValue);

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(sharedSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}

public sealed record SynapseSharedSecretRegistrationResult(
    string UserId,
    string? AccessToken,
    string? HomeServer);

public sealed record SynapseRegistrationNonceResponse(
    string Nonce);

public sealed record SynapseSharedSecretRegistrationRequest(
    string Nonce,
    string Username,
    string Password,
    bool Admin,
    string Mac);

public sealed record SynapseSharedSecretRegistrationResponse(
    [property: JsonPropertyName("user_id")]
    string UserId,

    [property: JsonPropertyName("access_token")]
    string? AccessToken,

    [property: JsonPropertyName("home_server")]
    string? HomeServer);
