namespace HostAgent.Runtime.Stacks.Destroy;

public sealed class RuntimeStackDestroyOwnershipRefusedException : InvalidOperationException
{
    public RuntimeStackDestroyOwnershipRefusedException(string message)
        : base(message)
    {
    }
}
