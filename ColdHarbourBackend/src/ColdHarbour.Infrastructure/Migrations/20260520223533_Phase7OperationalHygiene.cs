using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdHarbour.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase7OperationalHygiene : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IntegrityStatus",
                table: "Tracks",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlayStats",
                columns: table => new
                {
                    TrackId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekOf = table.Column<DateOnly>(type: "date", nullable: false),
                    PlayCount = table.Column<int>(type: "integer", nullable: false),
                    TotalMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayStats", x => new { x.TrackId, x.WeekOf });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayStats");

            migrationBuilder.DropColumn(
                name: "IntegrityStatus",
                table: "Tracks");
        }
    }
}
