using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Renames Peers to KnownMachines and drops the certificate fingerprint,
    /// which went with the direct connection.
    /// </summary>
    /// <remarks>
    /// Scaffolded as a drop-and-create, and rewritten as a rename on purpose.
    /// The generated version would have taken every row with it, and a row here
    /// is when this library last took another machine's answers - rebuilt on the
    /// next merge, but until then the screen would report a laptop it has known
    /// for months as one it has never heard from.
    /// </remarks>
    public partial class RenamePeersToKnownMachines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "Peers",
                newName: "KnownMachines");

            migrationBuilder.RenameIndex(
                name: "IX_Peers_MachineId",
                table: "KnownMachines",
                newName: "IX_KnownMachines_MachineId");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                table: "KnownMachines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                table: "KnownMachines",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.RenameIndex(
                name: "IX_KnownMachines_MachineId",
                table: "KnownMachines",
                newName: "IX_Peers_MachineId");

            migrationBuilder.RenameTable(
                name: "KnownMachines",
                newName: "Peers");
        }
    }
}
