using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Records whether somebody here has said to take each other computer's
    /// changes.
    /// </summary>
    /// <remarks>
    /// Every computer already known has had its changes taken, by somebody
    /// pressing Sync, so each one is marked trusted (1) here and no laptop in
    /// the house has to be confirmed again. Only a computer met from now on
    /// starts undecided (0), and nothing from it is used until somebody says.
    /// </remarks>
    public partial class AddMachineTrust : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Trust",
                table: "KnownMachines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE KnownMachines SET Trust = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Trust",
                table: "KnownMachines");
        }
    }
}
