using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CardsRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardDirection",
                table: "Etablissement",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardDivision",
                table: "Etablissement",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardMinistry",
                table: "Etablissement",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardZone",
                table: "Etablissement",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LotsCartes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Session = table.Column<string>(type: "TEXT", nullable: false),
                    PromotionLabel = table.Column<string>(type: "TEXT", nullable: false),
                    StudentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CardCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Format = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedByName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotsCartes", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LotsCartes");

            migrationBuilder.DropColumn(
                name: "CardDirection",
                table: "Etablissement");

            migrationBuilder.DropColumn(
                name: "CardDivision",
                table: "Etablissement");

            migrationBuilder.DropColumn(
                name: "CardMinistry",
                table: "Etablissement");

            migrationBuilder.DropColumn(
                name: "CardZone",
                table: "Etablissement");
        }
    }
}
