using System.Text;
using Scolia.Application.Dtos;
using Scolia.Infrastructure.Documents;
using Scolia.Mobile.Server;

namespace Scolia.Mobile.Tests;

/// <summary>
/// Bulletins EPST, procès-verbaux et relevés LMD du téléphone serveur : PDF valides, une page par élève ou étudiant.
/// Avec SCOLIA_PDF_DIR, chaque document est écrit par le téléphone et par l'ordinateur pour les comparer.
/// </summary>
public sealed class AcademicDocumentTests
{
    private readonly SkiaAcademicDocuments phone = new();
    private readonly AcademicDocumentService desktop = new();

    private static int Pages(byte[] pdf) =>
        System.Text.RegularExpressions.Regex.Count(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]");

    private static void Check(string name, byte[] pdf, int pages, Func<byte[]> desktop)
    {
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal(pages, Pages(pdf));
        if (Environment.GetEnvironmentVariable("SCOLIA_PDF_DIR") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, $"{name}-telephone.pdf"), pdf);
        File.WriteAllBytes(Path.Combine(dir, $"{name}-ordinateur.pdf"), desktop());
    }

    private static EpstBulletinPrintDto Bulletins()
    {
        var periods = new List<EpstPeriodColumnDto>();
        var terms = new List<EpstTermColumnDto>();
        for (var t = 1; t <= 3; t++)
        {
            var codes = new[] { $"P{2 * t - 1}", $"P{2 * t}", $"EX{t}" };
            terms.Add(new($"T{t}", codes));
            periods.AddRange(codes.Select(c => new EpstPeriodColumnDto(Guid.NewGuid(), c, $"T{t}", c.StartsWith("EX"))));
        }
        var subjects = new[] { new EpstSubjectColumnDto(Guid.NewGuid(), "FR", "Français", 20), new EpstSubjectColumnDto(Guid.NewGuid(), "CALC", "Calcul", 10) };
        EpstPupilResultDto Pupil(string name, decimal fraction, int rank)
        {
            var lines = subjects.Select(s =>
            {
                var cells = periods.Select(p => new EpstCellDto(s.PeriodMax * (p.IsExam ? 2 : 1) * fraction, s.PeriodMax * (p.IsExam ? 2 : 1))).ToList();
                var termCells = terms.Select((_, i) => new EpstCellDto(cells.Skip(i * 3).Take(3).Sum(c => c.Points), cells.Skip(i * 3).Take(3).Sum(c => c.Max))).ToList();
                return new EpstSubjectLineDto(s.Id, s.Code, s.Name, cells, termCells, cells.Sum(c => c.Points), cells.Sum(c => c.Max), fraction * 100, fraction < 0.5m);
            }).ToList();
            var periodTotals = periods.Select((_, i) => new EpstCellDto(lines.Sum(l => l.Periods[i].Points), lines.Sum(l => l.Periods[i].Max))).ToList();
            var termTotals = terms.Select((_, i) => new EpstCellDto(lines.Sum(l => l.Terms[i].Points), lines.Sum(l => l.Terms[i].Max))).ToList();
            return new EpstPupilResultDto(Guid.NewGuid(), Guid.NewGuid(), "EP0" + rank, name, lines, periodTotals, termTotals,
                lines.Sum(l => l.Total), lines.Sum(l => l.Max), fraction * 100, 0, lines.Count(l => l.Failed), rank, [rank, rank, rank],
                periods.Where(p => !p.IsExam).Select(p => new EpstConductCellDto(p.Id, p.Code, "TB", "E")).ToList(), rank == 1 ? "Passe" : "Double");
        }
        var pupils = new[] { Pupil("MUKENDI KABEYA Grâce", 0.8m, 1), Pupil("ILUNGA MWEMA Daniel", 0.4m, 2) };
        var results = new EpstClassResultsDto(Guid.NewGuid(), "5e primaire A", "Primaire", "Primaire", "2026-2027", true, 50, 50, 2,
            terms, periods, subjects, pupils, "Secrétariat", DateTime.UtcNow, "Cloturee");
        return new EpstBulletinPrintDto("EP Les Flamboyants", "Avenue de la Paix 12", "Kinshasa", null, "Kinshasa-Lukunga", null, results, pupils);
    }

    private static LmdTranscriptPrintDto Transcript(string stage)
    {
        LmdStudentLineDto Student(string name, decimal mark, int rank, string? decision)
        {
            var ec = new[] { new LmdEcLineDto(Guid.NewGuid(), "A1", "Analyse", 30, mark, 1, "D"), new LmdEcLineDto(Guid.NewGuid(), "A2", "Algèbre", 30, mark - 1, 1, "E") };
            var units = new List<LmdUeLineDto>
            {
                new(Guid.NewGuid(), "MAT101", "Mathématiques", "S1", 30, mark - 0.5m, mark >= 10.5m, false, ec),
                new(Guid.NewGuid(), "INF102", "Programmation", "S2", 30, mark, mark >= 10, false,
                    [new LmdEcLineDto(Guid.NewGuid(), "P1", "Programmation", 30, mark, 2, "D")]),
            };
            var s1 = new LmdSemesterLineDto("S1", mark - 0.5m, mark >= 10.5m ? 30 : 0, 30, true);
            var s2 = new LmdSemesterLineDto("S2", mark, mark >= 10 ? 30 : 0, 30, true);
            return new LmdStudentLineDto(Guid.NewGuid(), Guid.NewGuid(), "GL00" + rank, name, s1, s2, mark - 0.25m,
                s1.CreditsAcquired + s2.CreditsAcquired, 60, true, rank, "Admis", null, null, decision, units);
        }
        var students = new[] { Student("MUKENDI KABEYA Grâce", 14, 1, "Admis"), Student("ILUNGA MWEMA Daniel", 10.2m, 2, "Admis avec dettes") };
        var promotion = new LmdPromotionResultsDto(Guid.NewGuid(), "Génie logiciel L1", "L1", "Génie logiciel", "2026-2027", stage, "Cloturee",
            20, 10, 15, "Secrétariat", DateTime.UtcNow, "Secrétariat", DateTime.UtcNow, "Secrétariat", DateTime.UtcNow, students);
        return new LmdTranscriptPrintDto("Université Lumière", "Route Kasapa", "Lubumbashi", null, "$", promotion, students);
    }

    [Fact]
    public void Primary_bulletins_print_one_landscape_page_per_pupil()
    {
        var d = Bulletins();
        Check("epst-bulletins", phone.EpstBulletins(d), 2, () => desktop.EpstBulletins(d));
    }

    [Fact]
    public void Primary_minutes_and_palmares_print_on_the_phone()
    {
        var pupils = new[]
        {
            new EpstDecisionDto(Guid.NewGuid(), "EP01", "MUKENDI KABEYA Grâce", 80, 1, 0, 0, "Passe", null, null, "Passe", "Passe", "Passe", []),
            new EpstDecisionDto(Guid.NewGuid(), "EP02", "ILUNGA MWEMA Daniel", 40, 2, 2, 0, "Redouble", "Redouble", "Absent à l'examen", "Redouble", "Redouble", "Double", []),
        };
        var deliberation = new EpstDeliberationDto(Guid.NewGuid(), "5e primaire A", "2026-2027", "Cloturee", "Secrétariat", DateTime.UtcNow,
            "Secrétariat", DateTime.UtcNow, 50, 50, 2, pupils);
        var d = new EpstPalmaresPrintDto("EP Les Flamboyants", "Avenue de la Paix 12", "Kinshasa", null, null, null, "Primaire", deliberation);
        Check("epst-palmares", phone.EpstPalmares(d), 1, () => desktop.EpstPalmares(d));
    }

    [Theory]
    [InlineData("S1")]
    [InlineData("Definitif")]
    public void University_transcripts_and_minutes_print_on_the_phone(string stage)
    {
        var d = Transcript(stage);
        Check($"lmd-releves-{stage}", phone.LmdTranscripts(d), 2, () => desktop.LmdTranscripts(d));
        Check($"lmd-pv-{stage}", phone.LmdMinutes(d), 1, () => desktop.LmdMinutes(d));
    }
}
