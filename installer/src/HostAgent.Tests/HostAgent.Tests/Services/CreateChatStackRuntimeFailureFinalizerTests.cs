using HostAgent.Commands;
using HostAgent.Matrix.Provisioning;
using HostAgent.Runtime.Databases;
using HostAgent.Runtime.Operations;
using HostAgent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Diagnostics;

namespace HostAgent.Tests.Services;

public sealed class CreateChatStackRuntimeFailureFinalizerTests
{
    [Fact]
    public async Task STACK_CREATE_REL_01A_cancelled_request_still_uses_independent_cleanup_and_journal_tokens()
    {
        var calls = new List<string>();
        var operations = new RecordingOperationStore { Calls = calls };
        var databases = new RecordingDatabaseService { Calls = calls };
        var diagnostics = new RecordingDiagnosticWriter { Calls = calls };
        var finalizer = new CreateChatStackRuntimeFailureFinalizer(
            operations,
            databases,
            diagnostics,
            NullLogger<CreateChatStackRuntimeFailureFinalizer>.Instance);
        var evidence = new List<HostAgentEvidence>();
        var command = Command();
        var provisioning = Provisioning(command.StackId, command.StackSlug!);

        var operationId = Guid.NewGuid();

        await finalizer.FinalizeAsync(
            operationId,
            "generate-synapse-config",
            command,
            provisioning,
            databaseOwnershipPersisted: false,
            activeRuntimeMutationStarted: false,
            new TaskCanceledException("request was cancelled"),
            evidence,
            requestCancellationRequested: true);

        Assert.Equal(TimeSpan.FromSeconds(20), CreateChatStackRuntimeFailureFinalizer.CleanupTimeout);
        Assert.Equal(TimeSpan.FromSeconds(20), CreateChatStackRuntimeFailureFinalizer.JournalTimeout);
        Assert.True(databases.DropCalled);
        Assert.True(databases.DropToken.CanBeCanceled);
        Assert.False(databases.DropToken.IsCancellationRequested);
        Assert.True(operations.FailCalled);
        Assert.True(operations.FailToken.CanBeCanceled);
        Assert.False(operations.FailToken.IsCancellationRequested);
        Assert.Equal("generate-synapse-config", operations.FailStep);
        Assert.Equal(TimeSpan.FromSeconds(5), CreateChatStackRuntimeFailureFinalizer.DiagnosticTimeout);
        Assert.True(diagnostics.WriteCalled);
        Assert.True(diagnostics.WriteToken.CanBeCanceled);
        Assert.False(diagnostics.WriteToken.IsCancellationRequested);
        Assert.Equal("host-agent.create-stack.failed", diagnostics.Request!.EventCode);
        Assert.Equal(operationId, diagnostics.Request.OperationId);
        Assert.Equal($"inc_op_{operationId:N}", diagnostics.Request.IncidentId);
        Assert.Equal("generate-synapse-config", diagnostics.Request.Stage);
        Assert.True(diagnostics.Request.CreateIncident);
        Assert.Equal(new[] { "journal", "diagnostic", "cleanup" }, calls);
        Assert.Contains(evidence, item => item.Code == "host-agent.create-stack.failed");
        Assert.Contains(evidence, item => item.Code == "host-agent.matrix.postgres.pre-registration-cleanup.completed");
    }

    [Fact]
    public async Task STACK_CREATE_REL_01A_journal_failure_still_emits_create_stack_diagnostic_before_cleanup()
    {
        var calls = new List<string>();
        var operations = new RecordingOperationStore
        {
            Calls = calls,
            FailFailure = new IOException("operation journal unavailable")
        };
        var databases = new RecordingDatabaseService { Calls = calls };
        var diagnostics = new RecordingDiagnosticWriter { Calls = calls };
        var finalizer = new CreateChatStackRuntimeFailureFinalizer(
            operations,
            databases,
            diagnostics,
            NullLogger<CreateChatStackRuntimeFailureFinalizer>.Instance);
        var command = Command();
        var operationId = Guid.NewGuid();

        await finalizer.FinalizeAsync(
            operationId,
            "generate-synapse-config",
            command,
            Provisioning(command.StackId, command.StackSlug!),
            databaseOwnershipPersisted: false,
            activeRuntimeMutationStarted: false,
            new TaskCanceledException("request was cancelled"),
            new List<HostAgentEvidence>(),
            requestCancellationRequested: true);

        Assert.True(diagnostics.WriteCalled);
        Assert.Equal(operationId, diagnostics.Request!.OperationId);
        Assert.True(databases.DropCalled);
        Assert.Equal(new[] { "journal", "diagnostic", "cleanup" }, calls);
    }

