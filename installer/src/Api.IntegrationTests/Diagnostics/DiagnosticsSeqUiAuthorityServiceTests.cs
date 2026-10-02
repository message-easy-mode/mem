using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Modules.Auth.Identity;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;
using Modules.Operator.Diagnostics.Services;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsSeqUiAuthorityServiceTests
{
    [Fact]
    public async Task Verified_managed_runtime_can_persist_and_clear_a_safe_UI_authority()
    {
        var operationId = Guid.NewGuid();
        var store = new RecordingBootstrapStateStore(new SeqBootstrapState(
            ManagementEnabled: true,
            EulaAccepted: true,
            SelectedHostPort: 15341,
            SetupStage: "completed",
            RuntimeVerifiedAtUtc: DateTimeOffset.UtcNow,
            LastOperationId: operationId));
        var service = new DiagnosticsSeqUiAuthorityService(
            store,
            new FixedRuntimeReader(ManagedRuntime()),
            TimeProvider.System);

        var configured = await service.UpdateAsync(
            new DiagnosticsSeqUiAuthorityUpdateRequest("http://127.0.0.1:15341"),
            Principal(),
            CancellationToken.None);

        Assert.True(configured.Configured);
        Assert.Equal("http://127.0.0.1:15341/", configured.Url);
        Assert.Equal(configured.Url, store.State.PrivateUiUrl);
        Assert.Equal(operationId, store.State.LastOperationId);
        Assert.True(store.State.EulaAccepted);

        var cleared = await service.UpdateAsync(
            new DiagnosticsSeqUiAuthorityUpdateRequest(null),
            Principal(),
            CancellationToken.None);

        Assert.False(cleared.Configured);
        Assert.Null(cleared.Url);
        Assert.Null(store.State.PrivateUiUrl);
    }

    [Theory]
    [InlineData("http://0.0.0.0:15341")]
    [InlineData("http://[::]:15341")]
    [InlineData("https://operator:secret@seq.example.test")]
    [InlineData("https://seq.example.test/?token=secret")]
    public async Task Unsafe_UI_authorities_are_rejected_without_persisting_them(string value)
    {
        var original = new SeqBootstrapState(
            ManagementEnabled: true,
            EulaAccepted: true,
            SelectedHostPort: 15341,
            RuntimeVerifiedAtUtc: DateTimeOffset.UtcNow);
        var store = new RecordingBootstrapStateStore(original);
        var service = new DiagnosticsSeqUiAuthorityService(
            store,
            new FixedRuntimeReader(ManagedRuntime()),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            service.UpdateAsync(
                new DiagnosticsSeqUiAuthorityUpdateRequest(value),
                Principal(),
                CancellationToken.None));

        Assert.Equal("seq_ui_authority_invalid", exception.Code);
        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Same(original, store.State);
    }


    [Fact]
    public async Task Non_owner_cannot_change_the_UI_authority()
    {
        var store = new RecordingBootstrapStateStore(new SeqBootstrapState(
            ManagementEnabled: true,
            EulaAccepted: true,
            SelectedHostPort: 15341,
            RuntimeVerifiedAtUtc: DateTimeOffset.UtcNow));
        var service = new DiagnosticsSeqUiAuthorityService(
            store,
            new FixedRuntimeReader(ManagedRuntime()),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            service.UpdateAsync(
                new DiagnosticsSeqUiAuthorityUpdateRequest("http://127.0.0.1:15341"),
                new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
                    new Claim(ClaimTypes.Role, MemOperatorRoles.Operator)
                ],
                "test")),
                CancellationToken.None));

        Assert.Equal("seq_ui_authority_owner_required", exception.Code);
        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
        Assert.Null(store.State.PrivateUiUrl);
    }

    [Fact]
    public async Task Unverified_or_unmanaged_runtime_cannot_gain_a_browser_authority()
    {
        var unverified = new DiagnosticsSeqUiAuthorityService(
            new RecordingBootstrapStateStore(new SeqBootstrapState(
                ManagementEnabled: true,
                EulaAccepted: true,
                SelectedHostPort: 15341)),
            new FixedRuntimeReader(ManagedRuntime()),
            TimeProvider.System);

        var unverifiedException = await Assert.ThrowsAsync<SeqOperationException>(() =>
            unverified.UpdateAsync(
                new DiagnosticsSeqUiAuthorityUpdateRequest("http://127.0.0.1:15341"),
                Principal(),
                CancellationToken.None));
        Assert.Equal("seq_ui_authority_requires_verified_runtime", unverifiedException.Code);

        var managedState = new RecordingBootstrapStateStore(new SeqBootstrapState(
            ManagementEnabled: true,
            EulaAccepted: true,
            SelectedHostPort: 15341,
            RuntimeVerifiedAtUtc: DateTimeOffset.UtcNow));
        var unmanaged = new DiagnosticsSeqUiAuthorityService(
            managedState,
            new FixedRuntimeReader(ManagedRuntime() with
            {
                Managed = false,
                OwnershipState = "unmanaged-conflict"
            }),
            TimeProvider.System);

        var unmanagedException = await Assert.ThrowsAsync<SeqOperationException>(() =>
            unmanaged.UpdateAsync(
                new DiagnosticsSeqUiAuthorityUpdateRequest("http://127.0.0.1:15341"),
                Principal(),
                CancellationToken.None));
        Assert.Equal("seq_ui_authority_requires_managed_runtime", unmanagedException.Code);
    }

    private static ClaimsPrincipal Principal() =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
        ],
        "test"));

    private static SeqStatusResponse ManagedRuntime() => new(
        "seq",
        "mem-seq",
        "2026.1.17044",
        "/data/seq",
        15341,
        Exists: true,
        Running: true,
        State: "running",
        Image: $"sha256:{new string('a', 64)}",
        UsesApprovedRuntime: true,
        Warnings: [],
        Managed: true,
        OwnershipState: "managed",
        WarningCode: null);

    private sealed class RecordingBootstrapStateStore(SeqBootstrapState state)
        : ISeqBootstrapStateStore
    {
        public SeqBootstrapState State { get; private set; } = state;

        public SeqBootstrapStateReadResult Read() => new(State, null);

        public Task WriteAsync(
            SeqBootstrapState updated,
            CancellationToken cancellationToken)
        {
            State = updated;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedRuntimeReader(SeqStatusResponse response)
        : ISeqRuntimeStatusReader
    {
        public Task<SeqStatusResponse> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
