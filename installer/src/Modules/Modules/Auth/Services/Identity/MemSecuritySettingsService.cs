using System.Data.Common;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Modules.Auth.Services.Identity;

public sealed record MemSecuritySettingsSnapshot(
    bool RequireHighRiskStepUp,
    int HighRiskStepUpGrantMinutes,
    bool IsDefaulted,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByOperatorId);

public sealed record UpdateMemSecuritySettingsCommand(
    bool RequireHighRiskStepUp,
    int HighRiskStepUpGrantMinutes);

public enum MemSecuritySettingsFailureKind
{
    Validation,
    PersistenceUnavailable
}

public sealed class MemSecuritySettingsException : Exception
{
    public MemSecuritySettingsException(
        string code,
        MemSecuritySettingsFailureKind kind)
        : base(code)
    {
        Code = code;
        Kind = kind;
    }

    public MemSecuritySettingsException(
        string code,
        MemSecuritySettingsFailureKind kind,
        Exception innerException)
        : base(code, innerException)
    {
        Code = code;
        Kind = kind;
    }

    public string Code { get; }
    public MemSecuritySettingsFailureKind Kind { get; }
}

public interface IMemSecuritySettingsService
{
    Task<MemSecuritySettingsSnapshot> GetEffectiveAsync(CancellationToken ct = default);

    Task<MemSecuritySettingsSnapshot> UpdateHighRiskStepUpAsync(
        Guid actorOperatorId,
        UpdateMemSecuritySettingsCommand command,
        string? correlationId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Server-owned security settings. A missing, unreadable, malformed, or not yet
/// migrated settings row fails closed to the recommended high-risk step-up
/// posture. Only an explicit, valid persisted row can relax the policy.
/// </summary>
public sealed class MemSecuritySettingsService(
    MemDbContext db,
    IMemOperatorAuditService audit,
    TimeProvider timeProvider) : IMemSecuritySettingsService
{
    public async Task<MemSecuritySettingsSnapshot> GetEffectiveAsync(
        CancellationToken ct = default)
    {
        try
        {
            var entity = await db.MemSecuritySettings
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entry => entry.Id == MemSecuritySettingsEntity.SingletonId,
                    ct);

            if (entity is null ||
                !MemSecuritySettingsDefaults.IsAllowedHighRiskStepUpGrantMinutes(
                    entity.HighRiskStepUpGrantMinutes))
            {
                return DefaultSnapshot();
            }

            return new MemSecuritySettingsSnapshot(
                entity.RequireHighRiskStepUp,
                entity.HighRiskStepUpGrantMinutes,
                IsDefaulted: false,
                entity.UpdatedAtUtc,
                entity.UpdatedByOperatorId);
        }
        catch (DbException)
        {
            return DefaultSnapshot();
        }
        catch (InvalidOperationException)
        {
            return DefaultSnapshot();
        }
    }

    public async Task<MemSecuritySettingsSnapshot> UpdateHighRiskStepUpAsync(
        Guid actorOperatorId,
        UpdateMemSecuritySettingsCommand command,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        if (!MemSecuritySettingsDefaults.IsAllowedHighRiskStepUpGrantMinutes(
                command.HighRiskStepUpGrantMinutes))
        {
            throw new MemSecuritySettingsException(
                "high_risk_step_up_window_invalid",
                MemSecuritySettingsFailureKind.Validation);
        }

        var now = timeProvider.GetUtcNow();

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var entity = await db.MemSecuritySettings.SingleOrDefaultAsync(
                entry => entry.Id == MemSecuritySettingsEntity.SingletonId,
                ct);

            if (entity is null)
            {
                entity = new MemSecuritySettingsEntity
                {
                    Id = MemSecuritySettingsEntity.SingletonId,
                    CreatedAtUtc = now
                };

                await db.MemSecuritySettings.AddAsync(entity, ct);
            }

            var previousRequired = entity.RequireHighRiskStepUp;
            var previousMinutes = entity.HighRiskStepUpGrantMinutes;

            var policyChanged = previousRequired != command.RequireHighRiskStepUp ||
                previousMinutes != command.HighRiskStepUpGrantMinutes;

            entity.RequireHighRiskStepUp = command.RequireHighRiskStepUp;
            entity.HighRiskStepUpGrantMinutes = command.HighRiskStepUpGrantMinutes;
            entity.UpdatedAtUtc = now;
            entity.UpdatedByOperatorId = actorOperatorId;
            entity.ConcurrencyStamp = Guid.NewGuid().ToString("N");

            if (policyChanged)
            {
                await RevokeActiveStepUpGrantsForPolicyChangeAsync(now, ct);
            }

            await db.SaveChangesAsync(ct);

            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "security.high_risk_step_up_policy.changed",
                    Outcome: "succeeded",
                    ActorOperatorId: actorOperatorId,
                    SubjectOperatorId: actorOperatorId,
                    CorrelationId: correlationId,
                    ReasonCode: BuildReasonCode(
                        previousRequired,
                        command.RequireHighRiskStepUp,
                        previousMinutes,
                        command.HighRiskStepUpGrantMinutes)),
                ct);

            await transaction.CommitAsync(ct);

            return new MemSecuritySettingsSnapshot(
                entity.RequireHighRiskStepUp,
                entity.HighRiskStepUpGrantMinutes,
                IsDefaulted: false,
                entity.UpdatedAtUtc,
                entity.UpdatedByOperatorId);
        }
        catch (DbException exception)
        {
            throw new MemSecuritySettingsException(
                "security_settings_persistence_unavailable",
                MemSecuritySettingsFailureKind.PersistenceUnavailable,
                exception);
        }
        catch (DbUpdateException exception)
        {
            throw new MemSecuritySettingsException(
                "security_settings_persistence_unavailable",
                MemSecuritySettingsFailureKind.PersistenceUnavailable,
                exception);
        }
    }


    private async Task RevokeActiveStepUpGrantsForPolicyChangeAsync(
        DateTimeOffset occurredAtUtc,
        CancellationToken ct)
    {
        var grants = await db.MemOperatorStepUpGrants
            .Where(entry => entry.RevokedAtUtc == null)
            .ToArrayAsync(ct);

        foreach (var grant in grants)
        {
            grant.RevokedAtUtc = occurredAtUtc;
            grant.RevokedReasonCode = "security_settings_changed";
        }
    }

    private static MemSecuritySettingsSnapshot DefaultSnapshot() =>
        new(
            MemSecuritySettingsDefaults.RequireHighRiskStepUp,
            MemSecuritySettingsDefaults.RecommendedStepUpGrantMinutes,
            IsDefaulted: true,
            UpdatedAtUtc: null,
            UpdatedByOperatorId: null);

    private static string BuildReasonCode(
        bool previousRequired,
        bool nextRequired,
        int previousMinutes,
        int nextMinutes) =>
        $"required:{ToCode(previousRequired)}->{ToCode(nextRequired)};reuse:{previousMinutes}->{nextMinutes}";

    private static string ToCode(bool value) => value ? "required" : "not_required";
}
