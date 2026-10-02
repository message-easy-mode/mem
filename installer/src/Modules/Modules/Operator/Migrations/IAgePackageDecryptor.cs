namespace Modules.Operator.Migrations;

public interface IAgePackageDecryptor
{
    Task DecryptAsync(string identity, string encryptedPath, string outputPath, CancellationToken cancellationToken);
}
