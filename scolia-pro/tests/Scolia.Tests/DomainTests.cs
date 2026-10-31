using Scolia.Application.Common;
using Scolia.Domain.Academics;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Security;
using Scolia.Domain.Students;

namespace Scolia.Tests;

public sealed class TextAndIdentityTests
{
    [Theory]
    [InlineData("  Élève   Kabila ", "ELEVE KABILA")]
    [InlineData("ÇA va", "CA VA")]
    [InlineData("naïve fille", "NAIVE FILLE")]
    [InlineData(null, "")]
    public void Normalize_removes_accents_case_and_extra_spaces(string? input, string expected) => Assert.Equal(expected, TextKey.Normalize(input));

    [Fact]
    public void ProperName_formats_compound_first_names() => Assert.Equal("Jean-Paul Marie", TextKey.ProperName("  jean-PAUL   marie "));

    [Fact]
    public void Matricule_follows_v79_format_and_skips_taken_codes()
    {
        Assert.Equal("INF101", MatriculeGenerator.Next("inf1", []));
        Assert.Equal("INF103", MatriculeGenerator.Next("INF1", ["INF101", "inf102"]));
        var all = Enumerable.Range(1, 99).Select(i => $"TLM1{i:00}");
        Assert.Equal("TLM10A", MatriculeGenerator.Next("TML1", all)); // TML1 est corrigé en TLM1 comme en V79.
    }

    [Fact]
    public void Matricule_rejects_invalid_promotion_code() => Assert.Throws<DomainException>(() => MatriculeGenerator.Next("INF", []));

    [Theory]
    [InlineData("0812345678", "0812345678")]
    [InlineData("081 234 56 78", "0812345678")]
    [InlineData("+243 812 345 678", "0812345678")]
    [InlineData("081.234.56-78", "0812345678")]
    public void Phone_numbers_are_normalized(string input, string expected) => Assert.Equal(expected, IdentityRules.NormalizePhone(input));

    [Theory]
    [InlineData("12345")]
    [InlineData("08123456789")]
    [InlineData("081234567a")]
    [InlineData("+33612345678")]
    public void Invalid_phone_is_rejected(string input) => Assert.Throws<DomainException>(() => IdentityRules.NormalizePhone(input));

    [Fact]
    public void WhatsApp_number_uses_country_code() => Assert.Equal("243812345678", IdentityRules.ToInternational("0812345678", "243"));

    [Theory]
    [InlineData("Mbuyi", true)]
    [InlineData("N'Galula", true)]
    [InlineData("Jean-Paul", true)]
    [InlineData("Paul2", false)]
    [InlineData("", false)]
    public void Names_contain_letters_only(string value, bool valid) => Assert.Equal(valid, IdentityRules.IsValidName(value));

    [Fact]
    public void Password_policy_requires_letters_digits_and_length()
    {
        Assert.Throws<DomainException>(() => PasswordPolicy.Validate("court1"));
        Assert.Throws<DomainException>(() => PasswordPolicy.Validate("sanschiffres"));
        PasswordPolicy.Validate("Scolia2026");
    }
}

public sealed class CalendarTests
{
    [Fact]
    public void Monthly_periods_follow_school_year_and_v79_due_rule()
    {
        var periods = AcademicPeriod.Monthly(Guid.NewGuid(), 2026, 9, 10);
        Assert.Equal(10, periods.Count);
        Assert.Equal("Septembre", periods[0].Name);
        Assert.Equal(new DateOnly(2026, 10, 6), periods[0].DueDate); // Exigible le 6 du mois suivant.
        Assert.Equal("Juin", periods[^1].Name);
        Assert.Equal(2027, periods[^1].CalendarYear);
    }

    [Theory]
    [InlineData("2026 - 2027", "2026-2027")]
    [InlineData("2026–2027", "2026-2027")]
    public void Year_label_is_normalized(string input, string expected) => Assert.Equal(expected, AcademicYear.NormalizeLabel(input));

    [Fact]
    public void Inconsistent_year_label_is_rejected() => Assert.Throws<DomainException>(() => AcademicYear.NormalizeLabel("2026-2028"));
}

