using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GestionLmd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AcademicUnitId",
                table: "Promotions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeliberationsLmd",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromotionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    S1PublishedBy = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    S1PublishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FirstSessionClosedBy = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    FirstSessionClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosedBy = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliberationsLmd", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliberationsLmd_AnneesScolaires_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AnneesScolaires",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliberationsLmd_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DettesLmd",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StudentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginEnrollmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UeCode = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    UeName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Semester = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    Credits = table.Column<long>(type: "INTEGER", nullable: false),
                    ClearedMark = table.Column<long>(type: "INTEGER", nullable: true),
                    ClearedBy = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    ClearedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DettesLmd", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DettesLmd_Inscriptions_OriginEnrollmentId",
                        column: x => x.OriginEnrollmentId,
                        principalTable: "Inscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotesLmd",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromotionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ElementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Session = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotesLmd", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotesLmd_AnneesScolaires_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AnneesScolaires",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotesLmd_Inscriptions_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Inscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotesLmd_ProgrammesPedagogiques_ElementId",
                        column: x => x.ElementId,
                        principalTable: "ProgrammesPedagogiques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotesLmd_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DecisionsLmd",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeliberationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    S1Average = table.Column<long>(type: "INTEGER", nullable: true),
                    S1Credits = table.Column<long>(type: "INTEGER", nullable: false),
                    FirstAverage = table.Column<long>(type: "INTEGER", nullable: true),
                    FirstCredits = table.Column<long>(type: "INTEGER", nullable: false),
                    FirstOutcome = table.Column<int>(type: "INTEGER", nullable: true),
                    Jury = table.Column<int>(type: "INTEGER", nullable: true),
                    JuryReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    FinalJury = table.Column<int>(type: "INTEGER", nullable: true),
                    FinalJuryReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    FinalAverage = table.Column<long>(type: "INTEGER", nullable: true),
                    FinalCredits = table.Column<long>(type: "INTEGER", nullable: false),
                    Final = table.Column<int>(type: "INTEGER", nullable: true),
                    Rank = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionsLmd", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DecisionsLmd_DeliberationsLmd_DeliberationId",
                        column: x => x.DeliberationId,
                        principalTable: "DeliberationsLmd",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DecisionsLmd_Inscriptions_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Inscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionsLmd_DeliberationId_EnrollmentId",
                table: "DecisionsLmd",
                columns: new[] { "DeliberationId", "EnrollmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DecisionsLmd_EnrollmentId",
                table: "DecisionsLmd",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliberationsLmd_AcademicYearId",
                table: "DeliberationsLmd",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliberationsLmd_PromotionId_AcademicYearId",
                table: "DeliberationsLmd",
                columns: new[] { "PromotionId", "AcademicYearId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DettesLmd_OriginEnrollmentId_UeId",
                table: "DettesLmd",
                columns: new[] { "OriginEnrollmentId", "UeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DettesLmd_StudentId",
                table: "DettesLmd",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_NotesLmd_AcademicYearId",
                table: "NotesLmd",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_NotesLmd_ElementId",
                table: "NotesLmd",
                column: "ElementId");

            migrationBuilder.CreateIndex(
                name: "IX_NotesLmd_EnrollmentId_ElementId_Session",
                table: "NotesLmd",
                columns: new[] { "EnrollmentId", "ElementId", "Session" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotesLmd_PromotionId_AcademicYearId",
                table: "NotesLmd",
                columns: new[] { "PromotionId", "AcademicYearId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DecisionsLmd");

            migrationBuilder.DropTable(
                name: "DettesLmd");

            migrationBuilder.DropTable(
                name: "NotesLmd");

            migrationBuilder.DropTable(
                name: "DeliberationsLmd");

            migrationBuilder.DropColumn(
                name: "AcademicUnitId",
                table: "Promotions");
        }
    }
}
