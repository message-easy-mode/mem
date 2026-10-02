using HostAgent.Runtime.Manifests;

namespace HostAgent.Runtime.Stacks.Identity;

public static class RuntimeStackIdentity
{
    public const string DisplayNameMetadataKey = "identity.displayName";
    public const string CategoryMetadataKey = "identity.category";
    public const string LogoSha256MetadataKey = "identity.logo.sha256";
    public const string LogoBytesMetadataKey = "identity.logo.bytes";
    public const string LogoWidthMetadataKey = "identity.logo.width";
    public const string LogoHeightMetadataKey = "identity.logo.height";
    public const int MaximumDisplayNameLength = 200;
    public const int MaximumCategoryLength = 40;

    public static string ResolveDisplayName(RuntimeStackManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return manifest.Metadata.TryGetValue(DisplayNameMetadataKey, out var value) &&
               !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : manifest.Slug;
    }

    public static string? ResolveCategory(RuntimeStackManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return manifest.Metadata.TryGetValue(CategoryMetadataKey, out var value)
            ? NormalizeCategory(value)
            : null;
    }

    public static RuntimeStackLogoMetadata? ResolveLogoMetadata(RuntimeStackManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return ResolveLogoMetadata(manifest.Metadata);
    }

    public static string? ResolveLogoUrl(RuntimeStackManifest manifest)
    {
        var logo = ResolveLogoMetadata(manifest);
        if (logo is null)
        {
            return null;
        }

        return $"/internal/host-agent/runtime-stacks/{Uri.EscapeDataString(manifest.Slug)}/logo?v={logo.Sha256}";
    }

    public static string NormalizeDisplayName(string? value, string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new InvalidOperationException("Stack slug is required before its display name can be resolved.");
        }

        var normalized = string.IsNullOrWhiteSpace(value)
            ? slug.Trim()
            : value.Trim();

        ValidateText(normalized, MaximumDisplayNameLength, "Stack display name");
        return normalized;
    }

    public static string? NormalizeCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        ValidateText(normalized, MaximumCategoryLength, "Stack category");
        return normalized;
    }

    public static IReadOnlyDictionary<string, string?> WithIdentityMetadata(
        IReadOnlyDictionary<string, string?> metadata,
        string slug,
        string? displayName,
        string? category)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var updated = metadata.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

        updated[DisplayNameMetadataKey] = NormalizeDisplayName(displayName, slug);

        var normalizedCategory = NormalizeCategory(category);
        if (normalizedCategory is null)
        {
            updated.Remove(CategoryMetadataKey);
        }
        else
        {
            updated[CategoryMetadataKey] = normalizedCategory;
        }

        return updated;
    }

    public static IReadOnlyDictionary<string, string?> WithLogoMetadata(
        IReadOnlyDictionary<string, string?> metadata,
        RuntimeStackLogoMetadata? logo)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var updated = metadata.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

        if (logo is null)
        {
            updated.Remove(LogoSha256MetadataKey);
            updated.Remove(LogoBytesMetadataKey);
            updated.Remove(LogoWidthMetadataKey);
            updated.Remove(LogoHeightMetadataKey);
            return updated;
        }

        if (!IsSha256(logo.Sha256) || logo.Bytes <= 0 || logo.Width <= 0 || logo.Height <= 0)
        {
            throw new InvalidOperationException("Stack logo metadata is invalid.");
        }

        updated[LogoSha256MetadataKey] = logo.Sha256.ToLowerInvariant();
        updated[LogoBytesMetadataKey] = logo.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        updated[LogoWidthMetadataKey] = logo.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        updated[LogoHeightMetadataKey] = logo.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return updated;
    }

    public static void CopyLogoMetadata(
        IReadOnlyDictionary<string, string?> source,
        IDictionary<string, string?> destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var logo = ResolveLogoMetadata(source);
        if (logo is null)
        {
            return;
        }

        foreach (var pair in WithLogoMetadata(
                     new Dictionary<string, string?>(),
                     logo))
        {
            destination[pair.Key] = pair.Value;
        }
    }

    private static RuntimeStackLogoMetadata? ResolveLogoMetadata(
        IReadOnlyDictionary<string, string?> metadata)
    {
        if (!metadata.TryGetValue(LogoSha256MetadataKey, out var sha256) ||
            string.IsNullOrWhiteSpace(sha256) ||
            !IsSha256(sha256) ||
            !TryReadPositiveLong(metadata, LogoBytesMetadataKey, out var bytes) ||
            !TryReadPositiveInt(metadata, LogoWidthMetadataKey, out var width) ||
            !TryReadPositiveInt(metadata, LogoHeightMetadataKey, out var height))
        {
            return null;
        }

        return new RuntimeStackLogoMetadata(
            Sha256: sha256.ToLowerInvariant(),
            Bytes: bytes,
            Width: width,
            Height: height);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool TryReadPositiveLong(
        IReadOnlyDictionary<string, string?> metadata,
        string key,
        out long value)
    {
        value = 0;
        return metadata.TryGetValue(key, out var raw) &&
               long.TryParse(
                   raw,
                   System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out value) &&
               value > 0;
    }

    private static bool TryReadPositiveInt(
        IReadOnlyDictionary<string, string?> metadata,
        string key,
        out int value)
    {
        value = 0;
        return metadata.TryGetValue(key, out var raw) &&
               int.TryParse(
                   raw,
                   System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out value) &&
               value > 0;
    }

    private static void ValidateText(string value, int maximumLength, string label)
    {
        if (value.Length > maximumLength)
        {
            throw new InvalidOperationException(
                $"{label} exceeds the maximum length of {maximumLength} characters.");
        }

        if (value.Any(char.IsControl))
        {
            throw new InvalidOperationException($"{label} cannot contain control characters.");
        }
    }
}

public sealed record RuntimeStackLogoMetadata(
    string Sha256,
    long Bytes,
    int Width,
    int Height);
