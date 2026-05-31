using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdHarbour.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlayEvent_ListenedMs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ListenedMs",
                table: "PlayEvents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PausedAtUtc",
                table: "PlayEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SegmentStartedAt",
                table: "PlayEvents",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Backfill: segment starts at track-start for all existing events.
            // Best-effort ListenedMs from wall-clock for closed events; Phase 5 refines orphans.
            migrationBuilder.Sql("""
                UPDATE "PlayEvents"
                SET "SegmentStartedAt" = "StartedAt";

                UPDATE "PlayEvents"
                SET "ListenedMs" = GREATEST(0,
                    EXTRACT(EPOCH FROM ("EndedAt" - "StartedAt")) * 1000)::bigint
                WHERE "EndedAt" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ListenedMs",
                table: "PlayEvents");

            migrationBuilder.DropColumn(
                name: "PausedAtUtc",
                table: "PlayEvents");

            migrationBuilder.DropColumn(
                name: "SegmentStartedAt",
                table: "PlayEvents");
        }
    }
}
