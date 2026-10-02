using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Qualification;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Legacy.V010.Qualification;

public static class SourceQualificationRenderer
{
    public const string EvidenceSchemaVersion =
        "mem.migration.two-server-source-evidence.v1";

    private static readonly JsonSerializerOptions PayloadJsonOptions =
        new(JsonSerializerDefaults.Web);

    public static async Task<SourceQualificationReport> WriteAsync(
        SourceQualificationReport report,
        SourceQualificationPayload payload,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(outputRoot, report.QualificationAttemptId);
        PrivateFilePermissions.EnsureDirectory(directory);
        var evidencePath = Path.Combine(directory, "two-server-source-evidence.json");
        var jsonPath = Path.Combine(directory, "two-server-source-qualification-report.json");
        var markdownPath = Path.Combine(directory, "two-server-source-qualification-report.md");
        var payloadSha256 = ComputePayloadSha256(payload);
        var envelope = new SourceQualificationEnvelope(
            EvidenceSchemaVersion,
            payloadSha256,
            payload);

        await File.WriteAllTextAsync(
            evidencePath,
            JsonSerializer.Serialize(envelope, CaptureJson.Options),
            cancellationToken);
        PrivateFilePermissions.EnsureFile(evidencePath);

        var finalized = report with
        {
            EvidencePath = evidencePath,
            EvidenceSha256 = payloadSha256,
            JsonPath = jsonPath,
            MarkdownPath = markdownPath
        };
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(finalized, CaptureJson.Options),
            cancellationToken);
        PrivateFilePermissions.EnsureFile(jsonPath);
        await File.WriteAllTextAsync(
            markdownPath,
            RenderMarkdown(finalized),
            cancellationToken);
        PrivateFilePermissions.EnsureFile(markdownPath);
        return finalized;
    }

    public static string ComputePayloadSha256(SourceQualificationPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, PayloadJsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    private static string RenderMarkdown(SourceQualificationReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# MEM MIG-PRODUCTION-01E Two-Server Source Qualification");
        text.AppendLine();
        text.AppendLine($"- Status: `{report.Status}`");
        text.AppendLine($"- Qualification attempt: `{report.QualificationAttemptId}`");
        text.AppendLine($"- Migration: `{report.MigrationId}`");
        text.AppendLine($"- Package revision: `{report.PackageRevisionId}`");
        text.AppendLine($"- Encrypted package SHA-256: `{report.EncryptedPackageSha256}`");
        text.AppendLine($"- Freeze attempt: `{report.FreezeAttemptId}`");
        text.AppendLine($"- Freeze plan: `{report.FreezePlanId}`");
        text.AppendLine($"- Source fingerprint: `{report.SourceFingerprint}`");
        text.AppendLine($"- Source stack: `{report.SourceStackSlug}`");
        text.AppendLine($"- Matrix server name: `{report.MatrixServerName}`");
        text.AppendLine($"- Source remains frozen: `{report.SourceFrozen}`");
        text.AppendLine($"- Development external control plane: `{report.DevelopmentExternalControlPlane}`");
        text.AppendLine($"- Evidence payload SHA-256: `{report.EvidenceSha256}`");
        text.AppendLine();
        text.AppendLine("## Source host identity");
        text.AppendLine();
        text.AppendLine($"- Machine name: `{report.SourceHost.MachineName}`");
        text.AppendLine($"- Machine ID SHA-256: `{report.SourceHost.MachineIdSha256}`");
        text.AppendLine($"- Docker name: `{report.SourceHost.DockerName}`");
        text.AppendLine($"- Docker Engine ID SHA-256: `{report.SourceHost.DockerEngineIdSha256}`");
        text.AppendLine($"- Docker version: `{report.SourceHost.DockerServerVersion}`");
        text.AppendLine($"- Operating system: `{report.SourceHost.OperatingSystem}`");
        text.AppendLine($"- Architecture: `{report.SourceHost.Architecture}`");
        text.AppendLine();
        text.AppendLine("## Source container evidence");
        text.AppendLine();
        foreach (var container in report.Containers)
        {
            text.AppendLine(
                $"- `{container.Role}` / `{container.ContainerName}`: state `{container.CurrentState}`, " +
                $"restart `{container.CurrentRestartPolicy}`, writer `{container.WriterContainer}`, " +
                $"identity matched `{container.IdentityMatched}`, frozen preserved `{container.FrozenStatePreserved}`");
        }

        if (report.Warnings.Length > 0)
        {
            text.AppendLine();
            text.AppendLine("## Warnings");
            text.AppendLine();
            foreach (var warning in report.Warnings)
            {
                text.AppendLine($"- {warning}");
            }
        }

        text.AppendLine();
        text.AppendLine("## Next steps");
        text.AppendLine();
        foreach (var step in report.NextSteps)
        {
            text.AppendLine($"- {step}");
        }

        return text.ToString();
    }
}
