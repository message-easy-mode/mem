using System.Text.Json;
using Mem.Cli.Clients;
using Mem.Cli.Commands;
using Mem.Cli.Config;
using Mem.Cli.Tests.TestSupport;

namespace Mem.Cli.Tests.Commands;

[Collection("Console output")]
public sealed class StackCommandsTests
{
    [Fact]
    public async Task Inspect_human_output_excludes_internal_runtime_details()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(StackInspectPayload));

        using var client = new HostAgentClient(
            CreateOptions(json: false),
            handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => StackCommands.RunAsync(
                ["stack", "inspect", "demo-stack"],
                "inspect",
                CreateOptions(json: false),
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains("matrix.example.test", captured.StandardOutput);
        Assert.Contains("https://matrix.example.test", captured.StandardOutput);
        Assert.DoesNotContain("mem-synapse-secret-container", captured.StandardOutput);
        Assert.DoesNotContain("synapse.internal", captured.StandardOutput);
        Assert.DoesNotContain("http://synapse.internal:8008", captured.StandardOutput);
        Assert.DoesNotContain("/var/lib/mem/stacks/demo-stack", captured.StandardOutput);
        Assert.DoesNotContain("npm-private-cert-id", captured.StandardOutput);
        Assert.DoesNotContain("DOCKER_HOST", captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Fact]
    public async Task Inspect_json_output_excludes_internal_runtime_details()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(StackInspectPayload));

        using var client = new HostAgentClient(
            CreateOptions(json: true),
            handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => StackCommands.RunAsync(
                ["stack", "inspect", "demo-stack", "--json"],
                "inspect",
                CreateOptions(json: true),
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;
        var matrix = root.GetProperty("matrix");

        Assert.Equal("matrix.example.test", matrix.GetProperty("publicHost").GetString());
        Assert.Equal("https://matrix.example.test", matrix.GetProperty("publicBaseUrl").GetString());
        Assert.False(matrix.TryGetProperty("containerId", out _));
        Assert.False(matrix.TryGetProperty("containerName", out _));
        Assert.False(matrix.TryGetProperty("internalHost", out _));
        Assert.False(matrix.TryGetProperty("internalBaseUrl", out _));
        Assert.False(matrix.TryGetProperty("dataPath", out _));
        Assert.False(matrix.TryGetProperty("configPath", out _));
        Assert.False(matrix.TryGetProperty("npmCertificateId", out _));
        Assert.False(matrix.TryGetProperty("runtimeMetadata", out _));

        Assert.DoesNotContain("mem-synapse-secret-container", captured.StandardOutput);
        Assert.DoesNotContain("synapse.internal", captured.StandardOutput);
        Assert.DoesNotContain("/var/lib/mem/stacks/demo-stack", captured.StandardOutput);
        Assert.DoesNotContain("DOCKER_HOST", captured.StandardOutput);
    }

    private static CliOptions CreateOptions(bool json) =>
        new(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: json);

    private const string StackInspectPayload =
        """
        {
          "source": "control-plane",
          "status": "ok",
          "stackId": "11111111-1111-1111-1111-111111111111",
          "slug": "demo-stack",
          "lastVerifiedAtUtc": "2026-07-06T04:00:00Z",
          "matrix": {
            "serviceKey": "matrix",
            "instanceId": "22222222-2222-2222-2222-222222222222",
            "containerId": "sha256:container-id",
            "containerName": "mem-synapse-secret-container",
            "internalHost": "synapse.internal",
            "internalBaseUrl": "http://synapse.internal:8008",
            "publicHost": "matrix.example.test",
            "publicBaseUrl": "https://matrix.example.test",
            "dataPath": "/var/lib/mem/stacks/demo-stack/synapse",
            "configPath": "/var/lib/mem/stacks/demo-stack/synapse/homeserver.yaml",
            "publicRouteId": "npm-private-route-id",
            "npmCertificateId": 42,
            "runtimeMetadata": {
              "DOCKER_HOST": "unix:///var/run/docker.sock",
              "privateCertificateReference": "npm-private-cert-id"
            }
          },
          "element": {
            "serviceKey": "element",
            "instanceId": "33333333-3333-3333-3333-333333333333",
            "containerId": "sha256:element-container-id",
            "containerName": "mem-element-secret-container",
            "internalHost": "element.internal",
            "internalBaseUrl": "http://element.internal:80",
            "publicHost": "chat.example.test",
            "publicBaseUrl": "https://chat.example.test",
            "dataPath": "/var/lib/mem/stacks/demo-stack/element",
            "configPath": "/var/lib/mem/stacks/demo-stack/element/config.json",
            "publicRouteId": "npm-private-element-route-id",
            "npmCertificateId": 43,
            "runtimeMetadata": {
              "privateCertificateReference": "npm-private-cert-id"
            }
          }
        }
        """;
}
