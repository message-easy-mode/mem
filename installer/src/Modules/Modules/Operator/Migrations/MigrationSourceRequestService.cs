using System.Text;
using System.Text.Json;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Modules.Operator.Migrations;

public sealed class MigrationSourceRequestService(MemDbContext db)
{
    internal const string RequestSchema = "mem-secure-intake-request";
    internal const int RequestSchemaVersion = 1;
    internal const string TargetControlPlaneVersion = "0.2.0";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task<MigrationSourceRequestFile> CreateAsync(
        string migrationId,
        string purpose,
        CancellationToken cancellationToken)
    {
        var normalizedPurpose = purpose.Trim().ToLowerInvariant();
        if (normalizedPurpose is not ("preview" or "final"))
        {
            throw new MigrationSourceRequestException(
                "migration_source_request_purpose_invalid",
                "Migration source request purpose must be preview or final.",
                StatusCodes.Status400BadRequest);
        }

        var intake = await db.MigrationIntakes
            .AsNoTracking()
            .Include(x => x.PackageRevisions)
            .SingleOrDefaultAsync(
                x => x.IntakeId == migrationId,
                cancellationToken);
        if (intake is null)
        {
            throw new MigrationSourceRequestException(
                "migration_session_not_found",
                "Migration session was not found.",
                StatusCodes.Status404NotFound);
        }

        var revision = ResolveActiveRevision(intake, normalizedPurpose);
        if (revision is null)
        {
            throw new MigrationSourceRequestException(
                "migration_source_request_revision_not_available",
                $"No active {normalizedPurpose} package revision is available for this Migration Session.",
                StatusCodes.Status409Conflict);
        }

        var now = DateTime.UtcNow;
        if (revision.ExpiresAtUtc is not { } expiresAtUtc || expiresAtUtc <= now)
        {
            throw new MigrationSourceRequestException(
                "migration_source_request_expired",
                $"The active {normalizedPurpose} package recipient has expired. Create a replacement recipient before downloading a new source request.",
                StatusCodes.Status409Conflict);
        }

        if (!string.Equals(revision.Status, "awaiting-package", StringComparison.Ordinal))
        {
            throw new MigrationSourceRequestException(
                "migration_source_request_not_awaiting_package",
                $"The active {normalizedPurpose} package revision is '{revision.Status}' and no longer accepts a source package.",
                StatusCodes.Status409Conflict);
        }

        if (string.IsNullOrWhiteSpace(revision.AgeRecipient) ||
            string.IsNullOrWhiteSpace(revision.RecipientFingerprint))
        {
            throw new MigrationSourceRequestException(
                "migration_source_request_recipient_incomplete",
                "The active package revision does not contain complete target encryption recipient evidence.",
                StatusCodes.Status409Conflict);
        }

        var request = new MigrationSourceRequestDto(
            RequestSchema,
            RequestSchemaVersion,
            intake.IntakeId,
            revision.PackageRevisionId,
            normalizedPurpose,
            revision.AgeRecipient,
            revision.RecipientFingerprint,
            new DateTimeOffset(NormalizeUtc(expiresAtUtc)),
            TargetControlPlaneVersion,
            SourceStackId: null);

        var json = JsonSerializer.Serialize(request, JsonOptions) + "\n";
        var fileName = $"mem-migration-request-{SafeFileSegment(intake.IntakeId)}-{normalizedPurpose}.json";
        return new MigrationSourceRequestFile(
            fileName,
            Encoding.UTF8.GetBytes(json),
            request);
    }

    private static MigrationPackageRevisionEntity? ResolveActiveRevision(
        Infrastructure.Data.Entities.Migrations.MigrationIntakeEntity intake,
        string purpose) =>
        intake.PackageRevisions
            .Where(revision =>
                string.Equals(revision.Purpose, purpose, StringComparison.Ordinal) &&
                revision.ActivePurposeKey is not null)
            .OrderByDescending(revision => revision.RevisionNumber)
            .ThenByDescending(revision => revision.CreatedAtUtc)
            .FirstOrDefault();

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string SafeFileSegment(string value) =>
        string.Concat(value.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-'));
}
