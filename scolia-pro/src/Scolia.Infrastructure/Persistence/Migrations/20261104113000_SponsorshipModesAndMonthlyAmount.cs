using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Scolia.Infrastructure.Persistence;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations;

/// <summary>
/// Migration additive : aucune prise en charge historique n'est modifiée ni supprimée.
/// Mode = 0 conserve l'ancienne couverture en pourcentage.
/// </summary>
[DbContext(typeof(ScoliaDbContext))]
[Migration("20261104113000_SponsorshipModesAndMonthlyAmount")]
public partial class SponsorshipModesAndMonthlyAmount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Mode", table: "PrisesEnCharge", type: "INTEGER",
            nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<decimal>(
            name: "MonthlyAmount", table: "PrisesEnCharge", type: "TEXT",
            nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>(
            name: "Amount", table: "PrisesEnChargeLignes", type: "TEXT",
            nullable: false, defaultValue: 0m);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Amount", table: "PrisesEnChargeLignes");
        migrationBuilder.DropColumn(name: "MonthlyAmount", table: "PrisesEnCharge");
        migrationBuilder.DropColumn(name: "Mode", table: "PrisesEnCharge");
    }
}
