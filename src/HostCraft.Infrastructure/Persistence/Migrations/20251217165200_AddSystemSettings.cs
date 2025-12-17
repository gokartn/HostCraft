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
                    Id = table.Column<int>(type: "integer", nullable: false),
                    HostCraftDomain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemSettings");
        }
    }
}
