using System.Text.Json.Serialization;
using Mem.Migrate.Core.Conversion;

namespace Mem.Migrate.Core.Worker;

public sealed record ConversionWorkerRequest
{
    public const string CurrentSchema = "mem-conversion-worker-request";
    public const int CurrentSchemaVersion = 2;

    public string Schema { get; init; } = CurrentSchema;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string OperationId { get; init; } = string.Empty;
    public string ArchivePath { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string ConversionId { get; init; } = string.Empty;
    public Guid? StackId { get; init; }
    public string SynapseImage { get; init; } = string.Empty;
    public string PostgresImage { get; init; } = string.Empty;
    public int CommandTimeoutSeconds { get; init; } = 900;
    public int ReadinessTimeoutSeconds { get; init; } = 120;
    public int BatchSize { get; init; } = 1000;
    public double RequiredFreeSpaceMultiplier { get; init; } = 3.0;
    public bool Resume { get; init; }
    public bool KeepResources { get; init; }

    public ConversionOptions ToConversionOptions()
    {
        if (!string.Equals(Schema, CurrentSchema, StringComparison.Ordinal) ||
            SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException("The conversion worker request schema is unsupported.");
        }

        var operationId = NormalizeIdentifier(OperationId, nameof(OperationId));
        var conversionId = NormalizeIdentifier(ConversionId, nameof(ConversionId));
        var archive = RequireAbsolutePath(ArchivePath, nameof(ArchivePath));
        var workspace = RequireAbsolutePath(WorkspacePath, nameof(WorkspacePath));
        var output = RequireAbsolutePath(OutputPath, nameof(OutputPath));
        var synapseImage = RequireImmutableImageIdentity(SynapseImage, nameof(SynapseImage));
        var postgresImage = RequireImmutableImageIdentity(PostgresImage, nameof(PostgresImage));

        _ = operationId;
        return new ConversionOptions
        {
            ArchivePath = archive,
            WorkspacePath = workspace,
            OutputPath = output,
            ConversionId = conversionId,
            StackId = StackId,
            SynapseImage = synapseImage,
            PostgresImage = postgresImage,
            CommandTimeoutSeconds = CommandTimeoutSeconds,
            ReadinessTimeoutSeconds = ReadinessTimeoutSeconds,
            BatchSize = BatchSize,
            RequiredFreeSpaceMultiplier = RequiredFreeSpaceMultiplier,
            Resume = Resume,
            KeepResources = KeepResources,
            JsonConsoleOutput = true
        }.Normalize();
    }

    private static string RequireImmutableImageIdentity(string value, string name)
    {
        var trimmed = value.Trim();
        var isImageId = trimmed.StartsWith("sha256:", StringComparison.Ordinal) &&
            trimmed.Length == "sha256:".Length + 64;
        var digestSeparator = trimmed.LastIndexOf("@sha256:", StringComparison.Ordinal);
        var isRepositoryDigest = digestSeparator > 0 &&
            trimmed.Length == digestSeparator + "@sha256:".Length + 64;

        if ((!isImageId && !isRepositoryDigest) ||
            !trimmed[(trimmed.LastIndexOf("sha256:", StringComparison.Ordinal) + "sha256:".Length)..]
                .All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException(
                $"{name} must be an immutable local image ID (sha256:...) or repository digest (...@sha256:...).");
        }

        return trimmed.ToLowerInvariant();
    }

    private static string RequireAbsolutePath(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException($"{name} must be an absolute server-owned path.");
        }

        return Path.GetFullPath(value);
    }

    private static string NormalizeIdentifier(string value, string name)
    {
        var trimmed = value.Trim();
        if (trimmed.Length is < 3 or > 80 ||
            trimmed.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException($"{name} must contain 3-80 ASCII letters, digits, '-' or '_'.");
        }

        return trimmed;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConversionWorkerEventType
{
    OperationStarted,
    StepStarted,
    StepCompleted,
    OperationCompleted,
    OperationFailed
}

public sealed record ConversionWorkerEvent(
    string Schema,
    int SchemaVersion,
    long Sequence,
    ConversionWorkerEventType EventType,
    string OperationId,
    DateTimeOffset OccurredAtUtc,
    string Phase,
    string Status,
    string Message,
    IReadOnlyDictionary<string, string?> Data)
{
    public const string CurrentSchema = "mem-conversion-worker-event";
    public const int CurrentSchemaVersion = 1;
}
