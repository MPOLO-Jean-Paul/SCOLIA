using System.Net;
using Scolia.Domain.Common;
using Scolia.Domain.Staff;

namespace Scolia.Tests;

/// <summary>Règles du pointage des enseignants : seuil de retard, statut des journées et conversion hebdomadaire.</summary>
public sealed class StaffAttendanceRulesTests
{
    private static readonly DateOnly Monday = new(2026, 10, 19);

    [Fact]
    public void Entry_at_eight_is_late_but_seven_fifty_nine_is_on_time()
    {
        Assert.False(StaffAttendanceRules.IsLate(new TimeOnly(7, 59, 59)));
        Assert.True(StaffAttendanceRules.IsLate(new TimeOnly(8, 0, 0)));
    }

    [Fact]
    public void Two_signatures_close_the_day_as_present_or_late()
    {
        var today = Monday.AddDays(7);
        Assert.Equal(StaffDayStatus.Present, StaffAttendanceRules.DayStatus(Monday, today, true, true, true, false, false));
        Assert.Equal(StaffDayStatus.PresentEnRetard, StaffAttendanceRules.DayStatus(Monday, today, true, true, true, true, false));
    }

    [Fact]
    public void Missing_signatures_stay_pending_today_and_become_absent_on_a_past_working_day()
    {
        var today = Monday;
        Assert.Equal(StaffDayStatus.EnAttente, StaffAttendanceRules.DayStatus(today, today, true, false, false, false, false));
        Assert.Equal(StaffDayStatus.Absent, StaffAttendanceRules.DayStatus(Monday.AddDays(-1), today, true, false, false, false, false));
        Assert.Equal(StaffDayStatus.Justifiee, StaffAttendanceRules.DayStatus(Monday.AddDays(-1), today, true, false, false, false, true));
        Assert.Equal(StaffDayStatus.SansObjet, StaffAttendanceRules.DayStatus(Monday.AddDays(-1), today, false, false, false, false, false));
        Assert.Equal(StaffDayStatus.SansObjet, StaffAttendanceRules.DayStatus(Monday.AddDays(2), today, true, false, false, false, false));
    }

    [Fact]
    public void Three_lates_in_one_week_make_one_absence_dated_on_the_third()
    {
        var lates = new[] { Monday, Monday.AddDays(1), Monday.AddDays(3) };
        var conversions = StaffAttendanceRules.Conversions(lates);
        var only = Assert.Single(conversions);
        Assert.Equal(Monday.AddDays(3), only.Date);
        Assert.Equal(Monday, only.WeekStart);
    }

    [Fact]
    public void Six_lates_make_two_absences_and_leftovers_are_not_carried_over()
    {
        var sixLates = Enumerable.Range(0, 6).Select(i => Monday.AddDays(i)).ToList();
        Assert.Equal(2, StaffAttendanceRules.Conversions(sixLates).Count);
        var fourLates = sixLates.Take(4).ToList();
        Assert.Single(StaffAttendanceRules.Conversions(fourLates));
    }

    [Fact]
    public void Lates_from_two_weeks_are_never_mixed_and_monday_starts_the_week()
    {
        var split = new[] { Monday.AddDays(5), Monday.AddDays(6), Monday.AddDays(7) };
        Assert.Empty(StaffAttendanceRules.Conversions(split.Take(2).Concat([Monday.AddDays(8)])));
        Assert.Equal(Monday, StaffAttendanceRules.WeekStart(Monday.AddDays(6)));
    }

    [Fact]
    public void Justification_reason_needs_five_to_two_thousand_characters()
    {
        Assert.Throws<DomainException>(() => StaffAttendanceRules.Reason("abcd"));
        Assert.Equal("Certificat médical", StaffAttendanceRules.Reason("  Certificat médical "));
    }
}

