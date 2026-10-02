using HostAgent.Runtime.Migrations.Acceptance;

namespace HostAgent.Tests.Runtime.Migrations.Acceptance;

public sealed class MigrationFinishTests
{
    [Fact]
    public void User_level_confirmations_are_all_required()
    {
        var request = ValidRequest() with
        {
            ConfirmRecoveryBoundaryChanges = false,
        };

        Assert.Equal(
            ["confirmRecoveryBoundaryChanges"],
            MigrationFinishService.GetMissingConfirmations(request));
    }

    [Fact]
    public void Finish_request_maps_to_the_existing_acceptance_safety_contract()
    {
        var mapped = MigrationFinishService.BuildAcceptanceRequest(ValidRequest(21));

        Assert.Equal(21, mapped.RetentionDays);
        Assert.Null(mapped.Note);
        Assert.True(mapped.AcknowledgeFreshPublicVerification);
        Assert.True(mapped.AcknowledgeTargetWriteDivergence);
        Assert.True(mapped.AcknowledgeRollbackBoundaryChanges);
        Assert.True(mapped.AcknowledgeLegacySourceResourcesRetained);
        Assert.True(mapped.AcknowledgeNoAutomaticLegacyDeletion);
        MigrationAcceptancePolicy.ValidateRequest(mapped);
    }

    [Fact]
    public void Finish_request_contains_no_server_owned_identifiers()
    {
        var names = typeof(FinishMigrationRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("ExecutionId", names);
        Assert.DoesNotContain("CandidateArtifactId", names);
        Assert.DoesNotContain("StagingRunId", names);
        Assert.DoesNotContain("RuntimeStackId", names);
        Assert.DoesNotContain("CatalogEntryId", names);
    }

    private static FinishMigrationRequest ValidRequest(int retentionDays = 14) =>
        new(
            RetentionDays: retentionDays,
            ConfirmVerifiedServerIsAuthoritative: true,
            ConfirmRecoveryBoundaryChanges: true,
            ConfirmRetainOldServerAndNoAutomaticDeletion: true);
}
