using System.Diagnostics;

namespace Modules.Operator.Migrations;

public sealed class AgePackageDecryptor : IAgePackageDecryptor
{
    public async Task DecryptAsync(string identity, string encryptedPath, string outputPath, CancellationToken cancellationToken)
    {
        var identityPath = Path.Combine(Path.GetTempPath(), $"mem-age-identity-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(identityPath, identity + Environment.NewLine, cancellationToken);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(identityPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var psi = new ProcessStartInfo { FileName = "age", RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("--decrypt"); psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(identityPath);
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(outputPath); psi.ArgumentList.Add(encryptedPath);
            using var process = new Process { StartInfo = psi };
            process.Start();
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                throw new SecureMigrationIntakeException("age_package_decryption_failed", "The package could not be decrypted for this secure intake.");
            _ = await stderr;
        }
        catch (SecureMigrationIntakeException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new SecureMigrationIntakeException("age_package_decryption_failed", "The package could not be decrypted for this secure intake.", ex); }
        finally { try { if (File.Exists(identityPath)) File.Delete(identityPath); } catch { } }
    }
}
