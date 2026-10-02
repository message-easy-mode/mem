using System.Text.Json.Serialization;

namespace Modules.Integrations.Npm.Contracts;

public sealed record CreateNpmProxyHostRequest(
    string Domain,
    string ForwardHost,
    int ForwardPort,
    bool ForceSsl = false,
    int CertificateId = 0,
    bool Http2 = true,
    string AdvancedConfig = ""
);

public sealed record NpmProxyHostResult(
    int Id,
    string[] DomainNames,
    string ForwardHost,
    int ForwardPort,
    bool SslForced,
    int CertificateId,
    bool Enabled,
    string AdvancedConfig = ""
);

public sealed record NpmProxyHost(
    int id,
    string[]? domain_names,
    string? forward_host,
    int? forward_port,
    int? access_list_id,
    int? certificate_id,
    string? forward_scheme,
    string? advanced_config,
    NpmProxyHostMeta? meta,
    object[]? locations,
    bool? ssl_forced,
    bool? http2_support,
    bool? allow_websocket_upgrade,
    bool? block_exploits,
    bool? caching_enabled,
    bool? enabled,
    bool? hsts_enabled,
    bool? hsts_subdomains,
    bool? trust_forwarded_proto
);


public sealed record NpmProxyHostSnapshot(
    int Id,
    string[] DomainNames,
    string ForwardHost,
    int ForwardPort,
    int AccessListId,
    int CertificateId,
    string ForwardScheme,
    string AdvancedConfig,
    object[] Locations,
    bool SslForced,
    bool Http2Support,
    bool AllowWebsocketUpgrade,
    bool BlockExploits,
    bool CachingEnabled,
    bool Enabled,
    bool HstsEnabled,
    bool HstsSubdomains,
    bool TrustForwardedProto);

public sealed record NpmProxyHostMeta(
    bool? nginx_online,
    string? nginx_err
);

public sealed record NpmProxyHostCreate(
    string[] domain_names,
    string forward_host,
    int forward_port,
    string forward_scheme = "http",
    int access_list_id = 0,
    int certificate_id = 0,
    bool ssl_forced = false,
    bool caching_enabled = false,
    bool block_exploits = true,
    string advanced_config = "",
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Dictionary<string, object>? meta = null,
    bool allow_websocket_upgrade = true,
    bool http2_support = true,
    bool enabled = true,
    object[]? locations = null,
    bool hsts_enabled = false,
    bool hsts_subdomains = false,
    bool trust_forwarded_proto = false
);

public sealed record NpmApiCredential(
    Guid InstallationId,
    string Identity,
    string Secret,
    DateTime? VerifiedAtUtc);

public interface INpmApiCredentialProvider
{
    Task<NpmApiCredential?> ResolveCurrentAsync(CancellationToken cancellationToken);
}

public sealed class NpmApiOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public int CertificateId { get; set; }
    public bool ForceSsl { get; set; }
    public bool Http2 { get; set; }
}

public sealed record NpmProxyHostUpdateRequest(
    [property: JsonPropertyName("domain_names")]
    string[] domain_names,

    [property: JsonPropertyName("forward_host")]
    string forward_host,

    [property: JsonPropertyName("forward_port")]
    int forward_port,

    [property: JsonPropertyName("forward_scheme")]
    string forward_scheme,

    [property: JsonPropertyName("access_list_id")]
    int access_list_id,

    [property: JsonPropertyName("certificate_id")]
    int certificate_id,

    [property: JsonPropertyName("ssl_forced")]
    bool ssl_forced,

    [property: JsonPropertyName("caching_enabled")]
    bool caching_enabled,

    [property: JsonPropertyName("block_exploits")]
    bool block_exploits,

    [property: JsonPropertyName("allow_websocket_upgrade")]
    bool allow_websocket_upgrade,

    [property: JsonPropertyName("http2_support")]
    bool http2_support,

    [property: JsonPropertyName("enabled")]
    bool enabled,

    [property: JsonPropertyName("advanced_config")]
    string advanced_config,

    [property: JsonPropertyName("locations")]
    object[] locations,

    [property: JsonPropertyName("hsts_enabled")]
    bool hsts_enabled,

    [property: JsonPropertyName("hsts_subdomains")]
    bool hsts_subdomains,

    [property: JsonPropertyName("trust_forwarded_proto")]
    bool trust_forwarded_proto = false
);