    [Theory]
    [InlineData(false, "synapse-generation-docker-wait-cancelled")]
    [InlineData(true, "synapse-generation-caller-cancelled")]
    public void STACK_CREATE_REL_01A_synapse_cancellation_classification_preserves_caller_state(
        bool callerCancellationRequested,
        string expected)
    {
        var exception = new SynapseConfigGenerationCanceledException(
            Guid.NewGuid(),
            "wait-for-exit",
            "container-1",
            callerCancellationRequested,
            exceptionCancellationRequested: callerCancellationRequested,
            TimeSpan.FromSeconds(2),
            new TaskCanceledException("docker wait cancelled"));

        Assert.Equal(
            expected,
            CreateChatStackRuntimeFailureFinalizer.ClassifyFailure(
                exception,
                requestCancellationRequested: callerCancellationRequested));
    }

    [Fact]
    public async Task STACK_CREATE_REL_01A_cleanup_refusal_does_not_prevent_terminal_failure_journal()
    {
        var operations = new RecordingOperationStore();
        var databases = new RecordingDatabaseService
        {
            DropFailure = new RuntimeStackDatabaseCleanupRefusedException("registered runtime stack exists")
        };
        var diagnostics = new RecordingDiagnosticWriter();
        var finalizer = new CreateChatStackRuntimeFailureFinalizer(
            operations,
            databases,
            diagnostics,
            NullLogger<CreateChatStackRuntimeFailureFinalizer>.Instance);
        var evidence = new List<HostAgentEvidence>();
        var command = Command();

        await finalizer.FinalizeAsync(
            Guid.NewGuid(),
            "persist-manifest",
            command,
            Provisioning(command.StackId, command.StackSlug!),
            databaseOwnershipPersisted: false,
            activeRuntimeMutationStarted: false,
            new InvalidOperationException("manifest persistence failed"),
            evidence,
            requestCancellationRequested: false);

        Assert.True(databases.DropCalled);
        Assert.True(operations.FailCalled);
        Assert.Equal("persist-manifest", operations.FailStep);
        Assert.Contains(evidence, item => item.Code == "host-agent.matrix.postgres.pre-registration-cleanup.refused");
    }

    [Fact]
    public async Task STACK_CREATE_REL_01A_cleanup_failure_does_not_replace_original_operation_failure()
    {
        var operations = new RecordingOperationStore();
        var databases = new RecordingDatabaseService
        {
            DropFailure = new IOException("postgres cleanup unavailable")
        };
        var diagnostics = new RecordingDiagnosticWriter();
        var finalizer = new CreateChatStackRuntimeFailureFinalizer(
            operations,
            databases,
            diagnostics,
            NullLogger<CreateChatStackRuntimeFailureFinalizer>.Instance);
        var evidence = new List<HostAgentEvidence>();
        var command = Command();
        var original = new InvalidOperationException("synapse generation failed");

        await finalizer.FinalizeAsync(
            Guid.NewGuid(),
            "generate-synapse-config",
            command,
            Provisioning(command.StackId, command.StackSlug!),
            databaseOwnershipPersisted: false,
            activeRuntimeMutationStarted: false,
            original,
            evidence,
            requestCancellationRequested: false);

        Assert.True(operations.FailCalled);
        Assert.Equal(original.Message, operations.FailError);
        Assert.Contains(evidence, item => item.Code == "host-agent.matrix.postgres.pre-registration-cleanup.failed");
    }

    [Fact]
    public async Task STACK_CREATE_REL_01A_dependency_cleanup_cancellation_still_journals_original_failure()
    {
        var operations = new RecordingOperationStore();
        var databases = new RecordingDatabaseService
        {
            DropFailure = new OperationCanceledException("cleanup timed out")
        };
        var diagnostics = new RecordingDiagnosticWriter();
        var finalizer = new CreateChatStackRuntimeFailureFinalizer(
            operations,
            databases,
            diagnostics,
            NullLogger<CreateChatStackRuntimeFailureFinalizer>.Instance);
        var evidence = new List<HostAgentEvidence>();
        var command = Command();
        var original = new InvalidOperationException("original create-stack failure");

        await finalizer.FinalizeAsync(
            Guid.NewGuid(),
            "generate-synapse-config",
            command,
            Provisioning(command.StackId, command.StackSlug!),
            databaseOwnershipPersisted: false,
            activeRuntimeMutationStarted: false,
            original,
            evidence,
            requestCancellationRequested: false);

        Assert.True(operations.FailCalled);
        Assert.Equal(original.Message, operations.FailError);
        Assert.Contains(evidence, item => item.Code == "host-agent.matrix.postgres.pre-registration-cleanup.failed");
    }

