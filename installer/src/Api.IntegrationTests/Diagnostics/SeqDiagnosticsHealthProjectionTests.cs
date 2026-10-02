using System.Security.Claims;
using Modules.Auth.Identity;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Services;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqDiagnosticsHealthProjectionTests
{
    [Fact]
    public void Platform_owner_receives_only_the_authoritative_seq_ui_url()
    {
        var options = new SeqDiagnosticsOptions
        {
            SinkEnabled = true,
            ManagementEnabled = true,
            IngestionUrl = "http://seq:5341",
            HealthUrl = "http://seq:80",
            UiUrl = "https://seq.example.test/"
        };
        var service = new DiagnosticsLoggingHealthService(
            new ReadyLocalHealth(),
            new ReadyStoreHealth(),
            options,
            new ReadySeqHealth(),
            new DiagnosticsCapabilityService(),
            TimeProvider.System);

        var owner = service.Get(Principal(MemOperatorRoles.PlatformOwner));
        var operation = service.Get(Principal(MemOperatorRoles.Operator));

        Assert.Equal("https://seq.example.test", owner.Seq.ServerUrl);
        Assert.Equal("ready", owner.Seq.Status);
        Assert.True(owner.Seq.Reachable);
        Assert.True(owner.SafeEventStore.HasEverRecordedEvent);
        Assert.Null(operation.Seq.ServerUrl);
        Assert.DoesNotContain("seq:5341", System.Text.Json.JsonSerializer.Serialize(owner));
    }


    [Fact]
    public void Guided_bootstrap_UI_authority_is_used_by_logging_health_after_setup()
    {
        var options = new SeqDiagnosticsOptions
        {
            SinkEnabled = false,
            ManagementEnabled = false,
            UiUrl = null
        };
        var provider = new SeqEffectiveConfigurationProvider(
            options,
            new FixedBootstrapStateStore(new SeqBootstrapState(
                ManagementEnabled: true,
                EulaAccepted: true,
                SelectedHostPort: 15341,
                PrivateUiUrl: "http://127.0.0.1:15341",
                RuntimeVerifiedAtUtc: DateTimeOffset.UtcNow)));
        var service = new DiagnosticsLoggingHealthService(
            new ReadyLocalHealth(),
            new ReadyStoreHealth(),
            options,
            new ReadySeqHealth(),
            new DiagnosticsCapabilityService(),
            TimeProvider.System,
            provider);

        var owner = service.Get(Principal(MemOperatorRoles.PlatformOwner));

        Assert.Equal("http://127.0.0.1:15341", owner.Seq.ServerUrl);
        Assert.True(owner.Seq.ManagementEnabled);
    }

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, role)
        ],
        "test"));

    private sealed class ReadySeqHealth : ISeqHealthReader
    {
        public SeqHealthSnapshot GetHealth() => new(
            "ready",
            SinkConfigured: true,
            Reachable: true,
            LastCheckedAtUtc: DateTimeOffset.UtcNow,
            LastSuccessAtUtc: DateTimeOffset.UtcNow,
            WarningCode: null);
    }

    private sealed class ReadyLocalHealth : IMemLocalLogHealthReader
    {
        public MemLocalLogHealth GetHealth() => new(
            Enabled: true,
            Status: "ready",
            PersistentRecorderConfigured: true,
            PersistentRecorderActive: true,
            PersistentFilePath: "/data/logs/control-plane/mem-control-plane-.clef",
            LastFileWriteAtUtc: DateTimeOffset.UtcNow,
            RetainedFileCount: 1,
            RetainedBytes: 100,
            SerilogSelfLogMessageCount: 0,
            WarningCode: null);
    }

    private sealed class FixedBootstrapStateStore(SeqBootstrapState state)
        : ISeqBootstrapStateStore
    {
        public SeqBootstrapStateReadResult Read() => new(state, null);

        public Task WriteAsync(
            SeqBootstrapState updated,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ReadyStoreHealth : IMemDiagnosticHealthReader
    {
        public MemDiagnosticStoreHealth GetHealth() => new(
            Enabled: true,
            Status: "ready",
            LastWriteAtUtc: DateTimeOffset.UtcNow,
            LastReadAtUtc: DateTimeOffset.UtcNow,
            StoredEventCount: 1,
            DroppedEventCount: 0,
            MalformedLineCount: 0,
            LastWriteErrorCode: null,
            LastReadWarningCode: null,
            LastRetentionRunAtUtc: null,
            LastRetentionDeletedFileCount: 0,
            LastRetentionDeletedBytes: 0,
            LastRetentionErrorCode: null,
            HasEverRecordedEvent: true);
    }
}
