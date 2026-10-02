using System.Text.Json;
using Modules.Operator.Diagnostics.Services;
using Shared.Diagnostics;

namespace Modules.Setup.SupportReports;

public sealed class SetupInstallationSupportReportSizeLimiter(
    DiagnosticsApiOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public SetupInstallationSupportReport Apply(SetupInstallationSupportReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var bounded = BoundDockerLog(report);
        if (SerializedSize(bounded) <= options.SupportReportMaximumBytes)
        {
            return bounded;
        }

        bounded = MarkSizeTruncated(
            bounded,
            "Safe diagnostic events were reduced to keep the Setup support report within its configured size limit.");

        var count = FindLargestEventCount(bounded);
        bounded = bounded with
        {
            Diagnostics = bounded.Diagnostics with
            {
                Events = bounded.Diagnostics.Events.Take(count).ToArray()
            }
        };
        if (SerializedSize(bounded) <= options.SupportReportMaximumBytes)
        {
            return bounded;
        }

        bounded = DropDockerLogContent(bounded);
        if (SerializedSize(bounded) <= options.SupportReportMaximumBytes)
        {
            return bounded;
        }

        bounded = MarkSizeTruncated(
            bounded with
            {
                Diagnostics = bounded.Diagnostics with
                {
                    Events = [],
                    Incidents = bounded.Diagnostics.Incidents.Take(25).ToArray()
                },
                Warnings = bounded.Warnings.Take(50).ToArray()
            },
            "Additional diagnostic detail was omitted to keep the Setup support report within its configured size limit.");
        if (SerializedSize(bounded) <= options.SupportReportMaximumBytes)
        {
            return bounded;
        }

        throw new InvalidOperationException(
            "The Setup installation support report could not be bounded within the configured maximum size.");
    }

    internal int SerializedSize(SetupInstallationSupportReport report) =>
        JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions).Length;

    private SetupInstallationSupportReport BoundDockerLog(
        SetupInstallationSupportReport report)
    {
        var logTail = report.Diagnostics.DockerEvidence?.LogTail;
        if (logTail is null ||
            logTail.Content.Length <= options.SupportReportMaximumDockerLogCharacters)
        {
            return report;
        }

        var keep = options.SupportReportMaximumDockerLogCharacters;
        var content = keep == 0
            ? string.Empty
            : logTail.Content[^Math.Min(keep, logTail.Content.Length)..];
        var dockerEvidence = report.Diagnostics.DockerEvidence! with
        {
            LogTail = logTail with
            {
                Content = content,
                ReturnedLines = CountLines(content),
                Truncated = true
            }
        };

        var warnings = new HashSet<string>(report.Warnings, StringComparer.Ordinal)
        {
            MemDiagnosticCodes.SupportReportDockerLogTruncated
        };
        var bounded = report with
        {
            Diagnostics = report.Diagnostics with { DockerEvidence = dockerEvidence },
            Warnings = warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Truncated = true
        };
        return AddOmission(
            bounded,
            "Docker log content was truncated to the configured Setup support-report limit.");
    }

    private static SetupInstallationSupportReport DropDockerLogContent(
        SetupInstallationSupportReport report)
    {
        var logTail = report.Diagnostics.DockerEvidence?.LogTail;
        if (logTail is null || logTail.Content.Length == 0)
        {
            return report;
        }

        var dockerEvidence = report.Diagnostics.DockerEvidence! with
        {
            LogTail = logTail with
            {
                Content = string.Empty,
                ReturnedLines = 0,
                Truncated = true
            }
        };
        var warnings = new HashSet<string>(report.Warnings, StringComparer.Ordinal)
        {
            MemDiagnosticCodes.SupportReportDockerLogTruncated,
            MemDiagnosticCodes.SupportReportSizeTruncated
        };
        return AddOmission(
            report with
            {
                Diagnostics = report.Diagnostics with { DockerEvidence = dockerEvidence },
                Warnings = warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                Truncated = true
            },
            "Docker log content was omitted to keep the Setup support report within its configured size limit.");
    }

    private static SetupInstallationSupportReport MarkSizeTruncated(
        SetupInstallationSupportReport report,
        string omission)
    {
        var warnings = new HashSet<string>(report.Warnings, StringComparer.Ordinal)
        {
            MemDiagnosticCodes.SupportReportSizeTruncated
        };
        return AddOmission(
            report with
            {
                Warnings = warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                Truncated = true
            },
            omission);
    }

    private static SetupInstallationSupportReport AddOmission(
        SetupInstallationSupportReport report,
        string omission)
    {
        var omitted = new HashSet<string>(report.Redaction.OmittedContent, StringComparer.Ordinal)
        {
            omission
        };
        return report with
        {
            Redaction = report.Redaction with
            {
                OmittedContent = omitted.OrderBy(value => value, StringComparer.Ordinal).ToArray()
            }
        };
    }

    private int FindLargestEventCount(SetupInstallationSupportReport report)
    {
        var low = 0;
        var high = report.Diagnostics.Events.Count;
        var best = 0;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = report with
            {
                Diagnostics = report.Diagnostics with
                {
                    Events = report.Diagnostics.Events.Take(middle).ToArray()
                }
            };

            if (SerializedSize(candidate) <= options.SupportReportMaximumBytes)
            {
                best = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return best;
    }

    private static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content)) return 0;
        var lines = 1;
        foreach (var character in content)
        {
            if (character == '\n') lines++;
        }
        return lines;
    }
}