    [Fact]
    public async Task STACK_CREATE_REL_01A_active_runtime_mutation_defers_isolated_database_cleanup()
    {
        var operations = new RecordingOperationStore();
        var databases = new RecordingDatabaseService();
        var diagnostics = new RecordingDiagnosticWriter();
        var finalizer = new CreateChatStackRuntimeFailureFinalizer(
            operations,
            databases,
            diagnostics,
            NullLogger<CreateChatStackRuntimeFailureFinalizer>.Instance);
        var evidence = new List<HostAgentEvidence>();
        var command = Command();

        await finalizer.FinalizeAsync(
            Guid.NewGuid(),
            "start-matrix",
            command,
            Provisioning(command.StackId, command.StackSlug!),
            databaseOwnershipPersisted: false,
            activeRuntimeMutationStarted: true,
            new InvalidOperationException("matrix startup failed"),
            evidence,
            requestCancellationRequested: false);

        Assert.False(databases.DropCalled);
        Assert.True(operations.FailCalled);
        Assert.Contains(evidence, item => item.Code == "host-agent.matrix.postgres.pre-registration-cleanup.deferred");
    }

    [Fact]
    public async Task STACK_CREATE_REL_01A_persisted_database_ownership_skips_pre_registration_cleanup()
    {
        var operations = new RecordingOperationStore();
        var databases = new RecordingDatabaseService();
        var diagnostics = new RecordingDiagnosticWriter();
        var finalizer = new CreateChatStackRuntimeFailureFinalizer(
            operations,
            databases,
            diagnostics,
            NullLogger<CreateChatStackRuntimeFailureFinalizer>.Instance);
        var evidence = new List<HostAgentEvidence>();
        var command = Command();

        await finalizer.FinalizeAsync(
            Guid.NewGuid(),
            "persist-stack-secret",
            command,
            Provisioning(command.StackId, command.StackSlug!),
            databaseOwnershipPersisted: true,
            activeRuntimeMutationStarted: true,
            new InvalidOperationException("secret persistence failed"),
            evidence,
            requestCancellationRequested: false);

        Assert.False(databases.DropCalled);
        Assert.True(operations.FailCalled);
    }

    [Theory]
    [InlineData(true, false, "synapse-generation-operation-timeout")]
    [InlineData(false, true, "synapse-generation-application-stopping")]
    public void STACK_CREATE_REL_01B_synapse_cancellation_classifies_server_owned_lifetime_reason(
        bool operationTimeoutRequested,
        bool applicationStoppingRequested,
        string expected)
    {
        var exception = new SynapseConfigGenerationCanceledException(
            Guid.NewGuid(),
            "wait-for-exit",
            "container-1",
            callerCancellationRequested: true,
            exceptionCancellationRequested: true,
            TimeSpan.FromSeconds(2),
            new TaskCanceledException("server-owned operation cancelled"));

        Assert.Equal(
            expected,
            CreateChatStackRuntimeFailureFinalizer.ClassifyFailure(
                exception,
                requestCancellationRequested: false,
                operationTimeoutRequested: operationTimeoutRequested,
                applicationStoppingRequested: applicationStoppingRequested));
    }

    [Fact]
    public void STACK_CREATE_REL_01B_transport_abort_is_observation_not_generic_cancellation_cause()
    {
        Assert.Equal(
            "operation-cancelled-after-transport-abort",
            CreateChatStackRuntimeFailureFinalizer.ClassifyFailure(
                new OperationCanceledException("dependency cancelled"),
                requestCancellationRequested: true));
    }

    private static CreateChatStackRuntimeCommand Command() =>
        new(
            StackId: Guid.NewGuid(),
            MatrixInstanceId: Guid.NewGuid(),
            ElementInstanceId: Guid.NewGuid(),
            StackSlug: "rel-01a-test",
            RequestedDomainId: null,
            MatrixImage: null,
            MatrixVersion: null,
            ElementImage: null,
            ElementVersion: null,
            IdempotencyKey: "stack-create-rel-01a-test");

