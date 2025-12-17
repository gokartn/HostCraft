using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostCraft.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostCraftDomain = table.Column<string>(type: "TEXT", nullable: true),
                    HostCraftEnableHttps = table.Column<bool>(type: "INTEGER", nullable: false),
                    HostCraftLetsEncryptEmail = table.Column<string>(type: "TEXT", nullable: true),
                    ConfiguredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProxyUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CertificateStatus = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemSettings");
        }
    }
}
