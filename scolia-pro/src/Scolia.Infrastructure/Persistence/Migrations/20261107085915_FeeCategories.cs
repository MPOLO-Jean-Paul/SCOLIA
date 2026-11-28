using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FeeCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "RubriquesFrais",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CategoriesFrais",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoriesFrais", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RubriquesFrais_CategoryId",
                table: "RubriquesFrais",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoriesFrais_NameKey",
                table: "CategoriesFrais",
                column: "NameKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RubriquesFrais_CategoriesFrais_CategoryId",
                table: "RubriquesFrais",
                column: "CategoryId",
                principalTable: "CategoriesFrais",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RubriquesFrais_CategoriesFrais_CategoryId",
                table: "RubriquesFrais");

            migrationBuilder.DropTable(
                name: "CategoriesFrais");

            migrationBuilder.DropIndex(
                name: "IX_RubriquesFrais_CategoryId",
                table: "RubriquesFrais");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "RubriquesFrais");
        }
    }
}
