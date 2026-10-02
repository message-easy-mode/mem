using Microsoft.Extensions.Options;

namespace Modules.Shared.RuntimeImages;

public sealed class ApprovedPostgresRuntimeProvider(
    IOptions<PostgresRuntimeImageOptions> options,
    IRuntimeImageInspector imageInspector,
    TimeProvider timeProvider) : IApprovedPostgresRuntimeProvider
{
    public ApprovedPostgresRuntimePolicy GetPolicy()
    {
        var value = options.Value;
        var approvedReference = ValidateApprovedReference(value.ApprovedReference);
        if (value.RequiredMajorVersion != 16)
        {
            throw new InvalidOperationException(
                "RuntimeImages:Postgres:RequiredMajorVersion must be 16 for this MEM release.");
        }

        var expectedVersion = value.ExpectedVersion?.Trim();
        if (string.IsNullOrWhiteSpace(expectedVersion) ||
            !expectedVersion.StartsWith("16.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "RuntimeImages:Postgres:ExpectedVersion must identify an approved PostgreSQL 16 patch release.");
        }

        if (value.AllowOperationalPull)
        {
            throw new InvalidOperationException(
                "RuntimeImages:Postgres:AllowOperationalPull must remain false. Sensitive operations may not pull images.");
        }

        return new ApprovedPostgresRuntimePolicy(
            approvedReference,
            value.RequiredMajorVersion,
            expectedVersion,
            value.AllowInstallPull,
            value.AllowOperationalPull);
    }

    public Task<ApprovedPostgresRuntimeDescriptor> ResolveForInstallationAsync(
        CancellationToken cancellationToken) =>
        ResolveAsync(allowPull: GetPolicy().AllowInstallPull, cancellationToken);

    public Task<ApprovedPostgresRuntimeDescriptor> ResolveForOperationAsync(
        CancellationToken cancellationToken) =>
        ResolveAsync(allowPull: false, cancellationToken);

    private async Task<ApprovedPostgresRuntimeDescriptor> ResolveAsync(
        bool allowPull,
        CancellationToken cancellationToken)
    {
        var policy = GetPolicy();
        var inspection = await imageInspector.InspectAsync(
            policy.ApprovedReference,
            cancellationToken);

        if (inspection is null && allowPull)
        {
            await imageInspector.PullAsync(policy.ApprovedReference, cancellationToken);
            inspection = await imageInspector.InspectAsync(
                policy.ApprovedReference,
                cancellationToken);
        }

        if (inspection is null)
        {
            throw new InvalidOperationException(
                $"The MEM-approved PostgreSQL runtime '{policy.ApprovedReference}' is not available locally. " +
                "MEM will not pull a replacement during this operation.");
        }

        var imageId = NormalizeImageId(inspection.ImageId);
        var major = ReadEnvironmentValue(inspection.EnvironmentVariables, "PG_MAJOR");
        var version = ReadEnvironmentValue(inspection.EnvironmentVariables, "PG_VERSION");
        if (!string.Equals(major, policy.RequiredMajorVersion.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The approved PostgreSQL image reported PG_MAJOR='{major ?? "<missing>"}', " +
                $"but MEM requires {policy.RequiredMajorVersion}.");
        }

        if (!MatchesApprovedPostgresVersion(version, policy.ExpectedVersion))
        {
            throw new InvalidOperationException(
                $"The approved PostgreSQL image reported PG_VERSION='{version ?? "<missing>"}', " +
                $"but this MEM release approves upstream PostgreSQL {policy.ExpectedVersion}.");
        }

        return new ApprovedPostgresRuntimeDescriptor(
            policy.ApprovedReference,
            imageId,
            inspection.RepositoryDigests,
            policy.RequiredMajorVersion,
            version!,
            timeProvider.GetUtcNow().UtcDateTime);
    }

    private static bool MatchesApprovedPostgresVersion(
        string? reportedVersion,
        string expectedUpstreamVersion)
    {
        var reported = reportedVersion?.Trim();
        if (string.IsNullOrWhiteSpace(reported))
        {
            return false;
        }

        if (string.Equals(reported, expectedUpstreamVersion, StringComparison.Ordinal))
        {
            return true;
        }

        if (!reported.StartsWith(expectedUpstreamVersion, StringComparison.Ordinal) ||
            reported.Length <= expectedUpstreamVersion.Length + 1 ||
            reported[expectedUpstreamVersion.Length] != '-')
        {
            return false;
        }

        var packageRevision = reported[(expectedUpstreamVersion.Length + 1)..];
        return packageRevision.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '.' or '+' or '~' or '-');
    }

    private static string ValidateApprovedReference(string? value)
    {
        var reference = value?.Trim() ?? string.Empty;
        const string marker = "@sha256:";
        var markerIndex = reference.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex <= 0)
        {
            throw new InvalidOperationException(
                "RuntimeImages:Postgres:ApprovedReference must be an immutable repository digest.");
        }

        var digest = reference[(markerIndex + marker.Length)..];
        if (digest.Length != 64 || !digest.All(char.IsAsciiHexDigit))
        {
            throw new InvalidOperationException(
                "RuntimeImages:Postgres:ApprovedReference contains an invalid SHA-256 digest.");
        }

        return reference.ToLowerInvariant();
    }

    private static string NormalizeImageId(string? imageId)
    {
        var normalized = imageId?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!normalized.StartsWith("sha256:", StringComparison.Ordinal) ||
            normalized.Length != "sha256:".Length + 64 ||
            !normalized["sha256:".Length..].All(char.IsAsciiHexDigit))
        {
            throw new InvalidOperationException(
                "Docker did not return a valid immutable local image ID for the approved PostgreSQL runtime.");
        }

        return normalized;
    }

    private static string? ReadEnvironmentValue(
        IEnumerable<string> environmentVariables,
        string name)
    {
        var prefix = name + "=";
        return environmentVariables
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal))?
            [prefix.Length..];
    }
}
