namespace Infrastructure.Data.Entities;

/// <summary>
/// Temporary ownership record for a restore target. ActiveClaimKey is populated
/// only while the claim is live, allowing historical released claims to remain
/// inspectable without blocking a future restore.
/// </summary>
public sealed class RestoreTargetClaimEntity
{
    public Guid Id { get; set; }

    public Guid RestoreAttemptId { get; set; }
    public RestoreAttemptEntity RestoreAttempt { get; set; } = default!;

    public string ResourceType { get; set; } = default!;
    public string ResourceValue { get; set; } = default!;
    public string? ActiveClaimKey { get; set; }

    public DateTime ClaimedAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public string? ReleaseReason { get; set; }
}
