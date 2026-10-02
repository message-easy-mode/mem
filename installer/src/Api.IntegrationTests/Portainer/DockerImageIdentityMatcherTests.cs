using Infrastructure.Docker;

namespace Api.IntegrationTests.Portainer;

public sealed class DockerImageIdentityMatcherTests
{
    [Fact]
    public void Immutable_local_image_ID_is_a_valid_existing_image_identity()
    {
        var imageId = $"sha256:{new string('a', 64)}";

        Assert.True(DockerImageIdentityMatcher.IsMatch(
            imageId,
            ["portainer/portainer-ce:2.39.5"],
            [$"portainer/portainer-ce@sha256:{new string('b', 64)}"],
            imageId));
    }

    [Fact]
    public void Exact_tag_and_repository_digest_are_also_recognized()
    {
        var digest = $"portainer/portainer-ce@sha256:{new string('b', 64)}";

        Assert.True(DockerImageIdentityMatcher.IsMatch(
            imageId: null,
            repositoryTags: ["portainer/portainer-ce:2.39.5"],
            repositoryDigests: [digest],
            reference: "portainer/portainer-ce:2.39.5"));
        Assert.True(DockerImageIdentityMatcher.IsMatch(
            imageId: null,
            repositoryTags: [],
            repositoryDigests: [digest],
            reference: digest));
    }

    [Fact]
    public void Unrelated_or_blank_references_do_not_match()
    {
        Assert.False(DockerImageIdentityMatcher.IsMatch(
            $"sha256:{new string('a', 64)}",
            ["portainer/portainer-ce:2.39.5"],
            [],
            "portainer/portainer-ce:2.39.4"));
        Assert.False(DockerImageIdentityMatcher.IsMatch(
            null,
            null,
            null,
            " "));
    }
}
