using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationReadMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LocationReadUtc",
                table: "Assets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_LocationReadUtc",
                table: "Assets",
                column: "LocationReadUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_PlaceId",
                table: "Assets",
                column: "PlaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_LocationReadUtc",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Assets_PlaceId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "LocationReadUtc",
                table: "Assets");
        }
    }
}
