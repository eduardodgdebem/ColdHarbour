using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ColdHarbour.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LibraryCrud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Tracks",
                keyColumn: "Id",
                keyValue: new Guid("33333333-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Tracks",
                keyColumn: "Id",
                keyValue: new Guid("33333333-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Albums",
                keyColumn: "Id",
                keyValue: new Guid("22222222-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Albums",
                keyColumn: "Id",
                keyValue: new Guid("22222222-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Artists",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Artists",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000002"));

            migrationBuilder.AddColumn<int>(
                name: "TrackNumber",
                table: "Tracks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverArtSha1",
                table: "Albums",
                type: "char(40)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_AudioSha1",
                table: "Tracks",
                column: "AudioSha1",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tracks_AudioSha1",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "TrackNumber",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "CoverArtSha1",
                table: "Albums");

            migrationBuilder.InsertData(
                table: "Artists",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { new Guid("11111111-0000-0000-0000-000000000001"), "HONNE" },
                    { new Guid("11111111-0000-0000-0000-000000000002"), "Remi Wolf" }
                });

            migrationBuilder.InsertData(
                table: "Albums",
                columns: new[] { "Id", "ArtistId", "CoverPath", "Title", "Year" },
                values: new object[,]
                {
                    { new Guid("22222222-0000-0000-0000-000000000001"), new Guid("11111111-0000-0000-0000-000000000001"), null, "HONNE", null },
                    { new Guid("22222222-0000-0000-0000-000000000002"), new Guid("11111111-0000-0000-0000-000000000002"), null, "Remi Wolf", null }
                });

            migrationBuilder.InsertData(
                table: "Tracks",
                columns: new[] { "Id", "AlbumId", "AudioSha1", "Bitrate", "Duration", "Format", "LocalPath", "Provider", "ProviderRef", "Title" },
                values: new object[,]
                {
                    { new Guid("33333333-0000-0000-0000-000000000001"), new Guid("22222222-0000-0000-0000-000000000001"), "0000000000000000000000000000000000000001", 128, 2100000000L, "mp3", "/assets/music/babyyourebad.mp3", "local", null, "Baby You're Bad" },
                    { new Guid("33333333-0000-0000-0000-000000000002"), new Guid("22222222-0000-0000-0000-000000000002"), "0000000000000000000000000000000000000002", 128, 2100000000L, "mp3", "/assets/music/liz.mp3", "local", null, "Liz" }
                });
        }
    }
}
