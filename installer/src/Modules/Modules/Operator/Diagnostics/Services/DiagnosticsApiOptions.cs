namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsApiOptions
{
    public const string SafeEventsSectionName = "Diagnostics:SafeEvents";

    public int RetentionDays { get; set; } = 14;

    public int MaximumQueryWindowHours { get; set; } = 168;

    public int MaximumPageSize { get; set; } = 200;

    public int MaximumSearchCharacters { get; set; } = 200;

    public int OverviewMaximumEvents { get; set; } = 1000;

    public int IncidentMaximumEvents { get; set; } = 250;

    public int SupportReportMaximumEvents { get; set; } = 250;

    public int SupportReportMaximumRequestBytes { get; set; } = 4096;

    public int SupportReportMaximumBytes { get; set; } = 2 * 1024 * 1024;

    public int SupportReportMaximumDockerLogCharacters { get; set; } = 20000;

    public static void Validate(DiagnosticsApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.RetentionDays is < 1 or > 90)
        {
            throw new InvalidOperationException(
                "Diagnostics API retention days must be between 1 and 90.");
        }

        if (options.MaximumQueryWindowHours is < 1 or > 744)
        {
            throw new InvalidOperationException(
                "Diagnostics API maximum query window must be between 1 and 744 hours.");
        }

        if (options.MaximumPageSize is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "Diagnostics API maximum page size must be between 1 and 1000.");
        }

        if (options.MaximumSearchCharacters is < 16 or > 2000)
        {
            throw new InvalidOperationException(
                "Diagnostics API search length must be between 16 and 2000 characters.");
        }

        if (options.OverviewMaximumEvents is < 100 or > 5000 ||
            options.IncidentMaximumEvents is < 10 or > 1000 ||
            options.SupportReportMaximumEvents is < 10 or > 1000)
        {
            throw new InvalidOperationException(
                "Diagnostics API event collection bounds are invalid.");
        }

        if (options.SupportReportMaximumRequestBytes is < 256 or > 65536)
        {
            throw new InvalidOperationException(
                "Diagnostics support-report request size must be between 256 and 65536 bytes.");
        }

        if (options.SupportReportMaximumBytes is < 65536 or > 16 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "Diagnostics support-report size must be between 65536 and 16777216 bytes.");
        }

        if (options.SupportReportMaximumDockerLogCharacters is < 0 or > 100000)
        {
            throw new InvalidOperationException(
                "Diagnostics support-report Docker log bound must be between 0 and 100000 characters.");
        }
    }
}
