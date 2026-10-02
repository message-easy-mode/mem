using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Core.Reporting;

public sealed class AssessmentReportWriter : IAssessmentReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public AssessmentRenderedReports Render(
        AssessmentResult result,
        bool includeSensitivePaths)
    {
        var rawJson = JsonSerializer.Serialize(result, JsonOptions);
        var publicNode = JsonNode.Parse(rawJson)
            ?? throw new InvalidOperationException("Assessment JSON was empty.");
        var privateNode = JsonNode.Parse(rawJson)
            ?? throw new InvalidOperationException("Assessment JSON was empty.");

        RedactNode(publicNode, includeSensitivePaths);
        RedactNode(privateNode, includeSensitivePaths: true);

        var publicJson = publicNode.ToJsonString(JsonOptions);
        var privateJson = privateNode.ToJsonString(JsonOptions);
        var publicMarkdown = RenderMarkdown(result, includeSensitivePaths);

        return new AssessmentRenderedReports(
            PublicJson: publicJson,
            PublicMarkdown: publicMarkdown,
            PrivateJson: privateJson);
    }

    public async Task WriteAsync(
        AssessmentRenderedReports reports,
        string outputDirectory,
        string workspaceDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(workspaceDirectory);

        FilePermissions.EnsureDirectoryPrivate(workspaceDirectory);

        var publicJsonPath = Path.Combine(outputDirectory, "assessment.json");
        var publicMarkdownPath = Path.Combine(outputDirectory, "assessment.md");
        var privateJsonPath = Path.Combine(workspaceDirectory, "assessment.private.json");

        await File.WriteAllTextAsync(
            publicJsonPath,
            reports.PublicJson + Environment.NewLine,
            cancellationToken);

        await File.WriteAllTextAsync(
            publicMarkdownPath,
            reports.PublicMarkdown + Environment.NewLine,
            cancellationToken);

        await File.WriteAllTextAsync(
            privateJsonPath,
            reports.PrivateJson + Environment.NewLine,
            cancellationToken);

        FilePermissions.EnsureFilePrivate(privateJsonPath);
    }

    private static void RedactNode(
        JsonNode node,
        bool includeSensitivePaths,
        string? propertyName = null)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj.ToList())
                {
                    if (pair.Value is null)
                    {
                        continue;
                    }

                    var isRowCountEntry =
                        string.Equals(
                            propertyName,
                            "rowCounts",
                            StringComparison.OrdinalIgnoreCase);

                    if (!isRowCountEntry &&
                        AssessmentRedactor.IsSensitiveKey(pair.Key))
                    {
                        var valueKind = pair.Value.GetValueKind();

                        if (valueKind is JsonValueKind.Number
                            or JsonValueKind.True
                            or JsonValueKind.False)
                        {
                            continue;
                        }

                        obj[pair.Key] = "<redacted>";
                        continue;
                    }

                    RedactNode(pair.Value, includeSensitivePaths, pair.Key);
                }

                break;

            case JsonArray array:
                // Redacting a JsonValue can replace the node in its parent array.
                // Enumerating the JsonArray while replacing a child invalidates the
                // collection enumerator and fails on real observations containing
                // string arrays (for example environment names and network aliases).
                for (var index = 0; index < array.Count; index++)
                {
                    var child = array[index];

                    if (child is not null)
                    {
                        RedactNode(child, includeSensitivePaths, propertyName);
                    }
                }

                break;

            case JsonValue value:
                if (!value.TryGetValue<string>(out var stringValue))
                {
                    return;
                }

                var redacted = AssessmentRedactor.RedactText(stringValue);

                if (!includeSensitivePaths &&
                    IsPathProperty(propertyName) &&
                    Path.IsPathFullyQualified(stringValue))
                {
                    redacted = AssessmentRedactor.DisplayPath(
                        stringValue,
                        includeSensitivePaths: false);
                }

                value.ReplaceWith(redacted);
                break;
        }
    }

    private static bool IsPathProperty(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        return propertyName.EndsWith("Path", StringComparison.OrdinalIgnoreCase)
            || propertyName.EndsWith("Root", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("Source", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("Destination", StringComparison.OrdinalIgnoreCase);
    }

    private static string RenderMarkdown(
        AssessmentResult result,
        bool includeSensitivePaths)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# MEM v0.1.0 Migration Assessment");
        builder.AppendLine();
        builder.AppendLine($"- **Assessment ID:** `{result.AssessmentId}`");
        builder.AppendLine($"- **Completed:** {result.CompletedAtUtc:O}");
        builder.AppendLine($"- **Classification:** `{result.Classification}`");
        builder.AppendLine($"- **Recommendation:** `{result.Recommendation}`");
        builder.AppendLine($"- **Capture allowed:** `{result.CanProceedToCapture}`");
        builder.AppendLine($"- **Source fingerprint:** `{result.SourceFingerprint}`");
        builder.AppendLine();

        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine(
            $"Detected {result.Docker.Containers.Length} Docker containers, " +
            $"{result.Database.Candidates.Length} PostgreSQL candidate(s), and " +
            $"{result.FileSystem.Stacks.Length} legacy Matrix stack file set(s).");
        builder.AppendLine();

        builder.AppendLine("## Findings");
        builder.AppendLine();

        if (result.Findings.Length == 0)
        {
            builder.AppendLine("No findings were recorded.");
        }
        else
        {
            foreach (var finding in result.Findings
                         .OrderByDescending(x => x.Severity)
                         .ThenBy(x => x.Code, StringComparer.Ordinal))
            {
                builder.AppendLine(
                    $"- **{finding.Severity} — `{finding.Code}`:** " +
                    $"{AssessmentRedactor.RedactText(finding.Message)}");

                if (!string.IsNullOrWhiteSpace(finding.Remediation))
                {
                    builder.AppendLine(
                        $"  - Remediation: {AssessmentRedactor.RedactText(finding.Remediation)}");
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Detection signals");
        builder.AppendLine();
        builder.AppendLine("| Signal | Present | Weight | Summary |");
        builder.AppendLine("|---|---:|---:|---|");

        foreach (var signal in result.Signals.OrderBy(x => x.Code, StringComparer.Ordinal))
        {
            builder.AppendLine(
                $"| `{Escape(signal.Code)}` | {signal.Present} | {signal.Weight} | {Escape(signal.Summary)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Host");
        builder.AppendLine();
        builder.AppendLine($"- Operating system: {Escape(result.Host.OperatingSystem)}");
        builder.AppendLine($"- Architecture: `{Escape(result.Host.Architecture)}`");
        builder.AppendLine($"- Workspace free: {FormatBytes(result.Host.WorkspaceAvailableBytes)}");
        builder.AppendLine(
            $"- Workspace: `{Escape(AssessmentRedactor.DisplayPath(result.Host.WorkspacePath, includeSensitivePaths))}`");

        builder.AppendLine();
        builder.AppendLine("## Legacy product");
        builder.AppendLine();
        builder.AppendLine(
            $"- Endpoint reachable: `{result.SystemConfig.Reachable}`");
        builder.AppendLine(
            $"- Product: `{Escape(result.SystemConfig.ProductName ?? "unknown")}`");
        builder.AppendLine(
            $"- Version: `{Escape(result.SystemConfig.ProductVersion ?? "unknown")}`");

        builder.AppendLine();
        builder.AppendLine("## Docker inventory");
        builder.AppendLine();

        if (!result.Docker.Available)
        {
            builder.AppendLine(
                $"> Docker unavailable: `{Escape(result.Docker.ErrorCode ?? "unknown")}`");
        }
        else
        {
            builder.AppendLine("| Name | Image | State | Health | Compose project |");
            builder.AppendLine("|---|---|---|---|---|");

            foreach (var container in result.Docker.Containers
                         .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine(
                    $"| `{Escape(container.Name)}` | `{Escape(container.Image)}` | " +
                    $"`{Escape(container.State)}` | `{Escape(container.Health ?? "-")}` | " +
                    $"`{Escape(container.ComposeProject ?? "-")}` |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Legacy database");
        builder.AppendLine();

        if (result.Database.Candidates.Length == 0)
        {
            builder.AppendLine("No legacy PostgreSQL application schema was confirmed.");
        }
        else
        {
            foreach (var candidate in result.Database.Candidates)
            {
                builder.AppendLine(
                    $"### `{Escape(candidate.ContainerName)}` / `{Escape(candidate.DatabaseName)}`");
                builder.AppendLine();
                builder.AppendLine($"- App schema present: `{candidate.AppSchemaPresent}`");
                builder.AppendLine($"- Exact supported schema: `{candidate.ExactSupportedSchema}`");
                builder.AppendLine(
                    $"- Migrations: {string.Join(", ", candidate.MigrationIds.Select(x => $"`{Escape(x)}`"))}");
                builder.AppendLine(
                    $"- Stacks: {candidate.Stacks.Length}");
                builder.AppendLine(
                    $"- Services: {candidate.Services.Length}");
                builder.AppendLine(
                    $"- Active guest chats: {candidate.ActiveGuestChats}");
                builder.AppendLine(
                    $"- Active password resets: {candidate.ActivePasswordResetRequests}");
                builder.AppendLine();

                if (candidate.Stacks.Length > 0)
                {
                    builder.AppendLine("| Stack | Status | Matrix service | Primary URL |");
                    builder.AppendLine("|---|---:|---|---|");

                    foreach (var stack in candidate.Stacks.OrderBy(x => x.Slug))
                    {
                        builder.AppendLine(
                            $"| `{Escape(stack.Slug)}` | {stack.Status} | " +
                            $"`{Escape(stack.MatrixInstanceId?.ToString("D") ?? "-")}` | " +
                            $"{Escape(stack.PrimaryUrl ?? "-")} |");
                    }

                    builder.AppendLine();
                }

                if (candidate.Services.Length > 0)
                {
                    builder.AppendLine("| Service | Stack | Image | Server name | Data root |");
                    builder.AppendLine("|---|---|---|---|---|");

                    foreach (var service in candidate.Services
                                 .OrderBy(x => x.StackId)
                                 .ThenBy(x => x.ServiceKey))
                    {
                        builder.AppendLine(
                            $"| `{Escape(service.ServiceKey)}` | `{service.StackId:D}` | " +
                            $"`{Escape(service.Image)}` | `{Escape(service.ServerName ?? "-")}` | " +
                            $"`{Escape(AssessmentRedactor.DisplayPath(service.DataPath, includeSensitivePaths))}` |");
                    }

                    builder.AppendLine();
                }
            }
        }

        builder.AppendLine("## Stack files");
        builder.AppendLine();

        if (result.FileSystem.Stacks.Length == 0)
        {
            builder.AppendLine("No legacy stack file sets were confirmed.");
        }
        else
        {
            builder.AppendLine(
                "| Stack | Database | Database size | Signing key | Media size | Configuration |");
            builder.AppendLine("|---|---|---:|---|---:|---|");

            foreach (var stack in result.FileSystem.Stacks.OrderBy(x => x.StackId))
            {
                builder.AppendLine(
                    $"| `{stack.StackId:D}` | " +
                    $"`{Escape(AssessmentRedactor.DisplayPath(stack.SqliteDatabase.Path, includeSensitivePaths))}` | " +
                    $"{FormatBytes(stack.SqliteDatabase.SizeBytes ?? 0)} | " +
                    $"`{stack.SigningKey.Exists}` | " +
                    $"{FormatBytes(stack.MediaStore.TotalBytes)} | " +
                    $"`{Escape(stack.ParsedConfiguration.DatabaseEngine ?? "unknown")}` |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Safety statement");
        builder.AppendLine();
        builder.AppendLine(
            "This MM-01 assessment is read-only with respect to the detected MEM installation. " +
            "It may create files only inside the selected migrator workspace and report output directories. " +
            "It does not stop containers, change restart policies, alter NPM routes, write to the legacy PostgreSQL database, or modify stack data.");

        return builder.ToString().TrimEnd();
    }

    private static string Escape(string value) =>
        AssessmentRedactor.RedactText(value)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double size = Math.Max(0, value);
        var index = 0;

        while (size >= 1024 && index < units.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return $"{size:0.##} {units[index]}";
    }
}

internal static class FilePermissions
{
    public static void EnsureDirectoryPrivate(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Could not enforce private permissions on directory '{path}'.",
                ex);
        }
    }

    public static void EnsureFilePrivate(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Could not enforce private permissions on file '{path}'.",
                ex);
        }
    }
}
