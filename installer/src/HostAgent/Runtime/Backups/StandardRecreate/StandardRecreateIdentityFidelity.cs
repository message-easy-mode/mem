using System.Security.Cryptography;

namespace HostAgent.Runtime.Backups.StandardRecreate;

/// <summary>
/// Normalizes the restored Synapse identity-bearing filesystem references to
/// the canonical paths used by MEM's normalized runtime and verifies that the
/// exact signing-key bytes copied from the Backup Catalog remain intact before
/// Synapse is allowed to start.
/// </summary>
internal static class StandardRecreateIdentityFidelity
{
    internal const string CanonicalSigningKeyContainerPath = "/data/signing.key";
    internal const string CanonicalMediaStoreContainerPath = "/data/media_store";

    public static StandardRecreateIdentityFidelityResult NormalizeAndVerify(
        string homeserverPath,
        string matrixDataPath,
        long expectedSigningKeyBytes,
        byte[] expectedSigningKeySha256)
    {
        if (string.IsNullOrWhiteSpace(homeserverPath) || !File.Exists(homeserverPath))
        {
            throw new InvalidDataException(
                "The restored Synapse homeserver.yaml is unavailable for signing-key identity verification.");
        }

        if (string.IsNullOrWhiteSpace(matrixDataPath))
        {
            throw new InvalidDataException(
                "The restored Matrix data path is unavailable for signing-key identity verification.");
        }

        if (expectedSigningKeyBytes <= 0 || expectedSigningKeySha256.Length == 0)
        {
            throw new InvalidDataException(
                "The managed Backup Catalog signing-key identity evidence is incomplete.");
        }

        var signingKeyPath = Path.Combine(matrixDataPath, "signing.key");
        if (!File.Exists(signingKeyPath))
        {
            throw new InvalidDataException(
                "The restored Matrix signing key is missing from the normalized runtime data directory.");
        }

        var actualSigningKeyBytes = new FileInfo(signingKeyPath).Length;
        if (actualSigningKeyBytes != expectedSigningKeyBytes)
        {
            throw new InvalidDataException(
                "The restored Matrix signing key size no longer matches the managed Backup Catalog payload.");
        }

        var actualSigningKeySha256 = SHA256.HashData(File.ReadAllBytes(signingKeyPath));
        if (!CryptographicOperations.FixedTimeEquals(
                actualSigningKeySha256,
                expectedSigningKeySha256))
        {
            throw new InvalidDataException(
                "The restored Matrix signing key bytes no longer match the managed Backup Catalog payload.");
        }

        var lines = File.ReadAllLines(homeserverPath).ToList();
        EnsureOrReplaceTopLevelScalar(
            lines,
            "signing_key_path",
            $"\"{CanonicalSigningKeyContainerPath}\"");
        EnsureOrReplaceTopLevelScalar(
            lines,
            "media_store_path",
            $"\"{CanonicalMediaStoreContainerPath}\"");
        File.WriteAllLines(homeserverPath, lines);

        var persistedLines = File.ReadAllLines(homeserverPath);
        var persistedSigningKeyPath = ReadTopLevelScalar(
            persistedLines,
            "signing_key_path");
        var persistedMediaStorePath = ReadTopLevelScalar(
            persistedLines,
            "media_store_path");

        if (!string.Equals(
                persistedSigningKeyPath,
                CanonicalSigningKeyContainerPath,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The restored Synapse configuration does not reference the preserved Matrix signing key at the canonical runtime path.");
        }

        if (!string.Equals(
                persistedMediaStorePath,
                CanonicalMediaStoreContainerPath,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The restored Synapse configuration does not reference the normalized Matrix media store path.");
        }

        // Re-read the key after persisting homeserver.yaml so this guard also
        // protects future refactors from accidentally mutating identity material
        // while normalizing the restored configuration.
        var finalSigningKeySha256 = SHA256.HashData(File.ReadAllBytes(signingKeyPath));
        if (!CryptographicOperations.FixedTimeEquals(
                finalSigningKeySha256,
                expectedSigningKeySha256))
        {
            throw new InvalidDataException(
                "The restored Matrix signing key changed while normalizing the Synapse configuration.");
        }

        return new StandardRecreateIdentityFidelityResult(
            SigningKeyBytes: actualSigningKeyBytes,
            SigningKeyContainerPath: persistedSigningKeyPath,
            MediaStoreContainerPath: persistedMediaStorePath);
    }

    private static void EnsureOrReplaceTopLevelScalar(
        List<string> lines,
        string key,
        string value)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            if (line.StartsWith(key + ":", StringComparison.Ordinal))
            {
                lines[index] = $"{key}: {value}";
                return;
            }
        }

        lines.Add($"{key}: {value}");
    }

    private static string? ReadTopLevelScalar(
        IReadOnlyList<string> lines,
        string key)
    {
        foreach (var line in lines)
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            if (!line.StartsWith(key + ":", StringComparison.Ordinal))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            return separator < 0 || separator == line.Length - 1
                ? null
                : line[(separator + 1)..].Trim().Trim('"', '\'');
        }

        return null;
    }
}

internal sealed record StandardRecreateIdentityFidelityResult(
    long SigningKeyBytes,
    string SigningKeyContainerPath,
    string MediaStoreContainerPath);
