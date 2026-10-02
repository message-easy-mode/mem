using System.Diagnostics;

namespace Mem.Cli.Tests;

public sealed class HostCommandInstallScriptTests
{
    private static string ScriptPath => Path.Combine(
        AppContext.BaseDirectory,
        "Scripts",
        "install-host-command.sh");

    [Fact]
    public void Installer_script_is_available_to_the_test_project()
    {
        Assert.True(
            File.Exists(ScriptPath),
            $"Expected the host command installer script to be copied to the test output: {ScriptPath}");
    }

    [Fact]
    public void Installer_script_documents_the_host_command_contract_without_secret_fallbacks()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("/opt/mem/cli", script, StringComparison.Ordinal);
        Assert.Contains("/usr/local/bin/mem", script, StringComparison.Ordinal);
        Assert.Contains("root:root", script, StringComparison.Ordinal);
        Assert.Contains("0755", script, StringComparison.Ordinal);
        Assert.Contains("secret-tool", script, StringComparison.Ordinal);
        Assert.Contains("libsecret-tools", script, StringComparison.Ordinal);
        Assert.DoesNotContain("MEM_INSTALLER_TOKEN", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--installer-token", script, StringComparison.Ordinal);
        Assert.DoesNotContain("X-MEM-Agent-Secret", script, StringComparison.Ordinal);
        Assert.DoesNotContain("plaintext", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Installer_script_help_and_dry_run_do_not_require_root_or_modify_the_host()
    {
        var help = await RunBashAsync(ScriptPath, "--help");

        Assert.Equal(0, help.ExitCode);
        Assert.Contains("Install MEM CLI host command", help.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("--binary <path>", help.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("--version <version>", help.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("--version 0.2.0", help.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("--version 0.1.1", help.StandardOutput, StringComparison.Ordinal);

        using var temp = new TemporaryDirectory();
        var fakeBinary = Path.Combine(temp.Path, "mem");
        await File.WriteAllTextAsync(fakeBinary, "#!/usr/bin/env sh\necho mem\n");

        var dryRun = await RunBashAsync(
            ScriptPath,
            "--dry-run",
            "--binary",
            fakeBinary,
            "--version",
            "test-0.1.1",
            "--install-root",
            Path.Combine(temp.Path, "opt", "mem", "cli"),
            "--link-path",
            Path.Combine(temp.Path, "usr", "local", "bin", "mem"),
            "--skip-secret-tool-check");

        Assert.Equal(0, dryRun.ExitCode);
        Assert.Contains("Dry-run mode", dryRun.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("would install", dryRun.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "opt")));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "usr")));
    }

    private static async Task<ProcessResult> RunBashAsync(
        string script,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(script);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start bash.");

        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new ProcessResult(
            process.ExitCode,
            standardOutput,
            standardError);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"mem-cli-host-install-tests-{Guid.NewGuid():N}");

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
