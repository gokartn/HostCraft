using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostCraft.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSwarmAndGitHubColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add Swarm-related columns to Servers table
            migrationBuilder.AddColumn<bool>(
                name: "IsSwarmWorker",
                table: "Servers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SwarmNodeAvailability",
                table: "Servers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwarmNodeId",
                table: "Servers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwarmNodeState",
                table: "Servers",
                type: "text",
                nullable: true);

            // Add GitHub and Swarm-related columns to Applications table
            migrationBuilder.AddColumn<bool>(
                name: "AutoDeployOnPush",
                table: "Applications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BuildArgs",
                table: "Applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CloneSubmodules",
                table: "Applications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnablePreviewDeployments",
                table: "Applications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastCommitMessage",
                table: "Applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastCommitSha",
                table: "Applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwarmEndpointSpec",
                table: "Applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwarmMode",
                table: "Applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwarmNetworks",
                table: "Applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwarmPlacementConstraints",
                table: "Applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SwarmReplicas",
                table: "Applications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwarmRollbackConfig",
                table: "Applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwarmServiceId",
                table: "Applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SwarmStopGracePeriod",
                table: "Applications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwarmUpdateConfig",
                table: "Applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WatchPaths",
                table: "Applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookSecret",
                table: "Applications",
                type: "text",
                nullable: true);

            // Add GitHub deployment columns to Deployments table
            migrationBuilder.AddColumn<string>(
                name: "CommitAuthor",
                table: "Deployments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommitMessage",
                table: "Deployments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommitSha",
                table: "Deployments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Deployments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsPreview",
                table: "Deployments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PreviewId",
                table: "Deployments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TriggeredBy",
                table: "Deployments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove Swarm columns from Servers
            migrationBuilder.DropColumn(name: "IsSwarmWorker", table: "Servers");
            migrationBuilder.DropColumn(name: "SwarmNodeAvailability", table: "Servers");
            migrationBuilder.DropColumn(name: "SwarmNodeId", table: "Servers");
            migrationBuilder.DropColumn(name: "SwarmNodeState", table: "Servers");

            // Remove GitHub/Swarm columns from Applications
            migrationBuilder.DropColumn(name: "AutoDeployOnPush", table: "Applications");
            migrationBuilder.DropColumn(name: "BuildArgs", table: "Applications");
            migrationBuilder.DropColumn(name: "CloneSubmodules", table: "Applications");
            migrationBuilder.DropColumn(name: "EnablePreviewDeployments", table: "Applications");
            migrationBuilder.DropColumn(name: "LastCommitMessage", table: "Applications");
            migrationBuilder.DropColumn(name: "LastCommitSha", table: "Applications");
            migrationBuilder.DropColumn(name: "SwarmEndpointSpec", table: "Applications");
            migrationBuilder.DropColumn(name: "SwarmMode", table: "Applications");
            migrationBuilder.DropColumn(name: "SwarmNetworks", table: "Applications");
            migrationBuilder.DropColumn(name: "SwarmPlacementConstraints", table: "Applications");
            migrationBuilder.DropColumn(name: "SwarmReplicas", table: "Applications");
            migrationBuilder.DropColumn(name: "SwarmRollbackConfig", table: "Applications");
            migrationBuilder.DropColumn(name: "SwarmServiceId", table: "Applications");
            migrationBuilder.DropColumn(name: "SwarmStopGracePeriod", table: "Applications");
            migrationBuilder.DropColumn(name: "SwarmUpdateConfig", table: "Applications");
            migrationBuilder.DropColumn(name: "WatchPaths", table: "Applications");
            migrationBuilder.DropColumn(name: "WebhookSecret", table: "Applications");

            // Remove GitHub columns from Deployments
            migrationBuilder.DropColumn(name: "CommitAuthor", table: "Deployments");
            migrationBuilder.DropColumn(name: "CommitMessage", table: "Deployments");
            migrationBuilder.DropColumn(name: "CommitSha", table: "Deployments");
            migrationBuilder.DropColumn(name: "CreatedAt", table: "Deployments");
            migrationBuilder.DropColumn(name: "IsPreview", table: "Deployments");
            migrationBuilder.DropColumn(name: "PreviewId", table: "Deployments");
            migrationBuilder.DropColumn(name: "TriggeredBy", table: "Deployments");
        }
    }
}
