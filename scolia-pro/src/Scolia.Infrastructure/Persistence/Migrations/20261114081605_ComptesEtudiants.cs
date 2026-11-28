using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComptesEtudiants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StudentId",
                table: "Utilisateurs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Utilisateurs_StudentId",
                table: "Utilisateurs",
                column: "StudentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Utilisateurs_Apprenants_StudentId",
                table: "Utilisateurs",
                column: "StudentId",
                principalTable: "Apprenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Utilisateurs_Apprenants_StudentId",
                table: "Utilisateurs");

            migrationBuilder.DropIndex(
                name: "IX_Utilisateurs_StudentId",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "StudentId",
                table: "Utilisateurs");
        }
    }
}
