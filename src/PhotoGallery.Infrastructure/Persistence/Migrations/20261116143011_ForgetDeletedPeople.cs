using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Blanks the name and birth year of everybody already deleted.
    /// </summary>
    /// <remarks>
    /// A deletion used to keep both on the tombstone, and every sync copied them
    /// into the sync folder for every other computer to keep too. Deleting a
    /// person blanks them now; this does the same for the people deleted before
    /// it did. Only the row's identity and its date are needed to keep them
    /// deleted, and the empty name cannot collide, because names are unique only
    /// among the people still here.
    /// </remarks>
    public partial class ForgetDeletedPeople : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE People
                SET DisplayName = '', BirthYear = NULL
                WHERE DeletedUtc IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Nothing to undo: what was blanked was meant to be gone, and there is
        /// no copy of it to put back.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
