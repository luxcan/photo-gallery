using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourcePairing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PairedSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Left = table.Column<Guid>(type: "TEXT", nullable: false),
                    Right = table.Column<Guid>(type: "TEXT", nullable: false),
                    PairedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PairedSources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PairedSources_Left_Right",
                table: "PairedSources",
                columns: new[] { "Left", "Right" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PairedSources");
        }
    }
}