public sealed class AccountingTests
{
    private static readonly Guid Sept = Guid.NewGuid(), Oct = Guid.NewGuid(), Nov = Guid.NewGuid(), Insc = Guid.NewGuid();

    private static FeeLineInput[] Fees() =>
    [
        new(Sept, "Septembre", 55000, new DateOnly(2026, 10, 6), 1),
        new(Oct, "Octobre", 55000, new DateOnly(2026, 11, 6), 2),
        new(Nov, "Novembre", 55000, new DateOnly(2026, 12, 6), 3),
        new(Insc, "Inscription", 25000, new DateOnly(2026, 9, 15), 0, null, FeeKind.Inscription),
    ];

    private static StudentAccount Account(Dictionary<Guid, decimal>? paid = null, Dictionary<Guid, decimal>? coverage = null) =>
        StudentAccount.Compute(Fees(), coverage ?? [], paid ?? [], new DateOnly(2026, 10, 23));

    private static StudentAccount AccountOn(DateOnly asOf) =>
        StudentAccount.Compute(Fees(), new Dictionary<Guid, decimal>(), new Dictionary<Guid, decimal>(), asOf);

    [Fact]
    public void Account_totals_and_overdue_are_computed()
    {
        var a = Account(new() { [Insc] = 25000, [Sept] = 20000 });
        Assert.Equal(190000, a.TotalNet);
        Assert.Equal(45000, a.TotalPaid);
        Assert.Equal(145000, a.Balance);
        Assert.Equal(35000, a.Overdue); // Septembre partiellement payé, échu le 06/10.
        Assert.Equal(LineStatus.Partiel, a.Lines.Single(l => l.FeeItemId == Sept).Status);
    }

    [Fact]
    public void Month_becomes_overdue_on_the_sixth_of_the_following_month()
    {
        var before = AccountOn(new DateOnly(2026, 11, 5));
        Assert.False(before.Lines.Single(l => l.FeeItemId == Oct).IsOverdue);
        Assert.True(before.Lines.Single(l => l.FeeItemId == Sept).IsOverdue);

        var onTheSixth = AccountOn(new DateOnly(2026, 11, 6));
        Assert.True(onTheSixth.Lines.Single(l => l.FeeItemId == Oct).IsOverdue);
        Assert.False(onTheSixth.Lines.Single(l => l.FeeItemId == Nov).IsOverdue);
    }

    [Fact]
    public void Paying_months_settles_each_selected_month_in_full()
    {
        var plan = PaymentAllocator.Months(Account(), [Sept, Oct]);
        Assert.Equal([(Sept, 55000m), (Oct, 55000m)], plan);
    }

    [Fact]
    public void Paying_a_month_already_partly_paid_settles_only_its_remainder()
    {
        var plan = PaymentAllocator.Months(Account(new() { [Sept] = 20000 }), [Sept]);
        Assert.Equal([(Sept, 35000m)], plan);
    }

