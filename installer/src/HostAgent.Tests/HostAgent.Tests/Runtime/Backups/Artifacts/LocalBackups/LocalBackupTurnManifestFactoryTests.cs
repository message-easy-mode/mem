using System.Text;
using HostAgent.Runtime.Backups.Artifacts.LocalBackups;
using HostAgent.Runtime.Stacks.Turn;

namespace HostAgent.Tests.Runtime.Backups.Artifacts.LocalBackups;

public sealed class LocalBackupTurnManifestFactoryTests
{
    [Fact]
    public void Matching_mem_managed_metadata_is_preserved_in_backup_manifest()
    {
        var path = WriteConfig(
            """
            server_name: "matrix.example.test"
            # MEM-managed TURN configuration for Matrix voice/video calls.
            # TURN public host: turn.example.test
            # TURN realm: example.test
            turn_uris:
              - "turn:turn.example.test:3478?transport=udp"
              - "turn:turn.example.test:3478?transport=tcp"
            turn_shared_secret: "secret"
            turn_user_lifetime: "1h"
            turn_allow_guests: true
            """);

        try
        {
            var bytes = File.ReadAllBytes(path);
            var hash = RuntimeStackTurnConfigTransaction.Hash(bytes);
            var warnings = new List<string>();
            var result = LocalBackupTurnManifestFactory.Create(
                path,
                new Dictionary<string, string?>
                {
                    ["turnManagement"] = RuntimeStackTurnManagementKinds.MemManaged,
                    ["turnConfigurationSource"] = "platform-coturn",
                    ["turnConfigurationSha256"] = hash
                },
                warnings);

            Assert.NotNull(result);
            Assert.True(result!.Configured);
            Assert.Equal(RuntimeStackTurnStates.Connected, result.State);
            Assert.Equal(RuntimeStackTurnManagementKinds.MemManaged, result.Management);
            Assert.Equal("platform-coturn", result.ConfigurationSource);
            Assert.Equal(hash, result.ConfigurationSha256);
            Assert.Equal(2, result.TurnUris.Count);
            Assert.True(result.SharedSecretPresent);
            Assert.Empty(warnings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void No_turn_settings_are_recorded_as_disconnected_even_when_stale_metadata_says_connected()
    {
        var path = WriteConfig(
            """
            server_name: "matrix.example.test"
            report_stats: false
            """);

        try
        {
            var warnings = new List<string>();
            var result = LocalBackupTurnManifestFactory.Create(
                path,
                new Dictionary<string, string?>
                {
                    ["turnManagement"] = RuntimeStackTurnManagementKinds.MemManaged,
                    ["turnConfigurationSha256"] = "sha256:stale"
                },
                warnings);

            Assert.NotNull(result);
            Assert.False(result!.Configured);
            Assert.Equal(RuntimeStackTurnStates.NotConnected, result.State);
            Assert.Equal(RuntimeStackTurnManagementKinds.None, result.Management);
            Assert.Equal("backup-disconnected", result.ConfigurationSource);
            Assert.Contains(warnings, warning => warning.Contains("did not match", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Unmarked_turn_settings_are_recorded_as_external()
    {
        var path = WriteConfig(
            """
            server_name: "matrix.example.test"
            turn_uris: ["turn:external.example.test:3478?transport=udp"]
            turn_shared_secret_path: "/run/secrets/external-turn"
            """);

        try
        {
            var result = LocalBackupTurnManifestFactory.Create(
                path,
                new Dictionary<string, string?>(),
                new List<string>());

            Assert.NotNull(result);
            Assert.True(result!.Configured);
            Assert.Equal(RuntimeStackTurnStates.External, result.State);
            Assert.Equal(RuntimeStackTurnManagementKinds.ExternalObserved, result.Management);
            Assert.True(result.SharedSecretPresent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteConfig(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mem-turn-backup-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
        return path;
    }
}
