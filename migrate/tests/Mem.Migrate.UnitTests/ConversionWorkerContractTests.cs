using System.Text.Json;
using Mem.Migrate.Cli;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Worker;

namespace Mem.Migrate.UnitTests;

public sealed class ConversionWorkerContractTests
{
    [Fact]
    public void Parser_accepts_only_the_versioned_request_file_contract()
    {
        var parsed = CommandLineParser.Parse([
            "worker", "convert", "--request", "/srv/mem/worker/request.json"
        ]);

        Assert.Equal("worker-convert", parsed.Name);
        Assert.Equal("/srv/mem/worker/request.json", parsed.ConversionWorkerRequestPath);
        Assert.False(parsed.ShowHelp);
    }

    [Fact]
    public void Request_requires_absolute_server_owned_paths()
    {
        var request = ValidRequest() with { ArchivePath = "relative/package.zip" };

        var exception = Assert.Throws<ArgumentException>(request.ToConversionOptions);

        Assert.Contains("absolute server-owned path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_maps_to_the_existing_conversion_engine_without_enabling_console_json()
    {
        var request = ValidRequest();

        var options = request.ToConversionOptions();

        Assert.Equal(request.ArchivePath, options.ArchivePath);
        Assert.Equal(request.WorkspacePath, options.WorkspacePath);
        Assert.Equal(request.OutputPath, options.OutputPath);
        Assert.Equal(request.ConversionId, options.ConversionId);
        Assert.Equal(request.SynapseImage, options.SynapseImage);
        Assert.Equal(request.PostgresImage, options.PostgresImage);
        Assert.True(options.JsonConsoleOutput);
    }

    [Theory]
    [InlineData("")]
    [InlineData("postgres:16")]
    [InlineData("postgres@sha256:short")]
    public void Request_rejects_mutable_or_invalid_postgres_image_identity(string postgresImage)
    {
        var request = ValidRequest() with { PostgresImage = postgresImage };

        var exception = Assert.Throws<ArgumentException>(request.ToConversionOptions);

        Assert.Contains("immutable local image ID", exception.Message, StringComparison.Ordinal);
    }


    [Theory]
    [InlineData("")]
    [InlineData("matrixdotorg/synapse:latest")]
    [InlineData("matrixdotorg/synapse@sha256:short")]
    public void Request_rejects_mutable_or_invalid_synapse_image_identity(string synapseImage)
    {
        var request = ValidRequest() with { SynapseImage = synapseImage };

        var exception = Assert.Throws<ArgumentException>(request.ToConversionOptions);

        Assert.Contains("immutable local image ID", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Protocol_emits_one_json_object_per_line_with_versioned_schema()
    {
        var writer = new StringWriter();
        var workerEvent = ConversionWorkerProtocol.CreateEvent(
            1,
            ConversionWorkerEventType.OperationStarted,
            "op-001",
            "conversion",
            "running",
            "Accepted");

        await ConversionWorkerProtocol.WriteEventAsync(
            writer, workerEvent, CancellationToken.None);

        var line = Assert.Single(writer.ToString().Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries));
        var parsed = JsonSerializer.Deserialize<ConversionWorkerEvent>(
            line, CaptureJson.Options);
        Assert.NotNull(parsed);
        Assert.Equal(ConversionWorkerEvent.CurrentSchema, parsed.Schema);
        Assert.Equal(ConversionWorkerEvent.CurrentSchemaVersion, parsed.SchemaVersion);
        Assert.Equal(ConversionWorkerEventType.OperationStarted, parsed.EventType);
    }

    private static ConversionWorkerRequest ValidRequest() => new()
    {
        OperationId = "op-001",
        ArchivePath = "/srv/mem/intake/package.memmigration.zip",
        WorkspacePath = "/srv/mem/work",
        OutputPath = "/srv/mem/output",
        ConversionId = "conversion-001",
        SynapseImage = "sha256:" + new string('b', 64),
        PostgresImage = "sha256:" + new string('a', 64)
    };
}