/// <summary>Pointage par l'API : signatures, sortie hors activité, justifications et bilan.</summary>
public sealed class StaffAttendanceApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private static async Task<string> TeacherAsync(HttpClient admin, string name, DateOnly hireDate, DateOnly? exitDate = null)
    {
        var staff = await admin.Post("/api/personnel", new
        {
            lastName = name, middleName = "", firstName = "Anne", gender = "F", function = "Enseignante", phone = "0812345678", email = (string?)null,
            hireDate, exitDate, baseSalary = 300000m, isActive = true,
        }).Ok();
        return staff.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Exit_needs_an_entry_and_each_mark_is_signed_once()
    {
        var admin = await app.InstalledAdmin();
        var teacher = await TeacherAsync(admin, "KABONGO", new DateOnly(2026, 1, 1));

        var (noEntry, _) = await admin.Post($"/api/personnel/{teacher}/signature/Sortie", new { }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, noEntry);

        var entry = await admin.Post($"/api/personnel/{teacher}/signature/Entree", new { }).Ok();
        var localNow = DateTime.Now.TimeOfDay;
        Assert.Equal(localNow >= new TimeSpan(8, 0, 0), entry.GetProperty("isLate").GetBoolean());

        var (again, _) = await admin.Post($"/api/personnel/{teacher}/signature/Entree", new { }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, again);
        await admin.Post($"/api/personnel/{teacher}/signature/Sortie", new { }).Ok();
        var (twice, _) = await admin.Post($"/api/personnel/{teacher}/signature/Sortie", new { }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, twice);
    }

    [Fact]
    public async Task A_member_outside_the_activity_period_cannot_sign()
    {
        var admin = await app.InstalledAdmin();
        var left = await TeacherAsync(admin, "ANCIEN", new DateOnly(2025, 1, 1), DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));
        var (status, _) = await admin.Post($"/api/personnel/{left}/signature/Entree", new { }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Past_working_day_without_signature_is_absent_until_justified_then_cancelled()
    {
        var admin = await app.InstalledAdmin();
        var teacher = await TeacherAsync(admin, "MBUYI", new DateOnly(2026, 1, 1));
        var from = DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Un jour ouvrable passé sans signature devient absent ; un week-end ne crée aucune absence.
        var days = (await admin.GetAsync($"/api/personnel/{teacher}/pointage?du={from:yyyy-MM-dd}&au={today:yyyy-MM-dd}").Ok()).EnumerateArray().ToList();
        var lastWorking = Enumerable.Range(1, 7).Select(i => DateOnly.FromDateTime(DateTime.Today.AddDays(-i)))
            .First(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));
        var workingRow = days.Single(d => d.GetProperty("date").GetString() == lastWorking.ToString("yyyy-MM-dd"));
        Assert.Equal("Absent", workingRow.GetProperty("status").GetString());
        // Le jour même reste en attente, seulement s'il est un jour ouvrable.
        var todayStatus = days.Single(d => d.GetProperty("date").GetString() == today.ToString("yyyy-MM-dd")).GetProperty("status").GetString();
        Assert.Equal(StaffAttendanceRules.IsWorkingDay(today) ? "En attente" : "—", todayStatus);

        // Justifier, puis annuler : le motif initial reste dans le registre.
        var justification = await admin.Post($"/api/personnel/{teacher}/justifications", new { date = lastWorking, reason = "Consultation médicale" }).Ok();
        var (twice, _) = await admin.Post($"/api/personnel/{teacher}/justifications", new { date = lastWorking, reason = "Autre motif" }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, twice);
        var (future, _) = await admin.Post($"/api/personnel/{teacher}/justifications", new { date = today.AddDays(2), reason = "Journée future" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, future);

        var justified = (await admin.GetAsync($"/api/personnel/{teacher}/pointage?du={from:yyyy-MM-dd}&au={today:yyyy-MM-dd}").Ok()).EnumerateArray()
            .Single(d => d.GetProperty("date").GetString() == lastWorking.ToString("yyyy-MM-dd"));
        Assert.Equal("Absence justifiée", justified.GetProperty("status").GetString());

        var cancelled = await admin.Post($"/api/personnel/justifications/{justification.GetProperty("id").GetString()}/annulation", new { reason = "Justificatif non reçu" }).Ok();
        Assert.Equal("Justificatif non reçu", cancelled.GetProperty("cancelReason").GetString());

        var bilan = (await admin.GetAsync($"/api/personnel/pointage/bilan?du={from:yyyy-MM-dd}&au={today:yyyy-MM-dd}").Ok()).EnumerateArray()
            .Single(b => b.GetProperty("name").GetString() == "MBUYI Anne");
        Assert.True(bilan.GetProperty("absent").GetInt32() >= 1);
        Assert.True(bilan.GetProperty("deductible").GetInt32() >= 1);
    }
}
