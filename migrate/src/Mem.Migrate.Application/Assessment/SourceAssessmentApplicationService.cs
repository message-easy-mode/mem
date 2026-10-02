using System.Text.Json;
using System.Text.Json.Nodes;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Legacy.V010;

namespace Mem.Migrate.Application.Assessment;

public interface ISourceAssessmentApplicationService
{
    Task<SourceAssessmentView> RunAsync(CancellationToken cancellationToken);

    Task<SourceAssessmentView?> GetLatestAsync(CancellationToken cancellationToken);

    Task<SourceAssessmentReport?> GetLatestReportAsync(CancellationToken cancellationToken);
}

public sealed class SourceAssessmentApplicationService(
    AssessmentOptions options,
    V010AssessmentService assessmentService,
    IAssessmentJournal journal) : ISourceAssessmentApplicationService
{
    private static readonly JsonSerializerOptions StoredJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AssessmentOptions _options = options.Normalize();

    public async Task<SourceAssessmentView> RunAsync(CancellationToken cancellationToken)
    {
        EnsurePrivateDirectory(_options.WorkspacePath);
        EnsurePrivateDirectory(_options.OutputPath);

        var execution = await assessmentService.RunAsync(_options, cancellationToken);
        return SourceAssessmentProjector.Project(execution.Result);
    }

    public async Task<SourceAssessmentView?> GetLatestAsync(CancellationToken cancellationToken)
    {
        await journal.InitializeAsync(cancellationToken);
        var stored = await journal.GetLatestAsync(cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var result = DeserializeStoredResult(stored.PublicJson);
        return SourceAssessmentProjector.Project(result);
    }

    public async Task<SourceAssessmentReport?> GetLatestReportAsync(CancellationToken cancellationToken)
    {
        await journal.InitializeAsync(cancellationToken);
        var stored = await journal.GetLatestAsync(cancellationToken);
        if (stored is null)
        {
            return null;
        }

        return new SourceAssessmentReport(
            stored.AssessmentId,
            stored.CompletedAtUtc,
            $"mem-migrate-assessment-{SafeFilePart(stored.AssessmentId)}.md",
            stored.PublicMarkdown);
    }

    private static AssessmentResult DeserializeStoredResult(string publicJson)
    {
        var root = JsonNode.Parse(publicJson)
            ?? throw new InvalidDataException(
                "The latest assessment journal entry did not contain valid JSON.");

        NormalizeLegacyRedactions(root);

        return root.Deserialize<AssessmentResult>(StoredJsonOptions)
            ?? throw new InvalidDataException(
                "The latest assessment journal entry did not contain a valid assessment result.");
    }

    private static void NormalizeLegacyRedactions(
        JsonNode node,
        string? propertyName = null)
    {
        switch (node)
        {
            case JsonObject obj when string.Equals(
                propertyName,
                "rowCounts",
                StringComparison.OrdinalIgnoreCase):
                foreach (var pair in obj.ToList())
                {
                    if (pair.Value is not null &&
                        pair.Value.GetValueKind() is JsonValueKind.Number)
                    {
                        continue;
                    }

                    obj.Remove(pair.Key);
                }

                break;

            case JsonObject obj:
                foreach (var pair in obj.ToList())
                {
                    if (pair.Value is null)
                    {
                        continue;
                    }

                    if (string.Equals(
                            pair.Key,
                            "activePasswordResetRequests",
                            StringComparison.OrdinalIgnoreCase) &&
                        IsLegacyRedactedValue(pair.Value))
                    {
                        obj[pair.Key] = 0;
                        continue;
                    }

                    NormalizeLegacyRedactions(pair.Value, pair.Key);
                }

                break;

            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    if (array[index] is { } child)
                    {
                        NormalizeLegacyRedactions(child, propertyName);
                    }
                }

                break;
        }
    }

    private static bool IsLegacyRedactedValue(JsonNode node) =>
        node is JsonValue value &&
        value.TryGetValue<string>(out var text) &&
        string.Equals(text, "<redacted>", StringComparison.Ordinal);

    private static void EnsurePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
    }

    private static string SafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value
            .Where(character => !invalid.Contains(character) && character is not '/' and not '\\')
            .ToArray());
    }
}
