using System;
using Microsoft.Extensions.Options;

namespace Shared.Settings;


public sealed class FrontendOptions
{
    public string BaseUrl { get; set; } = default!;
}

public interface IFrontendUrlBuilder
{
    string Absolute(string relativePath); // e.g. "/orders/123"
}

public sealed class FrontendUrlBuilder : IFrontendUrlBuilder
{
    private readonly Uri _baseUri;

    public FrontendUrlBuilder(IOptions<FrontendOptions> opts)
    {
        var baseUrl = (opts.Value.BaseUrl ?? "").Trim();

        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Frontend:BaseUrl is not configured.");

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException(
                $"Frontend:BaseUrl is not a valid absolute URL: '{baseUrl}'");

        // ✅ normalize using the parsed URI (handles odd slashes more safely)
        var abs = uri.AbsoluteUri;
        if (!abs.EndsWith("/"))
            uri = new Uri(abs + "/");

        _baseUri = uri;
    }

    public string Absolute(string relativePath)
    {
        // If caller asks for "base" URL
        if (string.IsNullOrWhiteSpace(relativePath))
            return _baseUri.ToString().TrimEnd('/');

        // allow callers to pass "/orders/..." or "orders/..."
        relativePath = relativePath.TrimStart('/');

        return new Uri(_baseUri, relativePath).ToString();
    }
}