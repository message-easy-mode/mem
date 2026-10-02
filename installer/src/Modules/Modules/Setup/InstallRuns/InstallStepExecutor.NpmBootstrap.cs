namespace Modules.Setup.InstallRuns;

public sealed partial class InstallStepExecutor
{
    private async Task<InstallStepResult> EnsureNpmAdministratorReadyAsync(
        InstallStepContext context,
        IngressSetupConfig ingress,
        CancellationToken cancellationToken)
    {
        await BeginProgressPhaseAsync(
            context,
            "npm.bootstrap.inspect",
            "Inspecting Nginx Proxy Manager first-administrator state.",
            cancellationToken);

        var result = await RunWithCurrentProgressHeartbeatAsync(
            context,
            "Nginx Proxy Manager administrator initialization is still in progress.",
            () => _npmInitialAdminBootstrap.BootstrapAsync(
                context.InstallationId,
                new NpmInitialAdminBootstrapTarget(
                    ContainerName: ingress.ContainerName.Trim(),
                    Image: NpmImage,
                    HttpPort: ingress.HttpPort,
                    HttpsPort: ingress.HttpsPort,
                    AdminPort: ingress.AdminPort,
                    DataVolumeName: NpmDataVolumeName,
                    LetsEncryptVolumeName: NpmLetsEncryptVolumeName,
                    NetworkName: NetworkName,
                    NetworkAlias: "npm"),
                cancellationToken),
            cancellationToken);

        if (result.Succeeded)
        {
            return Succeeded(result.Message);
        }

        var errorCode = string.IsNullOrWhiteSpace(result.ErrorCode)
            ? "NpmInitialAdminBootstrapFailed"
            : result.ErrorCode.Trim();
        var detail = $"{errorCode}: {result.Message}";

        if (string.Equals(
                result.ErrorCode,
                "NpmAdminCredentialRequired",
                StringComparison.Ordinal) ||
            string.Equals(
                result.ErrorCode,
                "NpmCredentialsRejected",
                StringComparison.Ordinal))
        {
            var message = string.Equals(
                result.ErrorCode,
                "NpmCredentialsRejected",
                StringComparison.Ordinal)
                ? "Nginx Proxy Manager rejected the stored administrator credential. Update the credential to continue this same installation."
                : "Nginx Proxy Manager needs an administrator credential before this installation can continue.";

            return ActionRequired(message, detail);
        }

        return Failed(
            "Nginx Proxy Manager administrator initialization failed.",
            detail);
    }
}
