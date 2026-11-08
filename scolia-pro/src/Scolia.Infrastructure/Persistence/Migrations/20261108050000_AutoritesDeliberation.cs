using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <summary>Autorités de la délibération (projet Délibération) : préfet, proviseur, président, secrétaire et membres du jury.</summary>
    public partial class AutoritesDeliberation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Prefect",
                table: "ReglagesDeliberation",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Principal",
                table: "ReglagesDeliberation",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JuryPresident",
                table: "ReglagesDeliberation",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JurySecretary",
                table: "ReglagesDeliberation",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JuryMembers",
                table: "ReglagesDeliberation",
                type: "TEXT",
                maxLength: 600,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Prefect",
                table: "ReglagesDeliberation");

            migrationBuilder.DropColumn(
                name: "Principal",
                table: "ReglagesDeliberation");

            migrationBuilder.DropColumn(
                name: "JuryPresident",
                table: "ReglagesDeliberation");

            migrationBuilder.DropColumn(
                name: "JurySecretary",
                table: "ReglagesDeliberation");

            migrationBuilder.DropColumn(
                name: "JuryMembers",
                table: "ReglagesDeliberation");
        }
    }
}
