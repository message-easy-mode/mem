using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Runtime;
using HostAgent.Runtime.Manifests;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Recovery;

/// <summary>
/// Host-only recovery entry point used by the developer authority migrator when
/// an active RuntimeStack row survived but its historical manifest file did not.
/// The command reconstructs one exact stack identity from durable database rows
/// and writes only the requested output file. It never matches by slug.
/// </summary>
public static class MemRuntimeManifestHostRecoveryCommand
{
    private const string Command = "runtime-manifest";
    private const string Action = "reconstruct";

    public static bool IsRequested(string[] args) =>
        args.Length >= 2 &&
        string.Equals(args[0], Command, StringComparison.Ordinal) &&
        string.Equals(args[1], Action, StringComparison.Ordinal);

    public static async Task<int> RunAsync(string[] args)
    {
        if (!TryParse(args, out var stackId, out var outputPath))
        {
            WriteUsage();
            return 2;
        }

        try
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    Args = Array.Empty<string>()
                });

            builder.Logging.ClearProviders();

            var sqlitePath = MemControlPlaneSqlitePathResolver.Resolve(
                builder.Configuration,
                builder.Environment);

            if (!File.Exists(sqlitePath))
            {
                Console.Error.WriteLine(
                    "ERROR: The MEM Control Plane database was not found. Manifest recovery will not create a database.");
                return 4;
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = sqlitePath,
                Cache = SqliteCacheMode.Shared,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

            builder.Services.AddDbContext<MemDbContext>(
                options => options.UseSqlite(connectionString));

            await using var provider = builder.Services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

            var stack = await db.RuntimeStacks
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.ServiceInstances)
                .Include(x => x.Routes)
                .SingleOrDefaultAsync(x => x.Id == stackId);

            if (stack is null)
            {
                Console.Error.WriteLine(
                    $"ERROR: Runtime stack '{stackId:D}' does not exist in the durable Control Plane database.");
                return 5;
            }

            var manifest = RuntimeStackManifestDatabaseReconstructor.Build(stack);
            var canonicalDataRoot = builder.Configuration["MEM_DATA_ROOT"];
            if (!string.IsNullOrWhiteSpace(canonicalDataRoot))
            {
                var metadata = manifest.Metadata.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);
                metadata["dataRoot"] = canonicalDataRoot;
                metadata["manifestPath"] = Path.Combine(
                    canonicalDataRoot,
                    "control-plane",
                    "runtime-stacks",
                    $"{stackId:N}.json");
                manifest = manifest with { Metadata = metadata };
            }

            if (manifest.StackId != stackId)
            {
                Console.Error.WriteLine(
                    "ERROR: Database reconstruction returned a different stack identity. No manifest was written.");
                return 6;
            }

            var fullOutputPath = Path.GetFullPath(outputPath);
            var directory = Path.GetDirectoryName(fullOutputPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                Console.Error.WriteLine("ERROR: Manifest output path must have a parent directory.");
                return 2;
            }

            Directory.CreateDirectory(directory);
            var temporary = fullOutputPath + $".mem-reconstruct-{Guid.NewGuid():N}.tmp";
            try
            {
                var json = JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    });
                await File.WriteAllTextAsync(temporary, json);
                File.Move(temporary, fullOutputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            Console.WriteLine(
                $"Reconstructed manifest for {manifest.Slug} ({manifest.StackId:D}) at {fullOutputPath}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"ERROR: Runtime manifest reconstruction failed ({exception.GetType().Name}): {exception.Message}");
            return 7;
        }
    }

    public static bool TryParse(
        string[] args,
        out Guid stackId,
        out string outputPath)
    {
        stackId = Guid.Empty;
        outputPath = string.Empty;

        if (args.Length != 4 ||
            !string.Equals(args[0], Command, StringComparison.Ordinal) ||
            !string.Equals(args[1], Action, StringComparison.Ordinal) ||
            !Guid.TryParse(args[2], out stackId) ||
            stackId == Guid.Empty ||
            string.IsNullOrWhiteSpace(args[3]))
        {
            return false;
        }

        outputPath = args[3].Trim();
        return outputPath.Length <= 4096 &&
            outputPath.All(character => !char.IsControl(character));
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine(
            "Usage: dotnet Api.dll runtime-manifest reconstruct <stack-id> <output-path>");
        Console.Error.WriteLine(
            "Reconstructs one exact active runtime manifest from durable database rows. Slug matching is never used.");
    }
}
