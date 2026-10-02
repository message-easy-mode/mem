namespace HostAgent.Runtime.Diagnostics.Docker;

public static class MemDockerEvidenceOptionsValidator
{
    public static void ThrowIfInvalid(MemDockerEvidenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        RequireRange(options.DefaultTailLines, 1, 5000, nameof(options.DefaultTailLines));
        RequireRange(options.MaximumTailLines, options.DefaultTailLines, 5000, nameof(options.MaximumTailLines));
        RequireRange(options.MaximumCharacters, 1000, 250000, nameof(options.MaximumCharacters));
        RequireRange(options.MaximumRawBytes, 4096, 16 * 1024 * 1024, nameof(options.MaximumRawBytes));
        RequireRange(options.MaximumFrameBytes, 1024, 16 * 1024 * 1024, nameof(options.MaximumFrameBytes));
        RequireRange(options.TimeoutSeconds, 1, 60, nameof(options.TimeoutSeconds));

        if (options.MaximumFrameBytes > options.MaximumRawBytes)
        {
            throw new InvalidOperationException(
                $"{MemDockerEvidenceOptions.SectionName}:MaximumFrameBytes cannot exceed MaximumRawBytes.");
        }
    }

    private static void RequireRange(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"{MemDockerEvidenceOptions.SectionName}:{name} must be between {minimum} and {maximum}.");
        }
    }
}
