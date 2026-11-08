using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <summary>Privilège jury d'un compte enseignant : il encode les épreuves du jury, sans en devenir titulaire.</summary>
    public partial class PrivilegeJury : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "JuryAccess",
                table: "Utilisateurs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JuryAccess",
                table: "Utilisateurs");
        }
    }
}
