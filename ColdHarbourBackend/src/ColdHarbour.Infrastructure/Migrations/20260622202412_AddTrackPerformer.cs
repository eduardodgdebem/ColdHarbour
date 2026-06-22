using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdHarbour.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackPerformer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Performer",
                table: "Tracks",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Performer",
                table: "Tracks");
        }
    }
}
