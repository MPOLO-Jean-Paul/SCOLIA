using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttributionsProgramme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttributionsProgramme",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurriculumItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeacherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndReason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AssignedByName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributionsProgramme", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttributionsProgramme_Personnel_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttributionsProgramme_ProgrammesPedagogiques_CurriculumItemId",
                        column: x => x.CurriculumItemId,
                        principalTable: "ProgrammesPedagogiques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttributionsProgramme_CurriculumItemId",
                table: "AttributionsProgramme",
                column: "CurriculumItemId",
                unique: true,
                filter: "\"EndedOn\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttributionsProgramme_TeacherId",
                table: "AttributionsProgramme",
                column: "TeacherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttributionsProgramme");
        }
    }
}
