using System;

namespace Infrastructure.Data.Entities;

public sealed class RuntimeOperationEntity
{
    public Guid Id { get; set; }

    public Guid? RuntimeStackId { get; set; }
    public RuntimeStackEntity? RuntimeStack { get; set; }

    /// <summary>Optional restore workspace that owns this operation.</summary>
    public Guid? RestoreAttemptId { get; set; }
    public RestoreAttemptEntity? RestoreAttempt { get; set; }

    /// <summary>Optional Domain that owns this operation.</summary>
    public Guid? DomainId { get; set; }
    public DomainEntity? Domain { get; set; }

    public string Operation { get; set; } = default!;
    public string Status { get; set; } = default!;

    public string? IdempotencyKey { get; set; }

    public string? RequestedBy { get; set; }
    public Guid? RequestedByUserId { get; set; }

    public DateTime RequestedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public string? CurrentStep { get; set; }

    public int AttemptCount { get; set; }
    public DateTime? LockedUntilUtc { get; set; }

    public string? InputJson { get; set; }
    public string? ResultJson { get; set; }
    public string? EvidenceJson { get; set; }

    public string? LastError { get; set; }

    public string? HostMutationLevel { get; set; }

    public bool RequiresConfirmation { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
}