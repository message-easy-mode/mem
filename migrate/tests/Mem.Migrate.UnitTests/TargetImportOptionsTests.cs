using Mem.Migrate.Core.Target;

namespace Mem.Migrate.UnitTests;

public sealed class TargetImportOptionsTests
{
    [Fact]
    public void Normalize_resolves_paths_and_keeps_explicit_attempt_id()
    {
        var options = new TargetImportOptions
        {
            ManifestPath = "manifest.json",
            StackExportPath = "stack.zip",
            ProfileName = "Target-Server",
            WorkspacePath = "work",
            AttemptId = "mm05c-proof"
        }.Normalize();

        Assert.Equal("mm05c-proof", options.AttemptId);
        Assert.Equal("target-server", options.ProfileName);
        Assert.True(Path.IsPathFullyQualified(options.ManifestPath));
        Assert.True(Path.IsPathFullyQualified(options.StackExportPath));
        Assert.True(Path.IsPathFullyQualified(options.WorkspacePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("-starts-with-hyphen")]
    [InlineData("ends-with-hyphen-")]
    public void Normalize_rejects_invalid_profile_names(string profileName)
    {
        var options = new TargetImportOptions
        {
            ManifestPath = "manifest.json",
            StackExportPath = "stack.zip",
            ProfileName = profileName
        };

        Assert.Throws<ArgumentException>(() => options.Normalize());
    }

    [Fact]
    public void Device_credential_validator_accepts_32_byte_base64url_shape()
    {
        var credential = Convert.ToBase64String(new byte[32])
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.True(TargetDeviceCredentialValidator.IsValid(credential));
        Assert.False(TargetDeviceCredentialValidator.IsValid("not-a-token"));
    }
}
