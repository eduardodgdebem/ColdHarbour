using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdHarbour.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionToPlaybackSessionSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "PlaybackSessionSnapshots",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Revision",
                table: "PlaybackSessionSnapshots");
        }
    }
}
