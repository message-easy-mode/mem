namespace HostAgent.Options;

public sealed class InstanceStorageOptions
{
    public string InstanceDataRoot { get; set; } = string.Empty;

    public string ResolveRequiredRoot()
    {
        if (string.IsNullOrWhiteSpace(InstanceDataRoot))
        {
            throw new InvalidOperationException(
                "Provisioning:InstanceDataRoot must be explicitly configured.");
        }

        return Path.GetFullPath(InstanceDataRoot.Trim());
    }
}
