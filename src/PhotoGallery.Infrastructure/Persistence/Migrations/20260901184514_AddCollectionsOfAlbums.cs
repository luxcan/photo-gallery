using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the shelf an album can stand on: the Collections table, and the one
    /// nullable column on Albums that says which shelf.
    /// </summary>
    /// <remarks>
    /// A column rather than a table between the two, which is what makes "an
    /// album is on at most one shelf" a fact about the schema rather than
    /// something a handler has to remember.
    ///
    /// <para>Left exactly as scaffolded, because everything here adds: a column
    /// that starts null on every row there already is, a table with nothing in
    /// it, and three indexes. The unique index on the name is filtered to live
    /// rows, the way a person's display name is - a name given back by a removal
    /// is free to use again, because a tombstone records what happened rather
    /// than reserving a word.</para>
    ///
    /// <para>There is no foreign key from Albums, deliberately. SQLite cannot
    /// add one to a table that already exists, so EF attaches it by rebuilding
    /// the whole table - and that rebuild turns foreign keys off, which cannot
    /// be done inside a transaction, so this migration would stop being
    /// all-or-nothing on somebody's real library to buy a constraint nothing
    /// rests on. Removing a collection clears the column itself, and the screen
    /// reads a shelf it has never heard of as no shelf.</para>
    /// </remarks>
    public partial class AddCollectionsOfAlbums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CollectionId",
                table: "Albums",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NamedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Albums_CollectionId",
                table: "Albums",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_Name",
                table: "Collections",
                column: "Name",
                unique: true,
                filter: "\"DeletedUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_PublicId",
                table: "Collections",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_Albums_CollectionId",
                table: "Albums");

            migrationBuilder.DropColumn(
                name: "CollectionId",
                table: "Albums");
        }
    }
}
