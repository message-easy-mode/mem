using HostAgent.Commands;

namespace HostAgent.Services;

public sealed partial class CreateChatStackRuntimeHandler
{
    private static void EnsureNoRuntimeImageOverrides(CreateChatStackRuntimeCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.MatrixImage) ||
            !string.IsNullOrWhiteSpace(command.MatrixVersion) ||
            !string.IsNullOrWhiteSpace(command.ElementImage) ||
            !string.IsNullOrWhiteSpace(command.ElementVersion))
        {
            throw new InvalidOperationException(
                "Caller-supplied Matrix/Element image or version overrides are no longer supported. " +
                "MEM uses release-approved immutable runtime image authorities.");
        }
    }
}
