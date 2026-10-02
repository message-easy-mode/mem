using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modules.Auth.Configuration;

namespace Modules.Auth.Services;

public interface IInstallerSetupTokenStore
{
    Task<string?> GetSetupTokenAsync(CancellationToken ct = default);
}

public sealed class InstallerSetupTokenStore(
    IConfiguration configuration,
    IHostEnvironment environment,
    IOptions<InstallerAuthOptions> options,
    ILogger<InstallerSetupTokenStore> logger)
    : IInstallerSetupTokenStore
{
    private readonly InstallerAuthOptions _options = options.Value;

    public Task<string?> GetSetupTokenAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var canonicalEnvironmentToken = ReadConfiguredValue(
            _options.TokenEnvironmentVariableName);
        if (canonicalEnvironmentToken is not null)
        {
            return Task.FromResult<string?>(canonicalEnvironmentToken);
        }

        var canonicalConfiguredPath = ReadConfiguredValue(
            _options.TokenPathEnvironmentVariableName);
        var canonicalPathToken = ReadTokenFile(canonicalConfiguredPath);
        if (canonicalPathToken is not null)
        {
            return Task.FromResult<string?>(canonicalPathToken);
        }

        var legacyEnvironmentToken = ReadConfiguredValue(
            _options.LegacyTokenEnvironmentVariableName);
        if (legacyEnvironmentToken is not null)
        {
            logger.LogWarning(
                "Using legacy Control Plane setup-token environment alias {EnvironmentVariableName}. Migrate bootstrap configuration to {CanonicalEnvironmentVariableName}.",
                _options.LegacyTokenEnvironmentVariableName,
                _options.TokenEnvironmentVariableName);
            return Task.FromResult<string?>(legacyEnvironmentToken);
        }

        var legacyConfiguredPath = ReadConfiguredValue(
            _options.LegacyTokenPathEnvironmentVariableName);
        var legacyPathToken = ReadTokenFile(legacyConfiguredPath);
        if (legacyPathToken is not null)
        {
            logger.LogWarning(
                "Using legacy Control Plane setup-token path alias {EnvironmentVariableName}. Migrate bootstrap configuration to {CanonicalEnvironmentVariableName}.",
                _options.LegacyTokenPathEnvironmentVariableName,
                _options.TokenPathEnvironmentVariableName);
            return Task.FromResult<string?>(legacyPathToken);
        }

        var defaultToken = ReadTokenFile(_options.DefaultTokenPath, warnWhenMissing: true);
        if (defaultToken is not null)
        {
            return Task.FromResult<string?>(defaultToken);
        }

        if (environment.IsDevelopment() && !string.IsNullOrWhiteSpace(_options.DevelopmentSetupToken))
        {
            logger.LogWarning("Using InstallerAuth:DevelopmentSetupToken. This must only be used for local development.");
            return Task.FromResult<string?>(_options.DevelopmentSetupToken.Trim());
        }

        return Task.FromResult<string?>(null);
    }

    private string? ReadConfiguredValue(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var value = configuration[key.Trim()];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private string? ReadTokenFile(string? path, bool warnWhenMissing = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var tokenPath = path.Trim();
        if (!File.Exists(tokenPath))
        {
            if (warnWhenMissing)
            {
                logger.LogWarning("Control Plane setup token file was not found at {TokenPath}.", tokenPath);
            }

            return null;
        }

        var token = File.ReadAllText(tokenPath).Trim();
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        logger.LogWarning("Control Plane setup token file exists but is empty at {TokenPath}.", tokenPath);
        return null;
    }
}
