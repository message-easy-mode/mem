using HostAgent.Docker;

namespace HostAgent.Tests.Docker;

public sealed class DockerImageReferencePolicyTests
{
    [Theory]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData(" SHA256:ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789 ")]
    public void Recognises_complete_local_immutable_image_ids(string image)
    {
        Assert.True(DockerImageReferencePolicy.IsLocalImmutableImageId(image));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256")]
    [InlineData("sha256:1234")]
    [InlineData("sha256:gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("postgres:16")]
    [InlineData("docker.io/library/postgres:16@sha256:33f923b05f64ca54ac4401c01126a6b92afe839a0aa0a52bc5aeb5cc958e5f20")]
    public void Rejects_tags_digests_and_malformed_local_ids(string? image)
    {
        Assert.False(DockerImageReferencePolicy.IsLocalImmutableImageId(image));
    }
}
