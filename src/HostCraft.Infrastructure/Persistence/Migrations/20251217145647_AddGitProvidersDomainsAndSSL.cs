using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostCraft.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGitProvidersDomainsAndSSL : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultLetsEncryptEmail",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProxyDeployedAt",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProxyVersion",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdditionalDomains",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateChallengePath",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeploymentMode",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "EnableHttps",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ForceHttps",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GitOwner",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GitProviderId",
                table: "Applications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitRepoName",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LetsEncryptEmail",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Certificates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Domain = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RenewalDays = table.Column<int>(type: "INTEGER", nullable: false),
                    LastRenewalAttempt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AutoRenew = table.Column<bool>(type: "INTEGER", nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", nullable: true),
                    Issuer = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                name: "GitProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    AvatarUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    AccessToken = table.Column<string>(type: "TEXT", nullable: false),
                    RefreshToken = table.Column<string>(type: "TEXT", nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Scopes = table.Column<string>(type: "TEXT", nullable: true),
                    ApiUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ProviderId = table.Column<string>(type: "TEXT", nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_Applications_GitProviderId",
                table: "Applications",
                column: "GitProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_ApplicationId",
                table: "Certificates",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_GitProviders_UserId",
                table: "GitProviders",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Applications_GitProviders_GitProviderId",
                table: "Applications",
                column: "GitProviderId",
                principalTable: "GitProviders",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Applications_GitProviders_GitProviderId",
                table: "Applications");

            migrationBuilder.DropTable(
                name: "Certificates");

            migrationBuilder.DropTable(
                name: "GitProviders");

            migrationBuilder.DropIndex(
                name: "IX_Applications_GitProviderId",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "DefaultLetsEncryptEmail",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "ProxyDeployedAt",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "ProxyVersion",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "AdditionalDomains",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "CertificateChallengePath",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "DeploymentMode",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "EnableHttps",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "ForceHttps",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "GitOwner",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "GitProviderId",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "GitRepoName",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "LetsEncryptEmail",
                table: "Applications");
        }
    }
}
