using HostAgent.Runtime.Migrations.ProductionAdoption;

namespace HostAgent.Tests.Runtime.Migrations.ProductionAdoption;

public sealed class MigrationPrivateServerCreationTests
{
    [Fact]
    public void Every_user_level_confirmation_is_required()
    {
        var missing = MigrationPrivateServerCreationService.GetMissingConfirmations(
            new CreateMigrationPrivateServerRequest(null, null, false, false, false));

        Assert.Equal(
            [
                "confirmVerifiedSnapshotIsAuthoritative",
                "confirmLaterSourceWritesAreNotIncluded",
                "confirmCreatePrivateServer",
            ],
            missing);
    }

    [Fact]
    public void One_explicit_create_confirmation_covers_the_private_server_boundary()
    {
        var missing = MigrationPrivateServerCreationService.GetMissingConfirmations(
            new CreateMigrationPrivateServerRequest(
                "tester",
                "element.example.test",
                true,
                true,
                true));

        Assert.Empty(missing);
    }
}
