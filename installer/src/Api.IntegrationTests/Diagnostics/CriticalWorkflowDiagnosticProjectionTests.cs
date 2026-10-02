using Modules.Operator.Diagnostics.Services;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class CriticalWorkflowDiagnosticProjectionTests
{
    [Fact]
    public void Stack_scoped_diagnostic_links_prefer_the_operator_facing_slug()
    {
        var links = new DiagnosticsWorkspaceLinkBuilder();
        var resource = new MemDiagnosticResource(
            Kind: "federation",
            Id: "stack-guid-placeholder",
            StackId: Guid.NewGuid().ToString("D"),
            StackSlug: "family-chat");

        Assert.Equal(
            "/stacks/family-chat/federation",
            links.Build(resource));
        Assert.Equal(
            "/stacks/family-chat/services",
            links.Build(resource with { Kind = "turn" }));
    }
}
