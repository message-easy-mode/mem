namespace Modules.Setup.InstallRuns;

public sealed class MemCliHostCommandOptions
{
    public const string SectionName = "MemCliHostCommand";

    /// <summary>
    /// Enables installer-run installation of the host-level <c>mem</c> command.
    /// This remains disabled by default until bootstrap/release packaging supplies
    /// a prebuilt CLI binary and the canonical CLI-owned install script.
    /// </summary>
    public bool Enabled { get; set; }

    public string InstallScriptPath { get; set; } = "/opt/mem/bootstrap/cli/install-host-command.sh";

    public string BinaryPath { get; set; } = "/opt/mem/bootstrap/cli/mem";

    public string Version { get; set; } = "dev";

    public string InstallRoot { get; set; } = "/opt/mem/cli";

    public string LinkPath { get; set; } = "/usr/local/bin/mem";
}
