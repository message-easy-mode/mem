namespace Mem.Migrate.Core;

public static class ExitCodes
{
    public const int Success = 0;
    public const int SupportedWithWarnings = 2;
    public const int Blocked = 10;
    public const int Unsupported = 11;
    public const int Ambiguous = 12;
    public const int CurrentTargetAlreadyPresent = 13;
    public const int InvalidArchive = 14;
    public const int InvalidArguments = 64;
    public const int ExecutionFailure = 70;
}
