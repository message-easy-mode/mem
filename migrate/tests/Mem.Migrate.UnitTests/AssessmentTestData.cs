using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Infrastructure.Postgres;

namespace Mem.Migrate.UnitTests;

internal static class AssessmentTestData
{
    public static DockerInventoryObservation Docker(
        DockerContainerObservation[]? containers = null,
        DockerNetworkObservation[]? networks = null,
        bool available = true) =>
        new(
            Available: available,
            ServerVersion: available ? "27.0.0" : null,
            ErrorCode: available ? null : "docker_unavailable",
            ErrorMessage: available ? null : "Docker unavailable.",
            Containers: containers ?? [],
            Networks: networks ?? [],
            ObservedAtUtc: DateTimeOffset.UtcNow);

    public static DockerContainerObservation Container(
        string name,
        string image,
        string? composeService = null,
        IReadOnlyDictionary<string, string>? labels = null,
        DockerMountObservation[]? mounts = null,
        DockerPortBinding[]? ports = null) =>
        new(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            Image: image,
            ImageId: "sha256:test",
            State: "running",
            Health: "healthy",
            RestartPolicy: "unless-stopped",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ComposeProject: "mem",
            ComposeService: composeService,
            ManagedLabels: labels
                ?? new Dictionary<string, string>(StringComparer.Ordinal),
            EnvironmentNames: [],
            SafeEnvironment: new Dictionary<string, string>(
                StringComparer.Ordinal),
            Ports: ports ?? [],
            Mounts: mounts ?? [],
            Networks: []);

    public static LegacyDatabaseCandidateObservation ExactDatabase(
        LegacyStackRecord[]? stacks = null,
        LegacyServiceRecord[]? services = null) =>
        new(
            ContainerId: "postgres-id",
            ContainerName: "postgres",
            DatabaseName: "mem",
            DatabaseUser: "postgres",
            ServerVersion: "16.4",
            AppSchemaPresent: true,
            ExactSupportedSchema: true,
            MigrationIds: [LegacyPostgresProbe.ExpectedMigrationId],
            TableNames: LegacyPostgresProbe.ExpectedTables,
            MissingTables: [],
            UnexpectedTables: [],
            RowCounts: new Dictionary<string, long>(StringComparer.Ordinal),
            Stacks: stacks ?? [],
            Services: services ?? [],
            PlatformRoutes: [],
            ActiveGuestChats: 0,
            ActivePasswordResetRequests: 0,
            ProvisioningJobs: 0,
            ErrorCode: null,
            ErrorMessage: null);

    public static LegacyStackRecord Stack(Guid stackId, Guid matrixId) =>
        new(
            Id: stackId,
            OwnerUserId: Guid.NewGuid(),
            Slug: "demo",
            Name: "Demo",
            Status: 3,
            Description: null,
            PrimaryUrl: "https://matrix.example.test",
            MatrixInstanceId: matrixId,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

    public static LegacyServiceRecord MatrixService(
        Guid stackId,
        Guid matrixId,
        string dataPath) =>
        new(
            Id: matrixId,
            StackId: stackId,
            ServiceKey: "matrix",
            Status: 2,
            Image: "matrixdotorg/synapse:latest",
            Version: string.Empty,
            DockerContainerId: "matrix-id",
            HostPort: 18008,
            BaseUrl: null,
            ServerName: "matrix.example.test",
            DataPath: dataPath,
            HasAdminAccessToken: true,
            MatrixPublicHost: "matrix.example.test",
            MatrixInternalHost: null,
            HomeserverInstanceId: null,
            ElementPublicHost: null,
            ElementInternalHost: null,
            PublicRouteId: "1",
            PublicDomain: "matrix.example.test",
            InternalRouteId: null,
            InternalDomain: null,
            ForwardHost: "mem-matrix",
            ForwardPort: 8008,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

    public static LegacyStackFileObservation StackFiles(
        Guid stackId,
        Guid matrixId,
        bool complete = true) =>
        new(
            StackId: stackId,
            MatrixServiceId: matrixId,
            MatrixContainerId: "matrix-id",
            DataRoot: "/srv/mem/demo/synapse",
            HomeserverConfiguration: File(
                "/srv/mem/demo/synapse/homeserver.yaml",
                complete),
            SqliteDatabase: File(
                "/srv/mem/demo/synapse/homeserver.db",
                complete),
            SigningKey: File(
                "/srv/mem/demo/synapse/server.signing.key",
                complete),
            MediaStore: new DirectorySizeObservation(
                "/srv/mem/demo/synapse/media_store",
                Exists: complete,
                TotalBytes: 100,
                FileCount: 2,
                Complete: true,
                ErrorCode: null),
            ParsedConfiguration: new HomeserverConfigurationObservation(
                "/srv/mem/demo/synapse/homeserver.yaml",
                Parsed: complete,
                ServerName: "matrix.example.test",
                DatabaseEngine: "sqlite3",
                DatabasePath: "/srv/mem/demo/synapse/homeserver.db",
                MediaStorePath: "/srv/mem/demo/synapse/media_store",
                SigningKeyPath: "/srv/mem/demo/synapse/server.signing.key",
                ErrorCode: complete ? null : "missing"),
            ElementServiceId: null,
            ElementDataRoot: null,
            ElementConfiguration: null,
            Findings: complete
                ? []
                : [
                    new AssessmentFinding(
                        "synapse_sqlite_missing",
                        FindingSeverity.Blocker,
                        "Missing SQLite.")
                ]);

    private static FileObservation File(string path, bool exists) =>
        new(
            Path: path,
            Exists: exists,
            IsRegularFile: exists,
            IsSymbolicLink: false,
            SizeBytes: exists ? 100 : null,
            LastWriteAtUtc: exists ? DateTimeOffset.UtcNow : null,
            ErrorCode: exists ? null : "missing");
}
