using System.Text.Json;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Operator.Migrations;
using Modules.Operator.Migrations.Workspace;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationSessionInventoryServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Returns_compact_paged_summary_and_excludes_archived_by_default()
    {
        await using var fixture = await Fixture.CreateAsync();

        fixture.Db.MigrationIntakes.AddRange(
            Intake(
                "mig_active",
                "Active migration",
                MigrationSessionLifecycleStatuses.Active,
                Now.AddHours(-1).UtcDateTime),
            Intake(
                "mig_closed",
                "Expired migration",
                MigrationSessionLifecycleStatuses.Closed,
                Now.AddHours(-2).UtcDateTime),
            Intake(
                "mig_cancelled",
                "Cancelled migration",
                MigrationSessionLifecycleStatuses.Cancelled,
                Now.AddHours(-3).UtcDateTime),
            Intake(
                "mig_completed",
                "Completed migration",
                MigrationSessionLifecycleStatuses.Completed,
                Now.AddHours(-4).UtcDateTime),
            Intake(
                "mig_archived",
                "Archived migration",
                MigrationSessionLifecycleStatuses.Completed,
                Now.AddHours(-5).UtcDateTime,
                archivedAtUtc: Now.AddMinutes(-5).UtcDateTime));
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.Service.ListAsync(
            Query(pageSize: 2),
            CancellationToken.None);

        Assert.Equal(4, response.TotalSessions);
        Assert.Equal(2, response.Sessions.Count);
        Assert.Equal(2, response.TotalPages);
        Assert.False(response.HasPreviousPage);
        Assert.True(response.HasNextPage);
        Assert.Equal(4, response.Summary.TotalSessions);
        Assert.Equal(1, response.Summary.ActiveCount);
        Assert.Equal(1, response.Summary.CompletedCount);
        Assert.Equal(1, response.Summary.ClosedCount);
        Assert.Equal(1, response.Summary.CancelledCount);
        Assert.Equal(0, response.Summary.ArchivedCount);
        Assert.DoesNotContain(
            response.Sessions,
            x => x.MigrationId == "mig_archived");

        var json = JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(
            "protectedAgeIdentity",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "manifestJson",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "workspacePath",
            json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_stage_target_sort_and_archived_filters_are_server_owned()
    {
        await using var fixture = await Fixture.CreateAsync();
        var conversion = Intake(
            "mig_conversion",
            "David source migration",
            MigrationSessionLifecycleStatuses.Active,
            Now.AddHours(-2).UtcDateTime);
        conversion.Sources.Add(new MigrationSourceEntity
        {
            Id = Guid.NewGuid(),
            SourceId = "source-david",
            SourceKind = "migration-package",
            Product = "MatrixEasyMode",
            ProductVersion = "0.1.0",
            SourceFingerprint = "fingerprint-david",
            CapturedAtUtc = Now.AddHours(-2).UtcDateTime,
        });

        var revision = Revision(conversion);
        conversion.PackageRevisions.Add(revision);
        conversion.ConversionAttempts.Add(new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(),
            ConversionAttemptId = "conv_inventory",
            MigrationIntakeEntityId = conversion.Id,
            MigrationIntake = conversion,
            MigrationPackageRevisionEntityId = revision.Id,
            PackageRevision = revision,
            ActiveMigrationKey = conversion.IntakeId,
            SourcePackageSha256 = revision.DecryptedArchiveSha256!,
            SourceAdapterId = "mem-v010",
            SourceAdapterVersion = "1",
            ConverterId = "mem-v010-to-postgresql",
            ConverterVersion = "1",
            Status = "running",
            CurrentStep = "worker-starting",
            CreatedAtUtc = Now.AddMinutes(-30).UtcDateTime,
            UpdatedAtUtc = Now.AddMinutes(-10).UtcDateTime,
        });

        var target = Intake(
            "mig_target",
            "Target collision",
            MigrationSessionLifecycleStatuses.Active,
            Now.AddHours(-1).UtcDateTime);
        AttachAdoption(target, "tester-restored");

        fixture.Db.MigrationIntakes.AddRange(conversion, target);
        await fixture.Db.SaveChangesAsync();

        var conversionResponse = await fixture.Service.ListAsync(
            Query(
                search: "fingerprint-david",
                stage: MigrationWorkspaceStageCodes.PrepareAndTest),
            CancellationToken.None);

        var conversionRow = Assert.Single(conversionResponse.Sessions);
        Assert.Equal("mig_conversion", conversionRow.MigrationId);
        Assert.Equal(
            MigrationWorkspaceStageCodes.PrepareAndTest,
            conversionRow.CurrentStageCode);
        Assert.Equal("continue", conversionRow.PrimaryAction.Kind);
        Assert.False(conversionRow.Capabilities.CanCancel);
        Assert.Equal("Message Easy Mode 0.1.0", conversionRow.SourceDisplay);

        var targetResponse = await fixture.Service.ListAsync(
            Query(targetStack: "tester-restored"),
            CancellationToken.None);
        var targetRow = Assert.Single(targetResponse.Sessions);
        Assert.Equal("mig_target", targetRow.MigrationId);
        Assert.Equal("tester-restored", targetRow.TargetStackSlug);

        var continueResponse = await fixture.Service.ListAsync(
            Query(action: "continue"),
            CancellationToken.None);
        Assert.Equal(
            new[] { "mig_conversion" },
            continueResponse.Sessions.Select(x => x.MigrationId));
        Assert.Equal("continue", continueResponse.Query.Action);

        var reviewResponse = await fixture.Service.ListAsync(
            Query(action: "review"),
            CancellationToken.None);
        Assert.Equal(
            new[] { "mig_target" },
            reviewResponse.Sessions.Select(x => x.MigrationId));
        Assert.Equal("review", reviewResponse.Query.Action);
        Assert.Equal(1, reviewResponse.Summary.NeedsActionCount);

        target.ArchivedAtUtc = Now.UtcDateTime;
        await fixture.Db.SaveChangesAsync();

        var archivedResponse = await fixture.Service.ListAsync(
            Query(lifecycle: "archived"),
            CancellationToken.None);
        var archived = Assert.Single(archivedResponse.Sessions);
        Assert.Equal("mig_target", archived.MigrationId);
        Assert.True(archived.Archived);
        Assert.True(archived.Capabilities.CanUnarchive);
        Assert.Equal("view", archived.PrimaryAction.Kind);
    }

    [Fact]
    public async Task Deletion_capability_is_server_authored_and_fails_closed_for_owned_sessions()
    {
        await using var fixture = await Fixture.CreateAsync();

        var disposable = Intake(
            "mig_disposable",
            "Disposable cancelled migration",
            MigrationSessionLifecycleStatuses.Cancelled,
            Now.AddMinutes(-10).UtcDateTime);

        var requestRevision = Revision(disposable);
        requestRevision.Status = "cancelled";
        requestRevision.RetentionState = "retired";
        requestRevision.ActivePurposeKey = null;
        requestRevision.ValidatedAtUtc = null;
        requestRevision.DecryptedArchiveSha256 = null;
        disposable.PackageRevisions.Add(requestRevision);

        var expired = Intake(
            "mig_expired_disposable",
            "Expired disposable request",
            MigrationSessionLifecycleStatuses.Active,
            Now.AddMinutes(-15).UtcDateTime);
        var expiredRevision = Revision(expired);
        expiredRevision.Status = "awaiting-package";
        expiredRevision.ValidatedAtUtc = null;
        expiredRevision.DecryptedArchiveSha256 = null;
        expiredRevision.ExpiresAtUtc = Now.AddMinutes(-1).UtcDateTime;
        expired.PackageRevisions.Add(expiredRevision);

        var completed = Intake(
            "mig_completed_protected",
            "Completed protected migration",
            MigrationSessionLifecycleStatuses.Completed,
            Now.AddMinutes(-20).UtcDateTime);

        var conversionOwned = Intake(
            "mig_conversion_protected",
            "Conversion protected migration",
            MigrationSessionLifecycleStatuses.Cancelled,
            Now.AddMinutes(-30).UtcDateTime);
        var revision = Revision(conversionOwned);
        conversionOwned.PackageRevisions.Add(revision);
        conversionOwned.ConversionAttempts.Add(new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(),
            ConversionAttemptId = "conv_protected",
            MigrationIntakeEntityId = conversionOwned.Id,
            MigrationIntake = conversionOwned,
            MigrationPackageRevisionEntityId = revision.Id,
            PackageRevision = revision,
            SourcePackageSha256 = revision.DecryptedArchiveSha256!,
            SourceAdapterId = "mem-v010",
            SourceAdapterVersion = "1",
            ConverterId = "mem-v010-to-postgresql",
            ConverterVersion = "1",
            Status = "failed",
            CurrentStep = "failed",
            CreatedAtUtc = conversionOwned.CreatedAtUtc,
            UpdatedAtUtc = conversionOwned.UpdatedAtUtc,
        });

        var packageOnly = Intake(
            "mig_package_only",
            "Disposable package-only migration",
            MigrationSessionLifecycleStatuses.Cancelled,
            Now.AddMinutes(-35).UtcDateTime);
        var retiredRevision = Revision(packageOnly);
        retiredRevision.Status = "cancelled";
        retiredRevision.RetentionState = "retired";
        retiredRevision.ActivePurposeKey = null;
        packageOnly.PackageRevisions.Add(retiredRevision);

        var activePackageAuthority = Intake(
            "mig_active_package_protected",
            "Active package authority migration",
            MigrationSessionLifecycleStatuses.Cancelled,
            Now.AddMinutes(-37).UtcDateTime);
        activePackageAuthority.PackageRevisions.Add(Revision(activePackageAuthority));

        fixture.Db.MigrationIntakes.AddRange(
            disposable,
            expired,
            completed,
            conversionOwned,
            packageOnly,
            activePackageAuthority);
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.Service.ListAsync(
            Query(pageSize: 10),
            CancellationToken.None);

        var disposableRow = Assert.Single(
            response.Sessions,
            x => x.MigrationId == disposable.IntakeId);
        Assert.True(disposableRow.Capabilities.CanDelete);
        Assert.Null(disposableRow.Capabilities.DeleteBlockedReason);

        var expiredRow = Assert.Single(
            response.Sessions,
            x => x.MigrationId == expired.IntakeId);
        Assert.Equal(MigrationSessionLifecycleStatuses.Closed, expiredRow.LifecycleStatus);
        Assert.True(expiredRow.Capabilities.CanDelete);
        Assert.Null(expiredRow.Capabilities.DeleteBlockedReason);

        var completedRow = Assert.Single(
            response.Sessions,
            x => x.MigrationId == completed.IntakeId);
        Assert.False(completedRow.Capabilities.CanDelete);
        Assert.Equal(
            "Accepted or completed Migration Sessions and their first native backup boundary must be retained.",
            completedRow.Capabilities.DeleteBlockedReason);

        var conversionRow = Assert.Single(
            response.Sessions,
            x => x.MigrationId == conversionOwned.IntakeId);
        Assert.False(conversionRow.Capabilities.CanDelete);
        Assert.Equal(
            "Conversion or candidate-artifact evidence is attached to this Session.",
            conversionRow.Capabilities.DeleteBlockedReason);

        var packageOnlyRow = Assert.Single(
            response.Sessions,
            x => x.MigrationId == packageOnly.IntakeId);
        Assert.True(packageOnlyRow.Capabilities.CanDelete);
        Assert.Null(packageOnlyRow.Capabilities.DeleteBlockedReason);

        var activePackageRow = Assert.Single(
            response.Sessions,
            x => x.MigrationId == activePackageAuthority.IntakeId);
        Assert.False(activePackageRow.Capabilities.CanDelete);
        Assert.Equal(
            "A current package revision is still active and must be cancelled or retired before permanent deletion.",
            activePackageRow.Capabilities.DeleteBlockedReason);

    }

    [Fact]
    public void Rejects_invalid_query_values_with_stable_fields()
    {
        var parsed = MigrationSessionInventoryQuery.Parse(
            page: "0",
            pageSize: "500",
            search: new string('x', 201),
            lifecycle: "destroyed",
            action: "erase",
            stage: "unknown-stage",
            targetStack: null,
            sortBy: "size",
            sortDirection: "sideways",
            includeArchived: "perhaps");

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Query);
        Assert.Contains("page", parsed.Errors.Keys);
        Assert.Contains("pageSize", parsed.Errors.Keys);
        Assert.Contains("search", parsed.Errors.Keys);
        Assert.Contains("lifecycle", parsed.Errors.Keys);
        Assert.Contains("action", parsed.Errors.Keys);
        Assert.Contains("stage", parsed.Errors.Keys);
        Assert.Contains("sortBy", parsed.Errors.Keys);
        Assert.Contains("sortDirection", parsed.Errors.Keys);
        Assert.Contains("includeArchived", parsed.Errors.Keys);
    }

    private static MigrationSessionInventoryQuery Query(
        int page = 1,
        int pageSize = 10,
        string? search = null,
        string lifecycle = "all",
        string action = "all",
        string? stage = null,
        string? targetStack = null) =>
        new(
            page,
            pageSize,
            search,
            lifecycle,
            action,
            stage,
            targetStack,
            "updated",
            "desc",
            lifecycle == "archived");

    private static MigrationIntakeEntity Intake(
        string migrationId,
        string displayName,
        string lifecycle,
        DateTime createdAtUtc,
        DateTime? archivedAtUtc = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            IntakeId = migrationId,
            DisplayName = displayName,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
            LifecycleStatus = lifecycle,
            StateVersion = 1,
            ClosedAtUtc = lifecycle == MigrationSessionLifecycleStatuses.Active
                ? null
                : createdAtUtc,
            ClosureKind = lifecycle switch
            {
                MigrationSessionLifecycleStatuses.Cancelled => "operator-cancelled",
                MigrationSessionLifecycleStatuses.Completed => "accepted-baseline-created",
                MigrationSessionLifecycleStatuses.Closed => "expired",
                _ => null,
            },
            CancelledAtUtc = lifecycle == MigrationSessionLifecycleStatuses.Cancelled
                ? createdAtUtc
                : null,
            ArchivedAtUtc = archivedAtUtc,
        };

    private static MigrationPackageRevisionEntity Revision(
        MigrationIntakeEntity intake) =>
        new()
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = $"mpr_{intake.IntakeId}",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            RevisionNumber = 1,
            Purpose = "preview",
            Status = "package-validated",
            RetentionState = "active",
            ActivePurposeKey = $"{intake.IntakeId}:preview",
            CreatedAtUtc = intake.CreatedAtUtc,
            ValidatedAtUtc = intake.CreatedAtUtc,
            DecryptedArchiveSha256 = new string('a', 64),
            ArchiveSourceProduct = "MatrixEasyMode",
            ArchiveSourceVersion = "0.1.0",
            CaptureKind = "preview",
            SourceFrozen = false,
            RehearsalOnly = true,
        };

    private static void AttachAdoption(
        MigrationIntakeEntity intake,
        string targetStackSlug)
    {
        var revision = Revision(intake);
        var conversion = new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(),
            ConversionAttemptId = $"conv_{intake.IntakeId}",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            MigrationPackageRevisionEntityId = revision.Id,
            PackageRevision = revision,
            SourcePackageSha256 = revision.DecryptedArchiveSha256!,
            SourceAdapterId = "mem-v010",
            SourceAdapterVersion = "1",
            ConverterId = "mem-v010-to-postgresql",
            ConverterVersion = "1",
            Status = "completed",
            CurrentStep = "candidate-created",
            CreatedAtUtc = intake.CreatedAtUtc,
            UpdatedAtUtc = intake.UpdatedAtUtc,
            CompletedAtUtc = intake.UpdatedAtUtc,
        };
        var candidate = new MigrationCandidateArtifactEntity
        {
            Id = Guid.NewGuid(),
            CandidateArtifactId = $"mca_{intake.IntakeId}",
            MigrationConversionAttemptEntityId = conversion.Id,
            ConversionAttempt = conversion,
            ArtifactKind = "synapse-postgresql-conversion",
            ArtifactSchemaVersion = "1",
            SourcePackageSha256 = revision.DecryptedArchiveSha256!,
            ArtifactSha256 = new string('2', 64),
            ManifestSha256 = new string('3', 64),
            ChecksumsSha256 = new string('4', 64),
            ProvenanceJson = "{}",
            VerificationStatus = "verified",
            RetentionState = "active",
            StorageKind = "private-file",
            ArtifactPath = "/private/candidate",
            CreatedAtUtc = intake.CreatedAtUtc,
            VerifiedAtUtc = intake.UpdatedAtUtc,
        };
        conversion.CandidateArtifact = candidate;
        var staging = new MigrationStagingRunEntity
        {
            Id = Guid.NewGuid(),
            StagingRunId = $"mstg_{intake.IntakeId}",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            MigrationCandidateArtifactEntityId = candidate.Id,
            CandidateArtifact = candidate,
            ActiveMigrationKey = intake.IntakeId,
            Status = "verified",
            CurrentStep = "private-verification-complete",
            CreatedAtUtc = intake.CreatedAtUtc,
            UpdatedAtUtc = intake.UpdatedAtUtc,
            CompletedAtUtc = intake.UpdatedAtUtc,
            PrivateOnly = true,
            DatabaseImportSucceeded = true,
            SynapseHealthPassed = true,
            ElementContainerStarted = true,
            ElementHealthPassed = true,
        };
        var adoption = new MigrationProductionAdoptionEntity
        {
            Id = Guid.NewGuid(),
            AdoptionPlanId = $"madp_{intake.IntakeId}",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            MigrationPackageRevisionEntityId = revision.Id,
            PackageRevision = revision,
            MigrationCandidateArtifactEntityId = candidate.Id,
            CandidateArtifact = candidate,
            MigrationStagingRunEntityId = staging.Id,
            StagingRun = staging,
            Status = "blocked",
            RevisionNumber = 1,
            PlanSha256 = new string('1', 64),
            CreatedAtUtc = intake.CreatedAtUtc,
            UpdatedAtUtc = intake.UpdatedAtUtc,
            PreparedAtUtc = intake.CreatedAtUtc,
            RuntimeStackId = Guid.NewGuid(),
            TargetStackSlug = targetStackSlug,
            TargetDisplayName = targetStackSlug,
            MatrixInstanceId = Guid.NewGuid(),
            ElementInstanceId = Guid.NewGuid(),
            MatrixServerName = "matrix.example.test",
            MatrixPublicHost = "matrix.example.test",
            MatrixPublicBaseUrl = "https://matrix.example.test",
            ElementPublicHost = "element.example.test",
            ElementPublicBaseUrl = "https://element.example.test",
            RuntimeNetworkName = "mem-gateway",
            RuntimeDataRoot = "/private/runtime",
            ManifestPath = "/private/runtime/manifest.json",
            MatrixContainerName = "mem-matrix-target",
            MatrixDataPath = "/private/runtime/matrix",
            ElementContainerName = "mem-element-target",
            ElementDataPath = "/private/runtime/element",
            MatrixImageReference = "matrix:approved",
            MatrixImageId = "sha256:matrix",
            ElementImageReference = "element:approved",
            ElementImageId = "sha256:element",
            DatabaseEngine = "postgresql",
            DatabaseHost = "mem-postgres",
            DatabasePort = 5432,
            DatabaseName = "matrix_target",
            DatabaseUsername = "matrix_target",
            DatabasePasswordSecretKind = "matrix-postgres-password",
            RoutePlanJson = "{}",
            ProvenanceJson = "{}",
            CollisionEvidenceJson = "{}",
        };

        intake.PackageRevisions.Add(revision);
        intake.ConversionAttempts.Add(conversion);
        intake.StagingRuns.Add(staging);
        intake.ProductionAdoption = adoption;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _dataRoot;

        private Fixture(
            SqliteConnection connection,
            MemDbContext db,
            MigrationSessionInventoryService service,
            string dataRoot)
        {
            _connection = connection;
            _dataRoot = dataRoot;
            Db = db;
            Service = service;
        }

        public MemDbContext Db { get; }
        public MigrationSessionInventoryService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var dataRoot = Path.Combine(
                Path.GetTempPath(),
                $"mem-migration-inventory-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dataRoot);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HostAgent:DataRoot"] = dataRoot,
                })
                .Build();

            return new Fixture(
                connection,
                db,
                new MigrationSessionInventoryService(
                    db,
                    new FixedTimeProvider(Now),
                    configuration),
                dataRoot);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(_dataRoot))
            {
                Directory.Delete(_dataRoot, recursive: true);
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
