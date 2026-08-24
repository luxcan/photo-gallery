using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiplePhotoSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_RelativePath",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "LibraryRoot",
                table: "LibrarySettings");

            migrationBuilder.AddColumn<int>(
                name: "PhotoSourceId",
                table: "Assets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PhotoSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    AddedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastScanUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoSources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_PhotoSourceId_RelativePath",
                table: "Assets",
                columns: new[] { "PhotoSourceId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoSources_Path",
                table: "PhotoSources",
                column: "Path",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_PhotoSources_PhotoSourceId",
                table: "Assets",
                column: "PhotoSourceId",
                principalTable: "PhotoSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_PhotoSources_PhotoSourceId",
                table: "Assets");

            migrationBuilder.DropTable(
                name: "PhotoSources");

            migrationBuilder.DropIndex(
                name: "IX_Assets_PhotoSourceId_RelativePath",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "PhotoSourceId",
                table: "Assets");

            migrationBuilder.AddColumn<string>(
                name: "LibraryRoot",
                table: "LibrarySettings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_RelativePath",
                table: "Assets",
                column: "RelativePath",
                unique: true);
        }
    }
}