    [Fact]
    public void Inscription_and_exam_fees_cannot_be_paid_by_month()
    {
        var error = Assert.Throws<DomainException>(() => PaymentAllocator.Months(Account(), [Insc]));
        Assert.Contains("scolarité", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_month_already_paid_cannot_be_paid_again()
    {
        var error = Assert.Throws<DomainException>(() => PaymentAllocator.Months(Account(new() { [Sept] = 55000 }), [Sept]));
        Assert.Contains("déjà réglé", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sponsorship_reduces_net_amounts()
    {
        var a = Account(coverage: new() { [Sept] = 100, [Oct] = 50 });
        Assert.Equal(LineStatus.Couvert, a.Lines.Single(l => l.FeeItemId == Sept).Status);
        Assert.Equal(27500, a.Lines.Single(l => l.FeeItemId == Oct).Net);
        Assert.Equal(25000 + 27500 + 55000, a.TotalNet);
    }

    [Theory]
    [InlineData(null, "À jour")]
    [InlineData("2026-10-10", "0-30 jours")]
    [InlineData("2026-08-01", "61-90 jours")]
    [InlineData("2026-05-01", "Plus de 90 jours")]
    public void Aging_buckets(string? oldest, string expected) =>
        Assert.Equal(expected, Aging.Bucket(oldest is null ? null : DateOnly.Parse(oldest, System.Globalization.CultureInfo.InvariantCulture), new DateOnly(2026, 10, 23)));

    [Fact]
    public void Document_numbers_are_sequential_and_codes_random()
    {
        Assert.Equal("RC-2026-000123", DocumentNumbers.Format("RC", 2026, 123));
        var codes = Enumerable.Range(0, 200).Select(_ => DocumentNumbers.NewVerificationCode()).ToHashSet();
        Assert.Equal(200, codes.Count);
        Assert.All(codes, c => Assert.Equal(10, c.Length));
    }

    [Theory]
    [InlineData(55000, "Cinquante-cinq mille francs congolais")]
    [InlineData(80, "Quatre-vingts francs congolais")]
    [InlineData(81000, "Quatre-vingt-un mille francs congolais")]
    [InlineData(71, "Soixante et onze francs congolais")]
    [InlineData(200000, "Deux cent mille francs congolais")]
    [InlineData(1250300, "Un million deux cent cinquante mille trois cents francs congolais")]
    public void Amounts_are_written_in_french(long amount, string expected) => Assert.Equal(expected, FrenchNumbers.Amount(amount, "francs congolais"));
}

public sealed class GradeAndAuditTests
{
    [Fact]
    public void Course_average_is_weighted_and_ignores_absences()
    {
        var average = GradeCalculator.CourseAverage([new(15, 20, 1), new(40, 50, 2), new(null, 20, 1)]);
        Assert.Equal(15.67m, average); // (15 + 16×2) / 3
    }

    [Fact]
    public void General_average_uses_coefficients()
    {
        Assert.Equal(13m, GradeCalculator.GeneralAverage([(10m, 1m), (14.5m, 2m)]));
        Assert.Equal("Admis", GradeCalculator.Decision(10));
        Assert.Equal("Ajourné", GradeCalculator.Decision(9.99m));
    }

    [Fact]
    public void Audit_hash_changes_when_content_changes()
    {
        var entry = new AuditEntry { Id = 1, AtUtc = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), UserName = "a", Action = "x", EntityType = "Recu", Summary = "Reçu 1" };
        var hash = entry.ComputeHash();
        entry.Summary = "Reçu 2";
        Assert.NotEqual(hash, entry.ComputeHash());
        entry.Summary = "Reçu 1";
        entry.AtUtc = DateTime.SpecifyKind(entry.AtUtc, DateTimeKind.Unspecified); // Relecture depuis SQLite.
        Assert.Equal(hash, entry.ComputeHash());
    }

    [Fact]
    public void Payroll_net_is_computed_and_cannot_be_negative()
    {
        var p = new Domain.Staff.PayrollEntry { BaseSalary = 500000, Allowances = 50000, Deductions = 20000, Advances = 100000 };
        p.Recompute();
        Assert.Equal(430000, p.Net);
        p.Advances = 600000;
        Assert.Throws<DomainException>(p.Recompute);
    }
}

public sealed class UnknownLoginTrackerTests
{
    [Fact]
    public void Unknown_names_lock_after_five_failures_then_unlock()
    {
        var tracker = new Scolia.Application.Services.UnknownLoginTracker();
        var now = new DateTime(2026, 10, 25, 8, 0, 0, DateTimeKind.Utc);
        var until = now.AddMinutes(2);
        for (var i = 1; i <= 4; i++) Assert.Equal(i, tracker.Fail("X", now, until, 5));
        Assert.Null(tracker.LockedUntil("X", now));
        Assert.Equal(5, tracker.Fail("X", now, until, 5));
        Assert.Equal(until, tracker.LockedUntil("X", now.AddSeconds(119)));
        Assert.Null(tracker.LockedUntil("X", until));
        Assert.Equal(1, tracker.Fail("X", until.AddSeconds(1), until.AddMinutes(2), 5));
    }
}
