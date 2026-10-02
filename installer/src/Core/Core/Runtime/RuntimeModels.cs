using System;

namespace Core.Runtime;

public sealed record RuntimePortPlan(
    int ContainerPort,
    int PreferredHostPort,
    int SelectedHostPort,
    bool IsPreferredPortAvailable,
    string Protocol,
    IReadOnlyList<string> Warnings
);

public sealed record RuntimeServicePlan(
    string ServiceName,
    string ContainerName,
    string Image,
    IReadOnlyList<RuntimePortPlan> Ports,
    string? HostDataPath,
    string? HostLetsEncryptPath,
    IReadOnlyList<string> Warnings
);