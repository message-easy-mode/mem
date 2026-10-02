namespace HostAgent.Services;

public sealed partial class CreateChatStackRuntimeHandler
{
    private static string Short(
        Guid value)
    {
        return value.ToString("N")[..8];
    }
}
