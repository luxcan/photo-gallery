using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Re-keys refusals of photographs from albums somebody made by the album's
    /// public id instead of its row number.
    /// </summary>
    /// <remarks>
    /// "made:&lt;row number&gt;" meant nothing on another computer, so a refusal
    /// travelled to a different album there or to none. The new key is
    /// <c>AlbumRefusalKey.Made</c>'s: the public id in lower case, while SQLite
    /// holds a Guid in upper case. A refusal whose album has gone has nothing left
    /// to refer to and is dropped. Keys that already name a Guid contain hyphens,
    /// which is what keeps them out of the row-number match.
    /// </remarks>
    public partial class KeyMadeAlbumRefusalsByPublicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM AlbumRejections
                WHERE ProposalKey GLOB 'made:[0-9]*'
                  AND ProposalKey NOT LIKE '%-%'
                  AND NOT EXISTS (
                      SELECT 1 FROM Albums
                      WHERE Albums.Id = CAST(substr(AlbumRejections.ProposalKey, 6) AS INTEGER));

                UPDATE AlbumRejections
                SET ProposalKey = 'made:' || lower((
                    SELECT PublicId FROM Albums
                    WHERE Albums.Id = CAST(substr(AlbumRejections.ProposalKey, 6) AS INTEGER)))
                WHERE ProposalKey GLOB 'made:[0-9]*'
                  AND ProposalKey NOT LIKE '%-%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE AlbumRejections
                SET ProposalKey = 'made:' || (
                    SELECT Albums.Id FROM Albums
                    WHERE 'made:' || lower(Albums.PublicId) = AlbumRejections.ProposalKey)
                WHERE EXISTS (
                    SELECT 1 FROM Albums
                    WHERE 'made:' || lower(Albums.PublicId) = AlbumRejections.ProposalKey);
                """);
        }
    }
}
