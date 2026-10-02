using System.Security.Claims;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Identity;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;
using Modules.Operator.Diagnostics.Services;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsSeqBootstrapExecutionServiceTests
{
    [Fact]
    public async Task Queues_one_secret_free_operation_and_keeps_the_password_only_in_the_work_item()
    {
        await using var fixture = await Fixture.CreateAsync();
        var review = fixture.StoreReview();
        const string password = "Correct-Horse-Battery-42";

        var accepted = await fixture.Service.QueueAsync(
            new DiagnosticsSeqBootstrapExecuteRequest(
                review.ReviewId,
                password,
                password),
            Principal(),
            CancellationToken.None);

        var entity = await fixture.Db.RuntimeOperations.SingleAsync();
        Assert.Equal("seq.bootstrap", entity.Operation);
        Assert.Equal("queued", entity.Status);
        Assert.Equal(accepted.OperationId, entity.Id);
        Assert.DoesNotContain(password, entity.InputJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(password, entity.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("administratorPassword", entity.InputJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        await using var enumerator = fixture.Queue
            .ReadAllAsync(CancellationToken.None)
            .GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        var workItem = enumerator.Current;
        Assert.Equal(password, new string(workItem.AdministratorPassword));
        Assert.Empty(workItem.ConnectionAdministratorPassword);
        workItem.Dispose();
        Assert.All(workItem.AdministratorPassword, value => Assert.Equal('\0', value));
        Assert.All(workItem.ConnectionAdministratorPassword, value => Assert.Equal('\0', value));

        var missing = Assert.Throws<SeqOperationException>(() =>
            fixture.ReviewStore.GetRequired(review.ReviewId));
        Assert.Equal("seq_bootstrap_review_not_found", missing.Code);
    }

    [Fact]
    public async Task Existing_administrator_secret_queues_current_password_only_for_connection_setup()
    {
        await using var fixture = await Fixture.CreateAsync();
        var review = fixture.StoreReview(administratorPasswordRequired: false);
        const string currentPassword = "Current-Seq-Administrator-42";

        var accepted = await fixture.Service.QueueAsync(
            new DiagnosticsSeqBootstrapExecuteRequest(
                review.ReviewId,
                string.Empty,
                string.Empty,
                currentPassword),
            Principal(),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, accepted.OperationId);
        var entity = await fixture.Db.RuntimeOperations.SingleAsync();
        Assert.DoesNotContain(currentPassword, entity.InputJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(currentPassword, entity.EvidenceJson ?? string.Empty, StringComparison.Ordinal);

        await using var enumerator = fixture.Queue
            .ReadAllAsync(CancellationToken.None)
            .GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        var workItem = enumerator.Current;
        Assert.Empty(workItem.AdministratorPassword);
        Assert.Equal(currentPassword, new string(workItem.ConnectionAdministratorPassword));
        workItem.Dispose();
        Assert.All(workItem.ConnectionAdministratorPassword, value => Assert.Equal('\0', value));
    }

    [Fact]
    public async Task Existing_administrator_secret_requires_current_password_for_connection_setup()
    {
        await using var fixture = await Fixture.CreateAsync();
        var review = fixture.StoreReview(administratorPasswordRequired: false);

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.QueueAsync(
                new DiagnosticsSeqBootstrapExecuteRequest(
                    review.ReviewId,
                    string.Empty,
                    string.Empty,
                    string.Empty),
                Principal(),
                CancellationToken.None));

        Assert.Equal("seq_connection_administrator_password_required", exception.Code);
        Assert.False(await fixture.Db.RuntimeOperations.AnyAsync());
    }

    [Fact]
    public async Task Existing_administrator_secret_rejects_an_unnecessary_plaintext_password()
    {
        await using var fixture = await Fixture.CreateAsync();
        var review = fixture.StoreReview(administratorPasswordRequired: false);

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.QueueAsync(
                new DiagnosticsSeqBootstrapExecuteRequest(
                    review.ReviewId,
                    "Correct-Horse-Battery-42",
                    "Correct-Horse-Battery-42",
                    "Current-Seq-Administrator-42"),
                Principal(),
                CancellationToken.None));

        Assert.Equal("seq_administrator_password_not_required", exception.Code);
        Assert.False(await fixture.Db.RuntimeOperations.AnyAsync());
    }

    [Theory]
    [InlineData("different-confirmation", "seq_administrator_password_mismatch")]
    [InlineData("short", "seq_administrator_password_weak")]
    public async Task Rejects_invalid_passwords_before_persisting_an_operation(
        string confirmation,
        string expectedCode)
    {
        await using var fixture = await Fixture.CreateAsync();
        var review = fixture.StoreReview();
        var password = confirmation == "short"
            ? "short"
            : "Correct-Horse-Battery-42";

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.QueueAsync(
                new DiagnosticsSeqBootstrapExecuteRequest(
                    review.ReviewId,
                    password,
                    confirmation),
                Principal(),
                CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
        Assert.False(await fixture.Db.RuntimeOperations.AnyAsync());
    }

    [Fact]
    public async Task Rejects_a_second_active_Seq_operation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var review = fixture.StoreReview();
        fixture.Db.RuntimeOperations.Add(new RuntimeOperationEntity
        {
            Id = Guid.NewGuid(),
            Operation = "seq.start",
            Status = "running",
            RequestedAtUtc = DateTime.UtcNow,
            CurrentStep = "starting",
            AttemptCount = 1,
            LockedUntilUtc = DateTime.UtcNow.AddMinutes(5),
            HostMutationLevel = "container-start",
            RequiresConfirmation = false
        });
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.QueueAsync(
                new DiagnosticsSeqBootstrapExecuteRequest(
                    review.ReviewId,
                    "Correct-Horse-Battery-42",
                    "Correct-Horse-Battery-42"),
                Principal(),
                CancellationToken.None));

        Assert.Equal("seq_operation_in_progress", exception.Code);
        Assert.Single(fixture.Db.RuntimeOperations);
    }

    [Fact]
    public async Task Projects_only_safe_durable_progress()
    {
        await using var fixture = await Fixture.CreateAsync();
        var operationId = Guid.NewGuid();
        fixture.Db.RuntimeOperations.Add(new RuntimeOperationEntity
        {
            Id = operationId,
            Operation = "seq.bootstrap",
            Status = "running",
            RequestedAtUtc = DateTime.UtcNow.AddSeconds(-2),
            StartedAtUtc = DateTime.UtcNow.AddSeconds(-1),
            CurrentStep = "hashing-password",
            AttemptCount = 1,
            LockedUntilUtc = DateTime.UtcNow.AddMinutes(5),
            HostMutationLevel = "container-create",
            RequiresConfirmation = true,
            EvidenceJson = System.Text.Json.JsonSerializer.Serialize(
                DiagnosticsSeqBootstrapExecutionService.InitialProgress())
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.GetOperationAsync(
            operationId,
            CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.Equal("running", result.Status);
        Assert.Equal("hashing-password", result.CurrentStep);
        Assert.Equal(9, result.Checks.Count);
        Assert.DoesNotContain("administratorPassword", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Correct-Horse-Battery-42", json, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
    }

    private static ClaimsPrincipal Principal() =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Name, "platform-owner"),
            new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
        ],
        "test"));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            MemDbContext db,
            SeqBootstrapReviewStore reviewStore,
            SeqBootstrapOperationQueue queue)
        {
            _connection = connection;
            Db = db;
            ReviewStore = reviewStore;
            Queue = queue;
            Service = new DiagnosticsSeqBootstrapExecutionService(
                reviewStore,
                queue,
                db,
                TimeProvider.System);
        }

        public MemDbContext Db { get; }
        public SeqBootstrapReviewStore ReviewStore { get; }
        public SeqBootstrapOperationQueue Queue { get; }
        public DiagnosticsSeqBootstrapExecutionService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new MemDbContext(new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options);
            await db.Database.EnsureCreatedAsync();
            return new Fixture(
                connection,
                db,
                new SeqBootstrapReviewStore(TimeProvider.System),
                new SeqBootstrapOperationQueue());
        }

        public SeqBootstrapReviewSnapshot StoreReview(
            bool administratorPasswordRequired = true,
            bool enableEventDelivery = true)
        {
            var now = TimeProvider.System.GetUtcNow();
            var review = new SeqBootstrapReviewSnapshot(
                $"seq_bootstrap_review_{Guid.NewGuid():N}",
                now,
                now.AddMinutes(10),
                AcceptEula: true,
                PrivateUiUrl: null,
                SelectedHostPort: 25341,
                ApprovedImageReference: "datalust/seq:2026.1.17044",
                ExpectedVersion: "2026.1.17044",
                StorageState: "ready-to-create",
                RuntimeOwnershipState: "absent",
                RuntimeExists: false,
                RuntimeManaged: false,
                AdministratorPasswordRequired: administratorPasswordRequired,
                EnableEventDelivery: enableEventDelivery,
                CurrentAdministratorPasswordRequired: !administratorPasswordRequired);
            ReviewStore.Store(review);
            return review;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
