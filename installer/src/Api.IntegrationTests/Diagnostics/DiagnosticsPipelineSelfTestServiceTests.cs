using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Services;
using Shared.Diagnostics;
using Shared.Exceptions;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsPipelineSelfTestServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 3, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Self_test_writes_one_information_event_reads_it_back_and_creates_no_incident()
    {
        var writer = new CapturingWriter();
        var reader = new MatchingReader(writer);
        var service = CreateService(writer, reader);

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal("passed", result.Status);
        Assert.StartsWith("diag_verify_", result.VerificationId);
        Assert.NotNull(result.EventId);
        Assert.Empty(result.Warnings);
        Assert.All(
            result.Checks.Where(check => check.Code != "seq_delivery"),
            check => Assert.Equal("passed", check.Status));
        Assert.Equal(
            "not-configured",
            result.Checks.Single(check => check.Code == "seq_delivery").Status);

        var request = Assert.Single(writer.Requests);
        Assert.Equal(MemDiagnosticSeverities.Information, request.Severity);
        Assert.Equal("diagnostics.pipeline_self_test", request.EventCode);
        Assert.Equal("diagnostics", request.Feature);
        Assert.Equal("pipeline-self-test", request.Stage);
        Assert.False(request.CreateIncident);
        Assert.Null(request.IncidentId);
        Assert.Equal(result.VerificationId, request.Context?.CorrelationId);
        Assert.Equal(result.VerificationId, request.Details?["verificationId"]);
    }

    [Fact]
    public async Task Concurrent_self_test_is_rejected_without_starting_a_second_write()
    {
        var writer = new BlockingWriter();
        var service = CreateService(writer, new MatchingReader(writer));

        var first = service.RunAsync(CancellationToken.None);
        await writer.Entered.Task;

        var exception = await Assert.ThrowsAsync<MemProblemException>(() =>
            service.RunAsync(CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal("diagnostics_self_test_in_progress", exception.Code);
        Assert.Equal(1, writer.CallCount);

        writer.Release.TrySetResult(true);
        Assert.Equal("passed", (await first).Status);
    }

    [Fact]
    public async Task Safe_event_write_failure_identifies_the_failed_stage_and_skips_read_back()
    {
        var writer = new FailingWriter(MemDiagnosticCodes.StoreWriteFailed);
        var reader = new EmptyReader();
        var service = CreateService(writer, reader);

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Equal(
            "failed",
            result.Checks.Single(check => check.Code == "safe_event_write").Status);
        Assert.Equal(
            "not-run",
            result.Checks.Single(check => check.Code == "safe_event_read_back").Status);
        Assert.Contains(MemDiagnosticCodes.StoreWriteFailed, result.Warnings);
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task Read_back_failure_is_reported_without_creating_an_incident()
    {
        var writer = new CapturingWriter();
        var reader = new EmptyReader();
        var service = CreateService(writer, reader);

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Equal(
            "failed",
            result.Checks.Single(check => check.Code == "safe_event_read_back").Status);
        Assert.Equal(
            "not-run",
            result.Checks.Single(check => check.Code == "correlation_round_trip").Status);
        Assert.Contains(
            MemDiagnosticCodes.SelfTestSafeEventReadBackFailed,
            result.Warnings);
        Assert.False(Assert.Single(writer.Requests).CreateIncident);
    }

    [Fact]
    public async Task Disabled_safe_store_is_not_presented_as_an_empty_ready_store()
    {
        var writer = new ThrowingWriter();
        var service = CreateService(
            writer,
            new EmptyReader(),
            storeHealth: new FakeStoreHealth(enabled: false));

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Equal(
            "disabled",
            result.Checks.Single(check => check.Code == "safe_event_write").Status);
        Assert.Contains(MemDiagnosticCodes.StoreDisabled, result.Warnings);
        Assert.Equal(0, writer.CallCount);
    }

    [Fact]
    public async Task Unavailable_safe_store_health_is_reported_as_a_named_write_failure()
    {
        var writer = new ThrowingWriter();
        var service = CreateService(
            writer,
            new EmptyReader(),
            storeHealth: new ThrowingStoreHealth());

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Equal(
            "failed",
            result.Checks.Single(check => check.Code == "safe_event_write").Status);
        Assert.Equal(
            "not-run",
            result.Checks.Single(check => check.Code == "safe_event_read_back").Status);
        Assert.Contains(
            MemDiagnosticCodes.SelfTestSafeEventWriteFailed,
            result.Warnings);
        Assert.Equal(0, writer.CallCount);
    }

    [Fact]
    public async Task Unavailable_local_recorder_fails_only_its_named_stage()
    {
        var writer = new CapturingWriter();
        var service = CreateService(
            writer,
            new MatchingReader(writer),
            localHealth: new FakeLocalHealth(
                status: "degraded",
                active: false,
                lastWriteAtUtc: null,
                warningCode: "diagnostics.local_recorder_unavailable"));

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal("failed", result.Status);
        var local = result.Checks.Single(check =>
            check.Code == "local_recorder_writable");
        Assert.Equal("failed", local.Status);
        Assert.Equal("diagnostics.local_recorder_unavailable", local.WarningCode);
        Assert.Equal(
            "passed",
            result.Checks.Single(check => check.Code == "safe_event_read_back").Status);
    }

    [Fact]
    public async Task Deliberately_disabled_seq_is_neutral_when_the_mem_native_round_trip_passes()
    {
        var writer = new CapturingWriter();
        var service = CreateService(
            writer,
            new MatchingReader(writer),
            seqOptions: new SeqDiagnosticsOptions
            {
                SinkEnabled = false,
                ManagementEnabled = true
            });

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal("passed", result.Status);
        Assert.Equal(
            "disabled",
            result.Checks.Single(check => check.Code == "seq_delivery").Status);
        Assert.All(
            result.Checks.Where(check => check.Code != "seq_delivery"),
            check => Assert.Equal("passed", check.Status));
    }

    [Fact]
    public async Task Expected_but_unreachable_seq_is_failed_while_core_round_trip_still_passes()
    {
        var writer = new CapturingWriter();
        var identity = new SeqDeliveryProcessIdentity(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var service = CreateService(
            writer,
            new MatchingReader(writer),
            seqOptions: new SeqDiagnosticsOptions
            {
                SinkEnabled = true,
                ManagementEnabled = true
            },
            seqHealth: new FakeSeqHealth(
                status: "unavailable",
                configured: true,
                reachable: false,
                warningCode: "diagnostics.seq_unavailable"),
            bootstrapState: new SeqBootstrapState(
                ActiveDeliveryProcessId: identity.Value,
                ActiveDeliveryVerifiedAtUtc: Now,
                LastActiveDeliveryVerificationId: "current-process-proof"),
            processIdentity: identity);

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Equal(
            "failed",
            result.Checks.Single(check => check.Code == "seq_delivery").Status);
        Assert.All(
            result.Checks.Where(check => check.Code != "seq_delivery"),
            check => Assert.Equal("passed", check.Status));
        Assert.Contains("diagnostics.seq_unavailable", result.Warnings);
    }


    [Fact]
    public async Task Staged_seq_enablement_is_reported_as_restart_pending_without_failing_mem_native_checks()
    {
        var writer = new CapturingWriter();
        var result = await CreateService(
            writer,
            new MatchingReader(writer),
            seqOptions: new SeqDiagnosticsOptions
            {
                SinkEnabled = false,
                ManagementEnabled = true
            },
            deliveryState: new SeqDeliveryState(
                EffectiveEnabled: false,
                DesiredEnabled: true,
                RestartRequired: true,
                UpdatedAtUtc: Now,
                WarningCode: null))
            .RunAsync(CancellationToken.None);

        Assert.Equal("passed", result.Status);
        var seq = result.Checks.Single(check => check.Code == "seq_delivery");
        Assert.Equal("restart-pending", seq.Status);
        Assert.Equal("diagnostics.seq_restart_required", seq.WarningCode);
    }

    [Fact]
    public async Task Failed_startup_prerequisites_make_expected_seq_delivery_failed()
    {
        var writer = new CapturingWriter();
        var result = await CreateService(
            writer,
            new MatchingReader(writer),
            seqOptions: new SeqDiagnosticsOptions
            {
                SinkEnabled = false,
                ManagementEnabled = true
            },
            deliveryState: new SeqDeliveryState(
                EffectiveEnabled: false,
                DesiredEnabled: true,
                RestartRequired: true,
                UpdatedAtUtc: Now,
                WarningCode: "seq_delivery_startup_prerequisites_unavailable"))
            .RunAsync(CancellationToken.None);

        Assert.Equal("failed", result.Status);
        var seq = result.Checks.Single(check => check.Code == "seq_delivery");
        Assert.Equal("failed", seq.Status);
        Assert.Equal(
            "seq_delivery_startup_prerequisites_unavailable",
            seq.WarningCode);
    }


    [Fact]
    public async Task Effective_delivery_with_an_unattached_seq_sink_is_failed()
    {
        var writer = new CapturingWriter();
        var result = await CreateService(
            writer,
            new MatchingReader(writer),
            seqOptions: new SeqDiagnosticsOptions
            {
                SinkEnabled = true,
                ManagementEnabled = true
            },
            seqLoggingRuntimeState: new SeqLoggingRuntimeState(
                SinkConfigured: false,
                WarningCode: "diagnostics.seq_sink_configuration_failed"),
            deliveryState: new SeqDeliveryState(true, true, false, Now, null))
            .RunAsync(CancellationToken.None);

        Assert.Equal("failed", result.Status);
        var seq = result.Checks.Single(check => check.Code == "seq_delivery");
        Assert.Equal("failed", seq.Status);
        Assert.Equal("diagnostics.seq_sink_configuration_failed", seq.WarningCode);
    }

    [Fact]
    public async Task Active_seq_requires_current_process_verification_before_the_pipeline_check_passes()
    {
        var writer = new CapturingWriter();
        var identity = new SeqDeliveryProcessIdentity(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var result = await CreateService(
            writer,
            new MatchingReader(writer),
            seqOptions: new SeqDiagnosticsOptions
            {
                SinkEnabled = true,
                ManagementEnabled = true
            },
            seqHealth: new FakeSeqHealth(
                status: "ready",
                configured: true,
                reachable: true),
            deliveryState: new SeqDeliveryState(true, true, false, Now, null),
            bootstrapState: new SeqBootstrapState(
                ActiveDeliveryProcessId: Guid.NewGuid(),
                ActiveDeliveryVerifiedAtUtc: Now,
                LastActiveDeliveryVerificationId: "old-process-proof"),
            processIdentity: identity)
            .RunAsync(CancellationToken.None);

        Assert.Equal("passed", result.Status);
        var seq = result.Checks.Single(check => check.Code == "seq_delivery");
        Assert.Equal("verification-required", seq.Status);
        Assert.Equal(
            "diagnostics.seq_delivery_verification_required",
            seq.WarningCode);
    }

    [Fact]
    public async Task Current_process_delivery_verification_makes_the_seq_pipeline_check_pass()
    {
        var writer = new CapturingWriter();
        var identity = new SeqDeliveryProcessIdentity(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var result = await CreateService(
            writer,
            new MatchingReader(writer),
            seqOptions: new SeqDiagnosticsOptions
            {
                SinkEnabled = true,
                ManagementEnabled = true
            },
            seqHealth: new FakeSeqHealth(
                status: "ready",
                configured: true,
                reachable: true),
            deliveryState: new SeqDeliveryState(true, true, false, Now, null),
            bootstrapState: new SeqBootstrapState(
                ActiveDeliveryProcessId: identity.Value,
                ActiveDeliveryVerifiedAtUtc: Now,
                LastActiveDeliveryVerificationId: "current-process-proof"),
            processIdentity: identity)
            .RunAsync(CancellationToken.None);

        Assert.Equal("passed", result.Status);
        Assert.Equal(
            "passed",
            result.Checks.Single(check => check.Code == "seq_delivery").Status);
    }

    private static DiagnosticsPipelineSelfTestService CreateService(
        IMemDiagnosticEventWriter writer,
        IMemDiagnosticEventReader reader,
        IMemLocalLogHealthReader? localHealth = null,
        IMemDiagnosticHealthReader? storeHealth = null,
        SeqDiagnosticsOptions? seqOptions = null,
        SeqLoggingRuntimeState? seqLoggingRuntimeState = null,
        ISeqHealthReader? seqHealth = null,
        SeqDeliveryState? deliveryState = null,
        SeqBootstrapState? bootstrapState = null,
        SeqDeliveryProcessIdentity? processIdentity = null)
    {
        var effectiveOptions = seqOptions ?? new SeqDiagnosticsOptions();
        var identity = processIdentity ?? new SeqDeliveryProcessIdentity(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        return new DiagnosticsPipelineSelfTestService(
            localHealth ?? new FakeLocalHealth(),
            storeHealth ?? new FakeStoreHealth(),
            writer,
            reader,
            effectiveOptions,
            seqLoggingRuntimeState ?? new SeqLoggingRuntimeState(
                SinkConfigured: effectiveOptions.SinkEnabled,
                WarningCode: null),
            seqHealth ?? new FakeSeqHealth(),
            new FixedDeliveryStateStore(deliveryState ?? new SeqDeliveryState(
                effectiveOptions.SinkEnabled,
                effectiveOptions.SinkEnabled,
                RestartRequired: false,
                UpdatedAtUtc: null,
                WarningCode: null)),
            new FixedBootstrapStateStore(bootstrapState),
            identity,
            new FixedTimeProvider(Now),
            NullLogger<DiagnosticsPipelineSelfTestService>.Instance);
    }

    private class CapturingWriter : IMemDiagnosticEventWriter
    {
        public List<MemDiagnosticWriteRequest> Requests { get; } = [];

        public virtual Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new MemDiagnosticWriteResult(
                Stored: true,
                EventId: "evt_self_test_123456",
                IncidentId: null,
                WarningCode: null));
        }
    }

    private sealed class BlockingWriter : CapturingWriter
    {
        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }

        public override async Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Requests.Add(request);
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return new MemDiagnosticWriteResult(
                Stored: true,
                EventId: "evt_self_test_123456",
                IncidentId: null,
                WarningCode: null);
        }
    }

    private sealed class FailingWriter(string warningCode)
        : IMemDiagnosticEventWriter
    {
        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MemDiagnosticWriteResult(
                Stored: false,
                EventId: "evt_self_test_failed",
                IncidentId: null,
                WarningCode: warningCode));
    }

    private sealed class ThrowingWriter : IMemDiagnosticEventWriter
    {
        public int CallCount { get; private set; }

        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("Writer should not be called.");
        }
    }

    private sealed class MatchingReader(CapturingWriter writer)
        : IMemDiagnosticEventReader
    {
        public Task<MemDiagnosticEventPage> QueryAsync(
            MemDiagnosticQuery query,
            CancellationToken cancellationToken = default)
        {
            var request = writer.Requests.Single();
            var @event = CreateEvent(
                query.EventId ?? "evt_self_test_123456",
                request.Context?.CorrelationId);
            return Task.FromResult(Page([@event]));
        }
    }

    private sealed class EmptyReader : IMemDiagnosticEventReader
    {
        public int CallCount { get; private set; }

        public Task<MemDiagnosticEventPage> QueryAsync(
            MemDiagnosticQuery query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Page([]));
        }
    }

    private sealed class FakeLocalHealth(
        string status = "ready",
        bool active = true,
        DateTimeOffset? lastWriteAtUtc = null,
        string? warningCode = null) : IMemLocalLogHealthReader
    {
        public MemLocalLogHealth GetHealth() => new(
            Enabled: true,
            Status: status,
            PersistentRecorderConfigured: true,
            PersistentRecorderActive: active,
            PersistentFilePath: "/data/logs/control-plane/mem-control-plane-.clef",
            LastFileWriteAtUtc: lastWriteAtUtc ?? (active ? Now : null),
            RetainedFileCount: active ? 1 : 0,
            RetainedBytes: active ? 100 : 0,
            SerilogSelfLogMessageCount: 0,
            WarningCode: warningCode);
    }

    private sealed class FakeStoreHealth(bool enabled = true)
        : IMemDiagnosticHealthReader
    {
        public MemDiagnosticStoreHealth GetHealth() => new(
            Enabled: enabled,
            Status: enabled ? "ready" : "disabled",
            LastWriteAtUtc: enabled ? Now : null,
            LastReadAtUtc: null,
            StoredEventCount: 1,
            DroppedEventCount: 0,
            MalformedLineCount: 0,
            LastWriteErrorCode: null,
            LastReadWarningCode: null,
            LastRetentionRunAtUtc: null,
            LastRetentionDeletedFileCount: 0,
            LastRetentionDeletedBytes: 0,
            LastRetentionErrorCode: null,
            HasEverRecordedEvent: enabled);
    }

    private sealed class ThrowingStoreHealth : IMemDiagnosticHealthReader
    {
        public MemDiagnosticStoreHealth GetHealth() =>
            throw new IOException("Safe store unavailable.");
    }

    private sealed class FakeSeqHealth(
        string status = "optional-disabled",
        bool configured = false,
        bool reachable = false,
        string? warningCode = null) : ISeqHealthReader
    {
        public SeqHealthSnapshot GetHealth() => new(
            Status: status,
            SinkConfigured: configured,
            Reachable: reachable,
            LastCheckedAtUtc: null,
            LastSuccessAtUtc: null,
            WarningCode: warningCode);
    }


    private sealed class FixedDeliveryStateStore(SeqDeliveryState state)
        : ISeqDeliveryStateStore
    {
        public SeqDeliveryState GetState() => state;

        public Task<SeqDeliveryState> SetDesiredAsync(
            bool enabled,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedBootstrapStateStore(SeqBootstrapState? state)
        : ISeqBootstrapStateStore
    {
        public SeqBootstrapStateReadResult Read() => new(state, null);

        public Task WriteAsync(
            SeqBootstrapState next,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static MemDiagnosticEventPage Page(
        IReadOnlyList<MemDiagnosticEvent> events) =>
        new(
            FromUtc: Now.AddMinutes(-1),
            UntilUtc: Now,
            PageSize: 1,
            WindowClamped: false,
            Events: events,
            NextCursor: null,
            Warnings: []);

    private static MemDiagnosticEvent CreateEvent(
        string eventId,
        string? correlationId) =>
        new(
            SchemaVersion: 1,
            EventId: eventId,
            TimestampUtc: Now,
            Severity: MemDiagnosticSeverities.Information,
            EventCode: "diagnostics.pipeline_self_test",
            Source: nameof(DiagnosticsPipelineSelfTestService),
            Feature: "diagnostics",
            Stage: "pipeline-self-test",
            Message: "MEM diagnostics pipeline verification event.",
            IncidentId: null,
            TraceId: null,
            SpanId: null,
            RequestId: null,
            CorrelationId: correlationId,
            OperationId: null,
            Resource: null,
            Expected: null,
            Observed: null,
            Details: null,
            Exception: null,
            SuggestedAction: null,
            Retryable: false,
            RedactionsApplied: false,
            Truncated: false);
}
