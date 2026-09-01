using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Renames the five album tables and the column that named them, following
    /// the domain word from Collection to Album.
    /// </summary>
    /// <remarks>
    /// Scaffolded as a drop-and-create and rewritten as renames, for the same
    /// reason RenamePeersToKnownMachines was: the generated version would have
    /// taken every row with it. In the library this was written against that is
    /// four albums, 441 memberships, four rejections and twelve rule-people
    /// rows - every one of them something a person decided, and none of them
    /// rebuildable by a pass.
    ///
    /// <para>Tables before columns, and columns before indexes. SQLite rewrites
    /// the foreign key clauses that point at a renamed table, and the ones that
    /// name a renamed column, so the children keep their links without being
    /// rebuilt; an index survives its table's rename under its old name, which
    /// is why the renames below name the old index against the new table.</para>
    ///
    /// <para>Both directions run in that order, and Down is not the statements
    /// of Up read backwards. EF turns a rename of an index on SQLite into a drop
    /// and a create, and to write the create it looks the index up in the model
    /// this migration is moving towards - by its new name, on the table named in
    /// the operation. Rename the index before its table and that lookup asks the
    /// old model for a table it has never heard of, and the whole migration
    /// fails to generate with "SQLite does not support this migration operation
    /// ('RenameIndexOperation')". It fails at generation, so nothing is half
    /// applied - but it fails on the way back, which is the direction nobody
    /// runs until they need it.</para>
    ///
    /// <para>No primary key is renamed because SQLite does not store one as a
    /// named object. The names in the scaffolded version were only ever what
    /// EF would have called them had it created the tables from nothing. For the
    /// same reason the constraint names written inside a table's own definition
    /// still read PK_CollectionMembers and FK_CollectionMembers_Collections_
    /// CollectionId after this runs. Nothing reads them: they are text inside
    /// the CREATE TABLE statement SQLite kept, and a library created from
    /// nothing replays these same migrations and ends up with exactly the same
    /// text, so an upgraded library and a fresh one still match.</para>
    /// </remarks>
    public partial class RenameAlbumTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "Collections", newName: "Albums");

            migrationBuilder.RenameTable(
                name: "CollectionMembers",
                newName: "AlbumMembers");

            migrationBuilder.RenameTable(
                name: "CollectionRejections",
                newName: "AlbumRejections");

            migrationBuilder.RenameTable(
                name: "CollectionRulePeople",
                newName: "AlbumRulePeople");

            migrationBuilder.RenameTable(
                name: "CollectionRulePlaces",
                newName: "AlbumRulePlaces");

            migrationBuilder.RenameColumn(
                name: "CollectionId",
                table: "AlbumMembers",
                newName: "AlbumId");

            migrationBuilder.RenameColumn(
                name: "CollectionId",
                table: "AlbumRulePeople",
                newName: "AlbumId");

            migrationBuilder.RenameColumn(
                name: "CollectionId",
                table: "AlbumRulePlaces",
                newName: "AlbumId");

            migrationBuilder.RenameColumn(
                name: "CollectionId",
                table: "AlbumFileMoves",
                newName: "AlbumId");

            migrationBuilder.RenameIndex(
                name: "IX_Collections_CoverAssetId",
                table: "Albums",
                newName: "IX_Albums_CoverAssetId");

            migrationBuilder.RenameIndex(
                name: "IX_Collections_Origin",
                table: "Albums",
                newName: "IX_Albums_Origin");

            migrationBuilder.RenameIndex(
                name: "IX_Collections_PlaceId",
                table: "Albums",
                newName: "IX_Albums_PlaceId");

            migrationBuilder.RenameIndex(
                name: "IX_Collections_ProposalKey",
                table: "Albums",
                newName: "IX_Albums_ProposalKey");

            migrationBuilder.RenameIndex(
                name: "IX_Collections_PublicId",
                table: "Albums",
                newName: "IX_Albums_PublicId");

            migrationBuilder.RenameIndex(
                name: "IX_Collections_StartUtc",
                table: "Albums",
                newName: "IX_Albums_StartUtc");

            migrationBuilder.RenameIndex(
                name: "IX_CollectionMembers_CollectionId",
                table: "AlbumMembers",
                newName: "IX_AlbumMembers_AlbumId");

            migrationBuilder.RenameIndex(
                name: "IX_CollectionRejections_ProposalKey",
                table: "AlbumRejections",
                newName: "IX_AlbumRejections_ProposalKey");

            migrationBuilder.RenameIndex(
                name: "IX_CollectionRulePeople_PersonId",
                table: "AlbumRulePeople",
                newName: "IX_AlbumRulePeople_PersonId");

            migrationBuilder.RenameIndex(
                name: "IX_CollectionRulePlaces_PlaceId",
                table: "AlbumRulePlaces",
                newName: "IX_AlbumRulePlaces_PlaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "Albums", newName: "Collections");

            migrationBuilder.RenameTable(
                name: "AlbumMembers",
                newName: "CollectionMembers");

            migrationBuilder.RenameTable(
                name: "AlbumRejections",
                newName: "CollectionRejections");

            migrationBuilder.RenameTable(
                name: "AlbumRulePeople",
                newName: "CollectionRulePeople");

            migrationBuilder.RenameTable(
                name: "AlbumRulePlaces",
                newName: "CollectionRulePlaces");

            migrationBuilder.RenameColumn(
                name: "AlbumId",
                table: "CollectionMembers",
                newName: "CollectionId");

            migrationBuilder.RenameColumn(
                name: "AlbumId",
                table: "CollectionRulePeople",
                newName: "CollectionId");

            migrationBuilder.RenameColumn(
                name: "AlbumId",
                table: "CollectionRulePlaces",
                newName: "CollectionId");

            migrationBuilder.RenameColumn(
                name: "AlbumId",
                table: "AlbumFileMoves",
                newName: "CollectionId");

            migrationBuilder.RenameIndex(
                name: "IX_Albums_CoverAssetId",
                table: "Collections",
                newName: "IX_Collections_CoverAssetId");

            migrationBuilder.RenameIndex(
                name: "IX_Albums_Origin",
                table: "Collections",
                newName: "IX_Collections_Origin");

            migrationBuilder.RenameIndex(
                name: "IX_Albums_PlaceId",
                table: "Collections",
                newName: "IX_Collections_PlaceId");

            migrationBuilder.RenameIndex(
                name: "IX_Albums_ProposalKey",
                table: "Collections",
                newName: "IX_Collections_ProposalKey");

            migrationBuilder.RenameIndex(
                name: "IX_Albums_PublicId",
                table: "Collections",
                newName: "IX_Collections_PublicId");

            migrationBuilder.RenameIndex(
                name: "IX_Albums_StartUtc",
                table: "Collections",
                newName: "IX_Collections_StartUtc");

            migrationBuilder.RenameIndex(
                name: "IX_AlbumMembers_AlbumId",
                table: "CollectionMembers",
                newName: "IX_CollectionMembers_CollectionId");

            migrationBuilder.RenameIndex(
                name: "IX_AlbumRejections_ProposalKey",
                table: "CollectionRejections",
                newName: "IX_CollectionRejections_ProposalKey");

            migrationBuilder.RenameIndex(
                name: "IX_AlbumRulePeople_PersonId",
                table: "CollectionRulePeople",
                newName: "IX_CollectionRulePeople_PersonId");

            migrationBuilder.RenameIndex(
                name: "IX_AlbumRulePlaces_PlaceId",
                table: "CollectionRulePlaces",
                newName: "IX_CollectionRulePlaces_PlaceId");
        }
    }
}
