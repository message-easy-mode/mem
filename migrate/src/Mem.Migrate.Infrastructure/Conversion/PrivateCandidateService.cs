using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Conversion;
using Mem.Migrate.Core.Processes;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Infrastructure.Conversion;

public sealed class PrivateCandidateService(
    IMigrationArchiveReader archiveReader,
    IVerifiedArchiveExtractor archiveExtractor,
    IProcessRunner processRunner) : IPrivateCandidateService
{
    public async Task<CandidateReport> VerifyAsync(CandidateOptions options, CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        PrivateFilePermissions.EnsureDirectory(normalized.WorkspacePath);
        PrivateFilePermissions.EnsureDirectory(normalized.OutputPath);

        var verification = await archiveReader.VerifyAsync(normalized.ArchivePath, normalized.ToSafetyLimits(), cancellationToken);
        if (!verification.Valid || verification.Manifest is null)
            throw new InvalidDataException(verification.Findings.FirstOrDefault()?.Message ?? "The migration archive is invalid.");

        var conversion = await ReadConversionReportAsync(normalized.ConversionReportPath, cancellationToken);
        var manifest = verification.Manifest;
        var stack = manifest.Stacks.SingleOrDefault(item => item.SourceStackId == conversion.SourceStackId)
            ?? throw new InvalidDataException("The conversion report stack is not present in the verified archive.");
        ValidateConversion(conversion, verification.InputSha256, stack);

        var candidateId = normalized.CandidateId!;
        var started = DateTimeOffset.UtcNow;
        var attemptRoot = Path.Combine(normalized.WorkspacePath, "candidates", candidateId);
        var privateRoot = Path.Combine(attemptRoot, "private");
        var postgresDataPath = Path.Combine(privateRoot, "postgres-data");
        var configRoot = Path.Combine(privateRoot, "candidate");
        var mediaRoot = Path.Combine(configRoot, "media_store");
        var signingPath = Path.Combine(configRoot, "signing.key");
        var configPath = Path.Combine(configRoot, "homeserver.yaml");
        var reportRoot = Path.Combine(normalized.OutputPath, candidateId);
        var evidencePath = Path.Combine(reportRoot, "candidate-evidence.json");
        var reportPath = Path.Combine(reportRoot, "candidate-report.json");
        var logPath = Path.Combine(reportRoot, "candidate-synapse.log");

        if (Directory.Exists(attemptRoot) || Directory.Exists(reportRoot))
            throw new IOException("The candidate ID already exists. Use a fresh candidate ID.");

        PrivateFilePermissions.EnsureDirectory(postgresDataPath);
        PrivateFilePermissions.EnsureDirectory(mediaRoot);
        PrivateFilePermissions.EnsureDirectory(reportRoot);

        var extraction = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [stack.SigningKeyPath] = signingPath
        };
        if (!string.IsNullOrWhiteSpace(stack.MediaPath))
        {
            var prefix = stack.MediaPath.TrimEnd('/') + "/";
            foreach (var file in manifest.IncludedFiles.Where(file => file.Path.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var relative = file.Path[prefix.Length..];
                extraction[file.Path] = Path.Combine(mediaRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            }
        }
        await archiveExtractor.ExtractFilesAsync(normalized.ArchivePath, extraction, cancellationToken);

        var signingHash = await Sha256File.ComputeAsync(signingPath, cancellationToken);
        var signingKeyId = ReadSigningKeyId(signingPath);
        var usesCapturedSourceImage = string.IsNullOrWhiteSpace(normalized.SynapseImage);
        var synapseImage = usesCapturedSourceImage
            ? conversion.SynapseImage
            : normalized.SynapseImage;
        await RequireImageAsync(
            normalized,
            synapseImage,
            usesCapturedSourceImage ? stack.SynapseImageId : null,
            cancellationToken);
        await RequireImageAsync(normalized, normalized.PostgresImage, null, cancellationToken);

        var suffix = ShortHash(candidateId);
        var networkName = $"mem-migrate-candidate-{suffix}";
        var postgresName = $"mem-migrate-candidate-pg-{suffix}";
        var synapseName = $"mem-migrate-candidate-synapse-{suffix}";
        var postgresAlias = "candidate-postgres";
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var resourcesCreated = false;
        var warnings = new List<string>();

        try
        {
            await RunRequiredAsync(normalized, ["network", "create", "--internal", "--label", $"mem.migrate.candidate={candidateId}", networkName], cancellationToken);
            resourcesCreated = true;
            await RunRequiredAsync(normalized,
            [
                "run", "-d", "--pull", "never", "--name", postgresName,
                "--network", networkName, "--network-alias", postgresAlias,
                "--label", $"mem.migrate.candidate={candidateId}",
                "-v", $"{postgresDataPath}:/var/lib/postgresql/data:rw",
                "-e", "POSTGRES_USER=synapse", "-e", $"POSTGRES_PASSWORD={password}",
                "-e", "POSTGRES_DB=synapse", "-e", "POSTGRES_INITDB_ARGS=--encoding=UTF8 --locale=C",
                normalized.PostgresImage
            ], cancellationToken);
            await WaitForPostgresAsync(normalized, postgresName, cancellationToken);

            await RunRequiredAsync(normalized, ["cp", conversion.PostgreSqlDumpPath, $"{postgresName}:/tmp/input.dump"], cancellationToken);
            await RunRequiredAsync(normalized,
                ["exec", postgresName, "pg_restore", "-U", "synapse", "-d", "synapse", "--no-owner", "--no-privileges", "/tmp/input.dump"],
                cancellationToken);

            var restoredCounts = await ReadPostgresCountsAsync(normalized, postgresName, conversion.TableCounts.Select(item => item.Table), cancellationToken);
            var reconciliation = conversion.TableCounts
                .Select(item => new ConversionTableCount(item.Table, item.TargetRows, restoredCounts.GetValueOrDefault(item.Table)))
                .ToArray();
            var countMismatch = reconciliation.Where(item => item.SourceRows != item.TargetRows).ToArray();
            if (countMismatch.Length > 0)
                throw new InvalidOperationException("Restored candidate PostgreSQL reconciliation failed: " + string.Join(", ", countMismatch.Select(item => $"{item.Table} {item.SourceRows}->{item.TargetRows}")));

            await File.WriteAllTextAsync(configPath, BuildCandidateConfiguration(stack.MatrixServerName, postgresAlias, password), new UTF8Encoding(false), cancellationToken);
            PrivateFilePermissions.EnsureFile(configPath);

            await RunRequiredAsync(normalized,
            [
                "run", "-d", "--pull", "never", "--name", synapseName,
                "--network", networkName, "--read-only", "--user", "0:0",
                "--tmpfs", "/tmp:rw,nosuid,nodev,size=256m",
                "--label", $"mem.migrate.candidate={candidateId}",
                "--entrypoint", "synapse_homeserver",
                "-v", $"{configPath}:/candidate/homeserver.yaml:ro",
                "-v", $"{signingPath}:/candidate/signing.key:ro",
                "-v", $"{mediaRoot}:/candidate/media_store:rw",
                synapseImage, "-c", "/candidate/homeserver.yaml"
            ], cancellationToken);

            var versionsJson = await WaitForCandidateAsync(normalized, synapseName, cancellationToken);
            var keyJson = await ProbeJsonAsync(normalized, synapseName, "http://127.0.0.1:8008/_matrix/key/v2/server", cancellationToken);
            var reportedServerName = keyJson.RootElement.GetProperty("server_name").GetString() ?? string.Empty;
            var verifyKeyIds = keyJson.RootElement.GetProperty("verify_keys").EnumerateObject().Select(item => item.Name).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            var publishedPorts = await RunRequiredAsync(normalized, ["inspect", synapseName, "--format", "{{json .NetworkSettings.Ports}}"], cancellationToken);
            var noPorts = PublishedPortsAreAbsent(publishedPorts.StandardOutput);

            var identity = new CandidateIdentityEvidence(stack.MatrixServerName, signingHash, signingKeyId, reportedServerName, verifyKeyIds);
            var identityValid = string.Equals(reportedServerName, stack.MatrixServerName, StringComparison.Ordinal) && verifyKeyIds.Contains(signingKeyId, StringComparer.Ordinal);
            var go = identityValid && noPorts;
            if (!go) throw new InvalidOperationException("The private candidate identity or no-public-port gate failed.");
            if (manifest.Capture.RehearsalOnly)
                warnings.Add("The source archive is rehearsal-only. This candidate is evidence only and must not be activated publicly.");

            var logs = await processRunner.RunAsync(new ProcessRequest(normalized.DockerCommand, ["logs", synapseName], TimeSpan.FromSeconds(60)), CancellationToken.None);
            await File.WriteAllTextAsync(logPath, AssessmentRedactor.RedactText(logs.StandardOutput + Environment.NewLine + logs.StandardError), new UTF8Encoding(false), cancellationToken);
            PrivateFilePermissions.EnsureFile(logPath);

            var completed = DateTimeOffset.UtcNow;
            var evidence = new CandidateEvidence(
                "mem-synapse-private-candidate-evidence", 1, candidateId, conversion.ConversionId,
                manifest.MigrationId, stack.SourceStackId, verification.InputSha256,
                conversion.PostgreSqlDumpSha256, synapseImage, normalized.PostgresImage,
                started, completed, identity, reconciliation,
                versionsJson.RootElement.TryGetProperty("versions", out _), true, noPorts, false, warnings.ToArray());
            await WriteJsonAsync(evidencePath, evidence, cancellationToken);

            var report = new CandidateReport(
                "mem-synapse-private-candidate-report", 1, candidateId, CandidateLifecycleStatus.Completed,
                started, completed, conversion.ConversionId, manifest.MigrationId, stack.SourceStackId,
                stack.MatrixServerName, normalized.ArchivePath, verification.InputSha256,
                conversion.PostgreSqlDumpPath, conversion.PostgreSqlDumpSha256,
                synapseImage, normalized.PostgresImage, evidencePath, logPath,
                normalized.KeepResources, true, identity, reconciliation, warnings.ToArray(),
                [
                    "Review candidate evidence and logs.",
                    "Do not create public ingress for this rehearsal candidate.",
                    "Proceed to MM-05 only after the MM-04 go/no-go evidence is accepted."
                ]);
            await WriteJsonAsync(reportPath, report, cancellationToken);
            return report;
        }
        catch
        {
            try
            {
                var logs = await processRunner.RunAsync(new ProcessRequest(normalized.DockerCommand, ["logs", synapseName], TimeSpan.FromSeconds(60)), CancellationToken.None);
                if (logs.Started)
                {
                    await File.WriteAllTextAsync(logPath, AssessmentRedactor.RedactText(logs.StandardOutput + Environment.NewLine + logs.StandardError), new UTF8Encoding(false), CancellationToken.None);
                    PrivateFilePermissions.EnsureFile(logPath);
                }
            }
            catch { }
            throw;
        }
        finally
        {
            if (resourcesCreated && !normalized.KeepResources)
                await CleanupAsync(normalized, candidateId, synapseName, postgresName, networkName, postgresDataPath, CancellationToken.None);
        }
    }

    private static async Task<ConversionReport> ReadConversionReportAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The MM-04A conversion report was not found.", path);
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ConversionReport>(stream, CaptureJson.Options, cancellationToken)
            ?? throw new InvalidDataException("The MM-04A conversion report is invalid.");
    }

    private static void ValidateConversion(ConversionReport report, string archiveSha256, MigrationArchiveStack stack)
    {
        if (report.Status != ConversionLifecycleStatus.Completed) throw new InvalidDataException("The MM-04A conversion report is not completed.");
        if (!string.Equals(report.ArchiveSha256, archiveSha256, StringComparison.OrdinalIgnoreCase) || report.SourceStackId != stack.SourceStackId)
            throw new InvalidDataException("The MM-04A conversion report does not match the verified archive and stack.");
        if (!File.Exists(report.PostgreSqlDumpPath)) throw new FileNotFoundException("The converted PostgreSQL dump was not found.", report.PostgreSqlDumpPath);
        var actual = Sha256File.ComputeAsync(report.PostgreSqlDumpPath, CancellationToken.None).GetAwaiter().GetResult();
        if (!string.Equals(actual, report.PostgreSqlDumpSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The PostgreSQL dump checksum does not match the MM-04A report.");
    }

    private async Task RequireImageAsync(CandidateOptions options, string image, string? expectedImageId, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(new ProcessRequest(options.DockerCommand, ["image", "inspect", image, "--format", "{{.Id}}"], TimeSpan.FromSeconds(60)), cancellationToken);
        if (!result.Succeeded) throw new InvalidOperationException($"Required image '{image}' is not available locally. Automatic pulls are prohibited.");
        if (!string.IsNullOrWhiteSpace(expectedImageId) && !string.Equals(result.StandardOutput.Trim(), expectedImageId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected Synapse image does not match the immutable MM-03 image identity.");
    }

    private async Task WaitForPostgresAsync(CandidateOptions options, string container, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.ReadinessTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await processRunner.RunAsync(new ProcessRequest(options.DockerCommand, ["exec", container, "pg_isready", "-U", "synapse", "-d", "synapse"], TimeSpan.FromSeconds(15)), cancellationToken);
            if (result.Succeeded) return;
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        throw new InvalidOperationException("The private candidate PostgreSQL target did not become ready.");
    }

    private async Task<JsonDocument> WaitForCandidateAsync(CandidateOptions options, string container, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.ReadinessTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try { return await ProbeJsonAsync(options, container, "http://127.0.0.1:8008/_matrix/client/versions", cancellationToken); }
            catch (InvalidOperationException) { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
        }
        throw new InvalidOperationException("The private Synapse candidate did not become healthy before the timeout.");
    }

    private async Task<JsonDocument> ProbeJsonAsync(CandidateOptions options, string container, string url, CancellationToken cancellationToken)
    {
        var code = "import urllib.request; print(urllib.request.urlopen('" + url + "', timeout=10).read().decode('utf-8'))";
        var result = await processRunner.RunAsync(new ProcessRequest(options.DockerCommand, ["exec", container, "python", "-c", code], TimeSpan.FromSeconds(20)), cancellationToken);
        if (!result.Succeeded) throw new InvalidOperationException("The private candidate HTTP probe failed.");
        return JsonDocument.Parse(result.StandardOutput);
    }

    private async Task<Dictionary<string, long>> ReadPostgresCountsAsync(CandidateOptions options, string container, IEnumerable<string> tables, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var result = await RunRequiredAsync(options, ["exec", container, "psql", "-U", "synapse", "-d", "synapse", "-At", "-c", $"SELECT count(*) FROM {table};"], cancellationToken);
            if (!long.TryParse(result.StandardOutput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                throw new InvalidDataException($"Could not parse candidate PostgreSQL row count for '{table}'.");
            counts[table] = count;
        }
        return counts;
    }

    private async Task<ProcessResult> RunRequiredAsync(CandidateOptions options, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(new ProcessRequest(options.DockerCommand, arguments, TimeSpan.FromSeconds(options.CommandTimeoutSeconds)), cancellationToken);
        if (!result.Succeeded) throw new InvalidOperationException($"Docker operation failed: {string.Join(" ", arguments.Take(3))} (exit={result.ExitCode}, timeout={result.TimedOut}).");
        return result;
    }

    private async Task CleanupAsync(CandidateOptions options, string candidateId, string synapse, string postgres, string network, string postgresDataPath, CancellationToken cancellationToken)
    {
        foreach (var container in new[] { synapse, postgres })
            await processRunner.RunAsync(new ProcessRequest(options.DockerCommand, ["rm", "-f", container], TimeSpan.FromSeconds(60)), cancellationToken);
        if (Directory.Exists(postgresDataPath))
        {
            await processRunner.RunAsync(new ProcessRequest(options.DockerCommand,
                ["run", "--rm", "--pull", "never", "--network", "none", "--read-only", "--tmpfs", "/tmp:rw,nosuid,nodev,size=16m", "--label", $"mem.migrate.candidate={candidateId}", "--entrypoint", "/bin/sh", "-v", $"{postgresDataPath}:/cleanup:rw", options.PostgresImage, "-c", "rm -rf -- /cleanup/* /cleanup/.[!.]* /cleanup/..?*"],
                TimeSpan.FromSeconds(120)), cancellationToken);
            try { Directory.Delete(postgresDataPath); } catch { }
        }
        await processRunner.RunAsync(new ProcessRequest(options.DockerCommand, ["network", "rm", network], TimeSpan.FromSeconds(60)), cancellationToken);
    }

    private static string BuildCandidateConfiguration(string serverName, string postgresHost, string password)
    {
        var encodedServerName = JsonSerializer.Serialize(serverName);
        return $"""
        server_name: {encodedServerName}
        public_baseurl: "https://{serverName}/"
        report_stats: false
        pid_file: "/tmp/homeserver.pid"
        media_store_path: "/candidate/media_store"
        signing_key_path: "/candidate/signing.key"
        macaroon_secret_key: "{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}"
        registration_shared_secret: "{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}"
        suppress_key_server_warning: true
        listeners:
          - port: 8008
            bind_addresses: ['0.0.0.0']
            type: http
            tls: false
            resources:
              - names: [client, federation]
                compress: false
        database:
          name: psycopg2
          args:
            user: synapse
            password: "{password}"
            database: synapse
            host: {postgresHost}
            port: 5432
            cp_min: 5
            cp_max: 10
        """;
    }

    private static bool PublishedPortsAreAbsent(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "null" : json);
        if (document.RootElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return true;
        if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() > 0)
                return false;
            if (property.Value.ValueKind != JsonValueKind.Null && property.Value.ValueKind != JsonValueKind.Array)
                return false;
        }
        return true;
    }

    private static string ReadSigningKeyId(string path)
    {
        var parts = File.ReadLines(path).First().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new InvalidDataException("The captured signing key is malformed.");
        return $"{parts[0]}:{parts[1]}";
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, CaptureJson.Options), new UTF8Encoding(false), cancellationToken);
        PrivateFilePermissions.EnsureFile(path);
    }

    private static string ShortHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();
}
