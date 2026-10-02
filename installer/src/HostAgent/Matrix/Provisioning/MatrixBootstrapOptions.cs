namespace HostAgent.Matrix.Provisioning;

public sealed class MatrixBootstrapOptions
{
    // Must match homeserver.yaml: registration_shared_secret
    public string SharedSecret { get; set; } = "";

    // Optional: automatically create initial admin after provision
    public bool AutoCreateAdmin { get; set; } = true;

    public string AdminUsername { get; set; } = "admin";
    public string AdminPassword { get; set; } = "ChangeMe-Now-123!";
}
