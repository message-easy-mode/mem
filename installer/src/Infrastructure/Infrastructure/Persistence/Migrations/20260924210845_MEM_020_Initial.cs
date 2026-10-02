using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MEM_020_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    IsBootstrapProvisioning = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastLoginAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastStepUpAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupCatalogEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogEntryId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    OriginKind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    PayloadState = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PayloadStorageKind = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PayloadDirectoryPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SourceStackSlug = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    SourceBackupId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ValidationId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    ManifestVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    MemVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MatrixServerName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    MatrixHost = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ElementHost = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    IntegrityStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IntegritySummary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    WarningCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PayloadBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ImportedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MaterialisedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PayloadRemovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PayloadRemovedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupCatalogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ControlPlaneUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedUsername = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlPlaneUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiagnosticsIncidentDispositions",
                columns: table => new
                {
                    IncidentId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Disposition = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByOperatorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ObservedThroughAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ObservedThroughEventId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SnoozedUntilUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ResolutionCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticsIncidentDispositions", x => x.IncidentId);
                });

            migrationBuilder.CreateTable(
                name: "Installations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ConfigJson = table.Column<string>(type: "TEXT", nullable: true),
                    FrozenConfigJson = table.Column<string>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MemBootstrapGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TotpVerifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PendingOperatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemBootstrapGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MemCliDeviceAuthorizationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstallationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserCodeHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    VerifierHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DeviceLabel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ApprovedByOperatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeniedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DeniedByOperatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemCliDeviceAuthorizationAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MemCliDeviceSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorizationAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstallationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperatorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CredentialHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DeviceLabel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IdleExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AbsoluteExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RevokedReasonCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemCliDeviceSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MemOperatorAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ActorOperatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubjectOperatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemOperatorAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MemOperatorEnrollmentGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperatorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    TotpVerifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemOperatorEnrollmentGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MemOperatorStepUpGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperatorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RevokedReasonCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemOperatorStepUpGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MemSecuritySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequireHighRiskStepUp = table.Column<bool>(type: "INTEGER", nullable: false),
                    HighRiskStepUpGrantMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByOperatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemSecuritySettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MigrationIntakes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IntakeId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LifecycleStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false, defaultValue: "active"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosureKind = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ArchivedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    StateVersion = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 1L),
                    CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelledBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationIntakes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeServices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServiceName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ContainerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Image = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContainerPort = table.Column<int>(type: "INTEGER", nullable: false),
                    PreferredHostPort = table.Column<int>(type: "INTEGER", nullable: false),
                    SelectedHostPort = table.Column<int>(type: "INTEGER", nullable: false),
                    HostPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SecondaryHostPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ContainerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeServices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeStacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    LastVerifiedStatus = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    LastVerifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BaseDomain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    DomainId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActiveCertificateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActiveNpmCertificateId = table.Column<int>(type: "INTEGER", nullable: true),
                    RuntimeNetworkName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DataRoot = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ManifestPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    MatrixInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ElementInstanceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MatrixPublicBaseUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ElementPublicBaseUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LastOperationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeStacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RestoreAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RestoreSessionId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    ActiveSourceKey = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    SourceCatalogEntryIdSnapshot = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SourceDisplayNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    SourceOriginKindSnapshot = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SourceStackSlugSnapshot = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    SourceBackupIdSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BackupCatalogEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CurrentStage = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TerminalAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastEventAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastErrorCode = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LastErrorSummary = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    WarningCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SessionDirectoryPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    LogDirectoryPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SupportReportPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    RuntimeOperationId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestoreAttempts_BackupCatalogEntries_BackupCatalogEntryId",
                        column: x => x.BackupCatalogEntryId,
                        principalTable: "BackupCatalogEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ControlPlaneUserRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ControlPlaneUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlPlaneUserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ControlPlaneUserRoles_ControlPlaneUsers_ControlPlaneUserId",
                        column: x => x.ControlPlaneUserId,
                        principalTable: "ControlPlaneUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstallationSecrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstallationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallationSecrets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstallationSecrets_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstallationStepExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstallationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StepName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallationStepExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstallationStepExecutions_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationAcceptances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcceptanceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MigrationIntakeEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CandidateArtifactId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StagingRunId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PublicVerificationStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PublicVerificationEvidenceJson = table.Column<string>(type: "TEXT", nullable: false),
                    PublicVerificationEvidenceSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PublicCutoverAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AcceptedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    FreshPublicVerificationAcknowledged = table.Column<bool>(type: "INTEGER", nullable: false),
                    TargetWriteDivergenceAcknowledged = table.Column<bool>(type: "INTEGER", nullable: false),
                    RollbackBoundaryAcknowledged = table.Column<bool>(type: "INTEGER", nullable: false),
                    LegacyRetentionAcknowledged = table.Column<bool>(type: "INTEGER", nullable: false),
                    NoAutomaticLegacyDeletionAcknowledged = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationAcceptances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationAcceptances_MigrationIntakes_MigrationIntakeEntityId",
                        column: x => x.MigrationIntakeEntityId,
                        principalTable: "MigrationIntakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MigrationPackageRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PackageRevisionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MigrationIntakeEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RetentionState = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ActivePurposeKey = table.Column<string>(type: "TEXT", maxLength: 180, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UploadedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ValidatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SupersededAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AgeRecipient = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ProtectedAgeIdentity = table.Column<string>(type: "TEXT", nullable: true),
                    RecipientFingerprint = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    PackageFileName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    PackageSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    EncryptedPackageSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DecryptedArchiveSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ArchiveMigrationId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    ArchiveSourceProduct = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    ArchiveSourceVersion = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    ArchiveStackCount = table.Column<int>(type: "INTEGER", nullable: true),
                    CaptureKind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    SourceFrozen = table.Column<bool>(type: "INTEGER", nullable: true),
                    RehearsalOnly = table.Column<bool>(type: "INTEGER", nullable: true),
                    VerifiedFileCount = table.Column<int>(type: "INTEGER", nullable: true),
                    VerifiedExpandedBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    ValidationCode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    ValidationSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationPackageRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationPackageRevisions_MigrationIntakes_MigrationIntakeEntityId",
                        column: x => x.MigrationIntakeEntityId,
                        principalTable: "MigrationIntakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MigrationSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationIntakeEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Product = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ProductVersion = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    SourceFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationSources_MigrationIntakes_MigrationIntakeEntityId",
                        column: x => x.MigrationIntakeEntityId,
                        principalTable: "MigrationIntakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MigrationTwoServerQualifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QualificationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MigrationIntakeEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    QualifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceEvidenceAttemptId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SourceEvidenceSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceEvidenceJson = table.Column<string>(type: "TEXT", nullable: false),
                    QualificationEvidenceSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    QualificationEvidenceJson = table.Column<string>(type: "TEXT", nullable: false),
                    SourceMigrationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PackageRevisionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EncryptedPackageSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceStackSlug = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    MatrixServerName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    FreezeAttemptId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FreezePlanId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FreezePlanSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AdoptionPlanId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RuntimeStackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionVerificationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AcceptanceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BaselineBackupHandoffId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BaselineCatalogEntryId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SourceMachineIdSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceDockerEngineIdSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetMachineIdSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetDockerEngineIdSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DistinctMachineIdentity = table.Column<bool>(type: "INTEGER", nullable: false),
                    DistinctDockerEngineIdentity = table.Column<bool>(type: "INTEGER", nullable: false),
                    DevelopmentExternalControlPlane = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClosureId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ClosureStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosureEvidenceSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ClosureEvidenceJson = table.Column<string>(type: "TEXT", nullable: true),
                    ClosureNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationTwoServerQualifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationTwoServerQualifications_MigrationIntakes_MigrationIntakeEntityId",
                        column: x => x.MigrationIntakeEntityId,
                        principalTable: "MigrationIntakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeReadinessReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuntimeStackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReportKind = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    AllPassed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReportJson = table.Column<string>(type: "TEXT", nullable: true),
                    EvidenceJson = table.Column<string>(type: "TEXT", nullable: true),
                    FailedCheckCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WarningCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TriggeredBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TriggeredByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeReadinessReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuntimeReadinessReports_RuntimeStacks_RuntimeStackId",
                        column: x => x.RuntimeStackId,
                        principalTable: "RuntimeStacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeServiceInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuntimeStackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServiceKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Image = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ContainerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ContainerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    NetworkName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    InternalHost = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    InternalBaseUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PublicHost = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    PublicBaseUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    HostPort = table.Column<int>(type: "INTEGER", nullable: true),
                    ContainerPort = table.Column<int>(type: "INTEGER", nullable: true),
                    DataPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ConfigPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    BackupInclude = table.Column<bool>(type: "INTEGER", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    RuntimeMetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeServiceInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuntimeServiceInstances_RuntimeStacks_RuntimeStackId",
                        column: x => x.RuntimeStackId,
                        principalTable: "RuntimeStacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeStackDatabases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuntimeStackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DatabaseEngine = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    DatabaseHost = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    DatabasePort = table.Column<int>(type: "INTEGER", nullable: false),
                    DatabaseName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DatabaseUsername = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PasswordSecretKind = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeStackDatabases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuntimeStackDatabases_RuntimeStacks_RuntimeStackId",
                        column: x => x.RuntimeStackId,
                        principalTable: "RuntimeStacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeStackSecrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuntimeStackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecretKind = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SecretValue = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RotatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeStackSecrets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuntimeStackSecrets_RuntimeStacks_RuntimeStackId",
                        column: x => x.RuntimeStackId,
                        principalTable: "RuntimeStacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeStackUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuntimeStackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatrixInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    MatrixUserId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsFirstAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MatrixSyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeStackUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuntimeStackUsers_RuntimeStacks_RuntimeStackId",
                        column: x => x.RuntimeStackId,
                        principalTable: "RuntimeStacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RestoreTargetClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RestoreAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ResourceValue = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    ActiveClaimKey = table.Column<string>(type: "TEXT", maxLength: 480, nullable: true),
                    ClaimedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReleaseReason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreTargetClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestoreTargetClaims_RestoreAttempts_RestoreAttemptId",
                        column: x => x.RestoreAttemptId,
                        principalTable: "RestoreAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LegacyRetentionRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RetentionRecordId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MigrationIntakeEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationAcceptanceEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RetainUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CleanupEligibleAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CleanupCompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SourceMigrationId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SourceProduct = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SourceVersion = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    SourcePackageRetained = table.Column<bool>(type: "INTEGER", nullable: false),
                    CandidateArtifactRetained = table.Column<bool>(type: "INTEGER", nullable: false),
                    PrivateStagingEvidenceRetained = table.Column<bool>(type: "INTEGER", nullable: false),
                    LegacySourceResourcesRetained = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutomaticDeletionAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyRetentionRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegacyRetentionRecords_MigrationAcceptances_MigrationAcceptanceEntityId",
                        column: x => x.MigrationAcceptanceEntityId,
                        principalTable: "MigrationAcceptances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegacyRetentionRecords_MigrationIntakes_MigrationIntakeEntityId",
                        column: x => x.MigrationIntakeEntityId,
                        principalTable: "MigrationIntakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MigrationBaselineBackupHandoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HandoffId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MigrationIntakeEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationAcceptanceEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TargetStackSlug = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    CandidateId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PrivateRuntimeId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BackupId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CatalogEntryId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BackupCreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BackupTotalBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    BackupTotalFiles = table.Column<long>(type: "INTEGER", nullable: true),
                    BackupWarningCount = table.Column<int>(type: "INTEGER", nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    FailureSummary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationBaselineBackupHandoffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationBaselineBackupHandoffs_MigrationAcceptances_MigrationAcceptanceEntityId",
                        column: x => x.MigrationAcceptanceEntityId,
                        principalTable: "MigrationAcceptances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MigrationBaselineBackupHandoffs_MigrationIntakes_MigrationIntakeEntityId",
                        column: x => x.MigrationIntakeEntityId,
                        principalTable: "MigrationIntakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MigrationConversionAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversionAttemptId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MigrationIntakeEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationPackageRevisionEntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActiveMigrationKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SourcePackageSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceAdapterId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SourceAdapterVersion = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ConverterId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ConverterVersion = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CurrentStep = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResultCode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    FailureSummary = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    WorkspacePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    EvidenceDirectoryPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LogDirectoryPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CompletionReportPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    RetryOfConversionAttemptEntityId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationConversionAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationConversionAttempts_MigrationConversionAttempts_RetryOfConversionAttemptEntityId",
                        column: x => x.RetryOfConversionAttemptEntityId,
                        principalTable: "MigrationConversionAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MigrationConversionAttempts_MigrationIntakes_MigrationIntakeEntityId",
                        column: x => x.MigrationIntakeEntityId,
                        principalTable: "MigrationIntakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MigrationConversionAttempts_MigrationPackageRevisions_MigrationPackageRevisionEntityId",
                        column: x => x.MigrationPackageRevisionEntityId,
                        principalTable: "MigrationPackageRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuntimeStackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuntimeServiceInstanceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ServiceKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RouteKind = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsPublic = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublicHost = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    PublicBaseUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ForwardScheme = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ForwardHost = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    ForwardPort = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderRouteId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CertificateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NpmCertificateId = table.Column<int>(type: "INTEGER", nullable: true),
                    SslExpected = table.Column<bool>(type: "INTEGER", nullable: false),
                    SslConfigured = table.Column<bool>(type: "INTEGER", nullable: false),
                    ForceSsl = table.Column<bool>(type: "INTEGER", nullable: false),
                    Http2 = table.Column<bool>(type: "INTEGER", nullable: false),
                    AdvancedConfigHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AdvancedConfigApplied = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    LastVerifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuntimeRoutes_RuntimeServiceInstances_RuntimeServiceInstanceId",
                        column: x => x.RuntimeServiceInstanceId,
                        principalTable: "RuntimeServiceInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RuntimeRoutes_RuntimeStacks_RuntimeStackId",
                        column: x => x.RuntimeStackId,
                        principalTable: "RuntimeStacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationCandidateArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateArtifactId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MigrationConversionAttemptEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArtifactKind = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ArtifactSchemaVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SourcePackageSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ArtifactSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ManifestSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ChecksumsSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProvenanceJson = table.Column<string>(type: "TEXT", nullable: false),
                    VerificationStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RetentionState = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StorageKind = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ArtifactPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    VerificationReportPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationCandidateArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationCandidateArtifacts_MigrationConversionAttempts_MigrationConversionAttemptEntityId",
                        column: x => x.MigrationConversionAttemptEntityId,
                        principalTable: "MigrationConversionAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationStagingRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StagingRunId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MigrationIntakeEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationCandidateArtifactEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActiveMigrationKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CurrentStep = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DestroyedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PrivateRuntimeStagingId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    WorkspacePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    EvidencePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PrivateOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublicRoutesCreated = table.Column<bool>(type: "INTEGER", nullable: false),
                    DatabaseImportSucceeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    SynapseHealthPassed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ElementConfigPresent = table.Column<bool>(type: "INTEGER", nullable: false),
                    ElementConfigSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ElementContainerName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ElementContainerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ElementImageReference = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ElementImageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ElementContainerStarted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ElementHealthPassed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ElementSynapseConnectivityPassed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ElementNetworkAttached = table.Column<bool>(type: "INTEGER", nullable: false),
                    SynapseImageReference = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SynapseImageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    UsersCount = table.Column<long>(type: "INTEGER", nullable: true),
                    RoomsCount = table.Column<long>(type: "INTEGER", nullable: true),
                    EventsCount = table.Column<long>(type: "INTEGER", nullable: true),
                    MatrixServerName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    FailureSummary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RetryOfStagingRunEntityId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationStagingRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationStagingRuns_MigrationCandidateArtifacts_MigrationCandidateArtifactEntityId",
                        column: x => x.MigrationCandidateArtifactEntityId,
                        principalTable: "MigrationCandidateArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MigrationStagingRuns_MigrationIntakes_MigrationIntakeEntityId",
                        column: x => x.MigrationIntakeEntityId,
                        principalTable: "MigrationIntakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MigrationStagingRuns_MigrationStagingRuns_RetryOfStagingRunEntityId",
                        column: x => x.RetryOfStagingRunEntityId,
                        principalTable: "MigrationStagingRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MigrationProductionAdoptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdoptionPlanId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MigrationIntakeEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationPackageRevisionEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationCandidateArtifactEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationStagingRunEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RevisionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    PlanSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PreparedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RuntimeStackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetStackSlug = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    TargetDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    MatrixInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ElementInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatrixServerName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    MatrixPublicHost = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    MatrixPublicBaseUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ElementPublicHost = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ElementPublicBaseUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    RuntimeNetworkName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RuntimeDataRoot = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ManifestPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    MatrixContainerName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    MatrixDataPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ElementContainerName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ElementDataPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    MatrixImageReference = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    MatrixImageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ElementImageReference = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ElementImageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DatabaseEngine = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    DatabaseHost = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    DatabasePort = table.Column<int>(type: "INTEGER", nullable: false),
                    DatabaseName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DatabaseUsername = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DatabasePasswordSecretKind = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ExpectedUsersCount = table.Column<long>(type: "INTEGER", nullable: true),
                    ExpectedRoomsCount = table.Column<long>(type: "INTEGER", nullable: true),
                    ExpectedEventsCount = table.Column<long>(type: "INTEGER", nullable: true),
                    RoutePlanJson = table.Column<string>(type: "TEXT", nullable: false),
                    ProvenanceJson = table.Column<string>(type: "TEXT", nullable: false),
                    CollisionEvidenceJson = table.Column<string>(type: "TEXT", nullable: false),
                    BlockerSummary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    MaterializationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MaterializationStatus = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    MaterializationStartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MaterializationCompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProductionDatabaseImported = table.Column<bool>(type: "INTEGER", nullable: false),
                    MatrixProductionContainerStarted = table.Column<bool>(type: "INTEGER", nullable: false),
                    MatrixProductionHealthPassed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ElementProductionContainerStarted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ElementProductionHealthPassed = table.Column<bool>(type: "INTEGER", nullable: false),
                    RuntimeManifestSaved = table.Column<bool>(type: "INTEGER", nullable: false),
                    DatabaseOwnershipSaved = table.Column<bool>(type: "INTEGER", nullable: false),
                    RuntimeRecordsCreated = table.Column<bool>(type: "INTEGER", nullable: false),
                    UserInventorySynchronized = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaterializationEvidenceJson = table.Column<string>(type: "TEXT", nullable: true),
                    MaterializationFailureCode = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    MaterializationFailureSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CutoverPreviewId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CutoverPreviewStatus = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    CutoverPreviewCreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CutoverPreviewExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CutoverPreviewSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CutoverPreviewJson = table.Column<string>(type: "TEXT", nullable: true),
                    CutoverExecutionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CutoverStatus = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    CutoverStartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CutoverCompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TargetPublicAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MatrixNpmRouteId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ElementNpmRouteId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RuntimePromotionCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublicRoutesCreated = table.Column<bool>(type: "INTEGER", nullable: false),
                    RouteCompensationAttempted = table.Column<bool>(type: "INTEGER", nullable: false),
                    RouteCompensationCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CutoverEvidenceJson = table.Column<string>(type: "TEXT", nullable: true),
                    CutoverRollbackCheckpointJson = table.Column<string>(type: "TEXT", nullable: true),
                    CutoverFailureCode = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    CutoverFailureSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ProductionVerificationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ProductionVerificationStatus = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    ProductionVerificationStartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProductionVerificationCompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProductionVerificationValidUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProductionVerificationCheckCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductionVerificationFailedCheckCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductionVerificationEvidenceSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ProductionVerificationEvidenceJson = table.Column<string>(type: "TEXT", nullable: true),
                    ProductionVerificationReadinessReportId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProductionVerificationFailureCode = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    ProductionVerificationFailureSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RollbackPreviewId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RollbackPreviewStatus = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    RollbackPreviewCreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RollbackPreviewExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RollbackPreviewSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RollbackPreviewJson = table.Column<string>(type: "TEXT", nullable: true),
                    RollbackExecutionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RollbackStatus = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    RollbackStartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RollbackCompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RollbackRoutesRestored = table.Column<bool>(type: "INTEGER", nullable: false),
                    RollbackRuntimeRoutesRemoved = table.Column<bool>(type: "INTEGER", nullable: false),
                    RollbackTargetContainersStopped = table.Column<bool>(type: "INTEGER", nullable: false),
                    RollbackTargetRouteCompensationAttempted = table.Column<bool>(type: "INTEGER", nullable: false),
                    RollbackTargetRouteCompensationCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    RollbackSourceHandoffId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RollbackSourceHandoffSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RollbackSourceHandoffJson = table.Column<string>(type: "TEXT", nullable: true),
                    RollbackEvidenceJson = table.Column<string>(type: "TEXT", nullable: true),
                    RollbackFailureCode = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    RollbackFailureSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RollbackCompletionStatus = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    RollbackSourceCompletionAttemptId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RollbackSourceCompletionSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RollbackSourceCompletionJson = table.Column<string>(type: "TEXT", nullable: true),
                    RollbackSourceCompletionImportedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CoordinatedRollbackCompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RollbackSourceRestored = table.Column<bool>(type: "INTEGER", nullable: false),
                    RollbackRestartPoliciesRestored = table.Column<bool>(type: "INTEGER", nullable: false),
                    RollbackOriginalRunningStatesRestored = table.Column<bool>(type: "INTEGER", nullable: false),
                    RollbackMatrixVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    RollbackElementVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    RollbackTargetAuthorityVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    RollbackTargetIntegrityVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    RollbackDevelopmentExternalControlPlane = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationProductionAdoptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationProductionAdoptions_MigrationCandidateArtifacts_MigrationCandidateArtifactEntityId",
                        column: x => x.MigrationCandidateArtifactEntityId,
                        principalTable: "MigrationCandidateArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MigrationProductionAdoptions_MigrationIntakes_MigrationIntakeEntityId",
                        column: x => x.MigrationIntakeEntityId,
                        principalTable: "MigrationIntakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MigrationProductionAdoptions_MigrationPackageRevisions_MigrationPackageRevisionEntityId",
                        column: x => x.MigrationPackageRevisionEntityId,
                        principalTable: "MigrationPackageRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MigrationProductionAdoptions_MigrationStagingRuns_MigrationStagingRunEntityId",
                        column: x => x.MigrationStagingRunEntityId,
                        principalTable: "MigrationStagingRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MigrationProductionAuthorities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionAuthorityId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MigrationIntakeEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationPackageRevisionEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationCandidateArtifactEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationStagingRunEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorityType = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ActiveMigrationKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EncryptedPackageSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DecryptedArchiveSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceMigrationId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    SourceFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MatrixServerName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    SigningKeyIdentitySha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CaptureKind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SourceFrozen = table.Column<bool>(type: "INTEGER", nullable: false),
                    RehearsalOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EvidenceSchemaVersion = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    EvidenceJson = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AcknowledgementsSchemaVersion = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    AcknowledgementsJson = table.Column<string>(type: "TEXT", nullable: false),
                    AcknowledgementsSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedByOperatorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SupersededAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SupersededByOperatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupersessionReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedByOperatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RevocationReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationProductionAuthorities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationProductionAuthorities_MigrationCandidateArtifacts_MigrationCandidateArtifactEntityId",
                        column: x => x.MigrationCandidateArtifactEntityId,
                        principalTable: "MigrationCandidateArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MigrationProductionAuthorities_MigrationIntakes_MigrationIntakeEntityId",
                        column: x => x.MigrationIntakeEntityId,
                        principalTable: "MigrationIntakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MigrationProductionAuthorities_MigrationPackageRevisions_MigrationPackageRevisionEntityId",
                        column: x => x.MigrationPackageRevisionEntityId,
                        principalTable: "MigrationPackageRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MigrationProductionAuthorities_MigrationStagingRuns_MigrationStagingRunEntityId",
                        column: x => x.MigrationStagingRunEntityId,
                        principalTable: "MigrationStagingRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MigrationStagingRetirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationIntakeEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationStagingRunEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrivateRuntimeStagingId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ResourcePlanJson = table.Column<string>(type: "TEXT", nullable: false),
                    RequestedByOperatorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    StateVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrentStep = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationStagingRetirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationStagingRetirements_MigrationIntakes_MigrationIntakeEntityId",
                        column: x => x.MigrationIntakeEntityId,
                        principalTable: "MigrationIntakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MigrationStagingRetirements_MigrationStagingRuns_MigrationStagingRunEntityId",
                        column: x => x.MigrationStagingRunEntityId,
                        principalTable: "MigrationStagingRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Certificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DomainId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CertificateId = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CommonName = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsWildcard = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsStaging = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsMainPlatformCertificate = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FullchainPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PrivateKeyPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Thumbprint = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    NpmCertificateId = table.Column<int>(type: "INTEGER", nullable: true),
                    ImportedToNpm = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastValidatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastImportedToNpmAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Certificates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Domains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BaseDomain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsMainPlatformDomain = table.Column<bool>(type: "INTEGER", nullable: false),
                    DnsProvider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DnsZone = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ActiveCertificateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Domains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Domains_Certificates_ActiveCertificateId",
                        column: x => x.ActiveCertificateId,
                        principalTable: "Certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DomainCertificateRenewalPolicies",
                columns: table => new
                {
                    DomainId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AutoRenewEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AcmeEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    RenewalWindowDays = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryIntervalHours = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainCertificateRenewalPolicies", x => x.DomainId);
                    table.ForeignKey(
                        name: "FK_DomainCertificateRenewalPolicies_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DomainSecrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DomainId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProtectedValue = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainSecrets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DomainSecrets_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuntimeStackId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RestoreAttemptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DomainId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RequestedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CurrentStep = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    InputJson = table.Column<string>(type: "TEXT", nullable: true),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    EvidenceJson = table.Column<string>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    HostMutationLevel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RequiresConfirmation = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuntimeOperations_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RuntimeOperations_RestoreAttempts_RestoreAttemptId",
                        column: x => x.RestoreAttemptId,
                        principalTable: "RestoreAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RuntimeOperations_RuntimeStacks_RuntimeStackId",
                        column: x => x.RuntimeStackId,
                        principalTable: "RuntimeStacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupCatalogEntries_CatalogEntryId",
                table: "BackupCatalogEntries",
                column: "CatalogEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupCatalogEntries_CreatedAtUtc",
                table: "BackupCatalogEntries",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BackupCatalogEntries_OriginKind",
                table: "BackupCatalogEntries",
                column: "OriginKind");

            migrationBuilder.CreateIndex(
                name: "IX_BackupCatalogEntries_PayloadState",
                table: "BackupCatalogEntries",
                column: "PayloadState");

            migrationBuilder.CreateIndex(
                name: "IX_BackupCatalogEntries_SourceStackSlug_SourceBackupId",
                table: "BackupCatalogEntries",
                columns: new[] { "SourceStackSlug", "SourceBackupId" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupCatalogEntries_ValidationId",
                table: "BackupCatalogEntries",
                column: "ValidationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_CertificateId",
                table: "Certificates",
                column: "CertificateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_DomainId_IsActive",
                table: "Certificates",
                columns: new[] { "DomainId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_IsMainPlatformCertificate",
                table: "Certificates",
                column: "IsMainPlatformCertificate");

            migrationBuilder.CreateIndex(
                name: "IX_ControlPlaneUserRoles_ControlPlaneUserId_Role",
                table: "ControlPlaneUserRoles",
                columns: new[] { "ControlPlaneUserId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ControlPlaneUserRoles_Role",
                table: "ControlPlaneUserRoles",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_ControlPlaneUsers_NormalizedEmail",
                table: "ControlPlaneUsers",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ControlPlaneUsers_NormalizedUsername",
                table: "ControlPlaneUsers",
                column: "NormalizedUsername",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticsIncidentDispositions_Disposition",
                table: "DiagnosticsIncidentDispositions",
                column: "Disposition");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticsIncidentDispositions_SnoozedUntilUtc",
                table: "DiagnosticsIncidentDispositions",
                column: "SnoozedUntilUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticsIncidentDispositions_UpdatedAtUtc",
                table: "DiagnosticsIncidentDispositions",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Domains_ActiveCertificateId",
                table: "Domains",
                column: "ActiveCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_Domains_BaseDomain",
                table: "Domains",
                column: "BaseDomain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Domains_IsMainPlatformDomain",
                table: "Domains",
                column: "IsMainPlatformDomain");

            migrationBuilder.CreateIndex(
                name: "IX_DomainSecrets_DomainId_Key",
                table: "DomainSecrets",
                columns: new[] { "DomainId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstallationSecrets_InstallationId_Key",
                table: "InstallationSecrets",
                columns: new[] { "InstallationId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstallationStepExecutions_InstallationId_Sequence",
                table: "InstallationStepExecutions",
                columns: new[] { "InstallationId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_LegacyRetentionRecords_CleanupEligibleAtUtc",
                table: "LegacyRetentionRecords",
                column: "CleanupEligibleAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyRetentionRecords_MigrationAcceptanceEntityId",
                table: "LegacyRetentionRecords",
                column: "MigrationAcceptanceEntityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyRetentionRecords_MigrationIntakeEntityId",
                table: "LegacyRetentionRecords",
                column: "MigrationIntakeEntityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyRetentionRecords_RetentionRecordId",
                table: "LegacyRetentionRecords",
                column: "RetentionRecordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyRetentionRecords_Status",
                table: "LegacyRetentionRecords",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MemBootstrapGrants_ExpiresAtUtc",
                table: "MemBootstrapGrants",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MemBootstrapGrants_PendingOperatorId",
                table: "MemBootstrapGrants",
                column: "PendingOperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_MemBootstrapGrants_Status",
                table: "MemBootstrapGrants",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MemCliDeviceAuthorizationAttempts_ExpiresAtUtc",
                table: "MemCliDeviceAuthorizationAttempts",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MemCliDeviceAuthorizationAttempts_InstallationId_Status",
                table: "MemCliDeviceAuthorizationAttempts",
                columns: new[] { "InstallationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MemCliDeviceAuthorizationAttempts_UserCodeHash",
                table: "MemCliDeviceAuthorizationAttempts",
                column: "UserCodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemCliDeviceSessions_AbsoluteExpiresAtUtc",
                table: "MemCliDeviceSessions",
                column: "AbsoluteExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MemCliDeviceSessions_AuthorizationAttemptId",
                table: "MemCliDeviceSessions",
                column: "AuthorizationAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemCliDeviceSessions_CredentialHash",
                table: "MemCliDeviceSessions",
                column: "CredentialHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemCliDeviceSessions_InstallationId_RevokedAtUtc",
                table: "MemCliDeviceSessions",
                columns: new[] { "InstallationId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MemCliDeviceSessions_OperatorId_RevokedAtUtc",
                table: "MemCliDeviceSessions",
                columns: new[] { "OperatorId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MemOperatorAuditEvents_ActorOperatorId",
                table: "MemOperatorAuditEvents",
                column: "ActorOperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_MemOperatorAuditEvents_EventType_OccurredAtUtc",
                table: "MemOperatorAuditEvents",
                columns: new[] { "EventType", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MemOperatorAuditEvents_OccurredAtUtc",
                table: "MemOperatorAuditEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MemOperatorAuditEvents_SubjectOperatorId",
                table: "MemOperatorAuditEvents",
                column: "SubjectOperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_MemOperatorEnrollmentGrants_CodeHash",
                table: "MemOperatorEnrollmentGrants",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemOperatorEnrollmentGrants_ExpiresAtUtc",
                table: "MemOperatorEnrollmentGrants",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MemOperatorEnrollmentGrants_OperatorId_Status",
                table: "MemOperatorEnrollmentGrants",
                columns: new[] { "OperatorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MemOperatorStepUpGrants_ExpiresAtUtc",
                table: "MemOperatorStepUpGrants",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MemOperatorStepUpGrants_OperatorId_RevokedAtUtc",
                table: "MemOperatorStepUpGrants",
                columns: new[] { "OperatorId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MemOperatorStepUpGrants_OperatorId_SessionId",
                table: "MemOperatorStepUpGrants",
                columns: new[] { "OperatorId", "SessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemSecuritySettings_UpdatedAtUtc",
                table: "MemSecuritySettings",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationAcceptances_AcceptanceId",
                table: "MigrationAcceptances",
                column: "AcceptanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationAcceptances_ExecutionId",
                table: "MigrationAcceptances",
                column: "ExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationAcceptances_MigrationIntakeEntityId",
                table: "MigrationAcceptances",
                column: "MigrationIntakeEntityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationBaselineBackupHandoffs_CatalogEntryId",
                table: "MigrationBaselineBackupHandoffs",
                column: "CatalogEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationBaselineBackupHandoffs_HandoffId",
                table: "MigrationBaselineBackupHandoffs",
                column: "HandoffId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationBaselineBackupHandoffs_MigrationAcceptanceEntityId",
                table: "MigrationBaselineBackupHandoffs",
                column: "MigrationAcceptanceEntityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationBaselineBackupHandoffs_MigrationIntakeEntityId",
                table: "MigrationBaselineBackupHandoffs",
                column: "MigrationIntakeEntityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationCandidateArtifacts_CandidateArtifactId",
                table: "MigrationCandidateArtifacts",
                column: "CandidateArtifactId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationCandidateArtifacts_MigrationConversionAttemptEntityId",
                table: "MigrationCandidateArtifacts",
                column: "MigrationConversionAttemptEntityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationCandidateArtifacts_RetentionState",
                table: "MigrationCandidateArtifacts",
                column: "RetentionState");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationCandidateArtifacts_VerificationStatus",
                table: "MigrationCandidateArtifacts",
                column: "VerificationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationConversionAttempts_ActiveMigrationKey",
                table: "MigrationConversionAttempts",
                column: "ActiveMigrationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationConversionAttempts_ConversionAttemptId",
                table: "MigrationConversionAttempts",
                column: "ConversionAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationConversionAttempts_MigrationIntakeEntityId_CreatedAtUtc",
                table: "MigrationConversionAttempts",
                columns: new[] { "MigrationIntakeEntityId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationConversionAttempts_MigrationPackageRevisionEntityId",
                table: "MigrationConversionAttempts",
                column: "MigrationPackageRevisionEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationConversionAttempts_RetryOfConversionAttemptEntityId",
                table: "MigrationConversionAttempts",
                column: "RetryOfConversionAttemptEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationConversionAttempts_Status",
                table: "MigrationConversionAttempts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationIntakes_ArchivedAtUtc_UpdatedAtUtc",
                table: "MigrationIntakes",
                columns: new[] { "ArchivedAtUtc", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationIntakes_CreatedAtUtc",
                table: "MigrationIntakes",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationIntakes_DisplayName",
                table: "MigrationIntakes",
                column: "DisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationIntakes_IntakeId",
                table: "MigrationIntakes",
                column: "IntakeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationIntakes_LifecycleStatus_UpdatedAtUtc",
                table: "MigrationIntakes",
                columns: new[] { "LifecycleStatus", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationIntakes_UpdatedAtUtc",
                table: "MigrationIntakes",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationPackageRevisions_ActivePurposeKey",
                table: "MigrationPackageRevisions",
                column: "ActivePurposeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationPackageRevisions_MigrationIntakeEntityId_Purpose_CreatedAtUtc",
                table: "MigrationPackageRevisions",
                columns: new[] { "MigrationIntakeEntityId", "Purpose", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationPackageRevisions_MigrationIntakeEntityId_RevisionNumber",
                table: "MigrationPackageRevisions",
                columns: new[] { "MigrationIntakeEntityId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationPackageRevisions_PackageRevisionId",
                table: "MigrationPackageRevisions",
                column: "PackageRevisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationPackageRevisions_RetentionState",
                table: "MigrationPackageRevisions",
                column: "RetentionState");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationPackageRevisions_Status",
                table: "MigrationPackageRevisions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_AdoptionPlanId",
                table: "MigrationProductionAdoptions",
                column: "AdoptionPlanId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_CutoverExecutionId",
                table: "MigrationProductionAdoptions",
                column: "CutoverExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_CutoverPreviewId",
                table: "MigrationProductionAdoptions",
                column: "CutoverPreviewId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_CutoverStatus",
                table: "MigrationProductionAdoptions",
                column: "CutoverStatus");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_ElementInstanceId",
                table: "MigrationProductionAdoptions",
                column: "ElementInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_MaterializationId",
                table: "MigrationProductionAdoptions",
                column: "MaterializationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_MaterializationStatus",
                table: "MigrationProductionAdoptions",
                column: "MaterializationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_MatrixInstanceId",
                table: "MigrationProductionAdoptions",
                column: "MatrixInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_MigrationCandidateArtifactEntityId",
                table: "MigrationProductionAdoptions",
                column: "MigrationCandidateArtifactEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_MigrationIntakeEntityId",
                table: "MigrationProductionAdoptions",
                column: "MigrationIntakeEntityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_MigrationPackageRevisionEntityId",
                table: "MigrationProductionAdoptions",
                column: "MigrationPackageRevisionEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_MigrationStagingRunEntityId",
                table: "MigrationProductionAdoptions",
                column: "MigrationStagingRunEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_ProductionVerificationId",
                table: "MigrationProductionAdoptions",
                column: "ProductionVerificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_ProductionVerificationStatus",
                table: "MigrationProductionAdoptions",
                column: "ProductionVerificationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_RollbackCompletionStatus",
                table: "MigrationProductionAdoptions",
                column: "RollbackCompletionStatus");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_RollbackExecutionId",
                table: "MigrationProductionAdoptions",
                column: "RollbackExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_RollbackPreviewId",
                table: "MigrationProductionAdoptions",
                column: "RollbackPreviewId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_RollbackSourceCompletionAttemptId",
                table: "MigrationProductionAdoptions",
                column: "RollbackSourceCompletionAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_RollbackSourceHandoffId",
                table: "MigrationProductionAdoptions",
                column: "RollbackSourceHandoffId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_RollbackStatus",
                table: "MigrationProductionAdoptions",
                column: "RollbackStatus");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_RuntimeStackId",
                table: "MigrationProductionAdoptions",
                column: "RuntimeStackId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_Status",
                table: "MigrationProductionAdoptions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAdoptions_TargetStackSlug",
                table: "MigrationProductionAdoptions",
                column: "TargetStackSlug");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAuthorities_ActiveMigrationKey",
                table: "MigrationProductionAuthorities",
                column: "ActiveMigrationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAuthorities_AuthorityType",
                table: "MigrationProductionAuthorities",
                column: "AuthorityType");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAuthorities_CreatedAtUtc",
                table: "MigrationProductionAuthorities",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAuthorities_MigrationCandidateArtifactEntityId",
                table: "MigrationProductionAuthorities",
                column: "MigrationCandidateArtifactEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAuthorities_MigrationIntakeEntityId",
                table: "MigrationProductionAuthorities",
                column: "MigrationIntakeEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAuthorities_MigrationPackageRevisionEntityId",
                table: "MigrationProductionAuthorities",
                column: "MigrationPackageRevisionEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAuthorities_MigrationStagingRunEntityId",
                table: "MigrationProductionAuthorities",
                column: "MigrationStagingRunEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAuthorities_ProductionAuthorityId",
                table: "MigrationProductionAuthorities",
                column: "ProductionAuthorityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationProductionAuthorities_Status",
                table: "MigrationProductionAuthorities",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationSources_MigrationIntakeEntityId_SourceId",
                table: "MigrationSources",
                columns: new[] { "MigrationIntakeEntityId", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationStagingRetirements_MigrationIntakeEntityId",
                table: "MigrationStagingRetirements",
                column: "MigrationIntakeEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationStagingRetirements_MigrationStagingRunEntityId",
                table: "MigrationStagingRetirements",
                column: "MigrationStagingRunEntityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationStagingRetirements_PrivateRuntimeStagingId",
                table: "MigrationStagingRetirements",
                column: "PrivateRuntimeStagingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationStagingRetirements_Status_RequestedAtUtc",
                table: "MigrationStagingRetirements",
                columns: new[] { "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationStagingRuns_ActiveMigrationKey",
                table: "MigrationStagingRuns",
                column: "ActiveMigrationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationStagingRuns_MigrationCandidateArtifactEntityId",
                table: "MigrationStagingRuns",
                column: "MigrationCandidateArtifactEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationStagingRuns_MigrationIntakeEntityId",
                table: "MigrationStagingRuns",
                column: "MigrationIntakeEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationStagingRuns_RetryOfStagingRunEntityId",
                table: "MigrationStagingRuns",
                column: "RetryOfStagingRunEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationStagingRuns_StagingRunId",
                table: "MigrationStagingRuns",
                column: "StagingRunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationTwoServerQualifications_ClosureId",
                table: "MigrationTwoServerQualifications",
                column: "ClosureId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationTwoServerQualifications_ClosureStatus",
                table: "MigrationTwoServerQualifications",
                column: "ClosureStatus");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationTwoServerQualifications_MigrationIntakeEntityId",
                table: "MigrationTwoServerQualifications",
                column: "MigrationIntakeEntityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationTwoServerQualifications_QualificationEvidenceSha256",
                table: "MigrationTwoServerQualifications",
                column: "QualificationEvidenceSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationTwoServerQualifications_QualificationId",
                table: "MigrationTwoServerQualifications",
                column: "QualificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationTwoServerQualifications_SourceEvidenceAttemptId",
                table: "MigrationTwoServerQualifications",
                column: "SourceEvidenceAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationTwoServerQualifications_SourceEvidenceSha256",
                table: "MigrationTwoServerQualifications",
                column: "SourceEvidenceSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationTwoServerQualifications_Status",
                table: "MigrationTwoServerQualifications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreAttempts_ActiveSourceKey",
                table: "RestoreAttempts",
                column: "ActiveSourceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestoreAttempts_BackupCatalogEntryId",
                table: "RestoreAttempts",
                column: "BackupCatalogEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreAttempts_RestoreSessionId",
                table: "RestoreAttempts",
                column: "RestoreSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestoreAttempts_RuntimeOperationId",
                table: "RestoreAttempts",
                column: "RuntimeOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreAttempts_SourceOriginKindSnapshot_SourceStackSlugSnapshot_SourceBackupIdSnapshot",
                table: "RestoreAttempts",
                columns: new[] { "SourceOriginKindSnapshot", "SourceStackSlugSnapshot", "SourceBackupIdSnapshot" });

            migrationBuilder.CreateIndex(
                name: "IX_RestoreAttempts_Status",
                table: "RestoreAttempts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreTargetClaims_ActiveClaimKey",
                table: "RestoreTargetClaims",
                column: "ActiveClaimKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestoreTargetClaims_RestoreAttemptId",
                table: "RestoreTargetClaims",
                column: "RestoreAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreTargetClaims_RestoreAttemptId_ResourceType",
                table: "RestoreTargetClaims",
                columns: new[] { "RestoreAttemptId", "ResourceType" });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeOperations_DomainId",
                table: "RuntimeOperations",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeOperations_DomainId_Operation_IdempotencyKey",
                table: "RuntimeOperations",
                columns: new[] { "DomainId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeOperations_DomainId_Operation_RequestedAtUtc",
                table: "RuntimeOperations",
                columns: new[] { "DomainId", "Operation", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeOperations_IdempotencyKey",
                table: "RuntimeOperations",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeOperations_Operation",
                table: "RuntimeOperations",
                column: "Operation");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeOperations_RequestedAtUtc",
                table: "RuntimeOperations",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeOperations_RestoreAttemptId",
                table: "RuntimeOperations",
                column: "RestoreAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeOperations_RuntimeStackId",
                table: "RuntimeOperations",
                column: "RuntimeStackId");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeOperations_RuntimeStackId_Operation_RequestedAtUtc",
                table: "RuntimeOperations",
                columns: new[] { "RuntimeStackId", "Operation", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeOperations_Status",
                table: "RuntimeOperations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeReadinessReports_RuntimeStackId",
                table: "RuntimeReadinessReports",
                column: "RuntimeStackId");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeReadinessReports_RuntimeStackId_CreatedAtUtc",
                table: "RuntimeReadinessReports",
                columns: new[] { "RuntimeStackId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeReadinessReports_RuntimeStackId_ReportKind",
                table: "RuntimeReadinessReports",
                columns: new[] { "RuntimeStackId", "ReportKind" });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeReadinessReports_Status",
                table: "RuntimeReadinessReports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeRoutes_Provider_PublicHost",
                table: "RuntimeRoutes",
                columns: new[] { "Provider", "PublicHost" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeRoutes_ProviderRouteId",
                table: "RuntimeRoutes",
                column: "ProviderRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeRoutes_RuntimeServiceInstanceId",
                table: "RuntimeRoutes",
                column: "RuntimeServiceInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeRoutes_RuntimeStackId_ServiceKey",
                table: "RuntimeRoutes",
                columns: new[] { "RuntimeStackId", "ServiceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeServiceInstances_ContainerName",
                table: "RuntimeServiceInstances",
                column: "ContainerName");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeServiceInstances_InstanceId",
                table: "RuntimeServiceInstances",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeServiceInstances_RuntimeStackId_ServiceKey",
                table: "RuntimeServiceInstances",
                columns: new[] { "RuntimeStackId", "ServiceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeServices_ServiceName",
                table: "RuntimeServices",
                column: "ServiceName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeStackDatabases_DatabaseName",
                table: "RuntimeStackDatabases",
                column: "DatabaseName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeStackDatabases_DatabaseUsername",
                table: "RuntimeStackDatabases",
                column: "DatabaseUsername",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeStackDatabases_RuntimeStackId",
                table: "RuntimeStackDatabases",
                column: "RuntimeStackId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeStackDatabases_Status",
                table: "RuntimeStackDatabases",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeStacks_LastVerifiedStatus",
                table: "RuntimeStacks",
                column: "LastVerifiedStatus");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeStacks_Slug",
                table: "RuntimeStacks",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeStacks_Status",
                table: "RuntimeStacks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeStackSecrets_RuntimeStackId_SecretKind",
                table: "RuntimeStackSecrets",
                columns: new[] { "RuntimeStackId", "SecretKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeStackSecrets_SecretKind",
                table: "RuntimeStackSecrets",
                column: "SecretKind");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeStackSecrets_Status",
                table: "RuntimeStackSecrets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeStackUsers_RuntimeStackId_IsFirstAdmin",
                table: "RuntimeStackUsers",
                columns: new[] { "RuntimeStackId", "IsFirstAdmin" });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeStackUsers_RuntimeStackId_MatrixUserId",
                table: "RuntimeStackUsers",
                columns: new[] { "RuntimeStackId", "MatrixUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeStackUsers_RuntimeStackId_Username",
                table: "RuntimeStackUsers",
                columns: new[] { "RuntimeStackId", "Username" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeStackUsers_Status",
                table: "RuntimeStackUsers",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_Certificates_Domains_DomainId",
                table: "Certificates",
                column: "DomainId",
                principalTable: "Domains",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Certificates_Domains_DomainId",
                table: "Certificates");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "ControlPlaneUserRoles");

            migrationBuilder.DropTable(
                name: "DiagnosticsIncidentDispositions");

            migrationBuilder.DropTable(
                name: "DomainCertificateRenewalPolicies");

            migrationBuilder.DropTable(
                name: "DomainSecrets");

            migrationBuilder.DropTable(
                name: "InstallationSecrets");

            migrationBuilder.DropTable(
                name: "InstallationStepExecutions");

            migrationBuilder.DropTable(
                name: "LegacyRetentionRecords");

            migrationBuilder.DropTable(
                name: "MemBootstrapGrants");

            migrationBuilder.DropTable(
                name: "MemCliDeviceAuthorizationAttempts");

            migrationBuilder.DropTable(
                name: "MemCliDeviceSessions");

            migrationBuilder.DropTable(
                name: "MemOperatorAuditEvents");

            migrationBuilder.DropTable(
                name: "MemOperatorEnrollmentGrants");

            migrationBuilder.DropTable(
                name: "MemOperatorStepUpGrants");

            migrationBuilder.DropTable(
                name: "MemSecuritySettings");

            migrationBuilder.DropTable(
                name: "MigrationBaselineBackupHandoffs");

            migrationBuilder.DropTable(
                name: "MigrationProductionAdoptions");

            migrationBuilder.DropTable(
                name: "MigrationProductionAuthorities");

            migrationBuilder.DropTable(
                name: "MigrationSources");

            migrationBuilder.DropTable(
                name: "MigrationStagingRetirements");

            migrationBuilder.DropTable(
                name: "MigrationTwoServerQualifications");

            migrationBuilder.DropTable(
                name: "RestoreTargetClaims");

            migrationBuilder.DropTable(
                name: "RuntimeOperations");

            migrationBuilder.DropTable(
                name: "RuntimeReadinessReports");

            migrationBuilder.DropTable(
                name: "RuntimeRoutes");

            migrationBuilder.DropTable(
                name: "RuntimeServices");

            migrationBuilder.DropTable(
                name: "RuntimeStackDatabases");

            migrationBuilder.DropTable(
                name: "RuntimeStackSecrets");

            migrationBuilder.DropTable(
                name: "RuntimeStackUsers");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "ControlPlaneUsers");

            migrationBuilder.DropTable(
                name: "Installations");

            migrationBuilder.DropTable(
                name: "MigrationAcceptances");

            migrationBuilder.DropTable(
                name: "MigrationStagingRuns");

            migrationBuilder.DropTable(
                name: "RestoreAttempts");

            migrationBuilder.DropTable(
                name: "RuntimeServiceInstances");

            migrationBuilder.DropTable(
                name: "MigrationCandidateArtifacts");

            migrationBuilder.DropTable(
                name: "BackupCatalogEntries");

            migrationBuilder.DropTable(
                name: "RuntimeStacks");

            migrationBuilder.DropTable(
                name: "MigrationConversionAttempts");

            migrationBuilder.DropTable(
                name: "MigrationPackageRevisions");

            migrationBuilder.DropTable(
                name: "MigrationIntakes");

            migrationBuilder.DropTable(
                name: "Domains");

            migrationBuilder.DropTable(
                name: "Certificates");
        }
    }
}
