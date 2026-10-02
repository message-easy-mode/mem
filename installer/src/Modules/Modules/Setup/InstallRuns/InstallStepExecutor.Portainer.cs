using Modules.Integrations.Portainer.Contracts;
using Modules.Integrations.Portainer.Services;

namespace Modules.Setup.InstallRuns;

public sealed partial class InstallStepExecutor
{
    private async Task<InstallStepResult> StartSelectedSupportToolsAsync(
        InstallStepContext context,
        CancellationToken cancellationToken)
    {
        var config = ReadConfig(context.ConfigJson);
        if (config is null)
        {
            return Failed(
                "Selected support tools could not be evaluated.",
                "The frozen installation configuration is unavailable or invalid.");
        }

        var results = new List<string>();

        await BeginProgressPhaseAsync(
            context,
            "support.evaluate",
            "Evaluating the selected optional support tools.",
            cancellationToken);

        if (config.SupportTools.Portainer.Enabled)
        {
            try
            {
                var portainer = await RunWithProgressHeartbeatAsync(
                    context,
                    "support.portainer.install",
                    "Starting and verifying the selected Portainer support tool.",
                    () => _portainerRuntimeService
                        .EnsureInstalledForSetupAsync(
                            new PortainerInstallRequest(
                                PreferredUiHostPort:
                                    config.SupportTools.Portainer.HostPort,
                                ForcePreferredPort: false,
                                UseExistingIfDetected:
                                    config.SupportTools.Portainer.UseExistingIfDetected),
                            cancellationToken),
                    cancellationToken);
                results.Add(portainer.Message);
            }
            catch (PortainerOperationException exception)
            {
                return Failed(
                    "Portainer setup could not be completed safely.",
                    $"{exception.Code}: {exception.Message}");
            }
        }

        if (config.SupportTools.Seq.Enabled)
        {
            await BeginProgressPhaseAsync(
                context,
                "support.seq.review",
                "Confirming that Seq remains controlled through Diagnostics.",
                cancellationToken);

            results.Add(
                "Seq remains controlled through its dedicated Diagnostics workspace.");
        }

        if (config.SupportTools.PgAdmin.Enabled)
        {
            await BeginProgressPhaseAsync(
                context,
                "support.pgadmin.review",
                "Confirming the deferred PgAdmin support-tool state.",
                cancellationToken);

            results.Add(
                "PgAdmin remains deferred and was not changed by this installation step.");
        }

        return Succeeded(results.Count == 0
            ? "No optional support tools were selected."
            : string.Join(" ", results));
    }
}
