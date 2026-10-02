using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Modules.Operator.Migrations;

/// <summary>
/// Creates or resumes the target-bound public recipient for the immutable final package revision
/// of an existing Migration Session. The private age identity remains protected server-side and
/// is never returned through browser contracts.
/// </summary>
public sealed class MigrationFinalPackageRecipientService(
    MemDbContext db,
    IAgeKeyPairGenerator ageKeyPairGenerator,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider)
{
    private const int LifetimeHours = 24;
    private const string FinalPurpose = "final";
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        SecureMigrationIntakeService.ProtectorPurpose);

    public async Task<MigrationFinalPackageRecipientDto> CreateOrResumeAsync(
        string migrationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            throw new MigrationFinalPackageRecipientException(
                "migration_session_not_found",
                "Migration session was not found.",
                StatusCodes.Status404NotFound);
        }

        var intake = await db.MigrationIntakes
            .Include(x => x.PackageRevisions)
            .Include(x => x.StagingRuns)
            .Include(x => x.Acceptance)
            .SingleOrDefaultAsync(x => x.IntakeId == migrationId, cancellationToken)
            ?? throw new MigrationFinalPackageRecipientException(
                "migration_session_not_found",
                "Migration session was not found.",
                StatusCodes.Status404NotFound);

        ValidateSession(intake);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var activeFinal = intake.PackageRevisions
            .SingleOrDefault(x =>
                x.ActivePurposeKey == SecureMigrationIntakeService.CreateActivePurposeKey(
                    migrationId,
                    FinalPurpose));

        if (activeFinal is not null)
        {
            if (activeFinal.Status == "awaiting-package" &&
                activeFinal.ExpiresAtUtc is { } expiresAtUtc &&
                expiresAtUtc > now &&
                !string.IsNullOrWhiteSpace(activeFinal.AgeRecipient) &&
                !string.IsNullOrWhiteSpace(activeFinal.RecipientFingerprint) &&
                !string.IsNullOrWhiteSpace(activeFinal.ProtectedAgeIdentity))
            {
                return ToDto(intake, activeFinal, resumedExisting: true);
            }

            if (activeFinal.Status == "awaiting-package" &&
                activeFinal.ExpiresAtUtc is { } expiredAtUtc &&
                expiredAtUtc <= now)
            {
                RetireExpired(activeFinal, now);
            }
            else
            {
                throw new MigrationFinalPackageRecipientException(
                    "final_package_revision_already_active",
                    $"A final package revision is already active with status '{activeFinal.Status}'.",
                    StatusCodes.Status409Conflict);
            }
        }

        var keyPair = await ageKeyPairGenerator.GenerateAsync(cancellationToken);
        var protectedIdentity = _protector.Protect(keyPair.Identity);
        var revisionNumber = intake.PackageRevisions.Count == 0
            ? 1
            : intake.PackageRevisions.Max(x => x.RevisionNumber) + 1;
        var packageRevisionId = $"mpr_{now:yyyyMMdd-HHmmssZ}_{Guid.NewGuid():N}"[..40];
        var revision = new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = packageRevisionId,
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            RevisionNumber = revisionNumber,
            Purpose = FinalPurpose,
            Status = "awaiting-package",
            RetentionState = "active",
            ActivePurposeKey = SecureMigrationIntakeService.CreateActivePurposeKey(
                migrationId,
                FinalPurpose),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(LifetimeHours),
            AgeRecipient = keyPair.Recipient,
            ProtectedAgeIdentity = protectedIdentity,
            RecipientFingerprint = SecureMigrationIntakeService.CreateFingerprint(
                keyPair.Recipient),
        };

        // The revision uses a preassigned durable GUID. Add it explicitly through the DbSet so
        // EF marks it Added rather than inferring an update from relationship discovery.
        db.MigrationPackageRevisions.Add(revision);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new MigrationFinalPackageRecipientException(
                "final_package_recipient_conflict",
                "A final package recipient was created concurrently. Refresh the Migration Session and use the active recipient.",
                StatusCodes.Status409Conflict,
                exception);
        }

        return ToDto(intake, revision, resumedExisting: false);
    }

    private static void ValidateSession(MigrationIntakeEntity intake)
    {
        if (intake.Acceptance is not null)
        {
            throw new MigrationFinalPackageRecipientException(
                "final_package_recipient_after_acceptance_not_allowed",
                "A final package recipient cannot be created after Migration Acceptance.",
                StatusCodes.Status409Conflict);
        }

        var preview = intake.PackageRevisions
            .Where(x =>
                x.Purpose == "preview" &&
                x.Status == "package-validated" &&
                x.ActivePurposeKey is not null)
            .OrderByDescending(x => x.RevisionNumber)
            .FirstOrDefault();

        if (preview is null ||
            preview.CaptureKind != "preview" ||
            preview.SourceFrozen is not false ||
            preview.RehearsalOnly is not true ||
            string.IsNullOrWhiteSpace(preview.DecryptedArchiveSha256))
        {
            throw new MigrationFinalPackageRecipientException(
                "final_package_preview_evidence_required",
                "A validated preview package revision is required before the final package recipient can be created.",
                StatusCodes.Status409Conflict);
        }

        var rehearsalVerified = intake.StagingRuns.Any(run =>
            run.Status == "verified" &&
            run.PrivateOnly &&
            !run.PublicRoutesCreated &&
            run.DatabaseImportSucceeded &&
            run.SynapseHealthPassed);

        if (!rehearsalVerified)
        {
            throw new MigrationFinalPackageRecipientException(
                "final_package_rehearsal_required",
                "A verified private rehearsal is required before the final package recipient can be created.",
                StatusCodes.Status409Conflict);
        }
    }

    private static void RetireExpired(MigrationPackageRevisionEntity revision, DateTime now)
    {
        revision.Status = "expired";
        revision.RetentionState = "retired";
        revision.ActivePurposeKey = null;
        revision.RetiredAtUtc = now;
        revision.ProtectedAgeIdentity = null;
    }

    private static MigrationFinalPackageRecipientDto ToDto(
        MigrationIntakeEntity intake,
        MigrationPackageRevisionEntity revision,
        bool resumedExisting) =>
        new(
            intake.IntakeId,
            revision.PackageRevisionId,
            revision.RevisionNumber,
            revision.Purpose,
            revision.Status,
            revision.AgeRecipient!,
            revision.RecipientFingerprint!,
            revision.CreatedAtUtc,
            revision.ExpiresAtUtc!.Value,
            resumedExisting);
}

public sealed record MigrationFinalPackageRecipientDto(
    string MigrationId,
    string PackageRevisionId,
    int RevisionNumber,
    string Purpose,
    string Status,
    string AgeRecipient,
    string RecipientFingerprint,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    bool ResumedExisting);

public sealed class MigrationFinalPackageRecipientException : Exception
{
    public MigrationFinalPackageRecipientException(
        string code,
        string message,
        int statusCode,
        Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}
