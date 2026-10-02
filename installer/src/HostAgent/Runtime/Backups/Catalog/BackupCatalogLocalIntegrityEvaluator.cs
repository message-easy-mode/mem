namespace HostAgent.Runtime.Backups.Catalog;

/// <summary>
/// Shared integrity policy for local backup registration. The initial catalog
/// treats the database dump, homeserver config, and signing key as required
/// recovery material. Element configuration and media warnings remain visible,
/// but do not by themselves make a backup invalid.
/// </summary>
public static class BackupCatalogLocalIntegrityEvaluator
{
    public static BackupCatalogIntegrityAssessment Assess(
        bool databaseDumpPresent,
        bool homeserverConfigPresent,
        bool signingKeyPresent,
        IEnumerable<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);

        var missing = new List<string>();

        if (!databaseDumpPresent)
        {
            missing.Add("database dump");
        }

        if (!homeserverConfigPresent)
        {
            missing.Add("homeserver configuration");
        }

        if (!signingKeyPresent)
        {
            missing.Add("Matrix signing key");
        }

        if (missing.Count > 0)
        {
            return new BackupCatalogIntegrityAssessment(
                BackupCatalogIntegrityStatuses.Invalid,
                "Required backup material is missing: " + string.Join(", ", missing) + ".");
        }

        var warningCount = warnings.Count(static warning =>
            !string.IsNullOrWhiteSpace(warning));

        if (warningCount > 0)
        {
            return new BackupCatalogIntegrityAssessment(
                BackupCatalogIntegrityStatuses.Warning,
                $"Required backup material is present; {warningCount} warning(s) were recorded.");
        }

        return new BackupCatalogIntegrityAssessment(
            BackupCatalogIntegrityStatuses.Valid,
            "Required backup material is present.");
    }
}
