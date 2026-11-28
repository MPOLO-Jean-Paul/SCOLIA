using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BulletinsOfficielsEpst : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Commune",
                table: "Etablissement",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficialCode",
                table: "Etablissement",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Province",
                table: "Etablissement",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PermanentNumber",
                table: "Apprenants",
                type: "TEXT",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Commune",
                table: "Etablissement");

            migrationBuilder.DropColumn(
                name: "OfficialCode",
                table: "Etablissement");

            migrationBuilder.DropColumn(
                name: "Province",
                table: "Etablissement");

            migrationBuilder.DropColumn(
                name: "PermanentNumber",
                table: "Apprenants");
        }
    }
}
