using System.Text.Json.Serialization;

namespace Modules.Integrations.Npm.Contracts;

public sealed record NpmCertificate(
    [property: JsonPropertyName("id")]
    int id,

    [property: JsonPropertyName("provider")]
    string? provider,

    [property: JsonPropertyName("nice_name")]
    string? nice_name,

    [property: JsonPropertyName("domain_names")]
    string[]? domain_names,

    [property: JsonPropertyName("expires_on")]
    string? expires_on,

    [property: JsonPropertyName("created_on")]
    string? created_on,

    [property: JsonPropertyName("modified_on")]
    string? modified_on
);

public sealed record NpmCustomCertificateCreateRequest(
    [property: JsonPropertyName("provider")]
    string provider,

    [property: JsonPropertyName("nice_name")]
    string nice_name,

    [property: JsonPropertyName("domain_names")]
    string[] domain_names
);