using System.Reflection;
using Api.Recovery;
using Carter;
using Modules.Auth.Endpoints;

namespace Api.IntegrationTests.Security;

public sealed class MemOperatorHostRecoveryCommandContractTests
{
    [Fact]
    public void Host_recovery_is_an_explicit_console_command_not_an_HTTP_route()
    {
        Assert.True(MemOperatorHostRecoveryCommand.IsRequested(
        [
            "operator",
            "reset-password",
            "admin"
        ]));

        Assert.False(MemOperatorHostRecoveryCommand.IsRequested(
        [
            "reset-password",
            "admin"
        ]));

        var endpointSource = string.Join(
            "\n",
            typeof(MemOperatorAuthEndpoints).Assembly
                .GetTypes()
                .Where(type => typeof(ICarterModule).IsAssignableFrom(type))
                .Select(type => type.FullName));

        Assert.DoesNotContain(
            "HostRecovery",
            endpointSource,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Program_checks_for_host_recovery_before_starting_the_web_application()
    {
        var programPath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "Api",
                "Program.cs"));

        Assert.True(File.Exists(programPath));
        var source = File.ReadAllText(programPath);

        var recoveryIndex = source.IndexOf(
            "MemOperatorHostRecoveryCommand.IsRequested(args)",
            StringComparison.Ordinal);
        var webBuilderIndex = source.IndexOf(
            "WebApplication.CreateBuilder(args)",
            StringComparison.Ordinal);

        Assert.True(recoveryIndex >= 0);
        Assert.True(webBuilderIndex >= 0);
        Assert.True(recoveryIndex < webBuilderIndex);
    }
}
