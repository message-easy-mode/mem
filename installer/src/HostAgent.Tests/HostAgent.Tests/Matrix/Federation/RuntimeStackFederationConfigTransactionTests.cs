using System.Security.Cryptography;
using System.Text;
using HostAgent.Matrix.Federation;
using Modules.Integrations.Npm.Contracts;

namespace HostAgent.Tests.Matrix.Federation;

public sealed class RuntimeStackFederationConfigTransactionTests
{
    [Fact]
    public async Task Applies_and_restores_through_same_directory_replacements()
    {
        var root = Path.Combine(Path.GetTempPath(), "mem-federation-transaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var active = Path.Combine(root, "homeserver.yaml");
            var before = Path.Combine(root, "homeserver.before.yaml");
            var candidate = Path.Combine(root, "homeserver.candidate.yaml");
            var beforeBytes = Encoding.UTF8.GetBytes("server_name: matrix.example\n");
            var candidateBytes = Encoding.UTF8.GetBytes(
                "server_name: matrix.example\nfederation_domain_whitelist:\n  - partner.example\n");
            await File.WriteAllBytesAsync(active, beforeBytes);
            await File.WriteAllBytesAsync(before, beforeBytes);
            await File.WriteAllBytesAsync(candidate, candidateBytes);

            var snapshot = Snapshot(root, active, before, candidate, Hash(beforeBytes));
            var transaction = new RuntimeStackFederationConfigTransaction();

            await transaction.ApplyCandidateAsync(snapshot, CancellationToken.None);
            Assert.Equal(candidateBytes, await File.ReadAllBytesAsync(active));
            Assert.True(File.Exists(candidate));
            Assert.Empty(Directory.GetFiles(root, ".homeserver.yaml.mem-*.tmp"));

            await transaction.RestoreBeforeAsync(snapshot, CancellationToken.None);
            Assert.Equal(beforeBytes, await File.ReadAllBytesAsync(active));
            Assert.Empty(Directory.GetFiles(root, ".homeserver.yaml.mem-*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Refuses_to_replace_an_active_config_that_changed_after_snapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "mem-federation-transaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var active = Path.Combine(root, "homeserver.yaml");
            var before = Path.Combine(root, "homeserver.before.yaml");
            var candidate = Path.Combine(root, "homeserver.candidate.yaml");
            var beforeBytes = Encoding.UTF8.GetBytes("server_name: matrix.example\n");
            var changedBytes = Encoding.UTF8.GetBytes("server_name: externally-changed.example\n");
            var candidateBytes = Encoding.UTF8.GetBytes(
                "server_name: matrix.example\nfederation_domain_whitelist:\n  - partner.example\n");
            await File.WriteAllBytesAsync(active, changedBytes);
            await File.WriteAllBytesAsync(before, beforeBytes);
            await File.WriteAllBytesAsync(candidate, candidateBytes);

            var snapshot = Snapshot(root, active, before, candidate, Hash(beforeBytes));
            var transaction = new RuntimeStackFederationConfigTransaction();

            await Assert.ThrowsAsync<RuntimeStackFederationConfigStateChangedException>(() =>
                transaction.ApplyCandidateAsync(snapshot, CancellationToken.None));
            Assert.Equal(changedBytes, await File.ReadAllBytesAsync(active));
            Assert.Empty(Directory.GetFiles(root, ".homeserver.yaml.mem-*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RuntimeStackFederationSnapshot Snapshot(
        string root,
        string active,
        string before,
        string candidate,
        string beforeHash) =>
        new(
            OperationDirectory: root,
            ActiveConfigPath: active,
            BeforeConfigPath: before,
            CandidateConfigPath: candidate,
            NpmRouteSnapshotPath: Path.Combine(root, "npm-route.before.json"),
            NpmLookupDomain: "matrix.example.test",
            NpmRouteSnapshot: new NpmProxyHostSnapshot(
                Id: 1,
                DomainNames: ["matrix.example.test"],
                ForwardHost: "mem-matrix-test",
                ForwardPort: 8008,
                AccessListId: 0,
                CertificateId: 1,
                ForwardScheme: "http",
                AdvancedConfig: string.Empty,
                Locations: [],
                SslForced: true,
                Http2Support: true,
                AllowWebsocketUpgrade: true,
                BlockExploits: true,
                CachingEnabled: false,
                Enabled: true,
                HstsEnabled: false,
                HstsSubdomains: false,
                TrustForwardedProto: false),
            BeforeConfigSha256: beforeHash,
            CandidateConfigSha256: "sha256:candidate",
            NpmRouteSnapshotSha256: "sha256:npm");

    private static string Hash(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
