using System.Runtime.InteropServices;
using Modules.Integrations.Portainer.Services;

namespace Api.IntegrationTests.Portainer;

public sealed class PortainerRuntimeOptionsTests
{
    [Fact]
    public void Defaults_pin_the_current_approved_LTS_patch_and_safe_runtime_contract()
    {
        var options = new PortainerRuntimeOptions();

        Assert.Equal("portainer/portainer-ce:2.39.5", options.ApprovedImageReference);
        Assert.Equal("2.39.5", options.ExpectedVersion);
        Assert.Equal("portainer", options.ContainerName);
        Assert.Equal("portainer_data", options.DataVolumeName);
        Assert.Equal(9443, options.PreferredHttpsHostPort);
        Assert.False(options.AllowOperationalPull);
        Assert.Equal(new[] { "amd64", "arm64" }, options.SupportedArchitectures);
        Assert.Empty(PortainerRuntimeOptionsValidator.Validate(options));
    }

    [Theory]
    [InlineData("portainer/portainer-ce:latest")]
    [InlineData("portainer/portainer-ce:lts")]
    [InlineData("portainer/portainer-ce:sts")]
    [InlineData("portainer/portainer-ce:2.39")]
    [InlineData("other/portainer-ce:2.39.5")]
    public void Moving_or_nonapproved_image_references_are_rejected(string image)
    {
        var options = new PortainerRuntimeOptions
        {
            ApprovedImageReference = image
        };

        Assert.NotEmpty(PortainerRuntimeOptionsValidator.Validate(options));
    }

    [Fact]
    public void A_different_exact_patch_cannot_be_reclassified_as_approved_by_configuration()
    {
        var options = new PortainerRuntimeOptions
        {
            ApprovedImageReference = "portainer/portainer-ce:2.40.1",
            ExpectedVersion = "2.40.1"
        };

        Assert.NotEmpty(PortainerRuntimeOptionsValidator.Validate(options));
    }

    [Fact]
    public void Mismatched_version_operational_pull_arbitrary_names_and_unsafe_URL_are_rejected()
    {
        var options = new PortainerRuntimeOptions
        {
            ExpectedVersion = "2.39.4",
            ContainerName = "operator-target",
            DataVolumeName = "other-volume",
            AllowOperationalPull = true,
            UiUrl = "https://user:password@example.test/portainer?token=secret",
            EnvironmentId = 0,
            SupportedArchitectures = ["amd64", "riscv64"]
        };

        var errors = PortainerRuntimeOptionsValidator.Validate(options);

        Assert.True(errors.Count >= 7);
    }

    [Fact]
    public void Architecture_policy_allows_only_the_approved_Docker_architectures()
    {
        Assert.Equal("amd64", PortainerArchitecturePolicy.ToDockerArchitecture(Architecture.X64));
        Assert.Equal("arm64", PortainerArchitecturePolicy.ToDockerArchitecture(Architecture.Arm64));
        Assert.Throws<PortainerOperationException>(() =>
            PortainerArchitecturePolicy.ToDockerArchitecture(Architecture.X86));
        Assert.Throws<PortainerOperationException>(() =>
            PortainerArchitecturePolicy.ToDockerArchitecture(Architecture.Arm));
    }
}
