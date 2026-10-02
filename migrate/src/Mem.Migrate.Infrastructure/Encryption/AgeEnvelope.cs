using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Processes;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Infrastructure.Encryption;

public sealed class AgeEnvelope(IProcessRunner processRunner) : IAgeEnvelope
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromHours(24);

    public async Task<AgeEnvelopeResult> EncryptAsync(
        string plaintextPath,
        string encryptedPath,
        string recipient,
        string ageCommand,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        var source = Path.GetFullPath(plaintextPath);
        var destination = Path.GetFullPath(encryptedPath);

        ValidateRegularInput(source, "The plaintext migration archive was not found.");

        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException(
                "Encrypted archive destination has no parent directory.");
        PrivateFilePermissions.EnsureDirectory(parent);

        if (File.Exists(destination))
        {
            throw new IOException(
                $"Encrypted archive destination already exists: {destination}");
        }

        var partialPath = destination + ".partial";
        File.Delete(partialPath);
        var ownsDestination = false;

        try
        {
            var result = await processRunner.RunAsync(
                new ProcessRequest(
                    ageCommand,
                    [
                        "--encrypt",
                        "--recipient",
                        recipient,
                        "--output",
                        partialPath,
                        source
                    ],
                    OperationTimeout,
                    MaximumOutputCharacters: 64 * 1024),
                cancellationToken);

            if (!result.Succeeded || !File.Exists(partialPath))
            {
                throw new InvalidOperationException(
                    $"age encryption failed: {SafeFailure(result)}");
            }

            PrivateFilePermissions.EnsureFile(partialPath);
            var sha256 = await Sha256File.ComputeAsync(
                partialPath,
                cancellationToken);
            var sizeBytes = new FileInfo(partialPath).Length;
            File.Move(partialPath, destination);
            ownsDestination = true;
            PrivateFilePermissions.EnsureFile(destination);

            return new AgeEnvelopeResult(
                destination,
                sha256,
                sizeBytes);
        }
        catch
        {
            File.Delete(partialPath);

            if (ownsDestination)
            {
                File.Delete(destination);
            }

            throw;
        }
    }

    public async Task DecryptAsync(
        string encryptedPath,
        string plaintextPath,
        string identityPath,
        string ageCommand,
        CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(encryptedPath);
        var destination = Path.GetFullPath(plaintextPath);
        var identity = Path.GetFullPath(identityPath);

        ValidateRegularInput(source, "The encrypted migration archive was not found.");
        ValidateRegularInput(identity, "The age identity file was not found.");

        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException(
                "Decrypted archive destination has no parent directory.");
        PrivateFilePermissions.EnsureDirectory(parent);

        if (File.Exists(destination))
        {
            throw new IOException(
                $"Decrypted archive destination already exists: {destination}");
        }

        var partialPath = destination + ".partial";
        File.Delete(partialPath);
        var ownsDestination = false;

        try
        {
            var result = await processRunner.RunAsync(
                new ProcessRequest(
                    ageCommand,
                    [
                        "--decrypt",
                        "--identity",
                        identity,
                        "--output",
                        partialPath,
                        source
                    ],
                    OperationTimeout,
                    MaximumOutputCharacters: 64 * 1024),
                cancellationToken);

            if (!result.Succeeded || !File.Exists(partialPath))
            {
                throw new InvalidOperationException(
                    $"age decryption failed: {SafeFailure(result)}");
            }

            PrivateFilePermissions.EnsureFile(partialPath);
            File.Move(partialPath, destination);
            ownsDestination = true;
            PrivateFilePermissions.EnsureFile(destination);
        }
        catch
        {
            File.Delete(partialPath);

            if (ownsDestination)
            {
                File.Delete(destination);
            }

            throw;
        }
    }


    private static void ValidateRegularInput(string path, string missingMessage)
    {
        var information = new FileInfo(Path.GetFullPath(path));

        if (!information.Exists)
        {
            throw new FileNotFoundException(missingMessage, information.FullName);
        }

        UnixFileTypeSafety.EnsureRegularFile(information.FullName);

        if ((information.Attributes & FileAttributes.ReparsePoint) != 0 ||
            information.LinkTarget is not null)
        {
            throw new InvalidDataException(
                $"Age input may not be a symbolic link: {information.FullName}");
        }
    }

    private static string SafeFailure(ProcessResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            return result.StandardError.Trim();
        }

        return result.ExitCode is null
            ? "the process did not start"
            : $"exit code {result.ExitCode}";
    }
}
