using System.Text.Json;
using Mem.Cli.Config;
using Mem.Cli.Output;
using Mem.Localization;

namespace Mem.Cli.Tests.Output;

public sealed class CliOutputTests
{
    [Fact]
    public void WriteJson_does_not_construct_or_read_a_localizer()
    {
        var factoryCalls = 0;
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();

        var output = new CliOutput(
            json: true,
            localizerFactory: () =>
            {
                factoryCalls++;
                throw new InvalidOperationException(
                    "JSON output must not construct a localizer.");
            },
            standardOutput: standardOutput,
            standardError: standardError);

        output.WriteJson(new JsonProjection(
            CatalogEntryId: "bkp_20260702_example",
            Status: "ok"));

        Assert.Equal(0, factoryCalls);
        Assert.Empty(standardError.ToString());

        using var document = JsonDocument.Parse(
            standardOutput.ToString());

        Assert.Equal(
            "bkp_20260702_example",
            document.RootElement
                .GetProperty("catalogEntryId")
                .GetString());
        Assert.Equal(
            "ok",
            document.RootElement
                .GetProperty("status")
                .GetString());
    }

    [Fact]
    public void WriteLocalizedLine_uses_the_configured_language()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();

        var output = CliOutput.Create(
            new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: "mem_cli_test_token",
                Json: false,
                Language: MemLanguage.German),
            standardOutput,
            standardError);

        output.WriteLocalizedLine(
            CliMessageKeys.HelpUsage);

        Assert.Equal(
            "Verwendung:" + Environment.NewLine,
            standardOutput.ToString());
        Assert.Empty(standardError.ToString());
    }

    [Fact]
    public void WriteHumanErrorLine_preserves_current_cli_usage_error_behaviour_when_json_is_requested()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();

        var output = new CliOutput(
            json: true,
            localizerFactory: () => throw new InvalidOperationException(),
            standardOutput: standardOutput,
            standardError: standardError);

        output.WriteHumanErrorLine("A usage error remains human-readable.");

        Assert.Empty(standardOutput.ToString());
        Assert.Equal(
            "A usage error remains human-readable." + Environment.NewLine,
            standardError.ToString());
    }

    private sealed record JsonProjection(
        string CatalogEntryId,
        string Status);
}
