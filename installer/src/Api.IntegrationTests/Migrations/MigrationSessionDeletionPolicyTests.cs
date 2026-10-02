using Infrastructure.Data.Entities.Migrations;
using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationSessionDeletionPolicyTests
{
    [Theory]
    [MemberData(nameof(EligibleCases))]
    public void Explicit_early_categories_are_deletable(
        object inputValue,
        string expectedAuditReasonCode)
    {
        var input = Assert.IsType<MigrationSessionDeletionPolicyInput>(inputValue);
        var decision = MigrationSessionDeletionPolicy.Evaluate(input);

        Assert.True(decision.CanDelete);
        Assert.Null(decision.BlockedCode);
        Assert.Null(decision.BlockedReason);
        Assert.Equal(expectedAuditReasonCode, decision.AuditReasonCode);
    }

    [Theory]
    [MemberData(nameof(BlockedCases))]
    public void Resource_owned_or_unverifiable_states_fail_closed(
        object inputValue,
        string expectedBlockedCode)
    {
        var input = Assert.IsType<MigrationSessionDeletionPolicyInput>(inputValue);
        var decision = MigrationSessionDeletionPolicy.Evaluate(input);

        Assert.False(decision.CanDelete);
        Assert.Equal(expectedBlockedCode, decision.BlockedCode);
        Assert.NotEmpty(decision.BlockedReason);
        Assert.Null(decision.AuditReasonCode);
    }

    public static IEnumerable<object[]> EligibleCases()
    {
        yield return Case(
            CancelledBeforeUpload() with
            {
                // Secure Sessions create a revision request before upload. The
                // absence of package evidence, not the absence of a revision,
                // defines this disposable category.
                HasPackageRevision = true,
            },
            "cancelled-before-upload");
        yield return Case(
            CancelledBeforeUpload() with
            {
                HasPackageRevision = true,
                HasPackageEvidence = true,
                TargetPackageMaterialPresent = true,
            },
            "cancelled-package-only");
        yield return Case(
            CancelledBeforeUpload() with
            {
                LifecycleStatus = MigrationSessionLifecycleStatuses.Closed,
                EffectiveStatus = "expired",
                ClosureKind = "expired",
                HasPackageRevision = true,
            },
            "expired-request");
    }

    public static IEnumerable<object[]> BlockedCases()
    {
        var eligible = CancelledBeforeUpload();

        yield return Blocked(
            eligible with { HasAcceptance = true },
            "migration_session_completed");
        yield return Blocked(
            eligible with { HasBaselineBackup = true },
            "migration_session_completed");
        yield return Blocked(
            eligible with
            {
                LifecycleStatus = MigrationSessionLifecycleStatuses.Completed,
            },
            "migration_session_completed");
        yield return Blocked(
            eligible with { EffectiveStatus = "accepted" },
            "migration_session_completed");
        yield return Blocked(
            eligible with { HasProductionAuthority = true },
            "migration_session_production_owned");
        yield return Blocked(
            eligible with { HasProductionAdoption = true },
            "migration_session_production_owned");
        yield return Blocked(
            eligible with { HasTwoServerQualification = true },
            "migration_session_two_server_qualification");
        yield return Blocked(
            eligible with { HasLegacyRetention = true },
            "migration_session_legacy_retention");
        yield return Blocked(
            eligible with { HasStaging = true },
            "migration_session_staging_exists");
        yield return Blocked(
            eligible with { HasConversion = true },
            "migration_session_conversion_exists");
        yield return Blocked(
            eligible with
            {
                LifecycleStatus = MigrationSessionLifecycleStatuses.Active,
                EffectiveStatus = "awaiting-package",
            },
            "migration_session_active");
        yield return Blocked(
            eligible with
            {
                HasPackageRevision = true,
                HasActivePackageAuthority = true,
            },
            "migration_session_package_authority_active");
        yield return Blocked(
            eligible with
            {
                HasPackageRevision = false,
                HasPackageEvidence = true,
            },
            "migration_session_package_evidence_inconsistent");
        yield return Blocked(
            eligible with
            {
                LifecycleStatus = MigrationSessionLifecycleStatuses.Closed,
                EffectiveStatus = "closed",
                ClosureKind = "operator-closed",
            },
            "migration_session_lifecycle_not_disposable");
        yield return Blocked(
            eligible with { TargetStorageVerified = false },
            "migration_session_target_state_unverifiable");
        yield return Blocked(
            eligible with { TargetPackageMaterialPresent = true },
            "migration_session_unexpected_package_material");
        yield return Blocked(
            eligible with
            {
                HasPackageRevision = true,
                HasPackageEvidence = true,
                TargetPackageMaterialPresent = true,
                TargetDecryptedPackageMaterialPresent = true,
            },
            "migration_session_plaintext_package_present");
    }

    private static MigrationSessionDeletionPolicyInput CancelledBeforeUpload() =>
        new(
            LifecycleStatus: MigrationSessionLifecycleStatuses.Cancelled,
            EffectiveStatus: "cancelled",
            ClosureKind: "operator-cancelled",
            HasPackageRevision: false,
            HasPackageEvidence: false,
            HasActivePackageAuthority: false,
            HasConversion: false,
            HasStaging: false,
            HasProductionAuthority: false,
            HasProductionAdoption: false,
            HasAcceptance: false,
            HasBaselineBackup: false,
            HasLegacyRetention: false,
            HasTwoServerQualification: false,
            TargetStorageVerified: true,
            TargetPackageMaterialPresent: false,
            TargetDecryptedPackageMaterialPresent: false);

    private static object[] Case(
        MigrationSessionDeletionPolicyInput input,
        string expectedCode) =>
        [input, expectedCode];

    private static object[] Blocked(
        MigrationSessionDeletionPolicyInput input,
        string expectedCode) =>
        [input, expectedCode];
}
