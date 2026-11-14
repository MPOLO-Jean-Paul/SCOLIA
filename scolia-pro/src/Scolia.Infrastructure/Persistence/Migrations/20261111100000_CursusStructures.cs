using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations;

/// <summary>
/// Migration exclusivement additive, sans aucune écriture dans les tables
/// historiques ITM : Cours, Notes, Apprenants, Promotions, Paiements et Periodes.
/// </summary>
[DbContext(typeof(ScoliaDbContext))]
[Migration("20261111100000_CursusStructures")]
public partial class CursusStructures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StructuresAcademiques",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                Kind = table.Column<int>(type: "INTEGER", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StructuresAcademiques", x => x.Id);
                table.ForeignKey(
                    name: "FK_StructuresAcademiques_StructuresAcademiques_ParentId",
                    column: x => x.ParentId,
                    principalTable: "StructuresAcademiques",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_StructuresAcademiques_ParentId_Kind_Code",
            table: "StructuresAcademiques",
            columns: new[] { "ParentId", "Kind", "Code" });

        migrationBuilder.CreateTable(
            name: "ProgrammesPedagogiques",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                PromotionId = table.Column<Guid>(type: "TEXT", nullable: false),
                AcademicUnitId = table.Column<Guid>(type: "TEXT", nullable: true),
                ParentItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                Kind = table.Column<int>(type: "INTEGER", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                Semester = table.Column<int>(type: "INTEGER", nullable: false),
                Credits = table.Column<long>(type: "INTEGER", nullable: false),
                Coefficient = table.Column<long>(type: "INTEGER", nullable: false),
                Hours = table.Column<long>(type: "INTEGER", nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProgrammesPedagogiques", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProgrammesPedagogiques_AnneesScolaires_AcademicYearId",
                    column: x => x.AcademicYearId,
                    principalTable: "AnneesScolaires",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ProgrammesPedagogiques_Promotions_PromotionId",
                    column: x => x.PromotionId,
                    principalTable: "Promotions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ProgrammesPedagogiques_StructuresAcademiques_AcademicUnitId",
                    column: x => x.AcademicUnitId,
                    principalTable: "StructuresAcademiques",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ProgrammesPedagogiques_ProgrammesPedagogiques_ParentItemId",
                    column: x => x.ParentItemId,
                    principalTable: "ProgrammesPedagogiques",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProgrammesPedagogiques_AcademicUnitId",
            table: "ProgrammesPedagogiques",
            column: "AcademicUnitId");

        migrationBuilder.CreateIndex(
            name: "IX_ProgrammesPedagogiques_ParentItemId",
            table: "ProgrammesPedagogiques",
            column: "ParentItemId");

        migrationBuilder.CreateIndex(
            name: "IX_ProgrammesPedagogiques_AcademicYearId_PromotionId_Semester",
            table: "ProgrammesPedagogiques",
            columns: new[] { "AcademicYearId", "PromotionId", "Semester" });

        migrationBuilder.CreateIndex(
            name: "IX_ProgrammesPedagogiques_PromotionId",
            table: "ProgrammesPedagogiques",
            column: "PromotionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProgrammesPedagogiques");
        migrationBuilder.DropTable(name: "StructuresAcademiques");
    }
}
