using Mem.Migrate.Core.Assessment;

namespace Mem.Migrate.Infrastructure.FileSystem;

public sealed class LegacyFileSystemProbe : ILegacyFileSystemProbe
{
    public Task<LegacyFileSystemObservation> ProbeAsync(
        AssessmentOptions options,
        DockerInventoryObservation docker,
        LegacyDatabaseObservation database,
        CancellationToken cancellationToken)
    {
        var candidate = database.Candidates
            .OrderByDescending(x => x.ExactSupportedSchema)
            .ThenByDescending(x => x.AppSchemaPresent)
            .FirstOrDefault();

        if (candidate is null)
        {
            return Task.FromResult(
                new LegacyFileSystemObservation(
                    Stacks: [],
                    ObservedAtUtc: DateTimeOffset.UtcNow));
        }

        var observations = new List<LegacyStackFileObservation>();

        foreach (var stack in candidate.Stacks.OrderBy(x => x.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var matrix = candidate.Services.FirstOrDefault(service =>
                service.StackId == stack.Id
                && string.Equals(
                    service.ServiceKey,
                    "matrix",
                    StringComparison.OrdinalIgnoreCase));

            if (matrix is null)
            {
                continue;
            }

            var matrixContainer = ResolveContainer(matrix, docker);
            var dataRoot = ResolveDataRoot(matrix, matrixContainer);
            var findings = new List<AssessmentFinding>();

            if (string.IsNullOrWhiteSpace(dataRoot))
            {
                findings.Add(
                    new AssessmentFinding(
                        "matrix_data_root_unresolved",
                        FindingSeverity.Blocker,
                        $"The Matrix data root for stack '{stack.Slug}' could not be resolved.",
                        "Restore the service runtime metadata or supply the source path explicitly in a later capture."));
            }

            var homeserverPath = string.IsNullOrWhiteSpace(dataRoot)
                ? string.Empty
                : Path.Combine(dataRoot, "homeserver.yaml");
            var homeserverFile = ObserveFile(homeserverPath);

            var parsed = HomeserverConfigurationParser.Parse(
                homeserverPath,
                options.MaximumConfigurationBytes);

            var databasePath = ResolveConfigurationPath(
                parsed.DatabasePath,
                dataRoot,
                homeserverPath,
                "homeserver.db");
            var sqliteFile = ObserveFile(databasePath);

            if (!sqliteFile.Exists)
            {
                findings.Add(
                    new AssessmentFinding(
                        "synapse_sqlite_missing",
                        FindingSeverity.Blocker,
                        $"The Synapse SQLite database for stack '{stack.Slug}' was not found.",
                        "Verify the homeserver database configuration and source bind mount."));
            }

            if (!string.Equals(
                    parsed.DatabaseEngine,
                    "sqlite3",
                    StringComparison.OrdinalIgnoreCase)
                && sqliteFile.Exists)
            {
                findings.Add(
                    new AssessmentFinding(
                        "synapse_database_engine_inferred",
                        FindingSeverity.Warning,
                        $"The homeserver database engine for stack '{stack.Slug}' was inferred from homeserver.db.",
                        "Review homeserver.yaml before capture."));
            }

            var signingKeyPath = ResolveSigningKeyPath(
                parsed.SigningKeyPath,
                dataRoot,
                homeserverPath);
            var signingKey = ObserveFile(signingKeyPath);

            if (!signingKey.Exists)
            {
                findings.Add(
                    new AssessmentFinding(
                        "synapse_signing_key_missing",
                        FindingSeverity.Blocker,
                        $"The Synapse signing key for stack '{stack.Slug}' was not found.",
                        "Locate and restore the original signing key before migration."));
            }

            var mediaPath = ResolveConfigurationPath(
                parsed.MediaStorePath,
                dataRoot,
                homeserverPath,
                "media_store");
            var media = ObserveDirectory(
                mediaPath,
                options.MaximumFileScanEntries,
                cancellationToken);

            if (!media.Exists)
            {
                findings.Add(
                    new AssessmentFinding(
                        "synapse_media_store_missing",
                        FindingSeverity.Warning,
                        $"The media store for stack '{stack.Slug}' was not found.",
                        "Confirm whether the installation has no media or uses a custom media path."));
            }

            var element = candidate.Services.FirstOrDefault(service =>
                service.StackId == stack.Id
                && string.Equals(
                    service.ServiceKey,
                    "element-web",
                    StringComparison.OrdinalIgnoreCase));
            var elementContainer = element is null
                ? null
                : ResolveContainer(element, docker);
            var elementDataRoot = element is null
                ? null
                : ResolveElementDataRoot(element, elementContainer);
            var elementConfigPath = ResolveElementConfigurationPath(
                elementDataRoot,
                elementContainer);
            var elementConfiguration = element is null
                ? null
                : ObserveFile(elementConfigPath);

            if (element is not null && elementConfiguration?.Exists != true)
            {
                findings.Add(
                    new AssessmentFinding(
                        "element_configuration_missing",
                        FindingSeverity.Warning,
                        $"The Element configuration for stack '{stack.Slug}' was not found.",
                        "Review the Element bind mount and service data path."));
            }

            observations.Add(
                new LegacyStackFileObservation(
                    StackId: stack.Id,
                    MatrixServiceId: matrix.Id,
                    MatrixContainerId: matrixContainer?.Id,
                    DataRoot: dataRoot,
                    HomeserverConfiguration: homeserverFile,
                    SqliteDatabase: sqliteFile,
                    SigningKey: signingKey,
                    MediaStore: media,
                    ParsedConfiguration: parsed,
                    ElementServiceId: element?.Id,
                    ElementDataRoot: elementDataRoot,
                    ElementConfiguration: elementConfiguration,
                    Findings: findings.ToArray()));
        }

        return Task.FromResult(
            new LegacyFileSystemObservation(
                Stacks: observations.ToArray(),
                ObservedAtUtc: DateTimeOffset.UtcNow));
    }

    public static FileObservation ObserveFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new FileObservation(
                Path: string.Empty,
                Exists: false,
                IsRegularFile: false,
                IsSymbolicLink: false,
                SizeBytes: null,
                LastWriteAtUtc: null,
                ErrorCode: "path_unresolved");
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return new FileObservation(
                Path: path,
                Exists: false,
                IsRegularFile: false,
                IsSymbolicLink: false,
                SizeBytes: null,
                LastWriteAtUtc: null,
                ErrorCode: "path_invalid");
        }

