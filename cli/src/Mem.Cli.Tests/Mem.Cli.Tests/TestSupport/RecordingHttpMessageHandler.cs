using System.Net;
using System.Net.Http;
using System.Text;

namespace Mem.Cli.Tests.TestSupport;

public sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private const string InstallerUnlockPath = "/api/installer-auth/unlock";
    private readonly Queue<HttpResponseMessage> _responses;
    private readonly List<RecordedHttpRequest> _requests = [];
    private readonly List<RecordedInstallerUnlockRequest> _installerUnlockRequests = [];
    private HttpResponseMessage? _installerUnlockResponse;

    public RecordingHttpMessageHandler(params HttpResponseMessage[] responses)
    {
        _responses = new Queue<HttpResponseMessage>(responses);
    }

    public IReadOnlyList<RecordedHttpRequest> Requests => _requests;

    public IReadOnlyList<RecordedInstallerUnlockRequest> InstallerUnlockRequests =>
        _installerUnlockRequests;

    public void SetInstallerUnlockResponse(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (_installerUnlockResponse is not null)
        {
            throw new InvalidOperationException(
                "An installer unlock response has already been configured.");
        }

        _installerUnlockResponse = response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var pathAndQuery = request.RequestUri?.PathAndQuery ?? string.Empty;
        var contentBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var hasRetiredSharedSecretHeader = request.Headers.Contains(
            "X-MEM-Agent-Secret");

        if (string.Equals(
                pathAndQuery,
                InstallerUnlockPath,
                StringComparison.Ordinal))
        {
            _installerUnlockRequests.Add(
                new RecordedInstallerUnlockRequest(
                    request.Method.Method,
                    pathAndQuery,
                    hasRetiredSharedSecretHeader,
                    request.Content?.Headers.ContentType?.MediaType,
                    contentBody));

            return _installerUnlockResponse ?? CreateInstallerUnlockSuccess();
        }

        _requests.Add(new RecordedHttpRequest(
            request.Method.Method,
            pathAndQuery,
            hasRetiredSharedSecretHeader,
            request.Headers.TryGetValues("Cookie", out var cookieValues)
                ? string.Join("; ", cookieValues)
                : null,
            request.Content?.Headers.ContentType?.MediaType,
            contentBody,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                "No scripted HTTP response was available for the request.");
        }

        return _responses.Dequeue();
    }

    public static HttpResponseMessage Json(
        string content,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                content,
                Encoding.UTF8,
                "application/json")
        };
    }

    public static HttpResponseMessage Bytes(
        byte[] content,
        string? downloadName = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        };

        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");

        if (!string.IsNullOrWhiteSpace(downloadName))
        {
            response.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment")
            {
                FileNameStar = downloadName
            };
        }

        return response;
    }

    private static HttpResponseMessage CreateInstallerUnlockSuccess()
    {
        var response = Json("""{ "unlocked": true }""");
        response.Headers.TryAddWithoutValidation(
            "Set-Cookie",
            "mem_installer_auth=mem-cli-test-auth; Path=/; HttpOnly");

        return response;
    }
}

public sealed record RecordedHttpRequest(
    string Method,
    string PathAndQuery,
    bool HasRetiredSharedSecretHeader,
    string? CookieHeader,
    string? ContentType,
    string? ContentBody,
    string? AuthorizationScheme = null,
    string? AuthorizationParameter = null);

public sealed record RecordedInstallerUnlockRequest(
    string Method,
    string PathAndQuery,
    bool HasRetiredSharedSecretHeader,
    string? ContentType,
    string? ContentBody);
