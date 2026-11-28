using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations;

/// <summary>Migration additive : aucun reçu existant n'est réécrit ou reconstitué.</summary>
[DbContext(typeof(ScoliaDbContext))]
[Migration("20261126144000_ReceiptPrintSnapshot")]
public sealed class ReceiptPrintSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "PrintSnapshotJson", table: "Recus", type: "TEXT", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "PrintSnapshotJson", table: "Recus");
}