        try
        {
            if (!File.Exists(fullPath))
            {
                return new FileObservation(
                    Path: fullPath,
                    Exists: false,
                    IsRegularFile: false,
                    IsSymbolicLink: false,
                    SizeBytes: null,
                    LastWriteAtUtc: null,
                    ErrorCode: null);
            }

            var info = new FileInfo(fullPath);
            var isLink = info.Attributes.HasFlag(FileAttributes.ReparsePoint);

            return new FileObservation(
                Path: fullPath,
                Exists: true,
                IsRegularFile: !isLink,
                IsSymbolicLink: isLink,
                SizeBytes: isLink ? null : info.Length,
                LastWriteAtUtc: info.LastWriteTimeUtc,
                ErrorCode: isLink ? "symbolic_link_not_supported" : null);
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or
            IOException)
        {
            return new FileObservation(
                Path: fullPath,
                Exists: true,
                IsRegularFile: false,
                IsSymbolicLink: false,
                SizeBytes: null,
                LastWriteAtUtc: null,
                ErrorCode: "file_inspection_failed");
        }
    }

    public static DirectorySizeObservation ObserveDirectory(
        string? path,
        int maximumEntries,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new DirectorySizeObservation(
                Path: string.Empty,
                Exists: false,
                TotalBytes: 0,
                FileCount: 0,
                Complete: false,
                ErrorCode: "path_unresolved");
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return new DirectorySizeObservation(
                Path: path,
                Exists: false,
                TotalBytes: 0,
                FileCount: 0,
                Complete: false,
                ErrorCode: "path_invalid");
        }

        if (!Directory.Exists(fullPath))
        {
            return new DirectorySizeObservation(
                Path: fullPath,
                Exists: false,
                TotalBytes: 0,
                FileCount: 0,
                Complete: true,
                ErrorCode: null);
        }

        long totalBytes = 0;
        long fileCount = 0;
        long entryCount = 0;
        var complete = true;
        string? errorCode = null;
        var pending = new Stack<string>();
        pending.Push(fullPath);

        try
        {
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = pending.Pop();

                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    entryCount++;

                    if (entryCount > maximumEntries)
                    {
                        complete = false;
                        errorCode = "file_scan_entry_limit";
                        pending.Clear();
                        break;
                    }

                    var attributes = File.GetAttributes(entry);

                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        complete = false;
                        errorCode ??= "symbolic_link_skipped";
                        continue;
                    }

                    if (attributes.HasFlag(FileAttributes.Directory))
                    {
                        pending.Push(entry);
                        continue;
                    }

                    var info = new FileInfo(entry);
                    totalBytes = checked(totalBytes + info.Length);
                    fileCount++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or
            IOException or
            OverflowException)
        {
            complete = false;
            errorCode = "directory_scan_failed";
        }

        return new DirectorySizeObservation(
            Path: fullPath,
            Exists: true,
            TotalBytes: totalBytes,
            FileCount: fileCount,
            Complete: complete,
            ErrorCode: errorCode);
    }

    private static DockerContainerObservation? ResolveContainer(
        LegacyServiceRecord service,
        DockerInventoryObservation docker)
    {
        var recordedContainerId = service.DockerContainerId;

        if (!string.IsNullOrWhiteSpace(recordedContainerId))
        {
            var byId = docker.Containers.FirstOrDefault(container =>
                container.Id.StartsWith(
                    recordedContainerId,
                    StringComparison.OrdinalIgnoreCase));

            if (byId is not null)
            {
                return byId;
            }
        }

        var serviceId = service.Id.ToString("D");

        return docker.Containers.FirstOrDefault(container =>
            container.ManagedLabels.TryGetValue(
                "mem.instanceId",
                out var value)
            && string.Equals(
                value,
                serviceId,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveDataRoot(
        LegacyServiceRecord service,
        DockerContainerObservation? container)
    {
        if (TryNormalizeExistingDirectory(service.DataPath, out var dataPath))
        {
            return dataPath;
        }

        var mount = container?.Mounts.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Destination,
                "/data",
                StringComparison.Ordinal)
            || candidate.Destination.EndsWith(
                "/synapse",
                StringComparison.OrdinalIgnoreCase));

        return TryNormalizeExistingDirectory(mount?.Source, out dataPath)
            ? dataPath
            : NormalizePath(service.DataPath ?? mount?.Source);
    }

    private static string? ResolveElementDataRoot(
        LegacyServiceRecord service,
        DockerContainerObservation? container)
    {
        if (TryNormalizeExistingDirectory(service.DataPath, out var dataPath))
        {
            return dataPath;
        }

        var configMount = container?.Mounts.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Destination,
                "/app/config.json",
                StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(configMount?.Source))
        {
            var configDirectory = Path.GetDirectoryName(configMount.Source);
            var parent = configDirectory is null
                ? null
                : Directory.GetParent(configDirectory)?.FullName;

            if (TryNormalizeExistingDirectory(parent, out dataPath))
            {
                return dataPath;
            }
        }

        return NormalizePath(service.DataPath);
    }

    private static string? ResolveElementConfigurationPath(
        string? elementDataRoot,
        DockerContainerObservation? container)
    {
        var configMount = container?.Mounts.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Destination,
                "/app/config.json",
                StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(configMount?.Source))
        {
            return NormalizePath(configMount.Source);
        }

        return string.IsNullOrWhiteSpace(elementDataRoot)
            ? null
            : Path.Combine(elementDataRoot, "config", "config.json");
    }

    private static string ResolveConfigurationPath(
        string? configuredPath,
        string? dataRoot,
        string homeserverPath,
        string fallbackName)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (Path.IsPathFullyQualified(configuredPath))
            {
                if (!string.IsNullOrWhiteSpace(dataRoot)
                    && (configuredPath.Equals("/data", StringComparison.Ordinal)
                        || configuredPath.StartsWith(
                            "/data/",
                            StringComparison.Ordinal)))
                {
                    var relative = configuredPath.Length == "/data".Length
                        ? string.Empty
                        : configuredPath[("/data/".Length)..];

                    return Path.GetFullPath(Path.Combine(dataRoot, relative));
                }

                return NormalizePath(configuredPath) ?? configuredPath;
            }

            var configDirectory = Path.GetDirectoryName(homeserverPath);

            if (!string.IsNullOrWhiteSpace(configDirectory))
            {
                return Path.GetFullPath(
                    Path.Combine(configDirectory, configuredPath));
            }
        }

        return string.IsNullOrWhiteSpace(dataRoot)
            ? string.Empty
            : Path.Combine(dataRoot, fallbackName);
    }

    private static string ResolveSigningKeyPath(
        string? configuredPath,
        string? dataRoot,
        string homeserverPath)
    {
        var resolved = ResolveConfigurationPath(
            configuredPath,
            dataRoot,
            homeserverPath,
            "server.signing.key");

        if (File.Exists(resolved) || string.IsNullOrWhiteSpace(dataRoot))
        {
            return resolved;
        }

        try
        {
            return Directory.EnumerateFiles(
                    dataRoot,
                    "*.signing.key",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.Ordinal)
                .FirstOrDefault()
                ?? resolved;
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or
            IOException)
        {
            return resolved;
        }
    }

    private static bool TryNormalizeExistingDirectory(
        string? path,
        out string? normalized)
    {
        normalized = NormalizePath(path);
        return normalized is not null && Directory.Exists(normalized);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return null;
        }
    }
}

