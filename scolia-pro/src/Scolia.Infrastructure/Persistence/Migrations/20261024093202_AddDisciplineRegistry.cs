using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDisciplineRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegistreDiscipline",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Measure = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    DecisionDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FirstDay = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LastDay = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    AppointmentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsAutomatic = table.Column<bool>(type: "INTEGER", nullable: false),
                    ParentName = table.Column<string>(type: "TEXT", nullable: true),
                    ParentPhone = table.Column<string>(type: "TEXT", nullable: true),
                    Reception = table.Column<int>(type: "INTEGER", nullable: false),
                    ReceptionNote = table.Column<string>(type: "TEXT", nullable: true),
                    ReceptionAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    CancelReason = table.Column<string>(type: "TEXT", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelledByName = table.Column<string>(type: "TEXT", nullable: true),
                    RecordedByName = table.Column<string>(type: "TEXT", nullable: false),
                    RequestKey = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistreDiscipline", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegistreDiscipline_Inscriptions_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Inscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegistreDiscipline_EnrollmentId",
                table: "RegistreDiscipline",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RegistreDiscipline_RequestKey",
                table: "RegistreDiscipline",
                column: "RequestKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegistreDiscipline");
        }
    }
}
