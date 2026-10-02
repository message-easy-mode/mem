using HostAgent.Runtime.Stacks.Turn;

namespace HostAgent.Tests.Runtime.Stacks.Turn;

public sealed class SynapseTurnConfigReaderTests
{
    [Fact]
    public async Task No_turn_keys_are_classified_as_supported_and_absent()
    {
        var path = Write("server_name: example.test\nlisteners: []\n");
        try
        {
            var result = await new SynapseTurnConfigReader().ReadAsync(path, CancellationToken.None);

            Assert.True(result.Supported);
            Assert.False(result.AnyTurnSettings);
            Assert.Empty(result.TurnUris);
            Assert.False(result.SharedSecretPresent);
            Assert.Equal("none", result.CredentialMechanism);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Mem_managed_block_is_read_without_losing_safe_configuration_evidence()
    {
        var path = Write("""
            server_name: example.test

            # MEM-managed TURN configuration for Matrix voice/video calls.
            # TURN public host: turn.example.test
            # TURN realm: example.test
            turn_uris:
              - "turn:turn.example.test:3478?transport=udp"
              - "turn:turn.example.test:3478?transport=tcp"
            turn_shared_secret: "super-secret"
            turn_user_lifetime: "1h"
            turn_allow_guests: false
            """);
        try
        {
            var result = await new SynapseTurnConfigReader().ReadAsync(path, CancellationToken.None);

            Assert.True(result.Supported);
            Assert.True(result.AnyTurnSettings);
            Assert.True(result.MemManagedMarkerPresent);
            Assert.Equal(2, result.TurnUris.Count);
            Assert.Equal("inline-shared-secret", result.CredentialMechanism);
            Assert.True(result.SharedSecretPresent);
            Assert.Equal("super-secret", result.SharedSecretValue);
            Assert.StartsWith("sha256:", result.SharedSecretFingerprint);
            Assert.Equal("turn.example.test", result.CommentPublicHost);
            Assert.Equal("example.test", result.CommentRealm);
            Assert.Equal("1h", result.UserLifetime);
            Assert.False(result.AllowGuests);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task External_shared_secret_path_is_observed_without_reading_its_value()
    {
        var path = Write("""
            turn_uris: ["turn:external.example:3478?transport=udp"]
            turn_shared_secret_path: /run/secrets/external-turn
            """);
        try
        {
            var result = await new SynapseTurnConfigReader().ReadAsync(path, CancellationToken.None);

            Assert.True(result.Supported);
            Assert.Equal("shared-secret-path", result.CredentialMechanism);
            Assert.True(result.SharedSecretPresent);
            Assert.Null(result.SharedSecretValue);
            Assert.Null(result.SharedSecretFingerprint);
            Assert.Equal("/run/secrets/external-turn", result.SharedSecretPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Unsupported_inline_uri_scalar_fails_closed_without_throwing()
    {
        var path = Write("""
            turn_uris: ["turn:turn.example.test:3478?transport=udp]
            turn_shared_secret: "secret"
            """);
        try
        {
            var result = await new SynapseTurnConfigReader().ReadAsync(path, CancellationToken.None);

            Assert.False(result.Supported);
            Assert.Equal("turn_config_uris_custom_unsupported", result.ProblemCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Duplicate_turn_keys_fail_closed()
    {
        var path = Write("""
            turn_uris: []
            turn_uris:
              - turn:duplicate.example:3478
            """);
        try
        {
            var result = await new SynapseTurnConfigReader().ReadAsync(path, CancellationToken.None);

            Assert.False(result.Supported);
            Assert.Equal("turn_config_ambiguous", result.ProblemCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Write(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mem-turn-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, content);
        return path;
    }
}
