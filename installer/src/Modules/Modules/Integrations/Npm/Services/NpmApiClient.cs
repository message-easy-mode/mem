using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Modules.Integrations.Npm.Contracts;

namespace Modules.Integrations.Npm.Services;

public sealed class NpmApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<bool> IsSetupAsync(
        string baseUrl,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("baseUrl required", nameof(baseUrl));

        using var response = await http.GetAsync(baseUrl.TrimEnd('/'), ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"NPM setup-state request failed ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("setup", out var setup) ||
            setup.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidOperationException(
                "NPM API root response did not contain a boolean setup state.");
        }

        return setup.GetBoolean();
    }

    public async Task<string> LoginAsync(
        string baseUrl,
        string identity,
        string secret,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("baseUrl required", nameof(baseUrl));

        var url = $"{baseUrl.TrimEnd('/')}/tokens";

        var response = await http.PostAsJsonAsync(
            url,
            new { identity, secret },
            JsonOpts,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"NPM login failed ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        string? token = null;

        if (doc.RootElement.TryGetProperty("token", out var t) &&
            t.ValueKind == JsonValueKind.String)
        {
            token = t.GetString();
        }

        if (token is null &&
            doc.RootElement.TryGetProperty("result", out var r) &&
            r.ValueKind == JsonValueKind.Object &&
            r.TryGetProperty("token", out var rt) &&
            rt.ValueKind == JsonValueKind.String)
        {
            token = rt.GetString();
        }

        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("NPM token response did not contain a token.");

        return token;
    }

    public async Task<IReadOnlyList<NpmProxyHost>> GetProxyHostsAsync(
        string baseUrl,
        string bearerToken,
        CancellationToken ct)
    {
        var url = $"{baseUrl.TrimEnd('/')}/nginx/proxy-hosts";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"NPM get proxy hosts failed ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }

        var items = await response.Content.ReadFromJsonAsync<List<NpmProxyHost>>(JsonOpts, ct);
        return items ?? [];
    }

    public async Task<NpmProxyHost?> FindProxyHostByDomainAsync(
        string baseUrl,
        string bearerToken,
        string domain,
        CancellationToken ct)
    {
        domain = (domain ?? "").Trim().ToLowerInvariant();
        if (domain.Length == 0)
            throw new ArgumentException("domain required", nameof(domain));

        var items = await GetProxyHostsAsync(baseUrl, bearerToken, ct);

        return items.FirstOrDefault(x =>
            x.domain_names?.Any(d => string.Equals(d?.Trim(), domain, StringComparison.OrdinalIgnoreCase)) == true);
    }

    public async Task<NpmProxyHost> CreateProxyHostAsync<T>(
        string baseUrl,
        string bearerToken,
        T payload,
        CancellationToken ct)
    {
        var url = $"{baseUrl.TrimEnd('/')}/nginx/proxy-hosts";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Content = JsonContent.Create(payload, options: JsonOpts);

        var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"NPM create proxy host failed ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }

        var created = await response.Content.ReadFromJsonAsync<NpmProxyHost>(JsonOpts, ct);
        return created ?? throw new InvalidOperationException("NPM create returned empty response.");
    }

    public async Task<NpmProxyHost> UpdateProxyHostAsync<T>(
        string baseUrl,
        string bearerToken,
        int proxyHostId,
        T payload,
        CancellationToken ct)
    {
        var url = $"{baseUrl.TrimEnd('/')}/nginx/proxy-hosts/{proxyHostId}";

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Content = JsonContent.Create(payload, options: JsonOpts);

        var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"NPM update proxy host failed ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }

        var updated = await response.Content.ReadFromJsonAsync<NpmProxyHost>(JsonOpts, ct);
        return updated ?? throw new InvalidOperationException("NPM update returned empty response.");
    }

    public async Task DeleteProxyHostAsync(
        string baseUrl,
        string bearerToken,
        int proxyHostId,
        CancellationToken ct)
    {
        var url = $"{baseUrl.TrimEnd('/')}/nginx/proxy-hosts/{proxyHostId}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"NPM delete failed ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }
    }

    public async Task DeleteCertificateAsync(
        string baseUrl,
        string bearerToken,
        int certificateId,
        CancellationToken ct)
    {
        if (certificateId <= 0)
            throw new ArgumentOutOfRangeException(nameof(certificateId));

        var url = $"{baseUrl.TrimEnd('/')}/nginx/certificates/{certificateId}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"NPM certificate delete failed ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }
    }

    public async Task<IReadOnlyList<NpmCertificate>> GetCertificatesAsync(
    string baseUrl,
    string bearerToken,
    CancellationToken ct)
    {
        var url = $"{baseUrl.TrimEnd('/')}/nginx/certificates";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);

            throw new HttpRequestException(
                $"NPM get certificates failed ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }

        var items = await response.Content.ReadFromJsonAsync<List<NpmCertificate>>(JsonOpts, ct);
        return items ?? [];
    }

    

    public async Task<NpmCertificate> CreateCustomCertificateRecordAsync(
    string baseUrl,
    string bearerToken,
    string niceName,
    string[] domainNames,
    CancellationToken ct)
    {
        var url = $"{baseUrl.TrimEnd('/')}/nginx/certificates";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        request.Content = JsonContent.Create(
            new NpmCustomCertificateCreateRequest(
                provider: "other",
                nice_name: niceName,
                domain_names: domainNames),
            options: JsonOpts);

        using var response = await http.SendAsync(request, ct);

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"NPM create custom certificate record failed ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }

        var certificate = await response.Content.ReadFromJsonAsync<NpmCertificate>(
            JsonOpts,
            ct);

        return certificate
            ?? throw new InvalidOperationException("NPM create custom certificate record returned empty response.");
    }

    public async Task UploadCustomCertificateFilesAsync(
        string baseUrl,
        string bearerToken,
        int certificateId,
        string fullchainPath,
        string privateKeyPath,
        CancellationToken ct)
    {
        var url = $"{baseUrl.TrimEnd('/')}/nginx/certificates/{certificateId}/upload";

        await using var certificateStream = File.OpenRead(fullchainPath);
        await using var privateKeyStream = File.OpenRead(privateKeyPath);

        using var form = new MultipartFormDataContent();

        var certificateContent = new StreamContent(certificateStream);
        certificateContent.Headers.ContentType =
            new MediaTypeHeaderValue("application/x-pem-file");

        var privateKeyContent = new StreamContent(privateKeyStream);
        privateKeyContent.Headers.ContentType =
            new MediaTypeHeaderValue("application/x-pem-file");

        form.Add(certificateContent, "certificate", "fullchain.pem");
        form.Add(privateKeyContent, "certificate_key", "privkey.pem");

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = form
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            // NPM's custom-certificate upload response can contain the uploaded
            // certificate and private-key PEM values. Never materialise that body
            // into an exception, log, Diagnostic event, or support report.
            throw new HttpRequestException(
                $"NPM custom certificate file upload failed ({(int)response.StatusCode}). Upstream response content was withheld because this endpoint can contain certificate private-key material.",
                null,
                response.StatusCode);
        }

        // Success response content is intentionally not read. NPM v2.15.1
        // returns the uploaded SSL-file fields, including certificate_key. The
        // caller already has authoritative non-secret certificate metadata and
        // can re-query NPM's normal certificate inventory when needed.
    }


}