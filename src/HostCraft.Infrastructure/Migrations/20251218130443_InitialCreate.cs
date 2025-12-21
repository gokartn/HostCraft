using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HostCraft.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrivateKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KeyData = table.Column<string>(type: "text", nullable: false),
                    Passphrase = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Uuid = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    HostCraftDomain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    HostCraftApiDomain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    HostCraftEnableHttps = table.Column<bool>(type: "boolean", nullable: false),
                    HostCraftLetsEncryptEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ConfiguredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProxyUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CertificateStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Uuid = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    EmailConfirmationToken = table.Column<string>(type: "text", nullable: true),
                    EmailConfirmationTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsLockedOut = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastPasswordChangeAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockoutEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordResetToken = table.Column<string>(type: "text", nullable: true),
                    PasswordResetTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecoveryCodes = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    TwoFactorSecret = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    Username = table.Column<string>(type: "text", nullable: true),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    AdditionalData = table.Column<string>(type: "text", nullable: true),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    SessionId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Token = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedByIp = table.Column<string>(type: "text", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByIp = table.Column<string>(type: "text", nullable: true),
                    ReplacedByToken = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Servers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PrivateKeyId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProxyType = table.Column<int>(type: "integer", nullable: false),
                    DefaultLetsEncryptEmail = table.Column<string>(type: "text", nullable: true),
                    ProxyVersion = table.Column<string>(type: "text", nullable: true),
                    ProxyDeployedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SwarmJoinToken = table.Column<string>(type: "text", nullable: true),
                    SwarmManagerAddress = table.Column<string>(type: "text", nullable: true),
                    RegionId = table.Column<int>(type: "integer", nullable: true),
                    IsSwarmManager = table.Column<bool>(type: "boolean", nullable: false),
                    SwarmManagerCount = table.Column<int>(type: "integer", nullable: true),
                    SwarmWorkerCount = table.Column<int>(type: "integer", nullable: true),
                    SwarmNodeId = table.Column<string>(type: "text", nullable: true),
                    IsSwarmWorker = table.Column<bool>(type: "boolean", nullable: false),
                    SwarmNodeState = table.Column<string>(type: "text", nullable: true),
                    SwarmNodeAvailability = table.Column<string>(type: "text", nullable: true),
                    SwarmId = table.Column<string>(type: "text", nullable: true),
                    ActualHostname = table.Column<string>(type: "text", nullable: true),
                    SwarmAdvertiseAddress = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastHealthCheck = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    LastFailureAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Servers_PrivateKeys_PrivateKeyId",
                        column: x => x.PrivateKeyId,
                        principalTable: "PrivateKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Servers_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GitProviderSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "text", nullable: true),
                    ClientSecret = table.Column<string>(type: "text", nullable: true),
                    ApiUrl = table.Column<string>(type: "text", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitProviderSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GitProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    AvatarUrl = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    AccessToken = table.Column<string>(type: "text", nullable: false),
                    RefreshToken = table.Column<string>(type: "text", nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Scopes = table.Column<string>(type: "text", nullable: true),
                    ApiUrl = table.Column<string>(type: "text", nullable: true),
                    ProviderId = table.Column<string>(type: "text", nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitProviders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitProviders_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Uuid = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    ServerId = table.Column<int>(type: "integer", nullable: false),
                    GitProviderId = table.Column<int>(type: "integer", nullable: true),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    GitRepository = table.Column<string>(type: "text", nullable: true),
                    GitBranch = table.Column<string>(type: "text", nullable: true),
                    GitOwner = table.Column<string>(type: "text", nullable: true),
                    GitRepoName = table.Column<string>(type: "text", nullable: true),
                    DockerImage = table.Column<string>(type: "text", nullable: true),
                    DockerComposeFile = table.Column<string>(type: "text", nullable: true),
                    Dockerfile = table.Column<string>(type: "text", nullable: true),
                    BuildContext = table.Column<string>(type: "text", nullable: true),
                    BuildArgs = table.Column<string>(type: "text", nullable: true),
                    WatchPaths = table.Column<string>(type: "text", nullable: true),
                    AutoDeployOnPush = table.Column<bool>(type: "boolean", nullable: false),
                    EnablePreviewDeployments = table.Column<bool>(type: "boolean", nullable: false),
                    WebhookSecret = table.Column<string>(type: "text", nullable: true),
                    LastCommitSha = table.Column<string>(type: "text", nullable: true),
                    LastCommitMessage = table.Column<string>(type: "text", nullable: true),
                    CloneSubmodules = table.Column<bool>(type: "boolean", nullable: false),
                    Domain = table.Column<string>(type: "text", nullable: true),
                    AdditionalDomains = table.Column<string>(type: "text", nullable: true),
                    EnableHttps = table.Column<bool>(type: "boolean", nullable: false),
                    ForceHttps = table.Column<bool>(type: "boolean", nullable: false),
                    LetsEncryptEmail = table.Column<string>(type: "text", nullable: true),
                    CertificateChallengePath = table.Column<string>(type: "text", nullable: true),
                    Port = table.Column<int>(type: "integer", nullable: true),
                    Replicas = table.Column<int>(type: "integer", nullable: false),
                    DeploymentMode = table.Column<int>(type: "integer", nullable: false),
                    MemoryLimitBytes = table.Column<long>(type: "bigint", nullable: true),
                    CpuLimit = table.Column<long>(type: "bigint", nullable: true),
                    AutoDeploy = table.Column<bool>(type: "boolean", nullable: false),
                    HealthCheckUrl = table.Column<string>(type: "text", nullable: true),
                    HealthCheckIntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    HealthCheckTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    AutoRestart = table.Column<bool>(type: "boolean", nullable: false),
                    AutoRollback = table.Column<bool>(type: "boolean", nullable: false),
                    BackupSchedule = table.Column<string>(type: "text", nullable: true),
                    BackupRetentionDays = table.Column<int>(type: "integer", nullable: true),
                    SwarmReplicas = table.Column<int>(type: "integer", nullable: true),
                    SwarmPlacementConstraints = table.Column<string>(type: "text", nullable: true),
                    SwarmUpdateConfig = table.Column<string>(type: "text", nullable: true),
                    SwarmRollbackConfig = table.Column<string>(type: "text", nullable: true),
                    SwarmMode = table.Column<string>(type: "text", nullable: true),
                    SwarmEndpointSpec = table.Column<string>(type: "text", nullable: true),
                    SwarmNetworks = table.Column<string>(type: "text", nullable: true),
                    SwarmStopGracePeriod = table.Column<long>(type: "bigint", nullable: true),
                    SwarmServiceId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastDeployedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastHealthCheckAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveHealthCheckFailures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Applications_GitProviders_GitProviderId",
                        column: x => x.GitProviderId,
                        principalTable: "GitProviders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Applications_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Applications_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Certificates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApplicationId = table.Column<int>(type: "integer", nullable: false),
                    Domain = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RenewalDays = table.Column<int>(type: "integer", nullable: false),
                    LastRenewalAttempt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AutoRenew = table.Column<bool>(type: "boolean", nullable: false),
                    SerialNumber = table.Column<string>(type: "text", nullable: true),
                    Issuer = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Certificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Certificates_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Deployments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Uuid = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ApplicationId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CommitHash = table.Column<string>(type: "text", nullable: true),
                    CommitSha = table.Column<string>(type: "text", nullable: true),
                    CommitMessage = table.Column<string>(type: "text", nullable: true),
                    CommitAuthor = table.Column<string>(type: "text", nullable: true),
                    TriggeredBy = table.Column<string>(type: "text", nullable: true),
                    IsPreview = table.Column<bool>(type: "boolean", nullable: false),
                    PreviewId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ImageTag = table.Column<string>(type: "text", nullable: true),
                    ImageDigest = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ContainerId = table.Column<string>(type: "text", nullable: true),
                    ServiceId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deployments_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EnvironmentVariables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApplicationId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    IsSecret = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnvironmentVariables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnvironmentVariables_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HealthChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApplicationId = table.Column<int>(type: "integer", nullable: true),
                    ServerId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResponseTimeMs = table.Column<int>(type: "integer", nullable: false),
                    StatusCode = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealthChecks_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HealthChecks_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Volumes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Uuid = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ApplicationId = table.Column<int>(type: "integer", nullable: true),
                    ServerId = table.Column<int>(type: "integer", nullable: false),
                    Driver = table.Column<string>(type: "text", nullable: true),
                    MountPoint = table.Column<string>(type: "text", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IsBackedUp = table.Column<bool>(type: "boolean", nullable: false),
                    BackupSchedule = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Volumes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Volumes_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Volumes_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeploymentId = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Level = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentLogs_Deployments_DeploymentId",
                        column: x => x.DeploymentId,
                        principalTable: "Deployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Backups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Uuid = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ApplicationId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    S3Bucket = table.Column<string>(type: "text", nullable: true),
                    S3Key = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    RetentionDays = table.Column<int>(type: "integer", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VolumeId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Backups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Backups_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Backups_Volumes_VolumeId",
                        column: x => x.VolumeId,
                        principalTable: "Volumes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_GitProviderId",
                table: "Applications",
                column: "GitProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_ProjectId",
                table: "Applications",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_ServerId_Name",
                table: "Applications",
                columns: new[] { "ServerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Applications_Uuid",
                table: "Applications",
                column: "Uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Backups_ApplicationId",
                table: "Backups",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Backups_ExpiresAt",
                table: "Backups",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Backups_Status",
                table: "Backups",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Backups_Uuid",
                table: "Backups",
                column: "Uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Backups_VolumeId",
                table: "Backups",
                column: "VolumeId");

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_ApplicationId",
                table: "Certificates",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentLogs_DeploymentId",
                table: "DeploymentLogs",
                column: "DeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ApplicationId",
                table: "Deployments",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_Status",
                table: "Deployments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_Uuid",
                table: "Deployments",
                column: "Uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnvironmentVariables_ApplicationId_Key",
                table: "EnvironmentVariables",
                columns: new[] { "ApplicationId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitProviders_UserId",
                table: "GitProviders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_GitProviderSettings_Type_ApiUrl",
                table: "GitProviderSettings",
                columns: new[] { "Type", "ApiUrl" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealthChecks_ApplicationId",
                table: "HealthChecks",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_HealthChecks_CheckedAt",
                table: "HealthChecks",
                column: "CheckedAt");

            migrationBuilder.CreateIndex(
                name: "IX_HealthChecks_ServerId",
                table: "HealthChecks",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateKeys_Name",
                table: "PrivateKeys",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Uuid",
                table: "Projects",
                column: "Uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regions_Code",
                table: "Regions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Servers_Name",
                table: "Servers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_PrivateKeyId",
                table: "Servers",
                column: "PrivateKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_RegionId",
                table: "Servers",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Uuid",
                table: "Users",
                column: "Uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EventType",
                table: "AuditLogs",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_IsSuccess",
                table: "AuditLogs",
                column: "IsSuccess");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ExpiresAt",
                table: "RefreshTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Volumes_ApplicationId",
                table: "Volumes",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Volumes_ServerId_Name",
                table: "Volumes",
                columns: new[] { "ServerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Volumes_Uuid",
                table: "Volumes",
                column: "Uuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Backups");

            migrationBuilder.DropTable(
                name: "Certificates");

            migrationBuilder.DropTable(
                name: "DeploymentLogs");

            migrationBuilder.DropTable(
                name: "EnvironmentVariables");

            migrationBuilder.DropTable(
                name: "HealthChecks");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "Volumes");

            migrationBuilder.DropTable(
                name: "Deployments");

            migrationBuilder.DropTable(
                name: "Applications");

            migrationBuilder.DropTable(
                name: "GitProviders");

            migrationBuilder.DropTable(
                name: "GitProviderSettings");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Servers");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "PrivateKeys");

            migrationBuilder.DropTable(
                name: "Regions");
        }
    }
}
