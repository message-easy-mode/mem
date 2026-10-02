using Api.Runtime;
using Microsoft.AspNetCore.Http;

namespace Api.IntegrationTests.Runtime;

public sealed class ControlPlaneDockerMutationGuardMiddlewareTests
{
    [Theory]
    [InlineData("GET", "/api/operator/stacks")]
    [InlineData("POST", "/api/auth/login")]
    [InlineData("POST", "/api/operator/security/step-up")]
    public void Read_only_or_non_Docker_routes_do_not_require_exclusive_ownership(
        string method,
        string path)
    {
        var request = Request(method, path);

        Assert.False(
            ControlPlaneDockerMutationGuardMiddleware.RequiresExclusiveDockerOwnership(request));
    }

    [Theory]
    [InlineData("POST", "/api/setup/install")]
    [InlineData("POST", "/api/operator/stacks")]
    [InlineData("DELETE", "/api/operator/stacks/demo")]
    [InlineData("POST", "/api/operator/restores")]
    [InlineData("POST", "/api/operator/diagnostics/seq/runtime/start")]
    [InlineData("PATCH", "/api/operator/turn")]
    public void Docker_backed_mutation_routes_require_exclusive_ownership(
        string method,
        string path)
    {
        var request = Request(method, path);

        Assert.True(
            ControlPlaneDockerMutationGuardMiddleware.RequiresExclusiveDockerOwnership(request));
    }

    private static HttpRequest Request(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        return context.Request;
    }
}
