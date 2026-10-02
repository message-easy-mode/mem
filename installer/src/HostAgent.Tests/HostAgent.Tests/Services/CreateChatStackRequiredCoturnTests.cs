using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Stacks.Turn;
using HostAgent.Services;

namespace HostAgent.Tests.Services;

public sealed class CreateChatStackRequiredCoturnTests
{
    [Fact]
    public void Missing_platform_turn_is_a_bounded_retryable_dependency_failure()
    {
        var error = Assert.Throws<CreateChatStackPlatformTurnUnavailableException>(
            () => CreateChatStackTurnPolicy.RequirePlatformConfiguration(null));

        Assert.Equal(
            CreateChatStackPlatformTurnUnavailableException.ErrorCodeValue,
            error.ErrorCode);
        Assert.Contains("platform TURN service is not ready", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "platform-turn-not-ready",
            CreateChatStackRuntimeFailureFinalizer.ClassifyFailure(
                error,
                requestCancellationRequested: false));
    }

    [Fact]
    public void Generated_MEM_managed_turn_configuration_must_match_the_reviewed_platform()
    {
        var platform = Platform();
        var live = Live(platform);

        CreateChatStackTurnPolicy.EnsureGeneratedConfigurationMatches(
            live,
            platform);
    }

    [Fact]
    public void Generated_turn_configuration_with_a_different_secret_is_rejected()
    {
        var platform = Platform();
        var live = Live(platform) with
        {
            SharedSecretValue = "different-protected-value"
        };

        var error = Assert.Throws<CreateChatStackTurnVerificationException>(
            () => CreateChatStackTurnPolicy.EnsureGeneratedConfigurationMatches(
                live,
                platform));

        Assert.Equal(
            CreateChatStackTurnVerificationException.ErrorCodeValue,
            error.ErrorCode);
        Assert.Equal(
            "generated-turn-configuration-mismatch",
            CreateChatStackRuntimeFailureFinalizer.ClassifyFailure(
                error,
                requestCancellationRequested: false));
        Assert.DoesNotContain(platform.SharedSecret, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_turn_configuration_requires_the_MEM_managed_marker()
    {
        var platform = Platform();
        var live = Live(platform) with
        {
            MemManagedMarkerPresent = false
        };

        Assert.Throws<CreateChatStackTurnVerificationException>(
            () => CreateChatStackTurnPolicy.EnsureGeneratedConfigurationMatches(
                live,
                platform));
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
            SharedSecret: "protected-platform-secret",
            UserLifetime: "1h",
            AllowGuests: true,
            RelayPortsPublished: true,
            ExpectedBaseDomain: "example.test");

    private static SynapseTurnConfigReadResult Live(
        CoturnSynapseConfig platform) =>
        new(
            Supported: true,
            AnyTurnSettings: true,
            MemManagedMarkerPresent: true,
            TurnUris: platform.TurnUris,
            CredentialMechanism: "inline-shared-secret",
            SharedSecretPresent: true,
            SharedSecretValue: platform.SharedSecret,
            SharedSecretFingerprint: "sha256:redacted-test-fingerprint",
            SharedSecretPath: null,
            UserLifetime: platform.UserLifetime,
            AllowGuests: platform.AllowGuests,
            CommentPublicHost: platform.PublicHost,
            CommentRealm: platform.Realm,
            FileSha256: "sha256:generated-config",
            ProblemCode: null,
            Detail: null);
}
