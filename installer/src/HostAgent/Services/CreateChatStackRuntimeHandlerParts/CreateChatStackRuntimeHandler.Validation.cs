using HostAgent.Commands;
using HostAgent.Runtime.Stacks.Identity;

namespace HostAgent.Services;

public sealed partial class CreateChatStackRuntimeHandler
{
    internal static void Validate(
        CreateChatStackRuntimeCommand command)
    {
        if (command.StackId == Guid.Empty)
        {
            throw new InvalidOperationException("StackId is required.");
        }

        if (command.MatrixInstanceId == Guid.Empty)
        {
            throw new InvalidOperationException("MatrixInstanceId is required.");
        }

        if (command.ElementInstanceId == Guid.Empty)
        {
            throw new InvalidOperationException("ElementInstanceId cannot be an empty GUID.");
        }

        if (string.IsNullOrWhiteSpace(command.StackSlug))
        {
            throw new InvalidOperationException("StackSlug is required.");
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new InvalidOperationException("IdempotencyKey is required.");
        }

        EnsureNoRuntimeImageOverrides(command);

        _ = RuntimeStackIdentity.NormalizeDisplayName(command.DisplayName, command.StackSlug);
        _ = RuntimeStackIdentity.NormalizeCategory(command.Category);
    }
}
