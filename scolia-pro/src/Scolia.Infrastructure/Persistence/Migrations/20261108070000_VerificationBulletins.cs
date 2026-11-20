using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <summary>Code de vérification des bulletins officiels (résultats figés à la clôture).</summary>
    public partial class VerificationBulletins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VerificationCode",
                table: "ResultatsDeliberation",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ResultatsDeliberation_VerificationCode",
                table: "ResultatsDeliberation",
                column: "VerificationCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResultatsDeliberation_VerificationCode",
                table: "ResultatsDeliberation");

            migrationBuilder.DropColumn(
                name: "VerificationCode",
                table: "ResultatsDeliberation");
        }
    }
}