    private static RuntimeStackDatabaseProvisioningResult Provisioning(Guid stackId, string slug)
    {
        var suffix = stackId.ToString("N")[..8];
        return new RuntimeStackDatabaseProvisioningResult(
            stackId,
            "postgres",
            "mem-postgres",
            5432,
            $"matrix_{slug.Replace('-', '_')}_{suffix}",
            $"mxu_{slug.Replace('-', '_')}_{suffix}",
            RuntimeStackDatabaseService.MatrixPostgresPasswordSecretKind,
            "active",
            true,
            true);
    }

    private sealed class RecordingDatabaseService : IRuntimeStackDatabaseService
    {
        public bool DropCalled { get; private set; }
        public CancellationToken DropToken { get; private set; }
        public Exception? DropFailure { get; init; }
        public IList<string>? Calls { get; init; }

        public Task<(RuntimeStackDatabaseProvisioningResult Provisioning, string Password)> ProvisionMatrixDatabaseAsync(
            Guid runtimeStackId,
            string stackSlug,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SaveOwnershipAsync(
            RuntimeStackDatabaseProvisioningResult provisioning,
            string password,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RuntimeStackDatabaseDropResult> DropUnregisteredMatrixDatabaseAsync(
            Guid runtimeStackId,
            string stackSlug,
            string databaseName,
            string databaseUsername,
            CancellationToken ct)
        {
            DropCalled = true;
            DropToken = ct;
            Calls?.Add("cleanup");
            if (DropFailure is not null)
            {
                return Task.FromException<RuntimeStackDatabaseDropResult>(DropFailure);
            }

            return Task.FromResult(new RuntimeStackDatabaseDropResult(
                true,
                databaseName,
                databaseUsername,
                [],
                "guarded cleanup completed"));
        }
    }

    private sealed class RecordingDiagnosticWriter : IMemDiagnosticEventWriter
    {
        public bool WriteCalled { get; private set; }
        public CancellationToken WriteToken { get; private set; }
        public MemDiagnosticWriteRequest? Request { get; private set; }
        public IList<string>? Calls { get; init; }

        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            WriteCalled = true;
            WriteToken = cancellationToken;
            Request = request;
            Calls?.Add("diagnostic");
            return Task.FromResult(new MemDiagnosticWriteResult(
                Stored: true,
                EventId: "evt_stack_create_rel_01a_corr_03",
                IncidentId: request.IncidentId,
                WarningCode: null));
        }
    }

    private sealed class RecordingOperationStore : IRuntimeOperationStore
    {
        public bool FailCalled { get; private set; }
        public CancellationToken FailToken { get; private set; }
        public string? FailStep { get; private set; }
        public string? FailError { get; private set; }
        public Exception? FailFailure { get; init; }
        public IList<string>? Calls { get; init; }

        public Task<Guid> StartAsync(
            Guid? runtimeStackId,
            string operation,
            string? idempotencyKey,
            string requestedBy,
            string hostMutationLevel,
            object? input,
            CancellationToken ct,
            Guid? restoreAttemptId = null) =>
            Task.FromResult(Guid.NewGuid());

        public Task UpdateStepAsync(Guid operationId, string currentStep, CancellationToken ct) =>
            Task.CompletedTask;

        public Task CompleteAsync(
            Guid operationId,
            string status,
            string? currentStep,
            object? result,
            object? evidence,
            CancellationToken ct) =>
            Task.CompletedTask;

        public Task FailAsync(
            Guid operationId,
            string? currentStep,
            string error,
            object? evidence,
            CancellationToken ct)
        {
            FailCalled = true;
            FailToken = ct;
            FailStep = currentStep;
            Calls?.Add("journal");
            FailError = error;
            return FailFailure is null
                ? Task.CompletedTask
                : Task.FromException(FailFailure);
        }

        public Task FailAsync(
            Guid operationId,
            string? currentStep,
            string error,
            object? result,
            object? evidence,
            CancellationToken ct) =>
            FailAsync(operationId, currentStep, error, evidence, ct);

        public Task<RuntimeOperationDetail?> FindByIdempotencyKeyAsync(
            Guid runtimeStackId,
            string operation,
            string idempotencyKey,
            CancellationToken ct) =>
            Task.FromResult<RuntimeOperationDetail?>(null);

        public Task<RuntimeOperationSummary?> FindActiveMutatingOperationForStackAsync(
            Guid runtimeStackId,
            CancellationToken ct) =>
            Task.FromResult<RuntimeOperationSummary?>(null);

        public Task<IReadOnlyList<RuntimeOperationSummary>> ListForStackAsync(
            Guid runtimeStackId,
            int limit,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RuntimeOperationSummary>>([]);
    }
}
