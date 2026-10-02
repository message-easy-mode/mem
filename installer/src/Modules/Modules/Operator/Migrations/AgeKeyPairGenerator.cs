using System.Diagnostics;

namespace Modules.Operator.Migrations;

/// <summary>
/// Generates a native age X25519 identity through the installed age-keygen
/// binary. The private identity is read only inside this process, never logged,
/// and the temporary file is deleted before the method returns.
/// </summary>
public sealed class AgeKeyPairGenerator : IAgeKeyPairGenerator
{
    public async Task<AgeKeyPair> GenerateAsync(CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"mem-age-{Guid.NewGuid():N}.key");

        try
        {
            var generate = await RunAsync(
                "age-keygen",
                ["-o", tempPath],
                cancellationToken);

            if (generate.ExitCode != 0 || !File.Exists(tempPath))
            {
                throw new SecureMigrationIntakeException(
                    "age_key_generation_failed",
                    "The target could not generate a migration encryption identity. Confirm that age-keygen is installed.");
            }

            var identityFile = await File.ReadAllTextAsync(tempPath, cancellationToken);
            var identity = AgeIdentityFileParser.Parse(identityFile);

            var derive = await RunAsync(
                "age-keygen",
                ["-y", tempPath],
                cancellationToken);

            var recipient = derive.StandardOutput.Trim();
            if (derive.ExitCode != 0 ||
                !recipient.StartsWith("age1", StringComparison.Ordinal) ||
                recipient.Any(char.IsWhiteSpace))
            {
                throw new SecureMigrationIntakeException(
                    "age_recipient_invalid",
                    "The target could not derive a valid migration encryption recipient.");
            }

            return new AgeKeyPair(recipient, identity);
        }
        catch (SecureMigrationIntakeException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SecureMigrationIntakeException(
                "age_key_generation_failed",
                "The target could not generate a migration encryption identity. Confirm that age-keygen is installed.",
                exception);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception exception)
                {
                    throw new SecureMigrationIntakeException(
                        "age_identity_cleanup_failed",
                        "The temporary migration identity could not be removed. The intake was not created.",
                        exception);
                }
            }
        }
    }

    private static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}

internal static class AgeIdentityFileParser
{
    public static string Parse(string contents)
    {
        var identities = contents
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#'))
            .Where(line => line.StartsWith("AGE-SECRET-KEY-", StringComparison.Ordinal))
            .ToArray();

        if (identities.Length != 1 ||
            identities[0].Any(char.IsWhiteSpace))
        {
            throw new SecureMigrationIntakeException(
                "age_identity_invalid",
                "The generated migration encryption identity was invalid.");
        }

        return identities[0];
    }
}
