using Microsoft.Extensions.Logging.Abstractions;
using Modules.Auth.Services;

namespace Api.IntegrationTests.Security;

public sealed class InstallerTokenValidatorContractTests
{
    [Fact]
    public async Task SEC_AUTH_00_characterizes_exact_setup_token_validation()
    {
        var validator = new InstallerTokenValidator(
            new StaticSetupTokenStore("mem_test_setup_token"),
            NullLogger<InstallerTokenValidator>.Instance);

        Assert.True(await validator.IsValidAsync("mem_test_setup_token"));
        Assert.False(await validator.IsValidAsync("wrong-token"));
        Assert.False(await validator.IsValidAsync(null));
    }

    private sealed class StaticSetupTokenStore(string? token) : IInstallerSetupTokenStore
    {
        public Task<string?> GetSetupTokenAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(token);
        }
    }
}
