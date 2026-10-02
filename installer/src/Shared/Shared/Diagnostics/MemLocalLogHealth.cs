namespace Shared.Diagnostics;

public sealed record MemLocalLogHealth(
    bool Enabled,
    string Status,
    bool PersistentRecorderConfigured,
    bool PersistentRecorderActive,
    string? PersistentFilePath,
    DateTimeOffset? LastFileWriteAtUtc,
    int RetainedFileCount,
    long RetainedBytes,
    int SerilogSelfLogMessageCount,
    string? WarningCode,
    MemStorageCapacityHealth? Storage = null);
