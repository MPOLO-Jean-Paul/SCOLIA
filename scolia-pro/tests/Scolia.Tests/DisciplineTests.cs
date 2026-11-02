using Scolia.Domain.Common;
using Scolia.Domain.Discipline;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Tests;

public sealed class DisciplineRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);
    private static readonly AcademicYear Year = new() { Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30) };

    private static DisciplineDecision Temporary(DateOnly first, DateOnly last, DisciplineMeasure measure = DisciplineMeasure.RenvoiTemporaire) => new()
    {
        Measure = measure, FirstDay = first, LastDay = last, DecisionDate = first, Reason = "Motif de test",
    };

    [Fact]
    public void Duration_counts_calendar_days_first_and_last_included()
    {
        Assert.Equal(3, DisciplineRules.Days(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3)));
        Assert.Equal(1, DisciplineRules.Days(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Return_is_allowed_the_day_after_the_last_day()
    {
        Assert.Equal(new DateOnly(2026, 10, 9), DisciplineRules.ReturnFrom(new DateOnly(2026, 10, 8)));
    }

    [Fact]
    public void Second_valid_temporary_renvoi_of_the_year_becomes_definitive()
    {
        Assert.False(DisciplineRules.BecomesDefinitive(DisciplineMeasure.RenvoiTemporaire, 0));
        Assert.True(DisciplineRules.BecomesDefinitive(DisciplineMeasure.RenvoiTemporaire, 1));
        Assert.False(DisciplineRules.BecomesDefinitive(DisciplineMeasure.RenvoiDefinitif, 1));
        Assert.False(DisciplineRules.BecomesDefinitive(DisciplineMeasure.Convocation, 2));
    }

    [Fact]
    public void Convocation_and_cancelled_decisions_are_not_exclusions()
    {
        var convocation = new DisciplineDecision { Measure = DisciplineMeasure.Convocation };
        var cancelled = Temporary(Today, Today.AddDays(1));
        cancelled.State = DecisionState.Annulee;
        Assert.False(DisciplineRules.IsExclusion(convocation));
        Assert.False(DisciplineRules.IsExclusion(cancelled));
        Assert.True(DisciplineRules.IsExclusion(Temporary(Today, Today.AddDays(1))));
    }

    [Fact]
    public void Status_is_suspended_during_temporary_renvoi_then_returns_to_base_status()
    {
        var renvoi = new[] { Temporary(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 25)) };
        Assert.Equal(EnrollmentStatus.Suspendu, DisciplineRules.EffectiveStatus(EnrollmentStatus.Actif, renvoi, Today));
        Assert.Equal(EnrollmentStatus.Actif, DisciplineRules.EffectiveStatus(EnrollmentStatus.Actif, renvoi, new DateOnly(2026, 10, 26)));
    }

    [Fact]
    public void Definitive_renvoi_shows_sorti_even_after_a_temporary_one_ended()
    {
        var definitive = new DisciplineDecision { Measure = DisciplineMeasure.RenvoiDefinitif, DecisionDate = Today.AddDays(-30) };
        Assert.Equal(EnrollmentStatus.Sorti, DisciplineRules.EffectiveStatus(EnrollmentStatus.Actif, [definitive], Today));
    }

    [Fact]
    public void Renvoi_must_stay_inside_the_school_year_and_end_after_it_starts()
    {
        var existing = Array.Empty<DisciplineDecision>();
        Assert.Throws<DomainException>(() => DisciplineRules.ValidateExclusion(Today, Today.AddDays(2), Today, Year, Today, existing));
        Assert.Throws<DomainException>(() => DisciplineRules.ValidateExclusion(Today, new DateOnly(2026, 8, 30), Today, Year, Today, existing));
        Assert.Throws<DomainException>(() => DisciplineRules.ValidateExclusion(Today, Today, new DateOnly(2027, 7, 1), Year, Today, existing));
    }

    [Fact]
    public void Future_decisions_are_refused()
    {
        Assert.Throws<DomainException>(() => DisciplineRules.ValidateDecisionDate(Today.AddDays(1), Today));
        DisciplineRules.ValidateDecisionDate(Today, Today);
    }

    [Fact]
    public void Overlapping_temporary_renvoi_is_refused_and_an_older_start_is_refused_for_chronology()
    {
        var existing = new[] { Temporary(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12)) };
        Assert.Throws<DomainException>(() => DisciplineRules.ValidateExclusion(Today, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 14), Year, Today, existing));
        Assert.Throws<DomainException>(() => DisciplineRules.ValidateExclusion(Today, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), Year, Today, existing));
        DisciplineRules.ValidateExclusion(Today, new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 15), Year, Today, existing);
    }

    [Fact]
    public void Reason_must_hold_between_5_and_2000_characters()
    {
        Assert.Throws<DomainException>(() => DisciplineRules.Reason("abcd"));
        Assert.Equal("abcde", DisciplineRules.Reason("  abcde  "));
        Assert.Throws<DomainException>(() => DisciplineRules.Reason(new string('a', 2001)));
    }

    [Fact]
    public void Parent_absent_is_only_recorded_after_the_appointment()
    {
        var convocation = new DisciplineDecision { Measure = DisciplineMeasure.Convocation, AppointmentAt = new DateTime(2026, 10, 26, 9, 0, 0) };
        Assert.Throws<DomainException>(() => DisciplineRules.ValidateReception(convocation, ParentReception.Absent, "Pas venu", new DateTime(2026, 10, 26, 8, 0, 0)));
        DisciplineRules.ValidateReception(convocation, ParentReception.Absent, "Pas venu", new DateTime(2026, 10, 26, 9, 30, 0));
    }

    [Fact]
    public void Reception_needs_a_convocation_and_a_written_account()
    {
        var convocation = new DisciplineDecision { Measure = DisciplineMeasure.Convocation, AppointmentAt = new DateTime(2026, 10, 20, 9, 0, 0) };
        Assert.Throws<DomainException>(() => DisciplineRules.ValidateReception(convocation, ParentReception.Recu, " ", DateTime.Now));
        Assert.Throws<DomainException>(() => DisciplineRules.ValidateReception(Temporary(Today, Today), ParentReception.Recu, "Compte rendu", DateTime.Now));
        DisciplineRules.ValidateReception(convocation, ParentReception.Recu, "Parent reçu et informé", DateTime.Now);
    }
}
