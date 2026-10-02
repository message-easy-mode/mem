using System.Text;
using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Stacks.Turn;

namespace HostAgent.Tests.Runtime.Stacks.Turn;

public sealed class SynapseTurnConfigEditorTests
{
    [Fact]
    public async Task Connect_block_preserves_unrelated_configuration_and_is_readable()
    {
        var editor = new SynapseTurnConfigEditor();
        var reader = new SynapseTurnConfigReader();
        var bytes = editor.RenderConnect(
            Encoding.UTF8.GetBytes("server_name: matrix.example.test\npublic_baseurl: https://matrix.example.test/\n"),
            Platform());
        var path = TemporaryFile(bytes);

        try
        {
            var text = Encoding.UTF8.GetString(bytes);
            Assert.Contains("server_name: matrix.example.test", text, StringComparison.Ordinal);
            Assert.Contains("# MEM-managed TURN configuration for Matrix voice/video calls.", text, StringComparison.Ordinal);
            Assert.Contains("turn_shared_secret: \"platform-secret\"", text, StringComparison.Ordinal);

            var result = await reader.ReadAsync(path, CancellationToken.None);
            Assert.True(result.Supported);
            Assert.True(result.AnyTurnSettings);
            Assert.True(result.MemManagedMarkerPresent);
            Assert.Equal(2, result.TurnUris.Count);
            Assert.Equal("platform-secret", result.SharedSecretValue);
            Assert.Equal("1h", result.UserLifetime);
            Assert.True(result.AllowGuests);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Existing_turn_keys_and_mem_comments_are_replaced_once()
    {
        var editor = new SynapseTurnConfigEditor();
        var before = Encoding.UTF8.GetBytes(
            "server_name: matrix.example.test\n" +
            "# MEM-managed TURN configuration for Matrix voice/video calls.\n" +
            "# TURN public host: old.example.test\n" +
            "# TURN realm: old.example.test\n" +
            "turn_uris:\n  - \"turn:old.example.test:3478?transport=udp\"\n" +
            "turn_shared_secret: \"old-secret\"\n" +
            "turn_user_lifetime: \"30m\"\n" +
            "turn_allow_guests: false\n" +
            "# unrelated top-level comment\n" +
            "report_stats: false\n");

        var text = Encoding.UTF8.GetString(editor.RenderConnect(before, Platform()));

        Assert.DoesNotContain("old.example.test", text, StringComparison.Ordinal);
        Assert.DoesNotContain("old-secret", text, StringComparison.Ordinal);
        Assert.Equal(1, Count(text, "turn_uris:"));
        Assert.Equal(1, Count(text, "turn_shared_secret:"));
        Assert.Contains("# unrelated top-level comment", text, StringComparison.Ordinal);
        Assert.Contains("report_stats: false", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disconnect_removes_only_mem_turn_settings_and_preserves_unrelated_yaml()
    {
        var editor = new SynapseTurnConfigEditor();
        var reader = new SynapseTurnConfigReader();
        var original = Encoding.UTF8.GetBytes(
            "server_name: matrix.example.test\n" +
            "# unrelated top-level comment\n" +
            "report_stats: false\n");
        var connected = editor.RenderConnect(original, Platform());
        var disconnected = editor.RenderDisconnect(connected);
        var text = Encoding.UTF8.GetString(disconnected);
        var path = TemporaryFile(disconnected);

        try
        {
            Assert.Contains("server_name: matrix.example.test", text, StringComparison.Ordinal);
            Assert.Contains("# unrelated top-level comment", text, StringComparison.Ordinal);
            Assert.Contains("report_stats: false", text, StringComparison.Ordinal);
            Assert.DoesNotContain("MEM-managed TURN", text, StringComparison.Ordinal);
            Assert.DoesNotContain("turn_uris:", text, StringComparison.Ordinal);
            Assert.DoesNotContain("turn_shared_secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("turn_user_lifetime", text, StringComparison.Ordinal);
            Assert.DoesNotContain("turn_allow_guests", text, StringComparison.Ordinal);
            Assert.Equal(original, disconnected);

            var result = await reader.ReadAsync(path, CancellationToken.None);
            Assert.True(result.Supported);
            Assert.False(result.AnyTurnSettings);
            Assert.Empty(result.TurnUris);
        }
        finally
        {
            File.Delete(path);
        }
    }


    [Fact]
    public async Task Preserve_copies_exact_source_turn_settings_without_replacing_unrelated_target_yaml()
    {
        var editor = new SynapseTurnConfigEditor();
        var reader = new SynapseTurnConfigReader();
        var target = Encoding.UTF8.GetBytes(
            "server_name: matrix.target.test\n" +
            "report_stats: false\n");
        var source = Encoding.UTF8.GetBytes(
            "server_name: matrix.source.test\n" +
            "# TURN public host: turn.external.test\n" +
            "# TURN realm: external.test\n" +
            "turn_uris:\n  - \"turn:turn.external.test:3478?transport=udp\"\n" +
            "turn_shared_secret_path: \"/data/external-turn.secret\"\n" +
            "turn_user_lifetime: \"45m\"\n" +
            "turn_allow_guests: false\n");

        var preserved = editor.RenderPreserve(target, source);
        var text = Encoding.UTF8.GetString(preserved);
        var path = TemporaryFile(preserved);

        try
        {
            Assert.Contains("server_name: matrix.target.test", text, StringComparison.Ordinal);
            Assert.DoesNotContain("server_name: matrix.source.test", text, StringComparison.Ordinal);
            Assert.Contains("turn_shared_secret_path: \"/data/external-turn.secret\"", text, StringComparison.Ordinal);
            Assert.Contains("# TURN realm: external.test", text, StringComparison.Ordinal);

            var result = await reader.ReadAsync(path, CancellationToken.None);
            Assert.True(result.Supported);
            Assert.True(result.AnyTurnSettings);
            Assert.Equal("shared-secret-path", result.CredentialMechanism);
            Assert.Equal("/data/external-turn.secret", result.SharedSecretPath);
            Assert.Equal("45m", result.UserLifetime);
            Assert.False(result.AllowGuests);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static CoturnSynapseConfig Platform() =>
        new(
            PublicHost: "turn.example.test",
            Realm: "example.test",
            TurnUris:
            [
                "turn:turn.example.test:3478?transport=udp",
                "turn:turn.example.test:3478?transport=tcp"
            ],
            SharedSecret: "platform-secret",
            UserLifetime: "1h",
            AllowGuests: true,
            RelayPortsPublished: true,
            ExpectedBaseDomain: "example.test");

    private static string TemporaryFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mem-turn-editor-{Guid.NewGuid():N}.yaml");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static int Count(string value, string needle) =>
        value.Split(needle, StringSplitOptions.None).Length - 1;
}