public static class HomeserverConfigurationParser
{
    public static HomeserverConfigurationObservation Parse(
        string? path,
        long maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Empty(path ?? string.Empty, "path_unresolved");
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return Empty(path, "path_invalid");
        }

        if (!File.Exists(fullPath))
        {
            return Empty(fullPath, "homeserver_configuration_missing");
        }

        try
        {
            var fileInfo = new FileInfo(fullPath);

            if (fileInfo.Length > maximumBytes)
            {
                return Empty(fullPath, "homeserver_configuration_too_large");
            }

            var lines = File.ReadAllLines(fullPath);
            string? serverName = null;
            string? mediaStorePath = null;
            string? signingKeyPath = null;
            string? databaseEngine = null;
            string? databasePath = null;
            int? databaseIndent = null;
            int? databaseArgsIndent = null;

            foreach (var rawLine in lines)
            {
                var line = StripComment(rawLine);

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var indent = CountIndent(line);
                var trimmed = line.Trim();

                if (TryReadScalar(trimmed, "server_name", out var server))
                {
                    serverName = server;
                    continue;
                }

                if (TryReadScalar(
                        trimmed,
                        "media_store_path",
                        out var media))
                {
                    mediaStorePath = media;
                    continue;
                }

                if (TryReadScalar(
                        trimmed,
                        "signing_key_path",
                        out var signing))
                {
                    signingKeyPath = signing;
                    continue;
                }

                if (trimmed.Equals(
                        "database:",
                        StringComparison.OrdinalIgnoreCase))
                {
                    databaseIndent = indent;
                    databaseArgsIndent = null;
                    continue;
                }

                if (databaseIndent is null)
                {
                    continue;
                }

                if (indent <= databaseIndent.Value)
                {
                    databaseIndent = null;
                    databaseArgsIndent = null;
                    continue;
                }

                if (trimmed.Equals(
                        "args:",
                        StringComparison.OrdinalIgnoreCase))
                {
                    databaseArgsIndent = indent;
                    continue;
                }

                if (databaseArgsIndent is null &&
                    TryReadScalar(trimmed, "name", out var engine))
                {
                    databaseEngine = engine;
                    continue;
                }

                if (databaseArgsIndent is not null &&
                    indent > databaseArgsIndent.Value &&
                    TryReadScalar(trimmed, "database", out var database))
                {
                    databasePath = database;
                }
            }

            var inferredDatabase = string.IsNullOrWhiteSpace(databasePath)
                ? Path.Combine(
                    Path.GetDirectoryName(fullPath) ?? string.Empty,
                    "homeserver.db")
                : databasePath;

            if (string.IsNullOrWhiteSpace(databaseEngine) &&
                File.Exists(inferredDatabase))
            {
                databaseEngine = "sqlite3";
            }

            return new HomeserverConfigurationObservation(
                Path: fullPath,
                Parsed: true,
                ServerName: serverName,
                DatabaseEngine: databaseEngine,
                DatabasePath: databasePath,
                MediaStorePath: mediaStorePath,
                SigningKeyPath: signingKeyPath,
                ErrorCode: null);
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or
            IOException or
            ArgumentException)
        {
            return Empty(fullPath, "homeserver_configuration_read_failed");
        }
    }

    private static HomeserverConfigurationObservation Empty(
        string path,
        string errorCode) =>
        new(
            Path: path,
            Parsed: false,
            ServerName: null,
            DatabaseEngine: null,
            DatabasePath: null,
            MediaStorePath: null,
            SigningKeyPath: null,
            ErrorCode: errorCode);

    private static bool TryReadScalar(
        string line,
        string key,
        out string? value)
    {
        value = null;
        var prefix = key + ":";

        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var raw = line[prefix.Length..].Trim();

        if (raw.Length == 0)
        {
            return false;
        }

        value = Unquote(raw);
        return true;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') ||
             (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static int CountIndent(string value)
    {
        var count = 0;

        foreach (var character in value)
        {
            if (character == ' ')
            {
                count++;
                continue;
            }

            if (character == '\t')
            {
                count += 2;
                continue;
            }

            break;
        }

        return count;
    }

    private static string StripComment(string value)
    {
        var singleQuoted = false;
        var doubleQuoted = false;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

            if (character == '\'' && !doubleQuoted)
            {
                singleQuoted = !singleQuoted;
                continue;
            }

            if (character == '"' && !singleQuoted)
            {
                doubleQuoted = !doubleQuoted;
                continue;
            }

            if (character == '#' && !singleQuoted && !doubleQuoted)
            {
                return value[..index];
            }
        }

        return value;
    }
}
