using HostAgent.Runtime.Backups.Catalog;

namespace HostAgent.Tests.Runtime.Backups.Catalog;

public sealed class BackupCatalogLocalIntegrityEvaluatorTests
{
    [Fact]
    public void Assess_returns_valid_when_required_material_is_present_without_warnings()
    {
        var result = BackupCatalogLocalIntegrityEvaluator.Assess(
            databaseDumpPresent: true,
            homeserverConfigPresent: true,
            signingKeyPresent: true,
            warnings: []);

        Assert.Equal(BackupCatalogIntegrityStatuses.Valid, result.Status);
    }

    [Fact]
    public void Assess_returns_warning_when_required_material_is_present_with_warnings()
    {
        var result = BackupCatalogLocalIntegrityEvaluator.Assess(
            databaseDumpPresent: true,
            homeserverConfigPresent: true,
            signingKeyPresent: true,
            warnings: ["Element config.json is missing."]);

        Assert.Equal(BackupCatalogIntegrityStatuses.Warning, result.Status);
    }

    [Fact]
    public void Assess_returns_invalid_when_a_required_component_is_missing()
    {
        var result = BackupCatalogLocalIntegrityEvaluator.Assess(
            databaseDumpPresent: true,
            homeserverConfigPresent: false,
            signingKeyPresent: true,
            warnings: []);

        Assert.Equal(BackupCatalogIntegrityStatuses.Invalid, result.Status);
        Assert.Contains("homeserver configuration", result.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
