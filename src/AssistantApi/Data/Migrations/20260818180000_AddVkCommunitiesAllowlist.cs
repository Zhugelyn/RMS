using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssistantApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVkCommunitiesAllowlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VkCommunitiesJson",
                table: "research_settings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VkCommunitiesJson",
                table: "research_settings");
        }
    }
}
