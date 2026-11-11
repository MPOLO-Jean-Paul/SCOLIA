using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeachingRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nouvelles colonnes des cours. Les données existantes sont reprises : numéro selon l'ordre du code,
            // année courante, et enseignant rattaché au cours converti en attribution active.
            migrationBuilder.AddColumn<Guid>(
                name: "AcademicYearId",
                table: "Cours",
                type: "TEXT",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "Cours",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxScore",
                table: "Cours",
                type: "TEXT",
                nullable: false,
                defaultValue: 20m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExitDate",
                table: "Personnel",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AffectationsCours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeacherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndReason = table.Column<string>(type: "TEXT", nullable: true),
                    AssignedByName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffectationsCours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffectationsCours_Cours_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Cours",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AffectationsCours_Personnel_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                UPDATE "Cours" SET "Number" = (
                    SELECT COUNT(*) FROM "Cours" AS c2
                    WHERE c2."PromotionId" = "Cours"."PromotionId" AND c2."Code" <= "Cours"."Code");
                UPDATE "Cours" SET "AcademicYearId" = (SELECT "Id" FROM "AnneesScolaires" WHERE "IsCurrent" = 1 LIMIT 1)
                WHERE "AcademicYearId" = '00000000-0000-0000-0000-000000000000';
                UPDATE "Cours" SET "AcademicYearId" = (SELECT "Id" FROM "AnneesScolaires" ORDER BY "StartDate" DESC LIMIT 1)
                WHERE "AcademicYearId" = '00000000-0000-0000-0000-000000000000';
                INSERT INTO "AffectationsCours" ("Id", "CourseId", "TeacherId", "StartedOn", "EndedOn", "EndReason", "AssignedByName",
                    "CreatedAtUtc", "CreatedBy", "UpdatedAtUtc", "UpdatedBy", "Version")
                SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' || substr(lower(hex(randomblob(2))), 2)
                    || '-8' || substr(lower(hex(randomblob(2))), 2) || '-' || lower(hex(randomblob(6))),
                    "Id", "TeacherId", date('now'), NULL, NULL, 'Migration', datetime('now'), NULL, NULL, NULL, 1
                FROM "Cours" WHERE "TeacherId" IS NOT NULL;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Cours_PromotionId_Code",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "TeacherId",
                table: "Cours");

            migrationBuilder.CreateIndex(
                name: "IX_Cours_PromotionId_Number",
                table: "Cours",
                columns: new[] { "PromotionId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffectationsCours_CourseId",
                table: "AffectationsCours",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_AffectationsCours_TeacherId",
                table: "AffectationsCours",
                column: "TeacherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AffectationsCours");

            migrationBuilder.DropIndex(
                name: "IX_Cours_PromotionId_Number",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "ExitDate",
                table: "Personnel");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Cours",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "TeacherId",
                table: "Cours",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("""UPDATE "Cours" SET "Code" = printf('%04d', "Number");""");

            migrationBuilder.CreateIndex(
                name: "IX_Cours_PromotionId_Code",
                table: "Cours",
                columns: new[] { "PromotionId", "Code" },
                unique: true);

            migrationBuilder.DropColumn(
                name: "AcademicYearId",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "MaxScore",
                table: "Cours");
        }
    }
}
