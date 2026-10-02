using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Conversion;
using Mem.Migrate.Core.Processes;
using Mem.Migrate.Core.Security;
using Microsoft.Data.Sqlite;

namespace Mem.Migrate.Infrastructure.Conversion;

public sealed class SynapseConversionService(
    IMigrationArchiveReader archiveReader,
    IVerifiedArchiveExtractor archiveExtractor,
    IProcessRunner processRunner,
    IBinaryProcessRunner binaryProcessRunner,
    IConversionJournal journal) : ISynapseConversionService
{
    private static readonly string[] ReconciliationTables =
    [
        "users", "profiles", "devices", "access_tokens", "rooms",
        "room_aliases", "events", "event_json", "current_state_events",
        "room_memberships", "state_groups", "local_media_repository",
        "remote_media_cache", "background_updates"
    ];

    public async Task<ConversionReport> ConvertAsync(
        ConversionOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        PrivateFilePermissions.EnsureDirectory(normalized.WorkspacePath);
        PrivateFilePermissions.EnsureDirectory(normalized.OutputPath);
        await journal.InitializeAsync(cancellationToken);

        var verification = await archiveReader.VerifyAsync(
            normalized.ArchivePath,
            normalized.ToSafetyLimits(),
            cancellationToken);
        if (!verification.Valid || verification.Manifest is null)
        {
            throw new InvalidDataException(
                verification.Findings.FirstOrDefault()?.Message
                ?? "The migration archive is invalid.");
        }

        var manifest = verification.Manifest;
        var stack = SelectStack(manifest, normalized.StackId);
        var conversionId = normalized.ConversionId!;
        var existing = await journal.GetAsync(conversionId, cancellationToken);
        if (existing is not null)
        {
            if (!normalized.Resume)
            {
                throw new InvalidOperationException(
                    "The conversion ID already exists. Use --resume to return a completed result or retry a failed attempt.");
            }

            if (!string.Equals(existing.ArchiveSha256, verification.InputSha256, StringComparison.OrdinalIgnoreCase) ||
                existing.SourceStackId != stack.SourceStackId)
            {
                throw new InvalidOperationException(
                    "The resumed conversion inputs do not match the durable journal.");
            }

            if (existing.Status == ConversionLifecycleStatus.Completed &&
                !string.IsNullOrWhiteSpace(existing.ReportJson))
            {
                return JsonSerializer.Deserialize<ConversionReport>(
                    existing.ReportJson,
                    CaptureJson.Options)
                    ?? throw new InvalidDataException("The stored conversion report is invalid.");
            }
        }
        else
        {
            await journal.StartAsync(
                conversionId,
                DateTimeOffset.UtcNow,
                verification.InputSha256,
                stack.SourceStackId,
                cancellationToken);
        }

        var started = existing?.StartedAtUtc ?? DateTimeOffset.UtcNow;
        var attemptRoot = Path.Combine(normalized.WorkspacePath, "conversions", conversionId);
        var privateRoot = Path.Combine(attemptRoot, "private");
        var sqliteWorkRoot = Path.Combine(privateRoot, "sqlite-work");
        var secretsRoot = Path.Combine(privateRoot, "secrets");
        var reportRoot = Path.Combine(normalized.OutputPath, conversionId);
        if (existing is not null && existing.Status != ConversionLifecycleStatus.Completed)
        {
            DeleteOwnedDirectory(attemptRoot, conversionId);
            DeleteOwnedDirectory(reportRoot, conversionId);
        }
        var databasePath = Path.Combine(sqliteWorkRoot, "homeserver.db");
        var signingPath = Path.Combine(secretsRoot, "signing.key");
        var postgresConfigPath = Path.Combine(privateRoot, "postgres.yaml");
        var postgresDataPath = Path.Combine(privateRoot, "postgres-data");

        PrivateFilePermissions.EnsureDirectory(privateRoot);
        PrivateFilePermissions.EnsureDirectory(sqliteWorkRoot);
        PrivateFilePermissions.EnsureDirectory(secretsRoot);
        PrivateFilePermissions.EnsureDirectory(postgresDataPath);
        PrivateFilePermissions.EnsureDirectory(reportRoot);
        var dumpPath = Path.Combine(reportRoot, $"{stack.Slug}-synapse.sql.dump");
        var evidencePath = Path.Combine(reportRoot, "conversion-evidence.json");
        var reportPath = Path.Combine(reportRoot, "conversion-report.json");
        var logPath = Path.Combine(reportRoot, "synapse-port-db.log");

        var suffix = ShortHash(conversionId);
        var networkName = $"mem-migrate-{suffix}";
        var postgresName = $"mem-migrate-pg-{suffix}";
        var postgresAlias = "postgres";
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var resourcesCreated = false;

        try
        {
            EnsureDiskHeadroom(attemptRoot, manifest.Limits.ExpandedBytes, normalized.RequiredFreeSpaceMultiplier);
            await archiveExtractor.ExtractFilesAsync(
                normalized.ArchivePath,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [stack.SqlitePath] = databasePath,
                    [stack.SigningKeyPath] = signingPath
                },
                cancellationToken);

            var sourceCounts = ReadSqliteCounts(databasePath);
            var signingHash = await Sha256File.ComputeAsync(signingPath, cancellationToken);
            var usesCapturedSourceImage = string.IsNullOrWhiteSpace(normalized.SynapseImage);
            var synapseImage = usesCapturedSourceImage
                ? stack.SynapseImageId
                : normalized.SynapseImage;
            if (string.IsNullOrWhiteSpace(synapseImage))
            {
                throw new InvalidOperationException(
                    "The captured stack has no immutable Synapse image identity. Supply --synapse-image with an approved digest.");
            }

            await RequireImageAsync(
                normalized,
                synapseImage,
                usesCapturedSourceImage ? stack.SynapseImageId : null,
                cancellationToken);
            await RequireImageAsync(normalized, normalized.PostgresImage, expectedImageId: null, cancellationToken);
            await RunRequiredAsync(normalized, ["network", "create", "--internal", "--label", $"mem.migrate.conversion={conversionId}", networkName], cancellationToken);
            resourcesCreated = true;
            await RunRequiredAsync(normalized,
            [
                "run", "-d", "--pull", "never", "--name", postgresName,
                "--network", networkName, "--network-alias", postgresAlias,
                "--label", $"mem.migrate.conversion={conversionId}",
                "-v", $"{postgresDataPath}:/var/lib/postgresql/data",
                "-e", "POSTGRES_USER=synapse", "-e", $"POSTGRES_PASSWORD={password}",
                "-e", "POSTGRES_DB=synapse", "-e", "POSTGRES_INITDB_ARGS=--encoding=UTF8 --locale=C",
                normalized.PostgresImage
            ], cancellationToken);
            await WaitForPostgresAsync(normalized, postgresName, cancellationToken);

            await File.WriteAllTextAsync(
                postgresConfigPath,
                BuildPostgresConfiguration(stack.MatrixServerName, postgresAlias, password),
                new UTF8Encoding(false),
                cancellationToken);
            PrivateFilePermissions.EnsureFile(postgresConfigPath);

            var portResult = await processRunner.RunAsync(
                new ProcessRequest(
                    normalized.DockerCommand,
                    BuildPortDatabaseDockerArguments(
                        networkName,
                        conversionId,
                        sqliteWorkRoot,
                        postgresConfigPath,
                        signingPath,
                        synapseImage,
                        normalized.BatchSize),
                    TimeSpan.FromSeconds(normalized.CommandTimeoutSeconds)),
                cancellationToken);
            await File.WriteAllTextAsync(
                logPath,
                AssessmentRedactor.RedactText(portResult.StandardOutput + Environment.NewLine + portResult.StandardError),
                new UTF8Encoding(false),
                cancellationToken);
            PrivateFilePermissions.EnsureFile(logPath);
            if (!portResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"synapse_port_db failed (exit={portResult.ExitCode}, timeout={portResult.TimedOut}). See the redacted conversion log.");
            }

            var sequenceRepairWarning = await ReconcileToDeviceSequenceAsync(
                normalized,
                postgresName,
                cancellationToken);

            var targetCounts = await ReadPostgresCountsAsync(normalized, postgresName, cancellationToken);
            var reconciliation = ReconciliationTables
                .Select(table => new ConversionTableCount(
                    table,
                    sourceCounts.GetValueOrDefault(table),
                    targetCounts.GetValueOrDefault(table)))
                .ToArray();
            var mismatches = reconciliation.Where(item => item.SourceRows != item.TargetRows).ToArray();
            if (mismatches.Length > 0)
            {
                throw new InvalidOperationException(
                    "Converted PostgreSQL row reconciliation failed: " +
                    string.Join(", ", mismatches.Select(item => $"{item.Table} {item.SourceRows}->{item.TargetRows}")));
            }

            var dump = await binaryProcessRunner.RunToFileAsync(
                new BinaryProcessRequest(
                    normalized.DockerCommand,
                    ["exec", postgresName, "pg_dump", "-U", "synapse", "-d", "synapse", "-Fc"],
                    dumpPath,
                    TimeSpan.FromSeconds(normalized.CommandTimeoutSeconds)),
                cancellationToken);
            if (!dump.Succeeded)
            {
                throw new InvalidOperationException(
                    $"PostgreSQL dump failed (exit={dump.ExitCode}, timeout={dump.TimedOut}).");
            }
            PrivateFilePermissions.EnsureFile(dumpPath);
            var dumpHash = await Sha256File.ComputeAsync(dumpPath, cancellationToken);

            var warnings = new List<string>();
            if (sequenceRepairWarning is not null)
            {
                warnings.Add(sequenceRepairWarning);
            }
            if (manifest.Capture.RehearsalOnly)
            {
                warnings.Add(
                    "The source archive is rehearsal-only. This conversion must not be used as a final cutover artifact.");
            }

            var resourcesRetained = normalized.KeepResources;
            if (resourcesCreated && !normalized.KeepResources)
            {
                var cleanupWarning = await CleanupAsync(
                    normalized,
                    conversionId,
                    postgresName,
                    networkName,
                    postgresDataPath);
                resourcesCreated = false;
                if (cleanupWarning is not null)
                {
                    warnings.Add(cleanupWarning);
                    resourcesRetained = true;
                }
            }

            var completed = DateTimeOffset.UtcNow;
            var warningArray = warnings.ToArray();
            var evidence = new ConversionEvidence(
                "mem-synapse-conversion-evidence", 1, conversionId, manifest.MigrationId,
                stack.SourceStackId, stack.MatrixServerName, verification.InputSha256,
                synapseImage, normalized.PostgresImage, signingHash, started, completed,
                reconciliation, warningArray);
            await WriteJsonAsync(evidencePath, evidence, cancellationToken);
            var report = new ConversionReport(
                "mem-synapse-conversion-report", 1, conversionId,
                ConversionLifecycleStatus.Completed, started, completed,
                manifest.MigrationId, stack.SourceStackId, stack.MatrixServerName,
                normalized.ArchivePath, verification.InputSha256, synapseImage,
                normalized.PostgresImage, dumpPath, dumpHash, dump.OutputBytes,
                evidencePath, resourcesRetained, reconciliation, warningArray,
                [
                    "Run MM-04B private candidate startup against this PostgreSQL dump.",
                    "Do not expose the candidate through public ingress.",
                    "Retain the original verified MM-03 archive unchanged."
                ]);
            var reportJson = JsonSerializer.Serialize(report, CaptureJson.Options);
            await File.WriteAllTextAsync(reportPath, reportJson, new UTF8Encoding(false), cancellationToken);
            PrivateFilePermissions.EnsureFile(reportPath);
            await journal.CompleteAsync(report, reportJson, cancellationToken);
            return report;
        }
        catch (Exception ex)
        {
            await journal.FailAsync(
                conversionId,
                DateTimeOffset.UtcNow,
                "conversion_failed",
                ex.Message,
                CancellationToken.None);
            throw;
        }
        finally
        {
            TryDelete(postgresConfigPath);
            TryDelete(databasePath + "-wal");
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-journal");
            TryDelete(databasePath);
            TryDelete(signingPath);
            if (resourcesCreated && !normalized.KeepResources)
            {
                await CleanupAsync(
                    normalized,
                    conversionId,
                    postgresName,
                    networkName,
                    postgresDataPath);
            }
        }
    }

    private async Task RequireImageAsync(ConversionOptions options, string image, string? expectedImageId, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            new ProcessRequest(options.DockerCommand, ["image", "inspect", image, "--format", "{{.Id}}"], TimeSpan.FromSeconds(60)),
            cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Required image '{image}' is not available locally. Automatic pulls are prohibited.");
        }
        if (!string.IsNullOrWhiteSpace(expectedImageId) &&
            !string.Equals(result.StandardOutput.Trim(), expectedImageId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected Synapse image does not match the immutable image identity captured in MM-03.");
        }
    }

    private async Task WaitForPostgresAsync(ConversionOptions options, string container, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.ReadinessTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await processRunner.RunAsync(
                new ProcessRequest(options.DockerCommand, ["exec", container, "pg_isready", "-U", "synapse", "-d", "synapse"], TimeSpan.FromSeconds(15)),
                cancellationToken);
            if (result.Succeeded) return;
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        throw new InvalidOperationException("The isolated PostgreSQL conversion target did not become ready.");
    }

    private async Task<string?> ReconcileToDeviceSequenceAsync(
        ConversionOptions options,
        string container,
        CancellationToken cancellationToken)
    {
        var before = await ReadToDeviceSequenceStateAsync(
            options,
            container,
            cancellationToken);
        var requiredValue = CalculateRequiredSequenceValue(
            before.LastValue,
            before.IsCalled,
            before.TableMaximum,
            before.StreamPositionMaximum);
        if (requiredValue <= before.AllocatedValue)
        {
            return null;
        }

        await RunRequiredAsync(
            options,
            [
                "exec", container, "psql",
                "-v", "ON_ERROR_STOP=1",
                "-U", "synapse", "-d", "synapse",
                "-At", "-c", BuildSequenceSetValueSql(requiredValue)
            ],
            cancellationToken);

        var after = await ReadToDeviceSequenceStateAsync(
            options,
            container,
            cancellationToken);
        if (after.AllocatedValue < requiredValue ||
            after.AllocatedValue < after.TableMaximum ||
            after.AllocatedValue < after.StreamPositionMaximum)
        {
            throw new InvalidOperationException(
                "The post-port device_inbox_sequence repair did not produce a consistent PostgreSQL state.");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Applied deterministic Synapse port-db sequence repair for upstream issue #19467: device_inbox_sequence advanced from {before.AllocatedValue} to {after.AllocatedValue} to cover stream_positions.to_device={before.StreamPositionMaximum}. No stream-position rows were deleted.");
    }

    private async Task<SequenceConsistencyState> ReadToDeviceSequenceStateAsync(
        ConversionOptions options,
        string container,
        CancellationToken cancellationToken)
    {
        var result = await RunRequiredAsync(
            options,
            [
                "exec", container, "psql",
                "-v", "ON_ERROR_STOP=1",
                "-U", "synapse", "-d", "synapse",
                "-At", "-F", "|",
                "-c", BuildToDeviceSequenceInspectionSql()
            ],
            cancellationToken);

        var values = result.StandardOutput.Trim().Split('|');
        if (values.Length != 4 ||
            !long.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lastValue) ||
            !TryParsePostgresBoolean(values[1], out var isCalled) ||
            !long.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tableMaximum) ||
            !long.TryParse(values[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var streamPositionMaximum))
        {
            throw new InvalidDataException(
                "Could not parse the post-port device_inbox_sequence consistency state.");
        }

        return new SequenceConsistencyState(
            lastValue,
            isCalled,
            isCalled ? lastValue : lastValue - 1,
            tableMaximum,
            streamPositionMaximum);
    }

    private static long CalculateRequiredSequenceValue(
        long lastValue,
        bool isCalled,
        long tableMaximum,
        long streamPositionMaximum)
    {
        var allocatedValue = isCalled ? lastValue : lastValue - 1;
        return Math.Max(
            Math.Max(allocatedValue, tableMaximum),
            streamPositionMaximum);
    }

    private static string BuildToDeviceSequenceInspectionSql() =>
        "SELECT last_value, is_called, " +
        "GREATEST(" +
        "COALESCE((SELECT MAX(stream_id) FROM device_inbox), 0), " +
        "COALESCE((SELECT MAX(stream_id) FROM device_federation_outbox), 0)), " +
        "COALESCE((SELECT MAX(stream_id) FROM stream_positions WHERE stream_name = 'to_device'), 0) " +
        "FROM device_inbox_sequence;";

    private static string BuildSequenceSetValueSql(long value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"SELECT setval('device_inbox_sequence', {value}, true);");

    private static bool TryParsePostgresBoolean(string value, out bool result)
    {
        if (string.Equals(value, "t", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }
        if (string.Equals(value, "f", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private async Task<Dictionary<string, long>> ReadPostgresCountsAsync(ConversionOptions options, string container, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in ReconciliationTables)
        {
            var presence = await RunRequiredAsync(options,
                ["exec", container, "psql", "-U", "synapse", "-d", "synapse", "-At", "-c", $"SELECT to_regclass('public.{table}') IS NOT NULL;"],
                cancellationToken);
            if (!string.Equals(presence.StandardOutput.Trim(), "t", StringComparison.OrdinalIgnoreCase))
            {
                result[table] = 0;
                continue;
            }

            var countResult = await RunRequiredAsync(options,
                ["exec", container, "psql", "-U", "synapse", "-d", "synapse", "-At", "-c", $"SELECT count(*) FROM {table};"],
                cancellationToken);
            if (!long.TryParse(countResult.StandardOutput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            {
                throw new InvalidDataException($"Could not parse PostgreSQL row count for '{table}'.");
            }
            result[table] = count;
        }
        return result;
    }

    private static Dictionary<string, long> ReadSqliteCounts(string path)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Cache=Private");
        connection.Open();
        foreach (var table in ReconciliationTables)
        {
            using var exists = connection.CreateCommand();
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
            exists.Parameters.AddWithValue("$name", table);
            if (Convert.ToInt64(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            {
                result[table] = 0;
                continue;
            }
            using var count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            result[table] = Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        return result;
    }

    private async Task<ProcessResult> RunRequiredAsync(ConversionOptions options, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            new ProcessRequest(options.DockerCommand, arguments, TimeSpan.FromSeconds(options.CommandTimeoutSeconds)),
            cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Docker operation failed: {string.Join(" ", arguments.Take(3))} (exit={result.ExitCode}, timeout={result.TimedOut}).");
        }
        return result;
    }

    private async Task<string?> CleanupAsync(
        ConversionOptions options,
        string conversionId,
        string container,
        string network,
        string postgresDataPath)
    {
        var failures = new List<string>();

        try
        {
            var removeContainer = await processRunner.RunAsync(
                new ProcessRequest(
                    options.DockerCommand,
                    ["rm", "-f", container],
                    TimeSpan.FromSeconds(60)),
                CancellationToken.None);
            if (!removeContainer.Succeeded &&
                !removeContainer.StandardError.Contains(
                    "No such container",
                    StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("PostgreSQL container removal failed");
            }
        }
        catch
        {
            failures.Add("PostgreSQL container removal failed");
        }

        try
        {
            if (Directory.Exists(postgresDataPath))
            {
                var clearData = await processRunner.RunAsync(
                    new ProcessRequest(
                        options.DockerCommand,
                        BuildPostgresDataCleanupDockerArguments(
                            conversionId,
                            postgresDataPath,
                            options.PostgresImage),
                        TimeSpan.FromSeconds(120)),
                    CancellationToken.None);
                if (!clearData.Succeeded)
                {
                    failures.Add("PostgreSQL data-directory cleanup failed");
                }
                else
                {
                    DeleteEmptyOwnedDirectory(postgresDataPath, conversionId);
                }
            }
        }
        catch
        {
            failures.Add("PostgreSQL data-directory cleanup failed");
        }

        try
        {
            var removeNetwork = await processRunner.RunAsync(
                new ProcessRequest(
                    options.DockerCommand,
                    ["network", "rm", network],
                    TimeSpan.FromSeconds(60)),
                CancellationToken.None);
            if (!removeNetwork.Succeeded &&
                !removeNetwork.StandardError.Contains(
                    "not found",
                    StringComparison.OrdinalIgnoreCase) &&
                !removeNetwork.StandardError.Contains(
                    "No such network",
                    StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("conversion-network removal failed");
            }
        }
        catch
        {
            failures.Add("conversion-network removal failed");
        }

        return failures.Count == 0
            ? null
            : "The conversion completed, but temporary resource cleanup was incomplete: " +
              string.Join("; ", failures.Distinct(StringComparer.Ordinal)) +
              ". Review migration-owned Docker resources and the private conversion workspace.";
    }

    private static MigrationArchiveStack SelectStack(MigrationArchiveManifest manifest, Guid? requested)
    {
        if (requested is not null)
        {
            return manifest.Stacks.SingleOrDefault(stack => stack.SourceStackId == requested.Value)
                ?? throw new InvalidOperationException("The selected stack is not present in the archive.");
        }
        return manifest.Stacks.Length == 1
            ? manifest.Stacks[0]
            : throw new InvalidOperationException("The archive contains multiple stacks. Supply --stack-id.");
    }

    private static void EnsureDiskHeadroom(string path, long expandedBytes, double multiplier)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? throw new IOException("The conversion workspace has no filesystem root.");
        var available = new DriveInfo(root).AvailableFreeSpace;
        var required = Math.Max(512L * 1024 * 1024, checked((long)Math.Ceiling(expandedBytes * multiplier)));
        if (available < required)
        {
            throw new IOException($"Insufficient conversion workspace disk. Required {required} bytes; available {available} bytes.");
        }
    }

    private static IReadOnlyList<string> BuildPortDatabaseDockerArguments(
        string networkName,
        string conversionId,
        string sqliteWorkRoot,
        string postgresConfigPath,
        string signingPath,
        string synapseImage,
        int batchSize)
    {
        return
        [
            "run", "--rm", "--pull", "never", "--network", networkName,
            "--read-only", "--tmpfs", "/tmp:rw,nosuid,nodev,size=256m",
            "--label", $"mem.migrate.conversion={conversionId}",
            "--entrypoint", "synapse_port_db",
            "-v", $"{sqliteWorkRoot}:/input/sqlite:rw",
            "-v", $"{postgresConfigPath}:/input/postgres.yaml:ro",
            "-v", $"{signingPath}:/input/signing.key:ro",
            synapseImage,
            "--sqlite-database", "/input/sqlite/homeserver.db",
            "--postgres-config", "/input/postgres.yaml",
            "--batch-size", batchSize.ToString(CultureInfo.InvariantCulture)
        ];
    }

    private static IReadOnlyList<string> BuildPostgresDataCleanupDockerArguments(
        string conversionId,
        string postgresDataPath,
        string postgresImage)
    {
        return
        [
            "run", "--rm", "--pull", "never", "--network", "none",
            "--read-only", "--tmpfs", "/tmp:rw,nosuid,nodev,size=16m",
            "--label", $"mem.migrate.conversion={conversionId}",
            "--entrypoint", "/bin/sh",
            "-v", $"{postgresDataPath}:/cleanup:rw",
            postgresImage,
            "-c", "rm -rf -- /cleanup/* /cleanup/.[!.]* /cleanup/..?*"
        ];
    }

    private static string BuildPostgresConfiguration(
        string serverName,
        string host,
        string password)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            throw new InvalidOperationException(
                "The captured stack has no Matrix server_name for synapse_port_db configuration.");
        }

        var encodedServerName = JsonSerializer.Serialize(serverName);
        return $"""
        server_name: {encodedServerName}
        report_stats: false
        media_store_path: "/tmp/media_store"
        signing_key_path: "/input/signing.key"
        suppress_key_server_warning: true
        database:
          name: psycopg2
          args:
            user: synapse
            password: "{password}"
            database: synapse
            host: {host}
            port: 5432
            cp_min: 5
            cp_max: 10
        """;
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, CaptureJson.Options), new UTF8Encoding(false), cancellationToken);
        PrivateFilePermissions.EnsureFile(path);
    }

    private sealed record SequenceConsistencyState(
        long LastValue,
        bool IsCalled,
        long AllocatedValue,
        long TableMaximum,
        long StreamPositionMaximum);

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    private static void DeleteOwnedDirectory(string path, string conversionId)
    {
        if (!Directory.Exists(path)) return;
        var fullPath = EnsureOwnedDirectory(path, conversionId);
        Directory.Delete(fullPath, recursive: true);
    }

    private static void DeleteEmptyOwnedDirectory(string path, string conversionId)
    {
        if (!Directory.Exists(path)) return;
        var fullPath = EnsureOwnedDirectory(path, conversionId);
        Directory.Delete(fullPath, recursive: false);
    }

    private static string EnsureOwnedDirectory(string path, string conversionId)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.Contains(
                $"{Path.DirectorySeparatorChar}conversions{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) &&
            !string.Equals(
                Path.GetFileName(fullPath),
                conversionId,
                StringComparison.Ordinal))
        {
            throw new IOException(
                "Refused to delete a directory that is not owned by an MM-04 conversion attempt.");
        }

        return fullPath;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
