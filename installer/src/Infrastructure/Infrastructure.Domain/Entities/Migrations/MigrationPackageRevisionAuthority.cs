namespace Infrastructure.Data.Entities.Migrations;

/// <summary>
/// Canonical package authority rules for one Migration Session. Package evidence,
/// recipient material, validation metadata and transfer status are owned by package
/// revisions and are never mirrored on the Session root.
/// </summary>
public static class MigrationPackageRevisionAuthority
{
    public const string PreviewPurpose = "preview";
    public const string FinalPurpose = "final";
    public const string AwaitingPackageStatus = "awaiting-package";
    public const string ValidatedStatus = "package-validated";
    public const string ExpiredStatus = "expired";

    public static MigrationPackageRevisionEntity? ResolveAuthoritativeValidated(
        IEnumerable<MigrationPackageRevisionEntity> revisions)
    {
        var values = revisions.ToArray();
        var activeFinal = ResolveActivePurpose(values, FinalPurpose);
        if (activeFinal is not null)
        {
            if (activeFinal.Status == ValidatedStatus)
            {
                return IsValidatedFinal(activeFinal) ? activeFinal : null;
            }

            if (activeFinal.Status != AwaitingPackageStatus)
            {
                return null;
            }
        }

        return values
            .Where(revision =>
                revision.Purpose == PreviewPurpose &&
                revision.ActivePurposeKey is not null &&
                IsValidatedPreview(revision))
            .OrderByDescending(revision => revision.RevisionNumber)
            .ThenByDescending(revision => revision.CreatedAtUtc)
            .FirstOrDefault();
    }

    public static MigrationPackageRevisionEntity? ResolveCurrentPackage(
        IEnumerable<MigrationPackageRevisionEntity> revisions)
    {
        var values = revisions.ToArray();
        return ResolveAuthoritativeValidated(values) ??
               ResolveActivePurpose(values, FinalPurpose) ??
               ResolveActivePurpose(values, PreviewPurpose) ??
               values
                   .OrderByDescending(revision => revision.RevisionNumber)
                   .ThenByDescending(revision => revision.CreatedAtUtc)
                   .FirstOrDefault();
    }

    public static MigrationPackageRevisionEntity? ResolveAwaitingUpload(
        IEnumerable<MigrationPackageRevisionEntity> revisions) =>
        revisions
            .Where(revision =>
                revision.Status == AwaitingPackageStatus &&
                revision.ActivePurposeKey is not null)
            .OrderByDescending(revision => revision.RevisionNumber)
            .ThenByDescending(revision => revision.CreatedAtUtc)
            .FirstOrDefault();

    public static MigrationPackageRevisionEntity? ResolveActivePurpose(
        IEnumerable<MigrationPackageRevisionEntity> revisions,
        string purpose) =>
        revisions.SingleOrDefault(revision =>
            revision.Purpose == purpose &&
            revision.ActivePurposeKey is not null);

    public static bool IsValidatedPreview(MigrationPackageRevisionEntity revision) =>
        revision.Status == ValidatedStatus &&
        revision.CaptureKind == PreviewPurpose &&
        revision.SourceFrozen is false &&
        revision.RehearsalOnly is true &&
        !string.IsNullOrWhiteSpace(revision.DecryptedArchiveSha256);

    public static bool IsValidatedFinal(MigrationPackageRevisionEntity revision) =>
        revision.Status == ValidatedStatus &&
        revision.CaptureKind == FinalPurpose &&
        revision.SourceFrozen is true &&
        revision.RehearsalOnly is false &&
        !string.IsNullOrWhiteSpace(revision.DecryptedArchiveSha256);

    public static string ResolveEffectiveSessionStatus(
        MigrationIntakeEntity intake,
        DateTime nowUtc)
    {
        if (string.Equals(
                intake.LifecycleStatus,
                MigrationSessionLifecycleStatuses.Cancelled,
                StringComparison.Ordinal) ||
            intake.CancelledAtUtc.HasValue)
        {
            return "cancelled";
        }

        var revision = ResolveCurrentPackage(intake.PackageRevisions);
        return revision is null
            ? "package-unavailable"
            : ResolveEffectiveRevisionStatus(revision, nowUtc);
    }

    public static string ResolveEffectiveRevisionStatus(
        MigrationPackageRevisionEntity revision,
        DateTime nowUtc) =>
        revision.Status == AwaitingPackageStatus &&
        revision.ExpiresAtUtc is { } expiresAtUtc &&
        expiresAtUtc <= nowUtc
            ? "expired"
            : revision.Status;

    public static bool IsSecureSession(MigrationIntakeEntity intake) =>
        intake.PackageRevisions.Count > 0;
}
