using Mem.Cli.Output;
using Mem.Localization;

namespace Mem.Cli.Tests.Output;

public sealed class CliHelpTests
{
    [Fact]
    public void Write_renders_german_human_headings_but_preserves_command_grammar()
    {
        var localizer = new MemLocalizer(
            MemLanguage.German);
        using var output = new StringWriter();

        CliHelp.Write(
            output,
            localizer);

        var rendered = output.ToString();

        Assert.Contains("Message Easy Mode CLI", rendered);
        Assert.DoesNotContain("Matrix Easy Mode CLI", rendered);
        Assert.Contains("Verwendung:", rendered);
        Assert.Contains("Optionen:", rendered);
        Assert.Contains("Umgebung:", rendered);
        Assert.DoesNotContain("Geplant:", rendered);
        Assert.DoesNotContain("Planned:", rendered);
        Assert.Contains("mem config get language", rendered);
        Assert.Contains("mem config set language <en|de>", rendered);
        Assert.Contains("mem config get default-profile", rendered);
        Assert.Contains("mem profile create <name> --server <url>", rendered);
        Assert.Contains("mem profile select <name>", rendered);
        Assert.Contains("mem login --device", rendered);
        Assert.Contains("mem account show", rendered);
        Assert.Contains("mem logout", rendered);
        Assert.Contains("Befehle:", rendered);
        Assert.Contains("Normale Autorisierung: Verwenden Sie mem login --device", rendered);
        Assert.Contains("Sitzung anzeigen: mem account show", rendered);
        Assert.Contains("Befehlsnamen, Flags, Umgebungsvariablen", rendered);
        Assert.Contains("JSON-Felder", rendered);
        Assert.DoesNotContain("Normal authority: use mem login --device", rendered);
        Assert.Contains("mem backups list", rendered);
        Assert.Contains("--profile <name>", rendered);
        Assert.Contains("Benanntes lokales Profil verwenden.", rendered);
        Assert.Contains("--server <url>", rendered);
        Assert.Contains("Profilserver für einen Befehl überschreiben.", rendered);
        Assert.DoesNotContain("--installer-token", rendered);
        Assert.DoesNotContain("--host-agent-url", rendered);
        Assert.Contains("--language <en|de>", rendered);
        Assert.Contains("--json", rendered);
        Assert.Contains("Stabile maschinenlesbare JSON-Ausgabe verwenden.", rendered);
        Assert.Contains("MEM_SERVER_URL", rendered);
        Assert.Contains("Standardserver für Befehle ohne Profil.", rendered);
        Assert.DoesNotContain("MEM_HOST_AGENT_URL", rendered);
        Assert.Contains("MEM_CLI_LANGUAGE", rendered);
        Assert.Contains("Sprache für menschenlesbare Ausgabe in dieser Shell-Sitzung.", rendered);
        Assert.DoesNotContain("MEM_INSTALLER_TOKEN", rendered);
        Assert.DoesNotContain("--agent-secret", rendered);
        Assert.DoesNotContain("MEM_AGENT_SECRET", rendered);
        Assert.DoesNotContain("mem stack compare", rendered);
    }

    [Theory]
    [InlineData("host", "mem host status", "mem stack list")]
    [InlineData("stack", "mem stack list", "mem backups list")]
    [InlineData("backups", "mem backups list", "mem restores list")]
    [InlineData("restores", "mem restores list", "mem host status")]
    public void WriteGroup_renders_contextual_commands_and_common_options(
        string command,
        string expectedCommand,
        string unrelatedCommand)
    {
        var localizer = new MemLocalizer(MemLanguage.English);
        using var output = new StringWriter();

        CliHelp.WriteGroup(output, localizer, command);

        var rendered = output.ToString();

        Assert.Contains("Message Easy Mode CLI", rendered);
        Assert.Contains("Usage:", rendered);
        Assert.Contains("Commands:", rendered);
        Assert.Contains(expectedCommand, rendered);
        Assert.DoesNotContain(unrelatedCommand, rendered);
        Assert.Contains("Options:", rendered);
        Assert.Contains("--profile <name>", rendered);
        Assert.Contains("--server <url>", rendered);
        Assert.Contains("--language <en|de>", rendered);
        Assert.Contains("--json", rendered);
    }
}
