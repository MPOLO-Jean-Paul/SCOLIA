using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations;

/// <summary>
/// Migration additive : toutes les bases ITM/IEM existantes héritent de ItmIm.
/// Aucune note, inscription, année, promotion ou donnée financière n'est modifiée.
/// </summary>
[DbContext(typeof(ScoliaDbContext))]
[Migration("20261111060000_InstitutionProfiles")]
public partial class InstitutionProfilesMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "InstitutionType",
            table: "Etablissement",
            type: "TEXT",
            maxLength: 32,
            nullable: false,
            defaultValue: "ItmIem");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "InstitutionType",
            table: "Etablissement");
    }
}
