using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AssistantApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchScheduler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "research_settings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_research_settings_enabled_next_run",
                table: "research_settings",
                columns: new[] { "Enabled", "NextRunAt" });

            migrationBuilder.CreateTable(
                name: "research_schedule_runs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PeriodKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_schedule_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_research_schedule_runs_user_period",
                table: "research_schedule_runs",
                columns: new[] { "UserId", "PeriodKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "research_schedule_runs");

            migrationBuilder.DropIndex(
                name: "ix_research_settings_enabled_next_run",
                table: "research_settings");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "research_settings");
        }
    }
}
