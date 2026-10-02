using System.Text.Json.Serialization;

namespace Modules.Operator.Migrations.Conversion;

public sealed record MigrationConversionWorkerRequest
{
    public string Schema { get; init; } = "mem-conversion-worker-request";
    public int SchemaVersion { get; init; } = 2;
    public string OperationId { get; init; } = string.Empty;
    public string ArchivePath { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string ConversionId { get; init; } = string.Empty;
    public Guid? StackId { get; init; }
    public string SynapseImage { get; init; } = string.Empty;
    public string PostgresImage { get; init; } = string.Empty;
    public bool Resume { get; init; }
    public bool KeepResources { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationConversionWorkerEventType
{
    OperationStarted,
    StepStarted,
    StepCompleted,
    OperationCompleted,
    OperationFailed,
}

public sealed record MigrationConversionWorkerEvent(
    string Schema,
    int SchemaVersion,
    long Sequence,
    MigrationConversionWorkerEventType EventType,
    string OperationId,
    DateTimeOffset OccurredAtUtc,
    string Phase,
    string Status,
    string Message,
    IReadOnlyDictionary<string, string?> Data);

public sealed record MigrationConversionReport(
    string Schema,
    int SchemaVersion,
    string ConversionId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string MigrationId,
    Guid SourceStackId,
    string MatrixServerName,
    string ArchivePath,
    string ArchiveSha256,
    string SynapseImage,
    string PostgresImage,
    string PostgreSqlDumpPath,
    string PostgreSqlDumpSha256,
    long PostgreSqlDumpBytes,
    string EvidencePath,
    bool ResourcesRetained,
    object[] TableCounts,
    string[] Warnings,
    string[] NextSteps);

public sealed record MigrationConversionWorkerRunResult(
    int ExitCode,
    string ReportPath,
    string EventsPath,
    string StandardErrorPath,
    IReadOnlyList<MigrationConversionWorkerEvent> Events,
    MigrationConversionReport? Report);
