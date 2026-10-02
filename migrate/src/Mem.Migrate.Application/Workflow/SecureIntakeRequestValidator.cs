using Mem.Migrate.Core.Capture;

namespace Mem.Migrate.Application.Workflow;

public sealed class SecureIntakeRequestValidator(TimeProvider? timeProvider = null)
{
    public const string Schema = "mem-secure-intake-request";
    public const int SchemaVersion = 1;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public SecureIntakeRequest Validate(SecureIntakeRequestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var schema = input.Schema?.Trim() ?? string.Empty;
        if (!string.Equals(schema, Schema, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Migration request schema must be '{Schema}'.");
        }

        if (input.SchemaVersion != SchemaVersion)
        {
            throw new ArgumentException(
                $"Migration request schema version {input.SchemaVersion} is not supported.");
        }

        var requestKind = input.RequestKind?.Trim().ToLowerInvariant() ?? string.Empty;
        if (requestKind is not ("preview" or "final"))
        {
            throw new ArgumentException(
                "Migration request kind must be preview or final.");
        }

        var intakeId = input.IntakeId?.Trim() ?? string.Empty;
        if (!CaptureIdentifier.IsValid(intakeId))
        {
            throw new ArgumentException(
                "Migration request Intake ID contains unsupported characters.");
        }

        var packageRevisionId = string.IsNullOrWhiteSpace(input.PackageRevisionId)
            ? null
            : input.PackageRevisionId.Trim();
        if (requestKind == "final" &&
            (packageRevisionId is null || !CaptureIdentifier.IsValid(packageRevisionId)))
        {
            throw new ArgumentException(
                "A valid package revision ID is required for a final request.");
        }

        if (packageRevisionId is not null &&
            !CaptureIdentifier.IsValid(packageRevisionId))
        {
            throw new ArgumentException(
                "Migration request package revision ID contains unsupported characters.");
        }

        var recipient = input.AgeRecipient?.Trim() ?? string.Empty;
        if (!IsNativeAgeRecipient(recipient))
        {
            throw new ArgumentException(
                "Migration request age recipient must be one native X25519 recipient beginning with age1.");
        }

        var fingerprint = input.RecipientFingerprint?.Trim().ToUpperInvariant() ?? string.Empty;
        var calculatedFingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(recipient);
        if (!string.Equals(calculatedFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Recipient fingerprint mismatch. The request declares {fingerprint}; " +
                $"the age recipient calculates to {calculatedFingerprint}.");
        }

        var expiresAtUtc = input.ExpiresAtUtc
            ?? throw new ArgumentException(
                "Migration request expiry is required.");
        if (expiresAtUtc <= _timeProvider.GetUtcNow())
        {
            throw new ArgumentException(
                "The migration request has expired. Create a fresh request on the target MEM server.");
        }

        var targetVersion = input.TargetControlPlaneVersion?.Trim() ?? string.Empty;
        if (targetVersion.Length is < 1 or > 64)
        {
            throw new ArgumentException(
                "Target control-plane version is required.");
        }

        var sourceStackId = string.IsNullOrWhiteSpace(input.SourceStackId)
            ? null
            : input.SourceStackId.Trim();
        if (sourceStackId is not null && !Guid.TryParse(sourceStackId, out _))
        {
            throw new ArgumentException(
                "Expected source stack ID must be a GUID when supplied.");
        }

        return new SecureIntakeRequest(
            schema,
            SchemaVersion,
            intakeId,
            packageRevisionId,
            requestKind,
            recipient,
            fingerprint,
            expiresAtUtc,
            targetVersion,
            sourceStackId);
    }

    private static bool IsNativeAgeRecipient(string value) =>
        value.Length == 62 &&
        value.StartsWith("age1", StringComparison.Ordinal) &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) &&
            !char.IsAsciiLetterUpper(character));

}
