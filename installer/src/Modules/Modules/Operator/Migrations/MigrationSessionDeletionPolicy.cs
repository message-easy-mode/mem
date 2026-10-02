using Infrastructure.Data.Entities.Migrations;

namespace Modules.Operator.Migrations;

internal sealed record MigrationSessionDeletionPolicyInput(
    string LifecycleStatus,
    string EffectiveStatus,
    string? ClosureKind,
    bool HasPackageRevision,
    bool HasPackageEvidence,
    bool HasActivePackageAuthority,
    bool HasConversion,
    bool HasStaging,
    bool HasProductionAuthority,
    bool HasProductionAdoption,
    bool HasAcceptance,
    bool HasBaselineBackup,
    bool HasLegacyRetention,
    bool HasTwoServerQualification,
    bool TargetStorageVerified,
    bool TargetPackageMaterialPresent,
    bool TargetDecryptedPackageMaterialPresent);

internal sealed record MigrationSessionDeletionDecision(
    bool CanDelete,
    string? BlockedCode,
    string? BlockedReason,
    string? AuditReasonCode);

internal static class MigrationSessionDeletionPolicy
{
    public static MigrationSessionDeletionDecision Evaluate(
        MigrationSessionDeletionPolicyInput input)
    {
        if (input.HasAcceptance ||
            input.HasBaselineBackup ||
            string.Equals(
                input.LifecycleStatus,
                MigrationSessionLifecycleStatuses.Completed,
                StringComparison.Ordinal) ||
            string.Equals(
                input.EffectiveStatus,
                "accepted",
                StringComparison.OrdinalIgnoreCase))
        {
            return Blocked(
                "migration_session_completed",
                "Accepted or completed Migration Sessions and their first native backup boundary must be retained.");
        }

        if (input.HasProductionAuthority || input.HasProductionAdoption)
        {
            return Blocked(
                "migration_session_production_owned",
                "Production authority, planning, runtime, public routing, or rollback obligations own this Session.");
        }

        if (input.HasTwoServerQualification)
        {
            return Blocked(
                "migration_session_two_server_qualification",
                "Two-server qualification evidence must be retained.");
        }

        if (input.HasLegacyRetention)
        {
            return Blocked(
                "migration_session_legacy_retention",
                "A legacy retention obligation is attached to this Session.");
        }

        if (input.HasStaging)
        {
            return Blocked(
                "migration_session_staging_exists",
                "Private staging history or runtime ownership is attached to this Session.");
        }

        if (input.HasConversion)
        {
            return Blocked(
                "migration_session_conversion_exists",
                "Conversion or candidate-artifact evidence is attached to this Session.");
        }

        if (string.Equals(
                input.LifecycleStatus,
                MigrationSessionLifecycleStatuses.Active,
                StringComparison.Ordinal))
        {
            return Blocked(
                "migration_session_active",
                "Active Migration Sessions cannot be permanently deleted. Cancel an eligible early Session first.");
        }

        if (input.HasActivePackageAuthority)
        {
            return Blocked(
                "migration_session_package_authority_active",
                "A current package revision is still active and must be cancelled or retired before permanent deletion.");
        }

        if (input.HasPackageEvidence && !input.HasPackageRevision)
        {
            return Blocked(
                "migration_session_package_evidence_inconsistent",
                "Package evidence exists without its canonical package revision and must be reconciled before permanent deletion.");
        }

        var category = ResolveDisposableCategory(input);
        if (category is null)
        {
            return Blocked(
                "migration_session_lifecycle_not_disposable",
                "This terminal Session is retained because its lifecycle is not one of the explicitly disposable early categories.");
        }

        if (!input.TargetStorageVerified)
        {
            return Blocked(
                "migration_session_target_state_unverifiable",
                "Target-side package or workspace state could not be proven safe for permanent deletion.");
        }

        if (!input.HasPackageEvidence && input.TargetPackageMaterialPresent)
        {
            return Blocked(
                "migration_session_unexpected_package_material",
                "Target-side package material exists without matching durable upload or validation evidence.");
        }

        if (input.TargetDecryptedPackageMaterialPresent)
        {
            return Blocked(
                "migration_session_plaintext_package_present",
                "Decrypted target package material remains and must be resolved before permanent deletion.");
        }

        return new MigrationSessionDeletionDecision(
            CanDelete: true,
            BlockedCode: null,
            BlockedReason: null,
            AuditReasonCode: category);
    }

    private static string? ResolveDisposableCategory(
        MigrationSessionDeletionPolicyInput input)
    {
        if (string.Equals(
                input.LifecycleStatus,
                MigrationSessionLifecycleStatuses.Cancelled,
                StringComparison.Ordinal) ||
            string.Equals(
                input.EffectiveStatus,
                "cancelled",
                StringComparison.OrdinalIgnoreCase))
        {
            return input.HasPackageEvidence
                ? "cancelled-package-only"
                : "cancelled-before-upload";
        }

        if (string.Equals(
                input.LifecycleStatus,
                MigrationSessionLifecycleStatuses.Closed,
                StringComparison.Ordinal) &&
            string.Equals(
                input.EffectiveStatus,
                "expired",
                StringComparison.OrdinalIgnoreCase) &&
            !input.HasPackageEvidence)
        {
            return "expired-request";
        }

        return null;
    }

    private static MigrationSessionDeletionDecision Blocked(
        string code,
        string reason) =>
        new(
            CanDelete: false,
            BlockedCode: code,
            BlockedReason: reason,
            AuditReasonCode: null);
}
