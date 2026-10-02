using System.Globalization;
using System.Text.Json;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Processes;

namespace Mem.Migrate.Infrastructure.Postgres;

public sealed class LegacyPostgresProbe(IProcessRunner processRunner)
    : ILegacyDatabaseProbe
{
    public const string ExpectedMigrationId =
        "20260507085953_InitialApplicationSchema";

    public static readonly string[] HistoricalMigrationIds =
    [
        "20260501105837_App-RemoveTenants",
        "20260501214804_adjustments"
    ];

    public static readonly string[] ExpectedTables =
    [
        "__EFMigrationsHistory",
        "guest_chat_sessions",
        "identity_refresh_tokens",
        "inbox_requests",
        "platform_ingress_bootstrap_state",
        "platform_settings",
        "provisioning_jobs",
        "service_instances",
        "space_user_password_reset_requests",
        "stack_users",
        "stacks",
        "user_account_roles",
        "user_accounts"
    ];

    public async Task<LegacyDatabaseObservation> ProbeAsync(
        AssessmentOptions options,
        DockerInventoryObservation docker,
        CancellationToken cancellationToken)
    {
        if (!docker.Available)
        {
            return new LegacyDatabaseObservation(
                ProbeAttempted: false,
                Candidates: [],
                ObservedAtUtc: DateTimeOffset.UtcNow);
        }

        var candidates = SelectCandidates(options, docker);
        var observations = new List<LegacyDatabaseCandidateObservation>();

        foreach (var container in candidates)
        {
            observations.Add(
                await ProbeContainerAsync(
                    options,
                    container,
                    cancellationToken));
        }

        return new LegacyDatabaseObservation(
            ProbeAttempted: candidates.Length > 0,
            Candidates: observations.ToArray(),
            ObservedAtUtc: DateTimeOffset.UtcNow);
    }

    private async Task<LegacyDatabaseCandidateObservation> ProbeContainerAsync(
        AssessmentOptions options,
        DockerContainerObservation container,
        CancellationToken cancellationToken)
    {
        var databaseUser = container.SafeEnvironment.GetValueOrDefault(
            "POSTGRES_USER",
            "postgres");
        var configuredDatabase = container.SafeEnvironment.GetValueOrDefault(
            "POSTGRES_DB",
            databaseUser);

        var databaseNames = new[]
            {
                configuredDatabase,
                "mem",
                "postgres"
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        ProcessResult? lastFailure = null;

        foreach (var databaseName in databaseNames)
        {
            var schemaProbe = await RunPsqlAsync(
                options,
                container,
                databaseUser,
                databaseName,
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.schemata
                    WHERE schema_name = 'app'
                )::text;
                """,
                cancellationToken);

            if (!schemaProbe.Succeeded)
            {
                lastFailure = schemaProbe;
                continue;
            }

            var schemaPresent = string.Equals(
                FirstLine(schemaProbe.StandardOutput),
                "true",
                StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    FirstLine(schemaProbe.StandardOutput),
                    "t",
                    StringComparison.OrdinalIgnoreCase);

            if (!schemaPresent)
            {
                continue;
            }

            return await ReadSupportedSchemaAsync(
                options,
                container,
                databaseUser,
                databaseName,
                cancellationToken);
        }

        return EmptyCandidate(
            container,
            configuredDatabase,
            databaseUser,
            lastFailure?.ErrorCode ?? "legacy_app_schema_not_found",
            SafeFailureMessage(lastFailure));
    }

    private async Task<LegacyDatabaseCandidateObservation> ReadSupportedSchemaAsync(
        AssessmentOptions options,
        DockerContainerObservation container,
        string databaseUser,
        string databaseName,
        CancellationToken cancellationToken)
    {
        var serverVersion = await QueryScalarAsync(
            options,
            container,
            databaseUser,
            databaseName,
            "SHOW server_version;",
            cancellationToken);

        var appMigrations = await QueryLinesAsync(
            options,
            container,
            databaseUser,
            databaseName,
            """
            SELECT "MigrationId"
            FROM app."__EFMigrationsHistory"
            ORDER BY "MigrationId";
            """,
            cancellationToken);

        var publicMigrations = await QueryLinesAsync(
            options,
            container,
            databaseUser,
            databaseName,
            """
            SELECT "MigrationId"
            FROM public."__EFMigrationsHistory"
            ORDER BY "MigrationId";
            """,
            cancellationToken);

        var migrations = appMigrations.Length > 0
            ? appMigrations
            : publicMigrations;

        var tables = await QueryLinesAsync(
            options,
            container,
            databaseUser,
            databaseName,
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'app'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """,
            cancellationToken);

        var requiredApplicationTables = ExpectedTables
            .Where(x => x != "__EFMigrationsHistory")
            .ToArray();

        var missingTables = requiredApplicationTables
            .Except(tables, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var unexpectedTables = tables
            .Except(ExpectedTables, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var canonicalMigrationLineage =
            appMigrations.SequenceEqual([ExpectedMigrationId], StringComparer.Ordinal);
        var historicalMigrationLineage =
            appMigrations.Length == 0
            && publicMigrations.SequenceEqual(
                HistoricalMigrationIds,
                StringComparer.Ordinal);

        var exactSchema =
            (canonicalMigrationLineage || historicalMigrationLineage)
            && missingTables.Length == 0
            && unexpectedTables.Length == 0;

        var rowCounts = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var table in ExpectedTables.Where(x => x != "__EFMigrationsHistory"))
        {
            if (!tables.Contains(table, StringComparer.Ordinal))
            {
                continue;
            }

            var sql = $"SELECT count(*)::text FROM app.{QuoteIdentifier(table)};";
            var countText = await QueryScalarAsync(
                options,
                container,
                databaseUser,
                databaseName,
                sql,
                cancellationToken);

            if (long.TryParse(
                countText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var count))
            {
                rowCounts[table] = count;
            }
        }

        var stacks = tables.Contains("stacks", StringComparer.Ordinal)
            ? ParseStacks(
                await QueryLinesAsync(
                    options,
                    container,
                    databaseUser,
                    databaseName,
                    StackQuery,
                    cancellationToken))
            : [];

        var services = tables.Contains("service_instances", StringComparer.Ordinal)
            ? ParseServices(
                await QueryLinesAsync(
                    options,
                    container,
                    databaseUser,
                    databaseName,
                    ServiceQuery,
                    cancellationToken))
            : [];

        var routes = tables.Contains(
                "platform_ingress_bootstrap_state",
                StringComparer.Ordinal)
            ? ParseRoutes(
                await QueryLinesAsync(
                    options,
                    container,
                    databaseUser,
                    databaseName,
                    PlatformRouteQuery,
                    cancellationToken))
            : [];

        var activeGuestChats = await CountWhereAsync(
            options,
            container,
            databaseUser,
            databaseName,
            tables,
            "guest_chat_sessions",
            """
            "ClosedAtUtc" IS NULL
            AND "ExpiresAtUtc" > now()
            """,
            cancellationToken);

        var activePasswordResets = await CountWhereAsync(
            options,
            container,
            databaseUser,
            databaseName,
            tables,
            "space_user_password_reset_requests",
            """
            "UsedAtUtc" IS NULL
            AND "RevokedAtUtc" IS NULL
            AND "ExpiresAtUtc" > now()
            """,
            cancellationToken);

        var provisioningJobs = rowCounts.GetValueOrDefault(
            "provisioning_jobs",
            0);

        return new LegacyDatabaseCandidateObservation(
            ContainerId: container.Id,
            ContainerName: container.Name,
            DatabaseName: databaseName,
            DatabaseUser: databaseUser,
            ServerVersion: serverVersion,
            AppSchemaPresent: true,
            ExactSupportedSchema: exactSchema,
            MigrationIds: migrations,
            TableNames: tables,
            MissingTables: missingTables,
            UnexpectedTables: unexpectedTables,
            RowCounts: rowCounts,
            Stacks: stacks,
            Services: services,
            PlatformRoutes: routes,
            ActiveGuestChats: activeGuestChats,
            ActivePasswordResetRequests: activePasswordResets,
            ProvisioningJobs: provisioningJobs,
            ErrorCode: exactSchema ? null : "legacy_schema_mismatch",
            ErrorMessage: exactSchema
                ? null
                : "The app schema does not exactly match the supported MEM v0.1.0 fingerprint.");
    }

    private async Task<long> CountWhereAsync(
        AssessmentOptions options,
        DockerContainerObservation container,
        string databaseUser,
        string databaseName,
        string[] availableTables,
        string tableName,
        string predicate,
        CancellationToken cancellationToken)
    {
        if (!availableTables.Contains(tableName, StringComparer.Ordinal))
        {
            return 0;
        }

        var value = await QueryScalarAsync(
            options,
            container,
            databaseUser,
            databaseName,
            $"SELECT count(*)::text FROM app.{QuoteIdentifier(tableName)} WHERE {predicate};",
            cancellationToken);

        return long.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var count)
            ? count
            : 0;
    }

    private async Task<string?> QueryScalarAsync(
        AssessmentOptions options,
        DockerContainerObservation container,
        string databaseUser,
        string databaseName,
        string sql,
        CancellationToken cancellationToken)
    {
        var result = await RunPsqlAsync(
            options,
            container,
            databaseUser,
            databaseName,
            sql,
            cancellationToken);

        return result.Succeeded
            ? FirstLine(result.StandardOutput)
            : null;
    }

    private async Task<string[]> QueryLinesAsync(
        AssessmentOptions options,
        DockerContainerObservation container,
        string databaseUser,
        string databaseName,
        string sql,
        CancellationToken cancellationToken)
    {
        var result = await RunPsqlAsync(
            options,
            container,
            databaseUser,
            databaseName,
            sql,
            cancellationToken);

        return result.Succeeded
            ? SplitLines(result.StandardOutput)
            : [];
    }

    private Task<ProcessResult> RunPsqlAsync(
        AssessmentOptions options,
        DockerContainerObservation container,
        string databaseUser,
        string databaseName,
        string sql,
        CancellationToken cancellationToken)
    {
        var arguments = new[]
        {
            "exec",
            container.Id,
            "psql",
            "-X",
            "--no-psqlrc",
            "--set",
            "ON_ERROR_STOP=1",
            "--tuples-only",
            "--no-align",
            "-U",
            databaseUser,
            "-d",
            databaseName,
            "-c",
            sql
        };

        return processRunner.RunAsync(
            new ProcessRequest(
                options.DockerCommand,
                arguments,
                TimeSpan.FromSeconds(options.CommandTimeoutSeconds),
                MaximumOutputCharacters: 16 * 1024 * 1024),
            cancellationToken);
    }

    private static DockerContainerObservation[] SelectCandidates(
        AssessmentOptions options,
        DockerInventoryObservation docker)
    {
        var requestedContainer = options.PostgresContainer;

        if (!string.IsNullOrWhiteSpace(requestedContainer))
        {
            return docker.Containers
                .Where(container =>
                    string.Equals(
                        container.Name,
                        requestedContainer,
                        StringComparison.OrdinalIgnoreCase)
                    || container.Id.StartsWith(
                        requestedContainer,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return docker.Containers
            .Where(container =>
                container.Image.StartsWith(
                    "postgres",
                    StringComparison.OrdinalIgnoreCase)
                || container.EnvironmentNames.Contains(
                    "POSTGRES_DB",
                    StringComparer.Ordinal)
                || container.Name.Contains(
                    "postgres",
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(container =>
                string.Equals(
                    container.SafeEnvironment.GetValueOrDefault("POSTGRES_DB"),
                    "mem",
                    StringComparison.OrdinalIgnoreCase))
            .ThenBy(container => container.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LegacyStackRecord[] ParseStacks(string[] lines)
    {
        var records = new List<LegacyStackRecord>();

        foreach (var line in lines)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                records.Add(
                    new LegacyStackRecord(
                        Id: GetGuid(root, "id"),
                        OwnerUserId: GetGuid(root, "ownerUserId"),
                        Slug: GetString(root, "slug") ?? string.Empty,
                        Name: GetString(root, "name") ?? string.Empty,
                        Status: GetInt32(root, "status"),
                        Description: GetString(root, "description"),
                        PrimaryUrl: GetString(root, "primaryUrl"),
                        MatrixInstanceId: GetNullableGuid(root, "matrixInstanceId"),
                        CreatedAt: GetNullableDateTimeOffset(root, "createdAt"),
                        UpdatedAt: GetNullableDateTimeOffset(root, "updatedAt")));
            }
            catch (JsonException)
            {
                // A malformed row is represented by the schema finding rather
                // than exposing the raw database output.
            }
        }

        return records.OrderBy(x => x.Id).ToArray();
    }

    private static LegacyServiceRecord[] ParseServices(string[] lines)
    {
        var records = new List<LegacyServiceRecord>();

        foreach (var line in lines)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                records.Add(
                    new LegacyServiceRecord(
                        Id: GetGuid(root, "id"),
                        StackId: GetGuid(root, "stackId"),
                        ServiceKey: GetString(root, "serviceKey") ?? string.Empty,
                        Status: GetInt32(root, "status"),
                        Image: GetString(root, "image") ?? string.Empty,
                        Version: GetString(root, "version") ?? string.Empty,
                        DockerContainerId: GetString(root, "dockerContainerId"),
                        HostPort: GetNullableInt32(root, "hostPort"),
                        BaseUrl: GetString(root, "baseUrl"),
                        ServerName: GetString(root, "serverName"),
                        DataPath: GetString(root, "dataPath"),
                        HasAdminAccessToken: GetBoolean(root, "hasAdminAccessToken"),
                        MatrixPublicHost: GetString(root, "matrixPublicHost"),
                        MatrixInternalHost: GetString(root, "matrixInternalHost"),
                        HomeserverInstanceId: GetNullableGuid(
                            root,
                            "homeserverInstanceId"),
                        ElementPublicHost: GetString(root, "elementPublicHost"),
                        ElementInternalHost: GetString(root, "elementInternalHost"),
                        PublicRouteId: GetString(root, "publicRouteId"),
                        PublicDomain: GetString(root, "publicDomain"),
                        InternalRouteId: GetString(root, "internalRouteId"),
                        InternalDomain: GetString(root, "internalDomain"),
                        ForwardHost: GetString(root, "forwardHost"),
                        ForwardPort: GetNullableInt32(root, "forwardPort"),
                        CreatedAt: GetNullableDateTimeOffset(root, "createdAt"),
                        UpdatedAt: GetNullableDateTimeOffset(root, "updatedAt")));
            }
            catch (JsonException)
            {
                // See ParseStacks.
            }
        }

        return records.OrderBy(x => x.Id).ToArray();
    }

    private static LegacyPlatformRouteRecord[] ParseRoutes(string[] lines)
    {
        var records = new List<LegacyPlatformRouteRecord>();

        foreach (var line in lines)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                records.Add(
                    new LegacyPlatformRouteRecord(
                        Key: GetString(root, "key") ?? string.Empty,
                        Version: GetString(root, "version") ?? string.Empty,
                        WebDomain: GetString(root, "webDomain"),
                        WebForwardHost: GetString(root, "webForwardHost"),
                        WebForwardPort: GetNullableInt32(root, "webForwardPort"),
                        WebRouteId: GetString(root, "webRouteId"),
                        ApiDomain: GetString(root, "apiDomain"),
                        ApiForwardHost: GetString(root, "apiForwardHost"),
                        ApiForwardPort: GetNullableInt32(root, "apiForwardPort"),
                        ApiRouteId: GetString(root, "apiRouteId"),
                        Completed: GetBoolean(root, "completed"),
                        CompletedAtUtc: GetNullableDateTimeOffset(
                            root,
                            "completedAtUtc")));
            }
            catch (JsonException)
            {
                // See ParseStacks.
            }
        }

        return records.ToArray();
    }

    private static LegacyDatabaseCandidateObservation EmptyCandidate(
        DockerContainerObservation container,
        string databaseName,
        string databaseUser,
        string errorCode,
        string? errorMessage)
    {
        return new LegacyDatabaseCandidateObservation(
            ContainerId: container.Id,
            ContainerName: container.Name,
            DatabaseName: databaseName,
            DatabaseUser: databaseUser,
            ServerVersion: null,
            AppSchemaPresent: false,
            ExactSupportedSchema: false,
            MigrationIds: [],
            TableNames: [],
            MissingTables: ExpectedTables,
            UnexpectedTables: [],
            RowCounts: new Dictionary<string, long>(StringComparer.Ordinal),
            Stacks: [],
            Services: [],
            PlatformRoutes: [],
            ActiveGuestChats: 0,
            ActivePasswordResetRequests: 0,
            ProvisioningJobs: 0,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage);
    }

    private static string? SafeFailureMessage(ProcessResult? result)
    {
        if (result is null)
        {
            return "No PostgreSQL database with the legacy app schema was found.";
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            var firstLine = FirstLine(result.StandardError);
            return string.IsNullOrWhiteSpace(firstLine)
                ? "PostgreSQL inspection failed."
                : firstLine;
        }

        return "PostgreSQL inspection failed.";
    }

    private static string QuoteIdentifier(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string? FirstLine(string value) =>
        SplitLines(value).FirstOrDefault();

    private static string[] SplitLines(string value) =>
        value.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .ToArray();

    private static Guid GetGuid(JsonElement element, string propertyName) =>
        Guid.TryParse(GetString(element, propertyName), out var value)
            ? value
            : Guid.Empty;

    private static Guid? GetNullableGuid(
        JsonElement element,
        string propertyName) =>
        Guid.TryParse(GetString(element, propertyName), out var value)
            ? value
            : null;

    private static string? GetString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static int GetInt32(JsonElement element, string propertyName) =>
        GetNullableInt32(element, propertyName) ?? 0;

    private static int? GetNullableInt32(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.Number &&
            value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(
            value.ToString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private static bool GetBoolean(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        if (value.ValueKind is JsonValueKind.True)
        {
            return true;
        }

        return bool.TryParse(value.ToString(), out var parsed) && parsed;
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(
        JsonElement element,
        string propertyName)
    {
        return DateTimeOffset.TryParse(
            GetString(element, propertyName),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var value)
            ? value
            : null;
    }

    private const string StackQuery =
        """
        SELECT json_build_object(
            'id', "Id",
            'ownerUserId', "OwnerUserId",
            'slug', "Slug",
            'name', "Name",
            'status', "Status",
            'description', "Description",
            'primaryUrl', "PrimaryUrl",
            'matrixInstanceId', "MatrixInstanceId",
            'createdAt', "CreatedAt",
            'updatedAt', "UpdatedAt"
        )::text
        FROM app.stacks
        ORDER BY "Id";
        """;

    private const string ServiceQuery =
        """
        SELECT json_build_object(
            'id', "Id",
            'stackId', "StackId",
            'serviceKey', "ServiceKey",
            'status', "Status",
            'image', "Image",
            'version', "Version",
            'dockerContainerId', "DockerContainerId",
            'hostPort', "HostPort",
            'baseUrl', "BaseUrl",
            'serverName', "ServerName",
            'dataPath', "DataPath",
            'hasAdminAccessToken', (
                "AdminAccessToken" IS NOT NULL
                AND length("AdminAccessToken") > 0
            ),
            'matrixPublicHost', "Metadata" ->> 'matrix.publicHost',
            'matrixInternalHost', "Metadata" ->> 'matrix.internalHost',
            'homeserverInstanceId', "Metadata" ->> 'element.homeserverInstanceId',
            'elementPublicHost', "Metadata" ->> 'element.publicHost',
            'elementInternalHost', "Metadata" ->> 'element.internalHost',
            'publicRouteId', COALESCE(
                "RuntimeMetadata" ->> 'ingress.public.routeId',
                "RuntimeMetadata" ->> 'npm.public.proxyHostId',
                "RuntimeMetadata" ->> 'npm.proxyHostId'
            ),
            'publicDomain', COALESCE(
                "RuntimeMetadata" ->> 'ingress.public.domain',
                "RuntimeMetadata" ->> 'npm.public.domain',
                "RuntimeMetadata" ->> 'npm.domain'
            ),
            'internalRouteId', COALESCE(
                "RuntimeMetadata" ->> 'ingress.internal.routeId',
                "RuntimeMetadata" ->> 'npm.internal.proxyHostId'
            ),
            'internalDomain', COALESCE(
                "RuntimeMetadata" ->> 'ingress.internal.domain',
                "RuntimeMetadata" ->> 'npm.internal.domain'
            ),
            'forwardHost', COALESCE(
                "RuntimeMetadata" ->> 'ingress.forwardHost',
                "RuntimeMetadata" ->> 'npm.forwardHost'
            ),
            'forwardPort', COALESCE(
                "RuntimeMetadata" ->> 'ingress.forwardPort',
                "RuntimeMetadata" ->> 'npm.forwardPort'
            ),
            'createdAt', "CreatedAt",
            'updatedAt', "UpdatedAt"
        )::text
        FROM app.service_instances
        ORDER BY "Id";
        """;

    private const string PlatformRouteQuery =
        """
        SELECT json_build_object(
            'key', "Key",
            'version', "Version",
            'webDomain', "WebDomain",
            'webForwardHost', "WebForwardHost",
            'webForwardPort', "WebForwardPort",
            'webRouteId', "WebRouteId",
            'apiDomain', "ApiDomain",
            'apiForwardHost', "ApiForwardHost",
            'apiForwardPort', "ApiForwardPort",
            'apiRouteId', "ApiRouteId",
            'completed', "Completed",
            'completedAtUtc', "CompletedAtUtc"
        )::text
        FROM app.platform_ingress_bootstrap_state
        ORDER BY "Key";
        """;
}
