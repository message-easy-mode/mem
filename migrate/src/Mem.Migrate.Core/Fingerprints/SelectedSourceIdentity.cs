using System.Security.Cryptography;
using System.Text;

namespace Mem.Migrate.Core.Fingerprints;

public sealed record SelectedSourceIdentityInput(
    string? ProductName,
    string? ProductVersion,
    Guid SourceStackId,
    string Slug,
    string? MatrixServerName,
    string? MatrixPublicHost,
    string? ElementPublicHost);

public static class SelectedSourceIdentity
{
    public static string Compute(SelectedSourceIdentityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.SourceStackId == Guid.Empty)
        {
            throw new ArgumentException(
                "Selected source stack ID cannot be empty.",
                nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.Slug))
        {
            throw new ArgumentException(
                "Selected source stack slug is required.",
                nameof(input));
        }

        var canonical = string.Join(
            "\n",
            new[]
            {
                "schema=mem-selected-source-identity-v1",
                $"product={Normalize(input.ProductName)}",
                $"version={Normalize(input.ProductVersion)}",
                $"stack.id={input.SourceStackId:D}",
                $"stack.slug={Normalize(input.Slug)}",
                $"matrix.server={Normalize(input.MatrixServerName)}",
                $"matrix.public={Normalize(input.MatrixPublicHost)}",
                $"element.public={Normalize(input.ElementPublicHost)}"
            });

        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
}
