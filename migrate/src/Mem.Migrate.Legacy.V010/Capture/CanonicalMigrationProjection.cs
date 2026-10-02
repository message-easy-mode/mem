using Mem.Migrate.Core.Assessment;

namespace Mem.Migrate.Legacy.V010.Capture;

public static class CanonicalMigrationProjection
{
    public static CanonicalLegacyStackExport Stack(LegacyStackRecord stack) =>
        new(
            stack.Id,
            stack.Slug,
            stack.Name,
            stack.Status,
            stack.Description,
            PublicUrl(stack.PrimaryUrl),
            stack.MatrixInstanceId,
            stack.CreatedAt,
            stack.UpdatedAt);

    public static CanonicalLegacyServiceExport Service(
        LegacyServiceRecord service) =>
        new(
            service.Id,
            service.StackId,
            service.ServiceKey,
            service.Status,
            service.Image,
            service.Version,
            service.DockerContainerId,
            service.ServerName,
            PublicHost(
                service.MatrixPublicHost ??
                service.ElementPublicHost ??
                service.PublicDomain ??
                service.BaseUrl),
            PublicUrl(
                service.MatrixPublicHost ??
                service.ElementPublicHost ??
                service.PublicDomain ??
                service.BaseUrl),
            service.PublicRouteId,
            service.InternalRouteId,
            "Source host ports and development transport overrides are intentionally excluded from the neutral export.");


    public static string? PublicHost(string? value)
    {
        var publicUrl = PublicUrl(value);

        if (publicUrl is null ||
            !Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Host;
    }

    public static string? PublicUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Contains("://", StringComparison.Ordinal)
            ? value
            : $"https://{value}";

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = -1,
            UserName = string.Empty,
            Password = string.Empty,
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }
}

public sealed record CanonicalLegacyStackExport(
    Guid Id,
    string Slug,
    string Name,
    int Status,
    string? Description,
    string? PrimaryUrl,
    Guid? MatrixInstanceId,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CanonicalLegacyServiceExport(
    Guid Id,
    Guid StackId,
    string ServiceKey,
    int Status,
    string Image,
    string Version,
    string? SourceContainerId,
    string? ServerName,
    string? PublicHost,
    string? PublicUrl,
    string? PublicRouteId,
    string? InternalRouteId,
    string Note);
