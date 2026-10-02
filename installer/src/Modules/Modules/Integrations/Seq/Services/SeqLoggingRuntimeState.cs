namespace Modules.Integrations.Seq.Services;

/// <summary>
/// Captures the actual Seq sink result from logging bootstrap for the current
/// API process. This is stronger than configuration intent: it proves whether
/// Serilog attached the Seq sink when the process started.
/// </summary>
public sealed record SeqLoggingRuntimeState(
    bool SinkConfigured,
    string? WarningCode);
