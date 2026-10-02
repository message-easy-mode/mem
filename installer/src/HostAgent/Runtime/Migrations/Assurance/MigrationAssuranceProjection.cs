using Infrastructure.Data.Entities.Migrations;

namespace HostAgent.Runtime.Migrations.Assurance;

public sealed record MigrationAssuranceSummary(
    string AuthorityType,
    string? ProductionAuthorityId,
    string? AuthorityEvidenceSha256,
    string PackageRevisionId,
    string CaptureKind,
    bool SourceFrozen,
    bool RehearsalOnly,
    bool FormalSourceFreezeEvidenceCollected,
    bool FinalRecapturePerformed,
    bool PostCaptureWritesIndependentlyExcluded,
    string RollbackAssurance,
    bool TwoServerQualificationRequired,
    string Detail);

public static class MigrationAssuranceProjection
{
    public const string CoordinatedRollback = "coordinated";
    public const string ReducedRollback = "reduced";

    public static MigrationAssuranceSummary Resolve(
        MigrationIntakeEntity intake,
        MigrationProductionAdoptionEntity? plan = null)
    {
        ArgumentNullException.ThrowIfNull(intake);
        plan ??= intake.ProductionAdoption
            ?? throw new InvalidOperationException(
                "Migration assurance cannot be resolved before production adoption is prepared.");

        var revision = plan.PackageRevision
            ?? throw new InvalidOperationException(
                "Migration assurance cannot be resolved because the production package revision is missing.");
        var authority = intake.ProductionAuthorities.SingleOrDefault(item =>
            string.Equals(item.Status, MigrationProductionAuthorityStatuses.Active, StringComparison.Ordinal) &&
            string.Equals(item.ActiveMigrationKey, intake.IntakeId, StringComparison.Ordinal));

        if (authority is not null)
        {
            if (!MigrationProductionAuthorityTypes.IsKnown(authority.AuthorityType) ||
                authority.MigrationPackageRevisionEntityId != plan.MigrationPackageRevisionEntityId ||
                authority.MigrationCandidateArtifactEntityId != plan.MigrationCandidateArtifactEntityId ||
                authority.MigrationStagingRunEntityId != plan.MigrationStagingRunEntityId ||
                !string.Equals(authority.PackageRevision.PackageRevisionId, revision.PackageRevisionId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The active production authority no longer matches the production adoption artifact chain.");
            }

            return authority.AuthorityType switch
            {
                MigrationProductionAuthorityTypes.OperatorAttestedSnapshot =>
                    new MigrationAssuranceSummary(
                        authority.AuthorityType,
                        authority.ProductionAuthorityId,
                        authority.EvidenceSha256,
                        revision.PackageRevisionId,
                        authority.CaptureKind,
                        authority.SourceFrozen,
                        authority.RehearsalOnly,
                        FormalSourceFreezeEvidenceCollected: false,
                        FinalRecapturePerformed: false,
                        PostCaptureWritesIndependentlyExcluded: false,
                        RollbackAssurance: ReducedRollback,
                        TwoServerQualificationRequired: false,
                        Detail: "Simplified assurance: production is authorized by an operator-attested verified snapshot. Formal source-freeze evidence and a final recapture were not collected; post-capture source writes were not independently excluded and rollback assurance is reduced."),
                MigrationProductionAuthorityTypes.FinalFrozen =>
                    BuildFinalFrozen(authority, revision),
                _ => throw new InvalidDataException(
                    $"Unknown production authority type '{authority.AuthorityType}'."),
            };
        }

        if (IsFinalFrozenRevision(revision))
        {
            return new MigrationAssuranceSummary(
                MigrationProductionAuthorityTypes.FinalFrozen,
                ProductionAuthorityId: null,
                AuthorityEvidenceSha256: null,
                revision.PackageRevisionId,
                revision.CaptureKind!,
                SourceFrozen: true,
                RehearsalOnly: false,
                FormalSourceFreezeEvidenceCollected: true,
                FinalRecapturePerformed: true,
                PostCaptureWritesIndependentlyExcluded: true,
                RollbackAssurance: CoordinatedRollback,
                TwoServerQualificationRequired: true,
                Detail: "High assurance: production is bound to a validated final frozen recapture with coordinated source rollback and two-server qualification evidence.");
        }

        throw new InvalidDataException(
            "The production adoption is not backed by a recognized production authority.");
    }

    public static bool IsSimplified(MigrationAssuranceSummary assurance) =>
        string.Equals(
            assurance.AuthorityType,
            MigrationProductionAuthorityTypes.OperatorAttestedSnapshot,
            StringComparison.Ordinal);

    private static MigrationAssuranceSummary BuildFinalFrozen(
        MigrationProductionAuthorityEntity authority,
        MigrationPackageRevisionEntity revision)
    {
        if (!IsFinalFrozenRevision(revision) ||
            !authority.SourceFrozen ||
            authority.RehearsalOnly)
        {
            throw new InvalidDataException(
                "Final-frozen production authority does not retain final frozen capture semantics.");
        }

        return new MigrationAssuranceSummary(
            authority.AuthorityType,
            authority.ProductionAuthorityId,
            authority.EvidenceSha256,
            revision.PackageRevisionId,
            authority.CaptureKind,
            SourceFrozen: true,
            RehearsalOnly: false,
            FormalSourceFreezeEvidenceCollected: true,
            FinalRecapturePerformed: true,
            PostCaptureWritesIndependentlyExcluded: true,
            RollbackAssurance: CoordinatedRollback,
            TwoServerQualificationRequired: true,
            Detail: "High assurance: production is bound to a validated final frozen recapture with coordinated source rollback and two-server qualification evidence.");
    }

    private static bool IsFinalFrozenRevision(MigrationPackageRevisionEntity revision) =>
        string.Equals(revision.Purpose, "final", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(revision.Status, "package-validated", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(revision.CaptureKind, "final", StringComparison.OrdinalIgnoreCase) &&
        revision.SourceFrozen == true &&
        revision.RehearsalOnly == false;
}
