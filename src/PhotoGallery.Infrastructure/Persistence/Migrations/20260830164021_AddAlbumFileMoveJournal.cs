using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlbumFileMoveJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlbumFileMoves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CollectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    AssetId = table.Column<int>(type: "INTEGER", nullable: false),
                    PhotoSourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceRelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    DestinationRelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ExpectedLength = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlbumFileMoves", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlbumFileMoves_AssetId",
                table: "AlbumFileMoves",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AlbumFileMoves_OperationId",
                table: "AlbumFileMoves",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_AlbumFileMoves_State",
                table: "AlbumFileMoves",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlbumFileMoves");
        }
    }
}
