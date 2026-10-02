using System.Diagnostics;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.ArchiveTests;

public sealed class UnixFileTypeSafetyTests
{
    [Fact]
    public async Task Rejects_fifo_and_hard_link_source_entries_on_linux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-migrate-file-type-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var regular = Path.Combine(root, "regular.txt");
            var hardLink = Path.Combine(root, "hard-link.txt");
            var fifo = Path.Combine(root, "named-pipe");
            await File.WriteAllTextAsync(regular, "payload");
            await RunAsync("ln", regular, hardLink);
            await RunAsync("mkfifo", fifo);

            Assert.Throws<InvalidDataException>(() =>
                UnixFileTypeSafety.EnsureRegularFile(hardLink));
            Assert.Throws<InvalidDataException>(() =>
                UnixFileTypeSafety.EnsureRegularFile(fifo));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task RunAsync(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Could not start test prerequisite '{fileName}'.");
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 0,
            $"{fileName} failed with exit code {process.ExitCode}: {error}");
    }
}
