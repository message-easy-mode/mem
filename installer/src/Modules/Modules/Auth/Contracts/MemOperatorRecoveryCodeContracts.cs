namespace Modules.Auth.Contracts;

/// <summary>
/// One-time replacement recovery codes for the currently signed-in operator.
/// Raw codes are returned only by the no-store regeneration endpoint and must
/// never be written to audit events, logs, browser persistence, or later reads.
/// </summary>
public sealed record MemOperatorRecoveryCodesResponse(
    string Status,
    IReadOnlyList<string> RecoveryCodes);
