namespace Modules.Setup.InstallRuns;

public static class InstallationStatuses
{
    public const string Draft = "Draft";
    public const string Ready = "Ready";
    public const string Running = "Running";
    public const string WaitingForUser = "WaitingForUser";
    public const string Failed = "Failed";
    public const string Succeeded = "Succeeded";
    public const string Cancelled = "Cancelled";

    public static readonly string[] ActiveFirstTimeSetup =
    [
        Draft,
        Ready,
        Running,
        WaitingForUser,
        Failed
    ];

    public static bool IsPlanning(string status) =>
        string.Equals(status, Draft, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Ready, StringComparison.OrdinalIgnoreCase);

    public static bool IsResumableRun(string status) =>
        string.Equals(status, Running, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, WaitingForUser, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase);
}

public static class InstallationStepStatuses
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string WaitingForUser = "WaitingForUser";
    public const string Failed = "Failed";
    public const string Succeeded = "Succeeded";
    public const string Skipped = "Skipped";
}
