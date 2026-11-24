using System.Net;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Application.Abstractions;
using Scolia.Domain.Finance;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>Un export du journal doit inclure toutes les pages, pas seulement les 1 000 premières.</summary>
public sealed class ReceiptsExportPaginationTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Excel_export_contains_every_receipt_beyond_one_thousand()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "INF1", name = "Première infirmière", level = 1,
            isActive = true, monthlyFee = 55000m, oneTimeFees = Array.Empty<object>()
        }).Ok();
        var student = await admin.Post("/api/apprenants", new
        {
            identity = new
            {
                lastName = "MUKENDI", middleName = "KABUYA", firstName = "Grace",
                gender = "F", birthDate = new DateOnly(2007, 3, 14),
                birthPlace = "Lubumbashi", guardianPhone = "0812345678"
            },
            promotionId = promotion.GetProperty("id").GetString()
        }).Ok();
        var studentId = Guid.Parse(student.GetProperty("id").GetString()!);
        var enrollmentId = Guid.Parse(student.GetProperty("enrollments")[0].GetProperty("id").GetString()!);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
            var academicYearId = await db.Enrollments.AsNoTracking()
                .Where(e => e.Id == enrollmentId).Select(e => e.AcademicYearId).SingleAsync();
            for (var i = 0; i < 1001; i++)
            {
                db.Receipts.Add(new Receipt
                {
                    Number = $"TEST-EXPORT-{i + 1:000000}",
                    VerificationCode = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
                    Date = DateOnly.FromDateTime(DateTime.Today),
                    StudentId = studentId,
                    EnrollmentId = enrollmentId,
                    AcademicYearId = academicYearId,
                    Amount = 1m,
                    Method = PaymentMethod.Especes
                });
            }
            // Les données fictives du test respectent le même garde-fou d'audit que la production.
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            audit.RecordAs("test-export", "TestSeed", "Receipt", null, "Insertion de 1001 reçus fictifs pour tester la pagination.");
            await db.SaveChangesAsync();
        }

        var response = await admin.GetAsync("/api/paiements/export?format=excel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheet(1);
        Assert.True(sheet.LastRowUsed()!.RowNumber() >= 1007,
            "L'export doit dépasser les 1 000 lignes et comprendre la ligne des totaux.");
        // Le journal est trié par date descendante, puis numéro de reçu décroissant.
        Assert.Equal("TEST-EXPORT-001001", sheet.Cell(6, 2).GetString());
        Assert.Equal("TEST-EXPORT-000001", sheet.Cell(1006, 2).GetString());
    }
}
