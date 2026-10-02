namespace Modules.Shared.Domains.Dns;

public static class DnsChallengeNameBuilder
{
    public static string BuildSubname(string domain, string zone)
    {
        var normalizedDomain = Normalize(domain);
        var normalizedZone = Normalize(zone);

        if (normalizedDomain.StartsWith("*."))
        {
            normalizedDomain = normalizedDomain[2..];
        }

        if (normalizedDomain == normalizedZone)
        {
            return "_acme-challenge";
        }

        var zoneSuffix = "." + normalizedZone;
        if (!normalizedDomain.EndsWith(zoneSuffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Domain '{domain}' is not inside zone '{zone}'.");
        }

        var relativeName = normalizedDomain[..^zoneSuffix.Length];
        return $"_acme-challenge.{relativeName}";
    }

    public static string BuildFullRecordName(string subname, string zone)
    {
        return $"{subname.TrimEnd('.')}.{Normalize(zone)}";
    }

    private static string Normalize(string value)
    {
        return value.Trim().TrimEnd('.').ToLowerInvariant();
    }
}
