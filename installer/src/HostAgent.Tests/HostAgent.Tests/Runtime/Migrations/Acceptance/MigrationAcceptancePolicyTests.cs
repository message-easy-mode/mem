using HostAgent.Runtime.Migrations.Acceptance;

namespace HostAgent.Tests.Runtime.Migrations.Acceptance;

public sealed class MigrationAcceptancePolicyTests
{
    [Theory]
    [InlineData(6)]
    [InlineData(31)]
    public void Retention_outside_supported_range_is_rejected(int retentionDays)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MigrationAcceptancePolicy.ValidateRequest(ValidRequest(retentionDays)));
        Assert.Contains("between 7 and 30 days", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_acceptance_acknowledgement_is_required()
    {
        var request = ValidRequest(14) with { AcknowledgeNoAutomaticLegacyDeletion = false };
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MigrationAcceptancePolicy.ValidateRequest(request));
        Assert.Contains("Every acceptance acknowledgement", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Valid_acceptance_decisions_are_allowed() =>
        MigrationAcceptancePolicy.ValidateRequest(ValidRequest(14));

    private static AcceptMigrationRequest ValidRequest(int retentionDays) => new(
        Note: null,
        RetentionDays: retentionDays,
        AcknowledgeFreshPublicVerification: true,
        AcknowledgeTargetWriteDivergence: true,
        AcknowledgeRollbackBoundaryChanges: true,
        AcknowledgeLegacySourceResourcesRetained: true,
        AcknowledgeNoAutomaticLegacyDeletion: true);
}
