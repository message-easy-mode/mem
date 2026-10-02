using Microsoft.Extensions.Logging;
using Modules.Setup.InstallPlans;
using Modules.Setup.Start;

namespace Modules.Setup.Lifecycle;

public sealed record FirstTimeSetupBeginResult(
    bool Allowed,
    string ReasonCode,
    string Message,
    InstallPlanResponse? Installation)
{
    public static FirstTimeSetupBeginResult AllowedResult(InstallPlanResponse installation) =>
        new(
            Allowed: true,
            ReasonCode: "available",
            Message: "First-time platform setup is ready to continue.",
            Installation: installation);
}

public sealed class FirstTimeSetupAuthorityService
{
    private readonly SetupStartService _setupStart;
    private readonly InstallPlanService _installPlans;
    private readonly ILogger<FirstTimeSetupAuthorityService> _logger;

    public FirstTimeSetupAuthorityService(
        SetupStartService setupStart,
        InstallPlanService installPlans,
        ILogger<FirstTimeSetupAuthorityService> logger)
    {
        _setupStart = setupStart;
        _installPlans = installPlans;
        _logger = logger;
    }

    public async Task<FirstTimeSetupBeginResult> BeginOrResumeAsync(
        CancellationToken cancellationToken)
    {
        var start = await _setupStart.GetAsync(cancellationToken);

        var canBegin =
            string.Equals(start.SetupMode, SetupStartModes.FreshInstall, StringComparison.Ordinal) &&
            string.Equals(start.StartupTarget, SetupStartTargets.SetupStart, StringComparison.Ordinal) &&
            (string.Equals(
                 start.RecommendedAction,
                 SetupStartRecommendedActions.RunSetupCheck,
                 StringComparison.Ordinal) ||
             string.Equals(
                 start.RecommendedAction,
                 SetupStartRecommendedActions.ContinueSetup,
                 StringComparison.Ordinal));

        if (!canBegin)
        {
            _logger.LogWarning(
                "First-time setup authority was not created because startup classification is not fresh setup. SetupMode={SetupMode} InstallationState={InstallationState} RecommendedAction={RecommendedAction} StartupTarget={StartupTarget}",
                start.SetupMode,
                start.InstallationState,
                start.RecommendedAction,
                start.StartupTarget);

            return new FirstTimeSetupBeginResult(
                Allowed: false,
                ReasonCode: "setup-not-available",
                Message: "First-time platform setup cannot begin from the current server state. Refresh Setup and follow the server-owned recovery guidance.",
                Installation: null);
        }

        var installation = await _installPlans.BeginSetupAsync(cancellationToken);
        return FirstTimeSetupBeginResult.AllowedResult(installation);
    }
}
