using HostAgent.Runtime.Migrations.ProductionAdoption;

namespace HostAgent.Tests.Runtime.Migrations.ProductionAdoption;

public sealed class MigrationProductionRuntimeMaterializationTests
{
    [Fact]
    public void Every_private_materialization_acknowledgement_is_required()
    {
        var missing = MigrationProductionRuntimeMaterializationService.GetMissingAcknowledgements(
            new MaterializeMigrationProductionRuntimeRequest(
                Operator: null,
                Note: null,
                ExecutePrivateProductionMaterialization: false,
                AcknowledgeCreatesNormalRuntimeRecords: false,
                AcknowledgeMutatesProductionPostgres: false,
                AcknowledgeStartsProductionContainers: false,
                AcknowledgeNoPublicRoutes: false,
                AcknowledgeNoAutomaticRollback: false));

        Assert.Equal(
            [
                "executePrivateProductionMaterialization",
                "acknowledgeCreatesNormalRuntimeRecords",
                "acknowledgeMutatesProductionPostgres",
                "acknowledgeStartsProductionContainers",
                "acknowledgeNoPublicRoutes",
                "acknowledgeNoAutomaticRollback",
            ],
            missing);
    }

    [Fact]
    public void Complete_private_materialization_confirmation_has_no_missing_acknowledgements()
    {
        var missing = MigrationProductionRuntimeMaterializationService.GetMissingAcknowledgements(
            new MaterializeMigrationProductionRuntimeRequest(
                Operator: "operator",
                Note: "Materialise privately before public cutover.",
                ExecutePrivateProductionMaterialization: true,
                AcknowledgeCreatesNormalRuntimeRecords: true,
                AcknowledgeMutatesProductionPostgres: true,
                AcknowledgeStartsProductionContainers: true,
                AcknowledgeNoPublicRoutes: true,
                AcknowledgeNoAutomaticRollback: true));

        Assert.Empty(missing);
    }
    [Fact]
    public void Cancelled_materialization_is_classified_as_a_durable_private_failure()
    {
        var failure = MigrationProductionRuntimeMaterializationService.ClassifyFailure(
            new OperationCanceledException());

        Assert.Equal("migration_production_materialization_cancelled", failure.Code);
        Assert.Contains("No public route was created", failure.Summary, StringComparison.Ordinal);
    }

}
