using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <summary>« Paramètres profs » du projet Délibération : mise à jour des cotes (fermée) et téléchargements (ouverts).</summary>
    public partial class ParametresEnseignants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TeacherUpdates",
                table: "ReglagesDeliberation",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TeacherDownloads",
                table: "ReglagesDeliberation",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TeacherUpdates",
                table: "ReglagesDeliberation");

            migrationBuilder.DropColumn(
                name: "TeacherDownloads",
                table: "ReglagesDeliberation");
        }
    }
}
