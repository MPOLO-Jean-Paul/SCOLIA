using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConduitesCyclesScolaires : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConduitesCyclesScolaires",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromotionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchoolCyclePeriodId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Application = table.Column<string>(type: "TEXT", maxLength: 4, nullable: true),
                    Conduct = table.Column<string>(type: "TEXT", maxLength: 4, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConduitesCyclesScolaires", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConduitesCyclesScolaires_AnneesScolaires_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AnneesScolaires",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConduitesCyclesScolaires_Inscriptions_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Inscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConduitesCyclesScolaires_PeriodesCyclesScolaires_SchoolCyclePeriodId",
                        column: x => x.SchoolCyclePeriodId,
                        principalTable: "PeriodesCyclesScolaires",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConduitesCyclesScolaires_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConduitesCyclesScolaires_AcademicYearId",
                table: "ConduitesCyclesScolaires",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_ConduitesCyclesScolaires_EnrollmentId_SchoolCyclePeriodId",
                table: "ConduitesCyclesScolaires",
                columns: new[] { "EnrollmentId", "SchoolCyclePeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConduitesCyclesScolaires_PromotionId_AcademicYearId",
                table: "ConduitesCyclesScolaires",
                columns: new[] { "PromotionId", "AcademicYearId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConduitesCyclesScolaires_SchoolCyclePeriodId",
                table: "ConduitesCyclesScolaires",
                column: "SchoolCyclePeriodId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConduitesCyclesScolaires");
        }
    }
}
