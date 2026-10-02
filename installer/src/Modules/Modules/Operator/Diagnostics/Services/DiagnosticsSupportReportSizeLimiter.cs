using System.Text.Json;
using Modules.Operator.Diagnostics.Contracts;
using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsSupportReportSizeLimiter(
    DiagnosticsApiOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public DiagnosticsSupportReport Apply(DiagnosticsSupportReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var bounded = BoundDockerLog(report);
        if (SerializedSize(bounded) <= options.SupportReportMaximumBytes)
        {
            return bounded;
        }

        var warnings = new HashSet<string>(bounded.Warnings, StringComparer.Ordinal)
        {
            MemDiagnosticCodes.SupportReportSizeTruncated
        };
        var omissions = new HashSet<string>(
            bounded.Redaction.OmittedContent,
            StringComparer.Ordinal)
        {
            "Technical events were reduced to keep the support report within its configured size limit."
        };

        var template = bounded with
        {
            Truncated = true,
            Warnings = warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Redaction = bounded.Redaction with
            {
                OmittedContent = omissions.OrderBy(value => value, StringComparer.Ordinal).ToArray()
            }
        };

        var eventCount = FindLargestEventCount(template);
        bounded = template with
        {
            Events = template.Events.Take(eventCount).ToArray()
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

        bounded = bounded with
        {
            Events = Array.Empty<DiagnosticsEventProjection>(),
            Operations = bounded.Operations.Take(10).ToArray(),
            Warnings = bounded.Warnings.Take(50).ToArray()
        };

        if (SerializedSize(bounded) <= options.SupportReportMaximumBytes)
        {
            return bounded;
        }

        throw new InvalidOperationException(
            "The diagnostics support report could not be bounded within the configured maximum size.");
    }

    internal int SerializedSize(DiagnosticsSupportReport report) =>
        JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions).Length;

    private DiagnosticsSupportReport BoundDockerLog(
        DiagnosticsSupportReport report)
    {
        var logTail = report.DockerEvidence?.LogTail;
        if (logTail is null ||
            logTail.Content.Length <= options.SupportReportMaximumDockerLogCharacters)
        {
            return report;
        }

        var keep = options.SupportReportMaximumDockerLogCharacters;
        var retainedCharacters = Math.Min(keep, logTail.Content.Length);
        var content = retainedCharacters == 0
            ? string.Empty
            : logTail.Content.Substring(
                logTail.Content.Length - retainedCharacters,
                retainedCharacters);
        var warningSet = new HashSet<string>(report.Warnings, StringComparer.Ordinal)
        {
            MemDiagnosticCodes.SupportReportDockerLogTruncated
        };
        var omissionSet = new HashSet<string>(
            report.Redaction.OmittedContent,
            StringComparer.Ordinal)
        {
            "Docker log content was shortened to keep the support report bounded."
        };

        return report with
        {
            DockerEvidence = report.DockerEvidence! with
            {
                LogTail = logTail with
                {
                    Content = content,
                    ReturnedLines = CountLines(content),
                    MaximumCharacters = keep,
                    Truncated = true
                }
            },
            Redaction = report.Redaction with
            {
                OmittedContent = omissionSet.OrderBy(value => value, StringComparer.Ordinal).ToArray()
            },
            Truncated = true,
            Warnings = warningSet.OrderBy(value => value, StringComparer.Ordinal).ToArray()
        };
    }

    private int FindLargestEventCount(DiagnosticsSupportReport template)
    {
        var low = 0;
        var high = template.Events.Count;
        var best = 0;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = template with
            {
                Events = template.Events.Take(middle).ToArray()
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

    private static DiagnosticsSupportReport DropDockerLogContent(
        DiagnosticsSupportReport report)
    {
        var logTail = report.DockerEvidence?.LogTail;
        if (logTail is null || logTail.Content.Length == 0)
        {
            return report;
        }

        var warningSet = new HashSet<string>(report.Warnings, StringComparer.Ordinal)
        {
            MemDiagnosticCodes.SupportReportDockerLogTruncated,
            MemDiagnosticCodes.SupportReportSizeTruncated
        };
        var omissionSet = new HashSet<string>(
            report.Redaction.OmittedContent,
            StringComparer.Ordinal)
        {
            "Docker log content was omitted to keep the support report within its configured size limit."
        };

        return report with
        {
            DockerEvidence = report.DockerEvidence! with
            {
                LogTail = logTail with
                {
                    Content = string.Empty,
                    ReturnedLines = 0,
                    Truncated = true
                }
            },
            Redaction = report.Redaction with
            {
                OmittedContent = omissionSet.OrderBy(value => value, StringComparer.Ordinal).ToArray()
            },
            Truncated = true,
            Warnings = warningSet.OrderBy(value => value, StringComparer.Ordinal).ToArray()
        };
    }

    private static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return 0;
        }

        var lines = 1;
        foreach (var character in content)
        {
            if (character == '\n')
            {
                lines++;
            }
        }

        return lines;
    }
}
