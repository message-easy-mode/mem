using HostAgent.Runtime.Coturn;
using System.Text;
using Modules.Setup.Platform.Coturn;

namespace HostAgent.Tests.Runtime.Coturn;

public sealed class CoturnRuntimeDockerExecSecurityTests
{
    [Fact]
    public void Allocation_probe_exec_reads_temporary_credentials_from_stdin()
    {
        const string username = "1785058320:mem-platform-check";
        const string password = "temporary-turn-password";

        var exec = CoturnRuntimeService.CreateAllocationProbeExecParameters(
            "turn.example.test");
        var command = string.Join("\0", exec.Cmd ?? Array.Empty<string>());

        Assert.True(exec.AttachStdin);
        Assert.True(exec.AttachStdout);
        Assert.True(exec.AttachStderr);
        Assert.DoesNotContain(username, command, StringComparison.Ordinal);
        Assert.DoesNotContain(password, command, StringComparison.Ordinal);
        Assert.Contains("turnutils_uclient", command, StringComparison.Ordinal);
        Assert.Contains("turn.example.test", command, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_runtime_dns_exec_queries_the_public_host_without_credentials()
    {
        var exec = CoturnRuntimeService.CreateProbeDnsExecParameters(
            "turn.example.test");
        var command = string.Join("\0", exec.Cmd ?? Array.Empty<string>());

        Assert.False(exec.AttachStdin);
        Assert.True(exec.AttachStdout);
        Assert.True(exec.AttachStderr);
        Assert.Contains("getent ahosts", command, StringComparison.Ordinal);
        Assert.Contains("turn.example.test", command, StringComparison.Ordinal);
        Assert.DoesNotContain("turnutils_uclient", command, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0L, "10.10.0.193 STREAM turn.example.test", CoturnCheckStatuses.Passed)]
    [InlineData(0L, "127.0.0.1 STREAM turn.example.test", CoturnCheckStatuses.Warning)]
    [InlineData(2L, "", CoturnCheckStatuses.Warning)]
    public void Probe_runtime_dns_evidence_requires_a_non_loopback_address(
        long exitCode,
        string output,
        string expectedStatus)
    {
        var result = CoturnRuntimeService.BuildProbeRuntimeDnsCheck(
            "turn.example.test",
            exitCode,
            output);

        Assert.Equal("probe-dns", result.Key);
        Assert.Equal(expectedStatus, result.Status);

        if (string.Equals(expectedStatus, CoturnCheckStatuses.Passed, StringComparison.Ordinal))
        {
            Assert.Contains("non-loopback", result.Summary, StringComparison.Ordinal);
            Assert.Contains("10.10.0.193", result.Detail ?? string.Empty, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("could not resolve", result.Summary, StringComparison.Ordinal);
            Assert.Contains("not attempted", result.Detail ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Allocation_probe_stdin_is_bounded_to_two_newline_terminated_values()
    {
        const string username = "1785058320:mem-platform-check";
        const string password = "temporary-turn-password";

        var bytes = CoturnRuntimeService.EncodeAllocationProbeStandardInput(
            username,
            password);

        Assert.Equal(
            $"{username}\n{password}\n",
            Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Setup_boundary_does_not_expose_secret_values_or_host_paths()
    {
        var propertyNames = typeof(PlatformCoturnSetupResult)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("SecretValue", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SecretPath", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Configuration", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("StorageRoot", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("bad\nusername", "password")]
    [InlineData("username", "bad\rpassword")]
    [InlineData("", "password")]
    [InlineData("username", "")]
    public void Allocation_probe_rejects_multiline_or_empty_credentials(
        string username,
        string password)
    {
        Assert.Throws<ArgumentException>(() =>
            CoturnRuntimeService.EncodeAllocationProbeStandardInput(
                username,
                password));
    }
}
