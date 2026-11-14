using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliberationsCyclesScolaires : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliberationsCyclesScolaires",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromotionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_DeliberationsCyclesScolaires", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliberationsCyclesScolaires_AnneesScolaires_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AnneesScolaires",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliberationsCyclesScolaires_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DecisionsCyclesScolaires",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeliberationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Computed = table.Column<int>(type: "INTEGER", nullable: false),
                    Percent = table.Column<long>(type: "INTEGER", nullable: true),
                    Rank = table.Column<int>(type: "INTEGER", nullable: true),
                    Jury = table.Column<int>(type: "INTEGER", nullable: true),
                    JuryReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    RepechageSubjects = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Final = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionsCyclesScolaires", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DecisionsCyclesScolaires_DeliberationsCyclesScolaires_DeliberationId",
                        column: x => x.DeliberationId,
                        principalTable: "DeliberationsCyclesScolaires",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DecisionsCyclesScolaires_Inscriptions_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Inscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RepechagesCyclesScolaires",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeliberationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Score = table.Column<long>(type: "INTEGER", nullable: true),
                    MaxScore = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepechagesCyclesScolaires", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepechagesCyclesScolaires_DeliberationsCyclesScolaires_DeliberationId",
                        column: x => x.DeliberationId,
                        principalTable: "DeliberationsCyclesScolaires",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepechagesCyclesScolaires_Inscriptions_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Inscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepechagesCyclesScolaires_ProgrammesPedagogiques_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "ProgrammesPedagogiques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionsCyclesScolaires_DeliberationId_EnrollmentId",
                table: "DecisionsCyclesScolaires",
                columns: new[] { "DeliberationId", "EnrollmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DecisionsCyclesScolaires_EnrollmentId",
                table: "DecisionsCyclesScolaires",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliberationsCyclesScolaires_AcademicYearId",
                table: "DeliberationsCyclesScolaires",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliberationsCyclesScolaires_PromotionId_AcademicYearId",
                table: "DeliberationsCyclesScolaires",
                columns: new[] { "PromotionId", "AcademicYearId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepechagesCyclesScolaires_DeliberationId_EnrollmentId_SubjectId",
                table: "RepechagesCyclesScolaires",
                columns: new[] { "DeliberationId", "EnrollmentId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepechagesCyclesScolaires_EnrollmentId",
                table: "RepechagesCyclesScolaires",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RepechagesCyclesScolaires_SubjectId",
                table: "RepechagesCyclesScolaires",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DecisionsCyclesScolaires");

            migrationBuilder.DropTable(
                name: "RepechagesCyclesScolaires");

            migrationBuilder.DropTable(
                name: "DeliberationsCyclesScolaires");
        }
    }
}
