namespace HostAgent.Planning;

public sealed record ChatStackHostnames(
    string MatrixHost,
    string MatrixPublicBaseUrl,
    string? ElementHost,
    string? ElementPublicBaseUrl);

public sealed class ChatStackHostnamePlanner
{
    public ChatStackHostnames Plan(
        Guid stackId,
        Guid matrixInstanceId,
        Guid? elementInstanceId,
        string? stackSlug,
        string baseDomain)
    {
        var safeToken = BuildSafeToken(stackId, stackSlug);

        var matrixHost = $"matrix-{safeToken}.{baseDomain}";
        var elementHost = elementInstanceId.HasValue
            ? $"chat-{safeToken}.{baseDomain}"
            : null;

        return new ChatStackHostnames(
            MatrixHost: matrixHost,
            MatrixPublicBaseUrl: $"https://{matrixHost}",
            ElementHost: elementHost,
            ElementPublicBaseUrl: elementHost is null ? null : $"https://{elementHost}");
    }

    private static string BuildSafeToken(Guid stackId, string? stackSlug)
    {
        if (!string.IsNullOrWhiteSpace(stackSlug))
        {
            var normalized = NormalizeSlug(stackSlug);

            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return stackId.ToString("N")[..8];
    }

    private static string NormalizeSlug(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
            .ToArray();

        var normalized = new string(chars);

        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized.Trim('-');
    }
}