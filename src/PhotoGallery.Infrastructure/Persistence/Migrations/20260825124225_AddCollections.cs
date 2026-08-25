using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CollectionRejections",
                columns: table => new
                {
                    AssetId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProposalKey = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    RejectedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRejections", x => new { x.AssetId, x.ProposalKey });
                    table.ForeignKey(
                        name: "FK_CollectionRejections_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    StartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PlaceId = table.Column<int>(type: "INTEGER", nullable: true),
                    CoverAssetId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Origin = table.Column<int>(type: "INTEGER", nullable: false),
                    ProposalKey = table.Column<string>(type: "TEXT", maxLength: 24, nullable: true),
                    WasRenamed = table.Column<bool>(type: "INTEGER", nullable: false),
                    BuiltUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CollectionMembers",
                columns: table => new
                {
                    AssetId = table.Column<int>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionMembers", x => x.AssetId);
                    table.ForeignKey(
                        name: "FK_CollectionMembers_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionMembers_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionMembers_CollectionId",
                table: "CollectionMembers",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRejections_ProposalKey",
                table: "CollectionRejections",
                column: "ProposalKey");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_CoverAssetId",
                table: "Collections",
                column: "CoverAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_Origin",
                table: "Collections",
                column: "Origin");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_PlaceId",
                table: "Collections",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_ProposalKey",
                table: "Collections",
                column: "ProposalKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_StartUtc",
                table: "Collections",
                column: "StartUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionMembers");

            migrationBuilder.DropTable(
                name: "CollectionRejections");

            migrationBuilder.DropTable(
                name: "Collections");
        }
    }
}
