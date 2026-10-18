using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKeptAndDismissedSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "KeptUtc",
                table: "Albums",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AlbumDismissals",
                columns: table => new
                {
                    ProposalKey = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    DismissedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DismissedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlbumDismissals", x => x.ProposalKey);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlbumDismissals");

            migrationBuilder.DropColumn(
                name: "KeptUtc",
                table: "Albums");
        }
    }
}
