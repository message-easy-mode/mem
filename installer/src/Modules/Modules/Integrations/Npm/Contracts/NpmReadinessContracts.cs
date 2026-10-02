namespace Modules.Integrations.Npm.Contracts;

public sealed record NpmReadinessResponse(
    bool ContainerExists,
    bool ContainerRunning,
    bool AdminUiReachable,
    bool Initialized,
    bool ApiAuthenticated,
    bool CertificateApiReachable,
    string? RuntimeState,
    string? BaseUrl,
    string? BaseUrlSource,
    int CertificateCount,
    string RecommendedAction,
    IReadOnlyList<string> Warnings
);