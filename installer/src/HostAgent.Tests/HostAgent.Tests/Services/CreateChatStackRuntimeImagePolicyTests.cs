using HostAgent.Commands;
using HostAgent.Services;

namespace HostAgent.Tests.Services;

public sealed class CreateChatStackRuntimeImagePolicyTests
{
    [Theory]
    [InlineData("matrixdotorg/synapse:latest", null, null, null)]
    [InlineData(null, "latest", null, null)]
    [InlineData(null, null, "vectorim/element-web:latest", null)]
    [InlineData(null, null, null, "latest")]
    public void PLATFORM_RUNTIME_IMAGE_PINNING_CORR_01_rejects_caller_supplied_runtime_image_authority(
        string? matrixImage,
        string? matrixVersion,
        string? elementImage,
        string? elementVersion)
    {
        var command = new CreateChatStackRuntimeCommand(
            StackId: Guid.NewGuid(),
            MatrixInstanceId: Guid.NewGuid(),
            ElementInstanceId: Guid.NewGuid(),
            StackSlug: "image-policy",
            RequestedDomainId: null,
            MatrixImage: matrixImage,
            MatrixVersion: matrixVersion,
            ElementImage: elementImage,
            ElementVersion: elementVersion,
            IdempotencyKey: Guid.NewGuid().ToString("N"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateChatStackRuntimeHandler.Validate(command));

        Assert.Contains("release-approved immutable runtime image", exception.Message);
    }

    [Fact]
    public void PLATFORM_RUNTIME_IMAGE_PINNING_CORR_01_accepts_server_owned_runtime_image_authority()
    {
        var command = new CreateChatStackRuntimeCommand(
            StackId: Guid.NewGuid(),
            MatrixInstanceId: Guid.NewGuid(),
            ElementInstanceId: Guid.NewGuid(),
            StackSlug: "image-policy",
            RequestedDomainId: null,
            MatrixImage: null,
            MatrixVersion: null,
            ElementImage: null,
            ElementVersion: null,
            IdempotencyKey: Guid.NewGuid().ToString("N"));

        CreateChatStackRuntimeHandler.Validate(command);
    }
}
