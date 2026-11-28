using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReglementsPedagogiques : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReglementsPedagogiques",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TermsCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodsPerTerm = table.Column<int>(type: "INTEGER", nullable: false),
                    ExamMaxFactor = table.Column<long>(type: "INTEGER", nullable: false),
                    PassPercent = table.Column<long>(type: "INTEGER", nullable: false),
                    SubjectPassPercent = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxRepechageSubjects = table.Column<int>(type: "INTEGER", nullable: false),
                    ShowRank = table.Column<bool>(type: "INTEGER", nullable: false),
                    MarkScale = table.Column<long>(type: "INTEGER", nullable: false),
                    UeValidationMark = table.Column<long>(type: "INTEGER", nullable: false),
                    CompensateWithinUe = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompensateBetweenUe = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxDebtCredits = table.Column<long>(type: "INTEGER", nullable: false),
                    LegalReference = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ValidatedByName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    ValidatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReglementsPedagogiques", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReglementsPedagogiques_AnneesScolaires_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AnneesScolaires",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReglementsPedagogiques_AcademicYearId_Scope",
                table: "ReglementsPedagogiques",
                columns: new[] { "AcademicYearId", "Scope" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReglementsPedagogiques");
        }
    }
}
