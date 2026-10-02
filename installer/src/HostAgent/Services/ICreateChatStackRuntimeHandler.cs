using HostAgent.Commands;

namespace HostAgent.Services;

public interface ICreateChatStackRuntimeHandler
{
    Task<CreateChatStackRuntimeResult> HandleAcceptedAsync(
        CreateChatStackRuntimeCommand command,
        Guid operationId);
}
