namespace Infrastructure.Docker;

public static class DockerImageIdentityMatcher
{
    public static bool IsMatch(
        string? imageId,
        IEnumerable<string>? repositoryTags,
        IEnumerable<string>? repositoryDigests,
        string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var expected = reference.Trim();
        if (string.Equals(imageId?.Trim(), expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (repositoryTags ?? [])
                   .Concat(repositoryDigests ?? [])
                   .Any(candidate => string.Equals(
                       candidate?.Trim(),
                       expected,
                       StringComparison.OrdinalIgnoreCase));
    }
}
