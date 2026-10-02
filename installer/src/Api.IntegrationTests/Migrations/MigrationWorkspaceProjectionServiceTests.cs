using System.Text.Json;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modules.Operator.Migrations;
using Modules.Operator.Migrations.Workspace;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationWorkspaceProjectionServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 0, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Awaiting_package_projects_six_stage_workspace_without_secret_material()
    {
        await using var fixture = await Fixture.CreateAsync();
        var createdAtUtc = Now.AddMinutes(-15).UtcDateTime;

        fixture.Db.MigrationIntakes.Add(new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = "mig_workspace_waiting",
            DisplayName = "Waiting migration",
            CreatedAtUtc = createdAtUtc,
            PackageRevisions =
            [
                new MigrationPackageRevisionEntity
                {
                    Id = Guid.NewGuid(),
                    PackageRevisionId = "mpr_workspace_waiting",
                    RevisionNumber = 1,
                    Purpose = "preview",
                    Status = "awaiting-package",
                    RetentionState = "active",
                    ActivePurposeKey = "mig_workspace_waiting:preview",
                    CreatedAtUtc = createdAtUtc,
                    ExpiresAtUtc = Now.AddHours(23).UtcDateTime,
                    AgeRecipient = "age1publicworkspace",
                    ProtectedAgeIdentity = "AGE-SECRET-KEY-REVISION-DO-NOT-EXPOSE",
                    RecipientFingerprint = "AAAA-BBBB-CCCC-DDDD",
                },
            ],
        });
        await fixture.Db.SaveChangesAsync();

        var workspace = await fixture.Workspace.GetAsync(
            "mig_workspace_waiting",
            CancellationToken.None);

        Assert.NotNull(workspace);
        Assert.Equal(2, workspace!.SchemaVersion);
        Assert.Equal(6, workspace.Stages.Count);
        Assert.Equal(MigrationWorkspaceStageCodes.CreateAndUploadPackage, workspace.OverallStatus.CurrentStageCode);
        Assert.Equal("upload-package", workspace.OverallStatus.NextAction?.Code);
        Assert.Equal(MigrationWorkspaceStageStates.Ready, workspace.Stages[0].State);
        Assert.All(workspace.Stages.Skip(1), stage => Assert.False(stage.Unlocked));
        Assert.Single(workspace.Activity);
        Assert.Equal("migration-created", workspace.Activity[0].Code);

        Assert.Equal(workspace.OverallStatus.CurrentStageCode, workspace.Guided.CurrentStageCode);
        Assert.Equal(workspace.OverallStatus.NextAction, workspace.Guided.NextAction);
        Assert.Equal(workspace.Stages[0].State, workspace.Guided.StageState);
        Assert.Equal("known", workspace.Guided.UncertaintyState);
        Assert.Equal("mig_workspace_waiting", workspace.Guided.Detail.Session.MigrationId);
        Assert.Equal("awaiting-package", workspace.Guided.Detail.Package?.Status);
        Assert.Matches("^[0-9a-f]{64}$", workspace.Guided.Revision);
        Assert.Equal(9, workspace.Guided.OperationRevisions.Count);
        Assert.NotNull(workspace.Guided.Cancellation);
        Assert.True(workspace.Guided.Cancellation.CanCancel);
        Assert.Equal("empty-session", workspace.Guided.Cancellation.ConfirmationKind);
        Assert.Equal(1, workspace.Guided.Cancellation.StateVersion);
        Assert.All(workspace.Guided.OperationRevisions.Values, value => Assert.Matches("^[0-9a-f]{64}$", value));

        // GET is observational: no StateVersion churn and no connection disposal.
        var beforeVersion = (await fixture.Db.MigrationIntakes.SingleAsync()).StateVersion;
        var again = await fixture.Workspace.GetAsync("mig_workspace_waiting", CancellationToken.None);
        Assert.Equal(workspace.Guided.Revision, again!.Guided.Revision);
        Assert.Equal(beforeVersion, (await fixture.Db.MigrationIntakes.SingleAsync()).StateVersion);
        Assert.Null(fixture.Db.Database.CurrentTransaction);
        Assert.Equal(System.Data.ConnectionState.Open, fixture.Connection.State);

        var json = JsonSerializer.Serialize(
            workspace,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("AGE-SECRET-KEY-WORKSPACE-DO-NOT-EXPOSE", json, StringComparison.Ordinal);
        Assert.DoesNotContain("AGE-SECRET-KEY-REVISION-DO-NOT-EXPOSE", json, StringComparison.Ordinal);
        Assert.DoesNotContain("protectedAgeIdentity", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manifestJson", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validated_package_without_retired_findings_projects_ready_review_stage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var createdAtUtc = Now.AddHours(-1).UtcDateTime;
        var packageSha = new string('2', 64);

        fixture.Db.MigrationIntakes.Add(new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = "mig_workspace_validated",
            DisplayName = "Validated migration",
            CreatedAtUtc = createdAtUtc,
            PackageRevisions =
            [
                new MigrationPackageRevisionEntity
                {
                    Id = Guid.NewGuid(),
                    PackageRevisionId = "mpr_workspace_validated",
                    RevisionNumber = 1,
                    Purpose = "preview",
                    Status = "package-validated",
                    RetentionState = "active",
                    ActivePurposeKey = "mig_workspace_validated:preview",
                    CreatedAtUtc = createdAtUtc,
                    UploadedAtUtc = createdAtUtc.AddMinutes(5),
                    ValidatedAtUtc = createdAtUtc.AddMinutes(6),
                    PackageFileName = "blocked.zip.age",
                    PackageSizeBytes = 1024,
                    EncryptedPackageSha256 = new string('3', 64),
                    DecryptedArchiveSha256 = packageSha,
                    ArchiveMigrationId = "source-blocked",
                    ArchiveSourceProduct = "MatrixEasyMode",
                    ArchiveSourceVersion = "0.1.0",
                    ArchiveStackCount = 1,
                },
            ],
        });
        await fixture.Db.SaveChangesAsync();

        var workspace = await fixture.Workspace.GetAsync(
            "mig_workspace_validated",
            CancellationToken.None);

        Assert.NotNull(workspace);
        Assert.Equal(MigrationWorkspaceStageCodes.ReviewOldServer, workspace!.OverallStatus.CurrentStageCode);
        Assert.Equal("start-conversion", workspace.OverallStatus.NextAction?.Code);
        Assert.Equal(MigrationWorkspaceStageStates.Completed, workspace.Stages[0].State);
        Assert.Equal(MigrationWorkspaceStageStates.Ready, workspace.Stages[1].State);
        Assert.Empty(workspace.Stages[1].Problems);
        Assert.True(workspace.Guided.Cancellation!.CanCancel);
        Assert.Equal("package", workspace.Guided.Cancellation.ConfirmationKind);
        Assert.Equal(0, workspace.Source.BlockerCount);
        Assert.Contains(workspace.Activity, item => item.Code == "package-verified");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SqliteConnection connection,
            MemDbContext db,
            MigrationWorkspaceProjectionService workspace)
        {
            Connection = connection;
            Db = db;
            Workspace = workspace;
        }

        public SqliteConnection Connection { get; }
        public MemDbContext Db { get; }
        public MigrationWorkspaceProjectionService Workspace { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var timeProvider = new FixedTimeProvider(Now);
            var sessionProjection = new MigrationSessionProjectionService(db, timeProvider);
            var workspace = new MigrationWorkspaceProjectionService(
                db,
                sessionProjection,
                timeProvider);
            return new Fixture(connection, db, workspace);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
