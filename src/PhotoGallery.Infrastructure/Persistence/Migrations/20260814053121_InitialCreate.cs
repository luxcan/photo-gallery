using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Length = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    TakenUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PerceptualHash = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    ThumbnailName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DuplicateSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    DetectedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsResolved = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuplicateSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LibrarySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    LibraryRoot = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    LastScanUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibrarySettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Faces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AssetId = table.Column<int>(type: "INTEGER", nullable: false),
                    DetectScore = table.Column<float>(type: "REAL", nullable: false),
                    Embedding = table.Column<byte[]>(type: "BLOB", nullable: false),
                    BoundsHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    BoundsWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    BoundsX = table.Column<int>(type: "INTEGER", nullable: false),
                    BoundsY = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Faces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Faces_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DuplicateMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DuplicateSetId = table.Column<int>(type: "INTEGER", nullable: false),
                    AssetId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Distance = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuplicateMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DuplicateMembers_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DuplicateMembers_DuplicateSets_DuplicateSetId",
                        column: x => x.DuplicateSetId,
                        principalTable: "DuplicateSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonEras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ToUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Centroid = table.Column<byte[]>(type: "BLOB", nullable: false),
                    SampleCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonEras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonEras_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FaceAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FaceId = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<float>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceAssignments_Faces_FaceId",
                        column: x => x.FaceId,
                        principalTable: "Faces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FaceAssignments_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ContentHash",
                table: "Assets",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Length",
                table: "Assets",
                column: "Length");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_RelativePath",
                table: "Assets",
                column: "RelativePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TakenUtc",
                table: "Assets",
                column: "TakenUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateMembers_AssetId",
                table: "DuplicateMembers",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateMembers_DuplicateSetId_AssetId",
                table: "DuplicateMembers",
                columns: new[] { "DuplicateSetId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateSets_Kind_IsResolved",
                table: "DuplicateSets",
                columns: new[] { "Kind", "IsResolved" });

            migrationBuilder.CreateIndex(
                name: "IX_FaceAssignments_FaceId_PersonId",
                table: "FaceAssignments",
                columns: new[] { "FaceId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaceAssignments_PersonId_Source",
                table: "FaceAssignments",
                columns: new[] { "PersonId", "Source" });

            migrationBuilder.CreateIndex(
                name: "IX_Faces_AssetId",
                table: "Faces",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_People_DisplayName",
                table: "People",
                column: "DisplayName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonEras_PersonId_FromUtc",
                table: "PersonEras",
                columns: new[] { "PersonId", "FromUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DuplicateMembers");

            migrationBuilder.DropTable(
                name: "FaceAssignments");

            migrationBuilder.DropTable(
                name: "LibrarySettings");

            migrationBuilder.DropTable(
                name: "PersonEras");

            migrationBuilder.DropTable(
                name: "DuplicateSets");

            migrationBuilder.DropTable(
                name: "Faces");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropTable(
                name: "Assets");
        }
    }
}
