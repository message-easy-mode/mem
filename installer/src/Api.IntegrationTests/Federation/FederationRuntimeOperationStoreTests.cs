using HostAgent.Matrix.Federation;
using HostAgent.Runtime.Operations;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Federation;

public sealed class FederationRuntimeOperationStoreTests
{
    [Fact]
    public async Task Federation_operation_lookup_step_refresh_and_mutation_conflict_use_the_existing_schema()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var stackId = Guid.NewGuid();
        db.RuntimeStacks.Add(new RuntimeStackEntity
        {
            Id = stackId,
            Slug = "federation-operation-stack",
            Status = "ready",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            MatrixInstanceId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        var store = new RuntimeOperationStore(db);
        var operationId = await store.StartAsync(
            stackId,
            RuntimeStackFederationApplyService.OperationName,
            "federation-operation-key",
            "owner",
            "filesystem,docker",
            new { requestHash = "sha256:test" },
            CancellationToken.None);
        await store.UpdateStepAsync(
            operationId,
            "validating-candidate-config",
            CancellationToken.None);

        var found = await store.FindByIdempotencyKeyAsync(
            stackId,
            RuntimeStackFederationApplyService.OperationName,
            "federation-operation-key",
            CancellationToken.None);
        var active = await store.FindActiveMutatingOperationForStackAsync(
            stackId,
            CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("validating-candidate-config", found!.CurrentStep);
        Assert.NotNull(found.LockedUntilUtc);
        Assert.Equal(operationId, active!.Id);

        await store.CompleteAsync(
            operationId,
            "succeeded",
            "completed",
            new { status = "succeeded" },
            new { safe = true },
            CancellationToken.None);

        Assert.Null(await store.FindActiveMutatingOperationForStackAsync(
            stackId,
            CancellationToken.None));
        var completed = await store.FindByIdempotencyKeyAsync(
            stackId,
            RuntimeStackFederationApplyService.OperationName,
            "federation-operation-key",
            CancellationToken.None);
        Assert.Equal("succeeded", completed!.Status);
        Assert.Contains("succeeded", completed.ResultJson, StringComparison.Ordinal);
    }
}
