using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mem.Migrate.Core.Cutover;

public static class CutoverPlanHash
{
    private static readonly JsonSerializerOptions CanonicalOptions = CreateOptions();

    public static string Compute(CutoverPlanDocument plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var json = JsonSerializer.Serialize(plan, CanonicalOptions);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    public static string ComputeInputBinding(CutoverPrepareOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var input = new
        {
            options.StageAttemptId,
            options.ExpectedSourceFingerprint,
            options.ValidForMinutes,
            options.DevelopmentExternalControlPlane,
            options.ProducerVersion,
            options.TargetWorkspacePath,
            options.Assessment.DockerCommand,
            options.Assessment.ApiUrl,
            options.Assessment.PostgresContainer,
            options.Assessment.CommandTimeoutSeconds,
            options.Assessment.HttpTimeoutSeconds,
            options.Assessment.MaximumFileScanEntries
        };
        var json = JsonSerializer.Serialize(input, CanonicalOptions);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
