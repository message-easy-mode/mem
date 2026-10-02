using System;

namespace Core.Orchestration;

public sealed class OrchestrationOptions
{
    public int PostgresReadyTimeoutSeconds { get; set; } = 90;
    public int NpmReadyTimeoutSeconds { get; set; } = 90;
    public int PollDelaySeconds { get; set; } = 2;
}