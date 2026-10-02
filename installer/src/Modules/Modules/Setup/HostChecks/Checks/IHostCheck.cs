namespace Modules.Setup.HostChecks.Checks;

public interface IHostCheck
{
    string Key { get; }
    string GroupKey { get; }
    string GroupTitle { get; }
    string GroupDescription { get; }

    Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken);
}