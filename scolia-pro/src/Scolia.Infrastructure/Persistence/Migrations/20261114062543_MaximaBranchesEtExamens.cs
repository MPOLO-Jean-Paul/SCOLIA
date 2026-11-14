using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MaximaBranchesEtExamens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PeriodMaxScore",
                table: "ProgrammesPedagogiques",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsExam",
                table: "PeriodesCyclesScolaires",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PeriodMaxScore",
                table: "ProgrammesPedagogiques");

            migrationBuilder.DropColumn(
                name: "IsExam",
                table: "PeriodesCyclesScolaires");
        }
    }
}
