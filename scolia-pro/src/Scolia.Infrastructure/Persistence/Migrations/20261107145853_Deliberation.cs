using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Deliberation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Semester",
                table: "Cours",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CodesAnonymes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsManual = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReplacedReason = table.Column<string>(type: "TEXT", nullable: true),
                    ReplacedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodesAnonymes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodesAnonymes_Inscriptions_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Inscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CotesDeliberation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    EnteredAnonymously = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CotesDeliberation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CotesDeliberation_Cours_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Cours",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CotesDeliberation_Inscriptions_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Inscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReglagesDeliberation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EncodingS1Open = table.Column<bool>(type: "INTEGER", nullable: false),
                    EncodingS2Open = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    PassPercent = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReglagesDeliberation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionsDeliberation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromotionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Period = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    President = table.Column<string>(type: "TEXT", nullable: true),
                    Members = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosedByName = table.Column<string>(type: "TEXT", nullable: true),
                    ReopenCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastReopenReason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionsDeliberation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionsDeliberation_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DecisionsJury",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionsJury", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DecisionsJury_Inscriptions_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Inscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DecisionsJury_SessionsDeliberation_SessionId",
                        column: x => x.SessionId,
                        principalTable: "SessionsDeliberation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResultatsDeliberation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Matricule = table.Column<string>(type: "TEXT", nullable: false),
                    StudentName = table.Column<string>(type: "TEXT", nullable: false),
                    Points = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxPoints = table.Column<long>(type: "INTEGER", nullable: false),
                    Percent = table.Column<long>(type: "INTEGER", nullable: false),
                    Missing = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedCourses = table.Column<int>(type: "INTEGER", nullable: false),
                    Computed = table.Column<int>(type: "INTEGER", nullable: false),
                    Final = table.Column<int>(type: "INTEGER", nullable: false),
                    Mention = table.Column<string>(type: "TEXT", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResultatsDeliberation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResultatsDeliberation_SessionsDeliberation_SessionId",
                        column: x => x.SessionId,
                        principalTable: "SessionsDeliberation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodesAnonymes_AcademicYearId_Code",
                table: "CodesAnonymes",
                columns: new[] { "AcademicYearId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodesAnonymes_EnrollmentId",
                table: "CodesAnonymes",
                column: "EnrollmentId",
                unique: true,
                filter: "\"IsActive\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CotesDeliberation_CourseId",
                table: "CotesDeliberation",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_CotesDeliberation_EnrollmentId_CourseId_Kind",
                table: "CotesDeliberation",
                columns: new[] { "EnrollmentId", "CourseId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DecisionsJury_EnrollmentId",
                table: "DecisionsJury",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DecisionsJury_SessionId_EnrollmentId",
                table: "DecisionsJury",
                columns: new[] { "SessionId", "EnrollmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReglagesDeliberation_AcademicYearId",
                table: "ReglagesDeliberation",
                column: "AcademicYearId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResultatsDeliberation_SessionId_EnrollmentId",
                table: "ResultatsDeliberation",
                columns: new[] { "SessionId", "EnrollmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionsDeliberation_PromotionId_Period",
                table: "SessionsDeliberation",
                columns: new[] { "PromotionId", "Period" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodesAnonymes");

            migrationBuilder.DropTable(
                name: "CotesDeliberation");

            migrationBuilder.DropTable(
                name: "DecisionsJury");

            migrationBuilder.DropTable(
                name: "ReglagesDeliberation");

            migrationBuilder.DropTable(
                name: "ResultatsDeliberation");

            migrationBuilder.DropTable(
                name: "SessionsDeliberation");

            migrationBuilder.DropColumn(
                name: "Semester",
                table: "Cours");
        }
    }
}
