using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_People_DisplayName",
                table: "People");

            // NamedUtc first, so the flag it replaces can be carried into it
            // before the column holding it goes. Dropped first, as the scaffold
            // had it, every name somebody typed would go back to being one a
            // rebuild may write over - which is the one thing the flag existed
            // to prevent.
            migrationBuilder.AddColumn<DateTime>(
                name: "NamedUtc",
                table: "Collections",
                type: "TEXT",
                nullable: true);

            // The earliest date there is, rather than a guess at when it was
            // typed. Non-null is what carries the flag's meaning across; the
            // value is unknown, and the merge treats the unknown as losing to
            // any real answer - which is what is wanted at every point where two
            // machines differ.
            migrationBuilder.Sql(
                "UPDATE Collections SET NamedUtc = '0001-01-01 00:00:00' WHERE WasRenamed = 1;");

            migrationBuilder.DropColumn(
                name: "WasRenamed",
                table: "Collections");

            migrationBuilder.AddColumn<Guid>(
                name: "SharedId",
                table: "PhotoSources",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "People",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedUtc",
                table: "People",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MachineId",
                table: "LibrarySettings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "MachineName",
                table: "LibrarySettings",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SharedFolder",
                table: "LibrarySettings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedUtc",
                table: "FaceAssignments",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "Collections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "Collections",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "RotatedUtc",
                table: "Assets",
                type: "TEXT",
                nullable: true);

            // Every row that already exists takes the column's default, which is
            // one value - and three of these columns are about to be made unique.
            // A library with two people would fail to migrate at all, and one
            // with a single person of each kind would migrate into a state where
            // every machine in the house claims the same identity for a different
            // person. So they are minted here, one apiece, before the indexes go
            // on.
            //
            // Version 4 as SQLite can make one: EF stores a Guid as text in the
            // 8-4-4-4-12 form, and this writes exactly that, so what the app
            // parses back is what it would have written itself.
            foreach (string table in new[] { "People", "Collections" })
            {
                migrationBuilder.Sql(MintInto(table, "PublicId"));
            }

            migrationBuilder.Sql(MintInto("PhotoSources", "SharedId"));

            migrationBuilder.CreateTable(
                name: "HeldDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SharedSourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Part = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    FromMachine = table.Column<Guid>(type: "TEXT", nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeldDecisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Peers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MachineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LastMergedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Peers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoSources_SharedId",
                table: "PhotoSources",
                column: "SharedId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_DisplayName",
                table: "People",
                column: "DisplayName",
                unique: true,
                filter: "\"DeletedUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_People_PublicId",
                table: "People",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_PublicId",
                table: "Collections",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HeldDecisions_SharedSourceId_RelativePath",
                table: "HeldDecisions",
                columns: new[] { "SharedSourceId", "RelativePath" });

            migrationBuilder.CreateIndex(
                name: "IX_HeldDecisions_SharedSourceId_RelativePath_Kind_Part",
                table: "HeldDecisions",
                columns: new[] { "SharedSourceId", "RelativePath", "Kind", "Part" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Peers_MachineId",
                table: "Peers",
                column: "MachineId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HeldDecisions");

            migrationBuilder.DropTable(
                name: "Peers");

            migrationBuilder.DropIndex(
                name: "IX_PhotoSources_SharedId",
                table: "PhotoSources");

            migrationBuilder.DropIndex(
                name: "IX_People_DisplayName",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_PublicId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_Collections_PublicId",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "SharedId",
                table: "PhotoSources");

            // Tombstones cannot be expressed by the schema being returned to, and
            // the unique index at the foot of this method is about to refuse them
            // anyway: a deleted "Ana" and a living one are two rows with one name.
            migrationBuilder.Sql("DELETE FROM People WHERE DeletedUtc IS NOT NULL;");
            migrationBuilder.Sql("DELETE FROM Collections WHERE DeletedUtc IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "People");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "UpdatedUtc",
                table: "People");

            migrationBuilder.DropColumn(
                name: "MachineId",
                table: "LibrarySettings");

            migrationBuilder.DropColumn(
                name: "MachineName",
                table: "LibrarySettings");

            migrationBuilder.DropColumn(
                name: "SharedFolder",
                table: "LibrarySettings");

            migrationBuilder.DropColumn(
                name: "DecidedUtc",
                table: "FaceAssignments");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "RotatedUtc",
                table: "Assets");

            migrationBuilder.AddColumn<bool>(
                name: "WasRenamed",
                table: "Collections",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Back into the flag before the date goes, the same way round as Up.
            // A downgrade loses when a name was typed, which the old schema had
            // nowhere to put; it must not also lose that one was.
            migrationBuilder.Sql("UPDATE Collections SET WasRenamed = 1 WHERE NamedUtc IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "NamedUtc",
                table: "Collections");

            migrationBuilder.CreateIndex(
                name: "IX_People_DisplayName",
                table: "People",
                column: "DisplayName",
                unique: true);
        }

        /// <summary>
        /// Gives every row in a table its own version 4 uuid, in the 8-4-4-4-12
        /// text form Entity Framework stores a <see cref="Guid"/> as.
        /// </summary>
        /// <remarks>
        /// <strong>Upper case, and that is not a matter of taste.</strong> A Guid
        /// bound as a query parameter reaches SQLite as upper-case text, and
        /// SQLite compares text case-sensitively - so an identity minted in lower
        /// case is read back perfectly well by <c>Guid.Parse</c> and then matches
        /// nothing the app ever looks it up by. Every person and album on an
        /// upgraded library would have an identity no query could find, and
        /// nothing would look broken until two machines failed to agree about
        /// anybody.
        ///
        /// <para><c>hex</c> already answers in upper case; the two fixed
        /// characters are the version and the variant, so they are written to
        /// match.</para>
        ///
        /// <para>Only rows still carrying the column's default, so running it
        /// again changes nothing and a row that somehow already has an identity
        /// keeps the one the rest of the house knows it by.</para>
        /// </remarks>
        private static string MintInto(string table, string column) =>
            $"""
             UPDATE {table} SET {column} =
                 hex(randomblob(4)) || '-' ||
                 hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' ||
                 substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)), 2) || '-' ||
                 hex(randomblob(6))
             WHERE {column} IS NULL OR {column} = '00000000-0000-0000-0000-000000000000';
             """;
    }
}
