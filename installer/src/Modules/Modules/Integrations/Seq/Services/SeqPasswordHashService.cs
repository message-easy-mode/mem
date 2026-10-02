using Infrastructure.Docker;

namespace Modules.Integrations.Seq.Services;

public sealed class SeqPasswordHashService(
    SeqDiagnosticsOptions options,
    SeqRuntimeImageResolver imageResolver,
    IDockerIsolatedStandardInputRunner dockerRunner)
{
    public async Task<string> HashAsync(
        ReadOnlyMemory<char> password,
        CancellationToken cancellationToken)
    {
        ValidatePassword(password);

        var image = await imageResolver.ResolveForSetupAsync(cancellationToken);
        var containerName = $"mem-seq-password-hash-{Guid.NewGuid():N}";

        var result = await dockerRunner.RunAsync(
            image.ResolvedImageId,
            containerName,
            ["config", "hash"],
            password,
            TimeSpan.FromSeconds(options.PasswordHashTimeoutSeconds),
            cancellationToken);

        if (result.TimedOut)
        {
            throw new SeqOperationException(
                "seq_password_hash_timeout",
                Microsoft.AspNetCore.Http.StatusCodes.Status504GatewayTimeout,
                "Seq administrator password hashing timed out.");
        }

        if (!result.Succeeded)
        {
            throw new SeqOperationException(
                "seq_password_hash_failed",
                Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable,
                "Seq administrator password hashing failed.");
        }

        var hash = result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(hash) ||
            hash.Length > 4096 ||
            hash.Any(char.IsWhiteSpace))
        {
            throw new SeqOperationException(
                "seq_password_hash_invalid",
                Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable,
                "Seq returned an invalid administrator password hash.");
        }

        return hash;
    }

    private static void ValidatePassword(ReadOnlyMemory<char> password)
    {
        var value = password.Span;
        if (value.IsEmpty ||
            value.Length > 1024 ||
            value.IndexOf('\0') >= 0 ||
            value.IndexOf('\r') >= 0 ||
            value.IndexOf('\n') >= 0 ||
            IsAllWhitespace(value))
        {
            throw new SeqOperationException(
                "seq_administrator_password_invalid",
                Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest,
                "The Seq administrator password is invalid.");
        }
    }

    private static bool IsAllWhitespace(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return true;
    }
}
