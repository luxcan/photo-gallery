using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEyesAndPreviewQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FaceEyes",
                columns: table => new
                {
                    FaceId = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: true),
                    Openness = table.Column<double>(type: "REAL", nullable: true),
                    Sharpness = table.Column<double>(type: "REAL", nullable: true),
                    MeasuredUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceEyes", x => x.FaceId);
                    table.ForeignKey(
                        name: "FK_FaceEyes_Faces_FaceId",
                        column: x => x.FaceId,
                        principalTable: "Faces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PreviewQuality",
                columns: table => new
                {
                    AssetId = table.Column<int>(type: "INTEGER", nullable: false),
                    ThumbnailName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PreviewWrittenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    Sharpness = table.Column<double>(type: "REAL", nullable: true),
                    ClippedHigh = table.Column<double>(type: "REAL", nullable: false),
                    ClippedLow = table.Column<double>(type: "REAL", nullable: false),
                    MeasuredUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreviewQuality", x => x.AssetId);
                    table.ForeignKey(
                        name: "FK_PreviewQuality_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaceEyes");

            migrationBuilder.DropTable(
                name: "PreviewQuality");
        }
    }
}
