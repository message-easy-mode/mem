using HostAgent.Runtime.Operations;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Runtime;

public sealed class RuntimeStackDoctorRequestAbortPersistenceTests
{
    [Fact]
    public async Task STACKS_UX_01G_request_abort_terminally_cancels_a_running_Doctor_operation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var diagnostics = new RecordingDiagnosticWriter();
        var store = new RuntimeOperationStore(fixture.Db, diagnostics);
        var operationId = await store.StartAsync(
            fixture.StackId,
            operation: "doctor-stack",
            idempotencyKey: null,
            requestedBy: "mem-cli",
            hostMutationLevel: "none",
            input: new { fixture.StackId, slug = "doctor-abort-stack" },
            CancellationToken.None);

        await store.CancelIfRunningAsync(
            operationId,
            currentStep: "request-aborted",
            result: new { terminationKind = "doctor-request-aborted" },
            evidence: null,
            CancellationToken.None);

        var operation = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .SingleAsync(x => x.Id == operationId);

        Assert.Equal("cancelled", operation.Status);
        Assert.Equal("request-aborted", operation.CurrentStep);
        Assert.NotNull(operation.CompletedAtUtc);
        Assert.Null(operation.LockedUntilUtc);
        Assert.Null(operation.LastError);
        Assert.Contains("doctor-request-aborted", operation.ResultJson, StringComparison.Ordinal);
        Assert.Null(operation.EvidenceJson);

        var cancelledEvent = Assert.Single(diagnostics.Requests.Where(
            request => string.Equals(
                request.EventCode,
                "runtime.operation.cancelled",
                StringComparison.Ordinal)));
        Assert.Equal(MemDiagnosticSeverities.Warning, cancelledEvent.Severity);
        Assert.False(cancelledEvent.CreateIncident);
        Assert.Null(cancelledEvent.IncidentId);
    }

    [Fact]
    public void STACKS_UX_01G_endpoint_journals_request_abort_before_rethrowing_cancellation()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "installer",
            "src",
            "HostAgent",
            "Endpoints",
            "HostAgentRuntimeStacksEndpoint.cs"));

        const string catchMarker =
            "catch (OperationCanceledException) when (ct.IsCancellationRequested)";
        var catchIndex = source.IndexOf(catchMarker, StringComparison.Ordinal);
        Assert.True(catchIndex >= 0, "Doctor request-cancellation catch was not found.");

        var cancelIndex = source.IndexOf(
            "await TryCancelDoctorOperationAsync(",
            catchIndex,
            StringComparison.Ordinal);
        var rethrowIndex = source.IndexOf(
            "throw;",
            catchIndex,
            StringComparison.Ordinal);

        Assert.True(cancelIndex > catchIndex, "Doctor cancellation was not journalled.");
        Assert.True(rethrowIndex > cancelIndex, "Doctor cancellation must be journalled before the request abort is rethrown.");
        Assert.Contains(
            "await using var journalScope = serviceScopeFactory.CreateAsyncScope();",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            ".GetRequiredService<RuntimeOperationStore>();",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task STACKS_UX_01G_request_abort_cannot_overwrite_an_already_completed_Doctor_operation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new RuntimeOperationStore(fixture.Db);
        var operationId = await store.StartAsync(
            fixture.StackId,
            operation: "doctor-stack",
            idempotencyKey: null,
            requestedBy: "mem-cli",
            hostMutationLevel: "none",
            input: new { fixture.StackId, slug = "doctor-abort-stack" },
            CancellationToken.None);

        await store.CompleteAsync(
            operationId,
            status: "passed",
            currentStep: "completed",
            result: new { reportId = Guid.NewGuid(), allPassed = true },
            evidence: new[] { new { code = "doctor.check", success = true } },
            CancellationToken.None);

        var beforeAbort = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .SingleAsync(x => x.Id == operationId);

        await store.CancelIfRunningAsync(
            operationId,
            currentStep: "request-aborted",
            result: new { terminationKind = "doctor-request-aborted" },
            evidence: null,
            CancellationToken.None);

        var afterAbort = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .SingleAsync(x => x.Id == operationId);

        Assert.Equal("passed", afterAbort.Status);
        Assert.Equal("completed", afterAbort.CurrentStep);
        Assert.Equal(beforeAbort.CompletedAtUtc, afterAbort.CompletedAtUtc);
        Assert.Equal(beforeAbort.ResultJson, afterAbort.ResultJson);
        Assert.Equal(beforeAbort.EvidenceJson, afterAbort.EvidenceJson);
        Assert.Null(afterAbort.LastError);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "installer",
                    "src",
                    "MemInstaller.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the MEM repository root from the test runtime.");
    }

    private sealed class RecordingDiagnosticWriter : IMemDiagnosticEventWriter
    {
        public List<MemDiagnosticWriteRequest> Requests { get; } = [];

        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new MemDiagnosticWriteResult(
                Stored: true,
                EventId: $"evt_{Requests.Count}",
                IncidentId: request.IncidentId,
                WarningCode: null));
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            MemDbContext db,
            Guid stackId)
        {
            _connection = connection;
            Db = db;
            StackId = stackId;
        }

        public MemDbContext Db { get; }
        public Guid StackId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var stackId = Guid.NewGuid();
            db.RuntimeStacks.Add(new RuntimeStackEntity
            {
                Id = stackId,
                Slug = "doctor-abort-stack",
                Status = "ready",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                MatrixInstanceId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();

            return new Fixture(connection, db, stackId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
