using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modules.Auth.Configuration;
using Modules.Auth.Services;
using Shared.ControlPlane;

namespace Api.IntegrationTests.Security;

public sealed class InstallerSetupTokenStoreCompatibilityTests
{
    [Fact]
    public async Task Canonical_environment_authority_wins_over_legacy_alias()
    {
        var store = CreateStore(new Dictionary<string, string?>
        {
            [MemControlPlaneIdentity.EnvironmentVariables.SetupToken] = "canonical-token",
            [MemControlPlaneIdentity.Legacy.EnvironmentVariables.SetupToken] = "legacy-token"
        });

        Assert.Equal("canonical-token", await store.GetSetupTokenAsync());
    }

    [Fact]
    public async Task Canonical_path_authority_wins_before_legacy_environment_alias()
    {
        var path = CreateTokenFile("canonical-path-token");
        try
        {
            var store = CreateStore(new Dictionary<string, string?>
            {
                [MemControlPlaneIdentity.EnvironmentVariables.SetupTokenPath] = path,
                [MemControlPlaneIdentity.Legacy.EnvironmentVariables.SetupToken] = "legacy-token"
            });

            Assert.Equal("canonical-path-token", await store.GetSetupTokenAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Legacy_environment_authority_remains_a_bounded_fallback()
    {
        var store = CreateStore(new Dictionary<string, string?>
        {
            [MemControlPlaneIdentity.Legacy.EnvironmentVariables.SetupToken] = "legacy-token"
        });

        Assert.Equal("legacy-token", await store.GetSetupTokenAsync());
    }

    [Fact]
    public async Task Legacy_path_authority_remains_a_bounded_fallback()
    {
        var path = CreateTokenFile("legacy-path-token");
        try
        {
            var store = CreateStore(new Dictionary<string, string?>
            {
                [MemControlPlaneIdentity.Legacy.EnvironmentVariables.SetupTokenPath] = path
            });

            Assert.Equal("legacy-path-token", await store.GetSetupTokenAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static InstallerSetupTokenStore CreateStore(
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var options = new InstallerAuthOptions
        {
            DefaultTokenPath = Path.Combine(
                Path.GetTempPath(),
                $"mem-missing-setup-token-{Guid.NewGuid():N}")
        };

        return new InstallerSetupTokenStore(
            configuration,
            new TestHostEnvironment(),
            Options.Create(options),
            NullLogger<InstallerSetupTokenStore>.Instance);
    }

    private static string CreateTokenFile(string token)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"mem-control-plane-setup-token-{Guid.NewGuid():N}");
        File.WriteAllText(path, token);
        return path;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
