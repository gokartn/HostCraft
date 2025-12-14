using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostCraft.Infrastructure.Persistence.Migrations
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    KeyData = table.Column<string>(type: "TEXT", nullable: false),
                    Passphrase = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Uuid = table.Column<Guid>(type: "TEXT", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Uuid = table.Column<Guid>(type: "TEXT", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Email = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Servers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PrivateKeyId = table.Column<int>(type: "INTEGER", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ProxyType = table.Column<int>(type: "INTEGER", nullable: false),
                    SwarmJoinToken = table.Column<string>(type: "TEXT", nullable: true),
                    SwarmManagerAddress = table.Column<string>(type: "TEXT", nullable: true),
                    RegionId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsSwarmManager = table.Column<bool>(type: "INTEGER", nullable: false),
                    SwarmManagerCount = table.Column<int>(type: "INTEGER", nullable: true),
                    SwarmWorkerCount = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastHealthCheck = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    LastFailureAt = table.Column<DateTime>(type: "TEXT", nullable: true)
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
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Uuid = table.Column<Guid>(type: "TEXT", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    GitRepository = table.Column<string>(type: "TEXT", nullable: true),
                    GitBranch = table.Column<string>(type: "TEXT", nullable: true),
                    DockerImage = table.Column<string>(type: "TEXT", nullable: true),
                    DockerComposeFile = table.Column<string>(type: "TEXT", nullable: true),
                    Dockerfile = table.Column<string>(type: "TEXT", nullable: true),
                    BuildContext = table.Column<string>(type: "TEXT", nullable: true),
                    Domain = table.Column<string>(type: "TEXT", nullable: true),
                    Port = table.Column<int>(type: "INTEGER", nullable: true),
                    Replicas = table.Column<int>(type: "INTEGER", nullable: false),
                    MemoryLimitBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    CpuLimit = table.Column<long>(type: "INTEGER", nullable: true),
                    AutoDeploy = table.Column<bool>(type: "INTEGER", nullable: false),
                    HealthCheckUrl = table.Column<string>(type: "TEXT", nullable: true),
                    HealthCheckIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    HealthCheckTimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxConsecutiveFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoRestart = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoRollback = table.Column<bool>(type: "INTEGER", nullable: false),
                    BackupSchedule = table.Column<string>(type: "TEXT", nullable: true),
                    BackupRetentionDays = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastDeployedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastHealthCheckAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConsecutiveHealthCheckFailures = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
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
                name: "Deployments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Uuid = table.Column<Guid>(type: "TEXT", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CommitHash = table.Column<string>(type: "TEXT", nullable: true),
                    ImageTag = table.Column<string>(type: "TEXT", nullable: true),
                    ImageDigest = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ContainerId = table.Column<string>(type: "TEXT", nullable: true),
                    ServiceId = table.Column<string>(type: "TEXT", nullable: true)
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    IsSecret = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ResponseTimeMs = table.Column<int>(type: "INTEGER", nullable: false),
                    StatusCode = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Uuid = table.Column<Guid>(type: "TEXT", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Driver = table.Column<string>(type: "TEXT", nullable: true),
                    MountPoint = table.Column<string>(type: "TEXT", nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    IsBackedUp = table.Column<bool>(type: "INTEGER", nullable: false),
                    BackupSchedule = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeploymentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Uuid = table.Column<Guid>(type: "TEXT", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    S3Bucket = table.Column<string>(type: "TEXT", nullable: true),
                    S3Key = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    RetentionDays = table.Column<int>(type: "INTEGER", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VolumeId = table.Column<int>(type: "INTEGER", nullable: true)
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
                column: "Name",
                unique: true);

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
                name: "Backups");

            migrationBuilder.DropTable(
                name: "DeploymentLogs");

            migrationBuilder.DropTable(
                name: "EnvironmentVariables");

            migrationBuilder.DropTable(
                name: "HealthChecks");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Volumes");

            migrationBuilder.DropTable(
                name: "Deployments");

            migrationBuilder.DropTable(
                name: "Applications");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Servers");

            migrationBuilder.DropTable(
                name: "PrivateKeys");

            migrationBuilder.DropTable(
                name: "Regions");
        }
    }
}
