using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AssistantApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAssistantDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "harness_episodes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Domain = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Task = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Result = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConversationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TraceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_harness_episodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "research_plans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WindowStart = table.Column<DateOnly>(type: "date", nullable: false),
                    WindowEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "research_settings",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InstagramHandle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CadenceDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 14),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NotifyChatId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    NextRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_settings", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "research_snapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_profiles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Locale = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_profiles", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "ix_harness_episodes_user_domain_at",
                table: "harness_episodes",
                columns: new[] { "UserId", "Domain", "At" });

            migrationBuilder.CreateIndex(
                name: "ix_research_plans_user_created",
                table: "research_plans",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_research_snapshots_user_captured",
                table: "research_snapshots",
                columns: new[] { "UserId", "CapturedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "harness_episodes");

            migrationBuilder.DropTable(
                name: "research_plans");

            migrationBuilder.DropTable(
                name: "research_settings");

            migrationBuilder.DropTable(
                name: "research_snapshots");

            migrationBuilder.DropTable(
                name: "user_profiles");
        }
    }
}
