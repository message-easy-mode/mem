using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;

namespace HostAgent.Runtime.Backups.Catalog;

/// <summary>
/// Separates a valid imported archive's structural/integrity outcome from
/// operator guidance that is expected for portable Matrix recovery material.
/// A successful import may carry security, safety, provenance, or metadata
/// advisories without being an integrity warning.
/// </summary>
public static class ImportedZipCatalogIntegrityClassifier
{
    public static ImportedZipCatalogIntegrityPresentation? Classify(
        ImportValidationResponse? validation)
    {
        if (validation is null ||
            !string.Equals(validation.Status, "valid", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var advisories = new List<BackupCatalogAdvisory>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var warning in validation.Warnings)
        {
            if (string.IsNullOrWhiteSpace(warning))
            {
                continue;
            }

            var advisory = ClassifyWarning(warning);
            var key = $"{advisory.Category}\n{advisory.Title}";

            if (seen.Add(key))
            {
                advisories.Add(advisory);
            }
        }

        var summary = advisories.Count == 0
            ? "Structural, manifest, and checksum validation passed."
            : $"Structural, manifest, and checksum validation passed. {advisories.Count} non-blocking import {Pluralize(advisories.Count, "advisory", "advisories")} retained for operator review.";

        return new ImportedZipCatalogIntegrityPresentation(
            IntegrityStatus: BackupCatalogIntegrityStatuses.Valid,
            IntegritySummary: summary,
            IntegrityWarningCount: 0,
            Advisories: advisories);
    }

    private static BackupCatalogAdvisory ClassifyWarning(string rawWarning)
    {
        var warning = StripManifestPrefix(rawWarning);
        var normalized = warning.ToLowerInvariant();

        if (normalized.Contains("signing identity"))
        {
            return new BackupCatalogAdvisory(
                Category: "security",
                Title: "Matrix signing identity included",
                Message: "This backup contains Matrix signing identity material. Store and transfer the archive securely.");
        }

        if (normalized.Contains("old public homeserver") ||
            normalized.Contains("same matrix server name"))
        {
            return new BackupCatalogAdvisory(
                Category: "operational-safety",
                Title: "Original Matrix server must be offline",
                Message: "Do not run two public homeservers with the same Matrix server name at the same time. Retire the original public route before recovering that identity.");
        }

        if (normalized.Contains("turn settings") ||
            normalized.Contains("turn metadata"))
        {
            return new BackupCatalogAdvisory(
                Category: "configuration",
                Title: "TURN metadata is incomplete",
                Message: "homeserver.yaml contains TURN settings, but the portable manifest does not describe complete TURN metadata. MEM preserves the homeserver TURN block; verify calling separately if TURN is required.");
        }

        if (normalized.Contains("regenerated from the managed backup catalog payload"))
        {
            return new BackupCatalogAdvisory(
                Category: "provenance",
                Title: "Regenerated portable export",
                Message: "This ZIP was regenerated from managed Backup Catalog material. It does not depend on retaining the original uploaded ZIP archive.");
        }

        if (normalized.Contains("does not include stack.matrixservername"))
        {
            return new BackupCatalogAdvisory(
                Category: "metadata",
                Title: "Manifest Matrix identity is incomplete",
                Message: "The portable manifest does not record stack.matrixServerName. MEM will use recoverable homeserver configuration where available and will block a restore if identity cannot be resolved safely.");
        }

        if (normalized.Contains("does not use a .zip extension"))
        {
            return new BackupCatalogAdvisory(
                Category: "format",
                Title: "Non-standard file extension",
                Message: "The uploaded archive did not use a .zip extension. Validation still completed successfully.");
        }

        if (normalized.Contains("source backup catalog entry carries") &&
            normalized.Contains("integrity warning"))
        {
            return new BackupCatalogAdvisory(
                Category: "provenance",
                Title: "Source catalog history retained",
                Message: "The portable export retained advisory history from its source Backup Catalog entry. Review the source history if additional context is needed.");
        }

        return new BackupCatalogAdvisory(
            Category: "operator-review",
            Title: "Import advisory",
            Message: warning.Trim());
    }

    private static string StripManifestPrefix(string value)
    {
        const string prefix = "Manifest warning:";
        var trimmed = value.Trim();

        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..].Trim()
            : trimmed;
    }

    private static string Pluralize(int value, string singular, string plural) =>
        value == 1 ? singular : plural;
}

public sealed record ImportedZipCatalogIntegrityPresentation(
    string IntegrityStatus,
    string IntegritySummary,
    int IntegrityWarningCount,
    IReadOnlyList<BackupCatalogAdvisory> Advisories);
