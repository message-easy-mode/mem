namespace HostAgent.Runtime.Coturn;

public sealed record CoturnPlatformInstallRequest(
    string? ExternalIp = null);

public sealed record CoturnPlatformInstallAcceptedResponse(
    Guid OperationId,
    string Status,
    string PollUrl,
    bool ReusedExistingOperation);

public sealed class CoturnPlatformInstallException(
    string code,
    string message,
    int statusCode) : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
