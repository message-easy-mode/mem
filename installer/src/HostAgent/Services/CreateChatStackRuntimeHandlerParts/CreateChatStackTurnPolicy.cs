using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Stacks.Turn;

namespace HostAgent.Services;

internal sealed class CreateChatStackPlatformTurnUnavailableException : InvalidOperationException
{
    public const string ErrorCodeValue = "create_stack_platform_turn_not_ready";

    public CreateChatStackPlatformTurnUnavailableException()
        : base("The shared platform TURN service is not ready. Install or repair platform TURN before creating a chat server.")
    {
    }

    public string ErrorCode => ErrorCodeValue;
}

internal sealed class CreateChatStackTurnVerificationException : InvalidOperationException
{
    public const string ErrorCodeValue = "create_stack_turn_effective_state_mismatch";

    public CreateChatStackTurnVerificationException()
        : base("The generated Synapse TURN configuration did not match the required MEM-managed platform TURN configuration.")
    {
    }

    public string ErrorCode => ErrorCodeValue;
}

internal static class CreateChatStackTurnPolicy
{
    public static CoturnSynapseConfig RequirePlatformConfiguration(
        CoturnSynapseConfig? platform) =>
        platform ?? throw new CreateChatStackPlatformTurnUnavailableException();

    public static void EnsureGeneratedConfigurationMatches(
        SynapseTurnConfigReadResult live,
        CoturnSynapseConfig platform)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(platform);

        if (!live.Supported ||
            !live.AnyTurnSettings ||
            !live.MemManagedMarkerPresent ||
            !SetsEqual(live.TurnUris, platform.TurnUris) ||
            !string.Equals(
                live.CredentialMechanism,
                "inline-shared-secret",
                StringComparison.Ordinal) ||
            !RuntimeStackTurnConfigTransaction.FixedEquals(
                live.SharedSecretValue,
                platform.SharedSecret) ||
            !string.Equals(
                live.UserLifetime,
                platform.UserLifetime,
                StringComparison.Ordinal) ||
            live.AllowGuests != platform.AllowGuests ||
            !string.Equals(
                live.CommentPublicHost,
                platform.PublicHost,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                live.CommentRealm,
                platform.Realm,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new CreateChatStackTurnVerificationException();
        }
    }

    private static bool SetsEqual(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        new HashSet<string>(left, StringComparer.OrdinalIgnoreCase)
            .SetEquals(right);
}
