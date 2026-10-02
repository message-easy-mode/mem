using Mem.Migrate.Core.Cutover;

namespace Mem.Migrate.Core.Qualification;

public sealed record SourceQualificationOptions
{
    public string CutoverPlanReportPath { get; init; } = string.Empty;
    public string FreezeReportPath { get; init; } = string.Empty;
    public string CaptureReportPath { get; init; } = string.Empty;
    public string PackageReportPath { get; init; } = string.Empty;
    public string ExpectedEncryptedPackageSha256 { get; init; } = string.Empty;
    public string OutputPath { get; init; } = ".";
    public string? QualificationAttemptId { get; init; }
    public string DockerCommand { get; init; } = "docker";
    public int CommandTimeoutSeconds { get; init; } = 30;
    public bool JsonConsoleOutput { get; init; }

    public SourceQualificationOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(CutoverPlanReportPath))
        {
            throw new ArgumentException("Cutover plan report path is required.");
        }

        if (string.IsNullOrWhiteSpace(FreezeReportPath))
        {
            throw new ArgumentException("Source-freeze report path is required.");
        }

        if (string.IsNullOrWhiteSpace(CaptureReportPath))
        {
            throw new ArgumentException("Final capture report path is required.");
        }

        if (string.IsNullOrWhiteSpace(PackageReportPath))
        {
            throw new ArgumentException("Final package report path is required.");
        }

        if (!IsSha256(ExpectedEncryptedPackageSha256))
        {
            throw new ArgumentException(
                "Expected encrypted package hash must be a 64-character SHA-256 value.");
        }

        if (string.IsNullOrWhiteSpace(DockerCommand))
        {
            throw new ArgumentException("Docker command is required.");
        }

        if (CommandTimeoutSeconds is < 5 or > 300)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CommandTimeoutSeconds),
                "Qualification command timeout must be between 5 and 300 seconds.");
        }

        var attemptId = string.IsNullOrWhiteSpace(QualificationAttemptId)
            ? $"mm01e-source-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..48]
            : QualificationAttemptId.Trim();
        if (!IsIdentifier(attemptId, 8, 96))
        {
            throw new ArgumentException(
                "Qualification attempt ID must contain 8-96 ASCII letters, digits, '-' or '_'.");
        }

        return this with
        {
            CutoverPlanReportPath = Path.GetFullPath(CutoverPlanReportPath),
            FreezeReportPath = Path.GetFullPath(FreezeReportPath),
            CaptureReportPath = Path.GetFullPath(CaptureReportPath),
            PackageReportPath = Path.GetFullPath(PackageReportPath),
            ExpectedEncryptedPackageSha256 = ExpectedEncryptedPackageSha256.ToLowerInvariant(),
            OutputPath = Path.GetFullPath(OutputPath),
            QualificationAttemptId = attemptId,
            DockerCommand = DockerCommand.Trim()
        };
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsIdentifier(string value, int minimum, int maximum) =>
        value.Length >= minimum &&
        value.Length <= maximum &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public sealed record QualificationHostIdentity(
    string MachineIdSha256,
    string MachineName,
    string OperatingSystem,
    string Architecture,
    string DockerEngineIdSha256,
    string DockerName,
    string DockerServerVersion);

public sealed record SourceQualificationObservedContainer(
    string Role,
    string ContainerId,
    string ContainerName,
    string ImageId,
    string State,
    bool Running,
    string RestartPolicy,
    bool WriterContainer);

public sealed record SourceQualificationEnvironmentObservation(
    QualificationHostIdentity Host,
    IReadOnlyList<SourceQualificationObservedContainer> Containers);

public interface ISourceQualificationEnvironmentProbe
{
    Task<SourceQualificationEnvironmentObservation> ObserveAsync(
        CutoverPlanDocument plan,
        string dockerCommand,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken);
}

public sealed record SourceQualificationContainerEvidence(
    string Role,
    string ContainerId,
    string ContainerName,
    string ImageId,
    bool WriterContainer,
    bool WasRunningBeforeFreeze,
    string CurrentState,
    bool CurrentlyRunning,
    string CurrentRestartPolicy,
    bool IdentityMatched,
    bool FrozenStatePreserved);

public sealed record SourceQualificationPayload(
    string QualificationAttemptId,
    DateTime GeneratedAtUtc,
    string MigrationId,
    string IntakeId,
    string PackageRevisionId,
    string EncryptedPackageSha256,
    long EncryptedPackageBytes,
    string SourceArchiveSha256,
    string FreezeAttemptId,
    string FreezePlanId,
    string FreezePlanSha256,
    string SourceFingerprint,
    string SourceStackSlug,
    string MatrixServerName,
    bool SourceFrozen,
    bool PublicRoutingMutationOccurred,
    bool DevelopmentExternalControlPlane,
    QualificationHostIdentity SourceHost,
    IReadOnlyList<SourceQualificationContainerEvidence> Containers);

public sealed record SourceQualificationEnvelope(
    string SchemaVersion,
    string PayloadSha256,
    SourceQualificationPayload Payload);

public sealed record SourceQualificationReport(
    string Schema,
    int SchemaVersion,
    string Status,
    string QualificationAttemptId,
    DateTimeOffset GeneratedAtUtc,
    string MigrationId,
    string PackageRevisionId,
    string EncryptedPackageSha256,
    string FreezeAttemptId,
    string FreezePlanId,
    string SourceFingerprint,
    string SourceStackSlug,
    string MatrixServerName,
    bool SourceFrozen,
    bool DistinctHostEvidenceReady,
    bool DevelopmentExternalControlPlane,
    QualificationHostIdentity SourceHost,
    SourceQualificationContainerEvidence[] Containers,
    string EvidencePath,
    string EvidenceSha256,
    string JsonPath,
    string MarkdownPath,
    string[] Warnings,
    string[] NextSteps);
