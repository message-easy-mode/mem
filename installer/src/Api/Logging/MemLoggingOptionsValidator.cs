using System.Net.Http.Headers;

namespace Api.Logging;

public static class MemLoggingOptionsValidator
{
    public static IReadOnlyList<string> Validate(MemLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.FilePath))
        {
            errors.Add("Diagnostics:Logging:FilePath is required.");
        }
        else if (options.FilePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            errors.Add("Diagnostics:Logging:FilePath contains invalid path characters.");
        }

        if (options.FileSizeLimitMiB is < 1 or > 512)
        {
            errors.Add("Diagnostics:Logging:FileSizeLimitMiB must be between 1 and 512.");
        }

        if (options.RetainedFileCountLimit is < 1 or > 365)
        {
            errors.Add("Diagnostics:Logging:RetainedFileCountLimit must be between 1 and 365.");
        }

        if (options.FlushIntervalMilliseconds is < 100 or > 60000)
        {
            errors.Add("Diagnostics:Logging:FlushIntervalMilliseconds must be between 100 and 60000.");
        }

        if (options.LowDiskWarningMiB is < 64 or > 1048576)
        {
            errors.Add("Diagnostics:Logging:LowDiskWarningMiB must be between 64 and 1048576.");
        }

        if (options.CriticalDiskWarningMiB is < 32 or > 1048576)
        {
            errors.Add("Diagnostics:Logging:CriticalDiskWarningMiB must be between 32 and 1048576.");
        }

        if (options.CriticalDiskWarningMiB > options.LowDiskWarningMiB)
        {
            errors.Add("Diagnostics:Logging:CriticalDiskWarningMiB cannot exceed LowDiskWarningMiB.");
        }

        if (options.PersistentFileEnabled &&
            !string.Equals(Path.GetExtension(options.FilePath), ".clef", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Diagnostics:Logging:FilePath must use the .clef extension.");
        }

        if (string.IsNullOrWhiteSpace(options.CorrelationHeaderName) ||
            !IsValidHeaderName(options.CorrelationHeaderName))
        {
            errors.Add("Diagnostics:Logging:CorrelationHeaderName must be a valid HTTP header name.");
        }

        return errors;
    }

    public static void ThrowIfInvalid(MemLoggingOptions options)
    {
        var errors = Validate(options);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }
    }

    private static bool IsValidHeaderName(string value)
    {
        try
        {
            using var request = new HttpRequestMessage();
            return request.Headers.TryAddWithoutValidation(value, "validation") &&
                   request.Headers.Contains(value);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
