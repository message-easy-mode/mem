using System.Security.Cryptography;
using HostAgent.Runtime.Backups.StandardRecreate;

namespace HostAgent.Tests.Runtime.Backups.StandardRecreate;

public sealed class StandardRecreateIdentityFidelityTests
{
    [Fact]
    public void NormalizeAndVerify_rewrites_stale_identity_paths_and_preserves_exact_signing_key_bytes()
    {
        using var fixture = Fixture.Create(
            homeserver:
            """
            server_name: "matrix.example.test"
            signing_key_path: "/data/matrix.example.test.signing.key"
            media_store_path: "/data/legacy_media"
            """);

        var expectedBytes = File.ReadAllBytes(fixture.SigningKeyPath);
        var expectedSha256 = SHA256.HashData(expectedBytes);

        var result = StandardRecreateIdentityFidelity.NormalizeAndVerify(
            fixture.HomeserverPath,
            fixture.MatrixDataPath,
            expectedBytes.LongLength,
            expectedSha256);

        var persisted = File.ReadAllText(fixture.HomeserverPath);
        Assert.Contains(
            "signing_key_path: \"/data/signing.key\"",
            persisted,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/data/matrix.example.test.signing.key",
            persisted,
            StringComparison.Ordinal);
        Assert.Contains(
            "media_store_path: \"/data/media_store\"",
            persisted,
            StringComparison.Ordinal);
        Assert.Equal(expectedBytes, File.ReadAllBytes(fixture.SigningKeyPath));
        Assert.Equal(expectedBytes.LongLength, result.SigningKeyBytes);
        Assert.Equal(
            StandardRecreateIdentityFidelity.CanonicalSigningKeyContainerPath,
            result.SigningKeyContainerPath);
        Assert.Equal(
            StandardRecreateIdentityFidelity.CanonicalMediaStoreContainerPath,
            result.MediaStoreContainerPath);
    }

    [Fact]
    public void NormalizeAndVerify_adds_canonical_identity_paths_when_backup_omits_them()
    {
        using var fixture = Fixture.Create(
            homeserver: "server_name: matrix.example.test");

        var expectedBytes = File.ReadAllBytes(fixture.SigningKeyPath);

        StandardRecreateIdentityFidelity.NormalizeAndVerify(
            fixture.HomeserverPath,
            fixture.MatrixDataPath,
            expectedBytes.LongLength,
            SHA256.HashData(expectedBytes));

        var persisted = File.ReadAllText(fixture.HomeserverPath);
        Assert.Contains(
            "signing_key_path: \"/data/signing.key\"",
            persisted,
            StringComparison.Ordinal);
        Assert.Contains(
            "media_store_path: \"/data/media_store\"",
            persisted,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeAndVerify_rejects_a_signing_key_that_no_longer_matches_backup_identity()
    {
        using var fixture = Fixture.Create(
            homeserver:
            """
            server_name: "matrix.example.test"
            signing_key_path: "/data/matrix.example.test.signing.key"
            """);

        var expectedBytes = File.ReadAllBytes(fixture.SigningKeyPath);
        var expectedSha256 = SHA256.HashData(expectedBytes);

        File.WriteAllText(
            fixture.SigningKeyPath,
            "ed25519 changed_identity not-the-backup-key");

        var exception = Assert.Throws<InvalidDataException>(() =>
            StandardRecreateIdentityFidelity.NormalizeAndVerify(
                fixture.HomeserverPath,
                fixture.MatrixDataPath,
                expectedBytes.LongLength,
                expectedSha256));

        Assert.Contains(
            "signing key",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeAndVerify_rejects_missing_signing_key_before_synapse_start()
    {
        using var fixture = Fixture.Create(
            homeserver: "server_name: matrix.example.test");

        var expectedBytes = File.ReadAllBytes(fixture.SigningKeyPath);
        var expectedSha256 = SHA256.HashData(expectedBytes);
        File.Delete(fixture.SigningKeyPath);

        var exception = Assert.Throws<InvalidDataException>(() =>
            StandardRecreateIdentityFidelity.NormalizeAndVerify(
                fixture.HomeserverPath,
                fixture.MatrixDataPath,
                expectedBytes.LongLength,
                expectedSha256));

        Assert.Contains(
            "signing key",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            string root,
            string matrixDataPath,
            string homeserverPath,
            string signingKeyPath)
        {
            Root = root;
            MatrixDataPath = matrixDataPath;
            HomeserverPath = homeserverPath;
            SigningKeyPath = signingKeyPath;
        }

        public string Root { get; }
        public string MatrixDataPath { get; }
        public string HomeserverPath { get; }
        public string SigningKeyPath { get; }

        public static Fixture Create(string homeserver)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"mem-standard-recreate-identity-{Guid.NewGuid():N}");
            var matrixDataPath = Path.Combine(root, "matrix");
            Directory.CreateDirectory(matrixDataPath);

            var homeserverPath = Path.Combine(matrixDataPath, "homeserver.yaml");
            var signingKeyPath = Path.Combine(matrixDataPath, "signing.key");

            File.WriteAllText(homeserverPath, homeserver);
            File.WriteAllBytes(
                signingKeyPath,
                "ed25519 backup_identity exact-preserved-key"u8.ToArray());

            return new Fixture(
                root,
                matrixDataPath,
                homeserverPath,
                signingKeyPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
