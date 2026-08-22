using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Assets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Status",
                table: "Assets",
                column: "Status");

            // Existing rows already know their own state; asking the new column to
            // start at Pending for all of them would send an hour-long pass back
            // over 11,482 pictures that were prepared long ago.
            migrationBuilder.Sql(
                "UPDATE Assets SET Status = 1 WHERE ThumbnailName IS NOT NULL;");

            // Videos last, so they end Skipped whatever else was recorded: there
            // are no video renditions yet, and 4,743 permanently pending rows
            // would make the progress bar's total meaningless.
            migrationBuilder.Sql("UPDATE Assets SET Status = 3 WHERE Kind = 2;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_Status",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Assets");
        }
    }
}
