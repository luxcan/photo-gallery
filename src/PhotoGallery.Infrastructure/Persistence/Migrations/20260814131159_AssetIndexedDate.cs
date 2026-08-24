using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetIndexedDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "IndexedUtc",
                table: "Assets",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Rows that predate the column were indexed by the scan that last
            // walked their source, so that timestamp is the true answer rather
            // than a guess. Where a source has never completed a scan, the
            // file's own modified date is the closest honest substitute.
            migrationBuilder.Sql(
                """
                UPDATE Assets
                SET IndexedUtc = COALESCE(
                        (SELECT LastScanUtc FROM PhotoSources
                         WHERE PhotoSources.Id = Assets.PhotoSourceId),
                        ModifiedUtc)
                WHERE IndexedUtc < '1900-01-01';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IndexedUtc",
                table: "Assets");
        }
    }
}
