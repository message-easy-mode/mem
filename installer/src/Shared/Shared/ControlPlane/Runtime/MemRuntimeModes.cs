namespace Shared.ControlPlane.Runtime;

public static class MemRuntimeModes
{
    public const string LocalDevelopment = "local-development";
    public const string ContainerizedDevelopment = "containerized-development";
    public const string ContainerizedProduction = "containerized-production";
    public const string AutomatedTest = "automated-test";

    public static IReadOnlyList<string> All { get; } =
    [
        LocalDevelopment,
        ContainerizedDevelopment,
        ContainerizedProduction,
        AutomatedTest
    ];

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return All.Contains(normalized, StringComparer.Ordinal);
    }

    public static bool IsContainerized(string mode) =>
        string.Equals(mode, ContainerizedDevelopment, StringComparison.Ordinal) ||
        string.Equals(mode, ContainerizedProduction, StringComparison.Ordinal);

    public static bool IsNonProduction(string mode) =>
        !string.Equals(mode, ContainerizedProduction, StringComparison.Ordinal);
}

public static class MemUiDeliveryModes
{
    public const string Vite = "vite";
    public const string EmbeddedSpa = "embedded-spa";
    public const string ApiOnly = "api-only";
    public const string TestHost = "test-host";

    public static IReadOnlyList<string> All { get; } =
    [
        Vite,
        EmbeddedSpa,
        ApiOnly,
        TestHost
    ];

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return All.Contains(normalized, StringComparer.Ordinal);
    }
}

public static class MemRuntimeValidationStates
{
    public const string Valid = "valid";
    public const string Warning = "warning";
}

public static class MemStateRootProfiles
{
    public const string Default = "default";
    public const string Custom = "custom";
}
