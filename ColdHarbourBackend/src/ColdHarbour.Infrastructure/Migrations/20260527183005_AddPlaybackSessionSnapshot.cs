using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdHarbour.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaybackSessionSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaybackSessionSnapshots",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveDeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrackId = table.Column<Guid>(type: "uuid", nullable: true),
                    PositionMs = table.Column<long>(type: "bigint", nullable: false),
                    IsPlaying = table.Column<bool>(type: "boolean", nullable: false),
                    Queue = table.Column<string>(type: "jsonb", nullable: false),
                    QueueIndex = table.Column<int>(type: "integer", nullable: false),
                    RepeatMode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Shuffle = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackSessionSnapshots", x => x.UserId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaybackSessionSnapshots");
        }
    }
}
