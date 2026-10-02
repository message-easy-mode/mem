using HostAgent.Runtime.Backups.StandardRecreate;

namespace HostAgent.Tests.Runtime.Backups.StandardRecreate;

public sealed class CatalogStandardRecreatePreflightInputTests
{
    [Fact]
    public void ToPreflightRequest_preserves_explicit_replacement_target_values()
    {
        var input = new CatalogStandardRecreatePreflightInput(
            TargetStackSlug: "demo-stack-restored",
            RequestedDomainId: "6a2f6679-6cf8-4571-b7e0-857ef3519e91",
            MatrixHost: "matrix-demo-stack.deltabox.dev",
            ElementHost: "chat-demo-stack-restored.deltabox.dev");

        var request = input.ToPreflightRequest();

        Assert.Equal("demo-stack-restored", request.TargetStackSlug);
        Assert.Equal("6a2f6679-6cf8-4571-b7e0-857ef3519e91", request.RequestedDomainId);
        Assert.Equal("matrix-demo-stack.deltabox.dev", request.MatrixHost);
        Assert.Equal("chat-demo-stack-restored.deltabox.dev", request.ElementHost);
    }

    [Fact]
    public void ToPreflightRequest_allows_matrix_host_to_be_omitted()
    {
        var input = new CatalogStandardRecreatePreflightInput(
            TargetStackSlug: "demo-stack-restored",
            RequestedDomainId: null,
            MatrixHost: null,
            ElementHost: "chat-demo-stack-restored.deltabox.dev");

        var request = input.ToPreflightRequest();

        Assert.Null(request.RequestedDomainId);
        Assert.Null(request.MatrixHost);
        Assert.Equal("demo-stack-restored", request.TargetStackSlug);
        Assert.Equal("chat-demo-stack-restored.deltabox.dev", request.ElementHost);
    }
}
