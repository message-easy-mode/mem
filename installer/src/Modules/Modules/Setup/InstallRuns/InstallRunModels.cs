namespace Modules.Setup.InstallRuns;

public sealed record InstallStepResult(
    bool Succeeded,
    string Message,
    string? ErrorMessage = null,
    string? StepStatus = null);

public sealed record InstallStepContext(
    Guid InstallationId,
    Guid StepId,
    string StepName,
    int Sequence,
    string? ConfigJson,
    int AttemptNumber = 0);

public sealed record SetupHandoffResponse(
    Guid InstallationId,
    string Status,
    string Message,
    string OperatorDashboardPath,
    string? BaseDomain,
    string? CertificateCommonName,
    string? CertificateId,
    bool IsStagingCertificate,
    DateTimeOffset? CertificateExpiresAtUtc,
    int? NpmCertificateId,
    string PostgresContainerName,
    string NpmContainerName,
    string CoturnContainerName,
    bool HandoffRequired,
    bool HandoffCompleted,
    IReadOnlyList<string> Warnings);

public sealed record SetupHandoffCompletionResponse(
    Guid InstallationId,
    bool Completed,
    string Status,
    string Message);
