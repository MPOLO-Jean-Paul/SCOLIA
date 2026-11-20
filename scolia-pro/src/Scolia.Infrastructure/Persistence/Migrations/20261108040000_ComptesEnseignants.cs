using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <summary>Comptes enseignants : le compte est relié à la fiche du personnel dont il encode les cours attribués.</summary>
    public partial class ComptesEnseignants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StaffMemberId",
                table: "Utilisateurs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Utilisateurs_StaffMemberId",
                table: "Utilisateurs",
                column: "StaffMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_Utilisateurs_Personnel_StaffMemberId",
                table: "Utilisateurs",
                column: "StaffMemberId",
                principalTable: "Personnel",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Utilisateurs_Personnel_StaffMemberId",
                table: "Utilisateurs");

            migrationBuilder.DropIndex(
                name: "IX_Utilisateurs_StaffMemberId",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "StaffMemberId",
                table: "Utilisateurs");
        }
    }
}
