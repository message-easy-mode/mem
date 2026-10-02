using System.Globalization;
using System.Net;

namespace HostAgent.Matrix.Federation;

public sealed record FederationDomainValidationResult(
    bool Valid,
    IReadOnlyList<string> CanonicalDomains,
    string? ErrorCode,
    string? Detail);

public sealed class FederationDomainValidator
{
    private readonly IdnMapping _idn = new();

    public FederationDomainValidationResult ValidateMany(
        IEnumerable<string> values,
        bool requireAtLeastOne = false)
    {
        ArgumentNullException.ThrowIfNull(values);

        var canonical = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            var result = ValidateOne(value);
            if (!result.Valid)
            {
                return new FederationDomainValidationResult(
                    Valid: false,
                    CanonicalDomains: [],
                    ErrorCode: result.ErrorCode,
                    Detail: result.Detail);
            }

            var domain = result.CanonicalDomains[0];
            if (!seen.Add(domain))
            {
                return Invalid(
                    "federation_domain_duplicate",
                    $"The homeserver domain '{domain}' is listed more than once after canonicalisation.");
            }

            canonical.Add(domain);
        }

        canonical.Sort(StringComparer.Ordinal);

        if (requireAtLeastOne && canonical.Count == 0)
        {
            return Invalid(
                "federation_domain_required",
                "Restricted federation requires at least one exact homeserver domain.");
        }

        return new FederationDomainValidationResult(
            Valid: true,
            CanonicalDomains: canonical,
            ErrorCode: null,
            Detail: null);
    }

    public FederationDomainValidationResult ValidateOne(string? value)
    {
        var candidate = value ?? string.Empty;
        if (candidate.Length == 0)
        {
            return Invalid(
                "federation_domain_required",
                "An exact homeserver domain is required.");
        }

        if (candidate.Any(char.IsWhiteSpace))
        {
            return Invalid(
                "federation_domain_whitespace_not_allowed",
                "Homeserver domains cannot contain whitespace.");
        }

        if (candidate.Contains("*", StringComparison.Ordinal))
        {
            return Invalid(
                "federation_domain_wildcard_not_allowed",
                "Wildcards are not supported. Enter one exact homeserver domain.");
        }

        if (candidate.Contains("://", StringComparison.Ordinal) ||
            candidate.Contains('/') ||
            candidate.Contains('?') ||
            candidate.Contains('#') ||
            candidate.Contains('@') ||
            candidate.Contains(':'))
        {
            return Invalid(
                "federation_domain_exact_hostname_required",
                "Enter only an exact DNS homeserver domain without a scheme, port, path, query, fragment, or user information.");
        }

        if (candidate.EndsWith(".", StringComparison.Ordinal))
        {
            candidate = candidate[..^1];
        }
        if (candidate.Length == 0)
        {
            return Invalid(
                "federation_domain_invalid",
                "The homeserver domain is not valid.");
        }

        if (IPAddress.TryParse(candidate, out _))
        {
            return Invalid(
                "federation_domain_ip_not_allowed",
                "IP address literals are not supported. Enter a DNS homeserver domain.");
        }

        string ascii;
        try
        {
            ascii = _idn.GetAscii(candidate).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return Invalid(
                "federation_domain_invalid_idna",
                "The homeserver domain could not be converted to a valid ASCII IDNA hostname.");
        }

        if (ascii.Length > 253)
        {
            return Invalid(
                "federation_domain_too_long",
                "The homeserver domain must be no longer than 253 characters.");
        }

        var labels = ascii.Split('.', StringSplitOptions.None);
        if (labels.Any(label => label.Length == 0))
        {
            return Invalid(
                "federation_domain_invalid",
                "The homeserver domain contains an empty label.");
        }

        foreach (var label in labels)
        {
            if (label.Length > 63)
            {
                return Invalid(
                    "federation_domain_label_too_long",
                    "Each homeserver domain label must be no longer than 63 characters.");
            }

            if (label[0] == '-' || label[^1] == '-')
            {
                return Invalid(
                    "federation_domain_invalid_label",
                    "Homeserver domain labels cannot begin or end with a hyphen.");
            }

            if (label.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character != '-'))
            {
                return Invalid(
                    "federation_domain_invalid_label",
                    "Homeserver domain labels may contain only ASCII letters, digits, and hyphens after IDNA conversion.");
            }
        }

        return new FederationDomainValidationResult(
            Valid: true,
            CanonicalDomains: [ascii],
            ErrorCode: null,
            Detail: null);
    }

    private static FederationDomainValidationResult Invalid(
        string code,
        string detail) =>
        new(
            Valid: false,
            CanonicalDomains: [],
            ErrorCode: code,
            Detail: detail);
}
