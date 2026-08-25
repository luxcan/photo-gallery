using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RuleFromUtc",
                table: "Collections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RuleToUtc",
                table: "Collections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CollectionRulePeople",
                columns: table => new
                {
                    CollectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRulePeople", x => new { x.CollectionId, x.PersonId });
                    table.ForeignKey(
                        name: "FK_CollectionRulePeople_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionRulePeople_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectionRulePlaces",
                columns: table => new
                {
                    CollectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlaceId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRulePlaces", x => new { x.CollectionId, x.PlaceId });
                    table.ForeignKey(
                        name: "FK_CollectionRulePlaces_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionRulePlaces_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRulePeople_PersonId",
                table: "CollectionRulePeople",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRulePlaces_PlaceId",
                table: "CollectionRulePlaces",
                column: "PlaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionRulePeople");

            migrationBuilder.DropTable(
                name: "CollectionRulePlaces");

            migrationBuilder.DropColumn(
                name: "RuleFromUtc",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "RuleToUtc",
                table: "Collections");
        }
    }
}
