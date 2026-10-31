using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Academics;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Security;
using Scolia.Domain.Staff;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>Premier démarrage : établissement, année scolaire, compte secrétariat (l'administrateur) et données de démonstration facultatives.</summary>
public sealed class SetupService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, IPasswordService passwords)
{
    public async Task<SetupStatusDto> StatusAsync(CancellationToken ct = default)
    {
        var school = await db.Schools.AsNoTracking().OrderBy(s => s.CreatedAtUtc).FirstOrDefaultAsync(ct);
        var hasUser = await db.Users.AnyAsync(ct);
        return new SetupStatusDto(school is not null && hasUser, school?.Name);
    }

    public static string DefaultYearLabel(DateOnly today)
    {
        var first = today.Month >= 8 ? today.Year : today.Year - 1;
        return $"{first}-{first + 1}";
    }

    public Task<AppUser> InitializeAsync(SetupRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        if (await db.Users.AnyAsync(ct) || await db.Schools.AnyAsync(ct)) throw new ConflictException("SCOLIA est déjà configuré.");
        var userName = (input.AdminUserName ?? string.Empty).Trim().ToLowerInvariant();
        new ValidationBuilder()
            .Require(!string.IsNullOrWhiteSpace(input.SchoolName), "schoolName", "Nom de l'établissement obligatoire.")
            .Require(!string.IsNullOrWhiteSpace(input.ShortName), "shortName", "Sigle obligatoire.")
            .Require(input.CurrencyCode is { Length: 3 }, "currencyCode", "Code monnaie à 3 lettres.")
            .Require(userName.Length is >= 3 and <= 40 && userName.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'), "adminUserName", "Identifiant de 3 à 40 caractères.")
            .Require(!string.IsNullOrWhiteSpace(input.AdminDisplayName), "adminDisplayName", "Nom du compte secrétariat obligatoire.")
            .ThrowIfInvalid();
        PasswordPolicy.Validate(input.AdminPassword);
        var school = new SchoolProfile
        {
            Name = input.SchoolName.Trim(), ShortName = input.ShortName.Trim(), Motto = string.IsNullOrWhiteSpace(input.Motto) ? null : input.Motto.Trim(),
            City = input.City, Phone = IdentityRules.NormalizePhone(input.Phone), CurrencyCode = input.CurrencyCode.ToUpperInvariant(), CurrencySymbol = input.CurrencySymbol.Trim(),
            ReceiptFooter = "Conservez ce reçu. Son authenticité peut être vérifiée en scannant le code QR.",
        };
        db.Schools.Add(school);
        if (input.LastOnTimeDay is < 1 or > 28)
            throw new DomainException("echeance", "Le dernier jour autorisé sans retard doit être compris entre 1 et 28.");
        var count = input.EndMonth is { } endMonth
            ? AcademicPeriod.CountBetween(input.StartMonth, endMonth)
            : input.MonthCount;
        var year = SchoolService.BuildYear(input.YearLabel, input.StartMonth, count, input.LastOnTimeDay + 1);
        year.IsCurrent = true;
        db.AcademicYears.Add(year);
        var admin = new AppUser
        {
            UserName = userName, NormalizedUserName = TextKey.Normalize(userName), DisplayName = input.AdminDisplayName.Trim(),
            Role = Role.Secretariat, PasswordHash = passwords.Hash(input.AdminPassword),
        };
        db.Users.Add(admin);
        var others = new List<AppUser>();
        foreach (var account in input.Accounts ?? [])
        {
            // Comptes supplémentaires de l'assistant : un autre poste de secrétariat ou la comptabilité.
            var name = (account.UserName ?? string.Empty).Trim().ToLowerInvariant();
            new ValidationBuilder()
                .Require(account.Role is "Secretariat" or "Comptabilite", "accounts", "Rôle de compte : secrétariat ou comptabilité uniquement.")
                .Require(name.Length is >= 3 and <= 40 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'), "accounts", "Identifiant de 3 à 40 caractères.")
                .Require(!string.IsNullOrWhiteSpace(account.DisplayName), "accounts", "Nom du compte obligatoire.")
                .Require(others.All(o => o.NormalizedUserName != TextKey.Normalize(name)) && name != userName, "accounts", "Deux comptes ne peuvent pas porter le même identifiant.")
                .ThrowIfInvalid();
            var role = account.Role == "Secretariat" ? Role.Secretariat : Role.Comptabilite;
            PasswordPolicy.Validate(account.Password);
            var user = new AppUser
            {
                UserName = name, NormalizedUserName = TextKey.Normalize(name), DisplayName = account.DisplayName.Trim(),
                Role = role, PasswordHash = passwords.Hash(account.Password),
            };
            others.Add(user);
            db.Users.Add(user);
        }
        audit.Record("installation", "Etablissement", school.Id.ToString(), $"Installation de SCOLIA pour {school.Name} ({year.Label}).");
        if (input.LoadDemoData) DemoData.Populate(db, year, clock.Today);
        await db.SaveChangesAsync(ct);
        return admin;
    }, ct);
}

/// <summary>Jeu de données fictif et cohérent pour découvrir le logiciel (aucune personne réelle).</summary>
public static class DemoData
{
    private static readonly string[] LastNames =
        ["MUKENDI", "KASONGO", "ILUNGA", "MBUYI", "TSHIBANDA", "KABAMBA", "NGOY", "LUKUSA", "MWAMBA", "KALONJI", "BANZA", "MULUMBA", "KAPINGA", "NZUZI",
         "MAKIESE", "LUBAMBA", "MATONDO", "NSIMBA", "KIBALA", "MASIALA", "LUZOLO", "NKOSI", "BOKETSHU", "MPIANA", "KAYEMBE"];
    private static readonly string[] MiddleNames =
        ["KABEYA", "MBOMBO", "TSHIMANGA", "KAZADI", "NDAYE", "MUJINGA", "KANKU", "BIAYA", "MUAMBA", "KALALA", "LWAMBA", "NGALULA", "KITENGE", "MPOYI"];
    private static readonly string[] MaleFirst = ["Patrick", "Joël", "Christian", "Daniel", "Josué", "Emmanuel", "Fiston", "Gloire", "Exaucé", "Héritier", "Jonathan", "Samuel"];
    private static readonly string[] FemaleFirst = ["Grâce", "Divine", "Esther", "Ruth", "Merveille", "Bénédicte", "Sarah", "Prisca", "Dorcas", "Rachel", "Naomi", "Plamedie"];

    public static void Populate(IAppDbContext db, AcademicYear year, DateOnly today)
    {
        var random = new Random(2026);
        var nursing = new Section { Code = "INF", Name = "Sciences infirmières" };
        var midwifery = new Section { Code = "ACC", Name = "Accoucheuse" };
        var lab = new Section { Code = "TLM", Name = "Techniques de laboratoire" };
        db.Sections.AddRange(nursing, midwifery, lab);
        var specs = new (string Code, string Name, Section Section, int Level, string? Next, decimal Fee, int Students)[]
        {
            ("INF1", "Première année", nursing, 1, "INF2", 55000, 28), ("INF2", "Deuxième année", nursing, 2, "INF3", 55000, 24),
            ("INF3", "Troisième année", nursing, 3, null, 55000, 20), ("ACC1", "Première année", midwifery, 1, "ACC2", 55000, 22),
            ("ACC2", "Deuxième année", midwifery, 2, null, 55000, 18), ("TLM1", "Première année", lab, 1, null, 55000, 16),
        };
        var counters = new Dictionary<string, long>();
        var receiptNo = 0L;
        var cashier = "Démonstration";
        var assessmentsByPromotion = new Dictionary<Guid, List<Assessment>>();
        foreach (var spec in specs)
        {
            var promotion = new Promotion
            {
                AcademicYearId = year.Id, Code = spec.Code, Name = spec.Name, SectionId = spec.Section.Id, Level = spec.Level, NextPromotionCode = spec.Next, Capacity = 40,
            };
            promotion.Fees.AddRange(SchoolService.MonthlyFees(promotion.Id, year, spec.Fee));
            promotion.Fees.Add(new FeeItem { PromotionId = promotion.Id, Kind = FeeKind.Inscription, Label = "Frais d'inscription", Amount = 25000, DueDate = year.StartDate.AddDays(14), Order = 0 });
            if (spec.Level == 3 || spec.Code == "ACC2")
                promotion.Fees.Add(new FeeItem { PromotionId = promotion.Id, Kind = FeeKind.Examen, Label = "Frais d'examen d'État", Amount = 40000, DueDate = year.EndDate.AddDays(-45), Order = 101 });
            db.Promotions.Add(promotion);

            var course = 0;
            foreach (var (name, coefficient) in new[] { ("Anatomie et physiologie", 4m), ("Soins infirmiers", 5m), ("Pharmacologie", 3m), ("Hygiène et santé publique", 2m), ("Français", 2m), ("Éthique professionnelle", 1m) })
            {
                var c = new Course { AcademicYearId = year.Id, PromotionId = promotion.Id, Number = ++course, Name = name, Coefficient = coefficient, Hours = coefficient * 15, MaxScore = 20 };
                db.Courses.Add(c);
                if (today > year.StartDate.AddDays(30))
                {
                    var assessment = new Assessment { CourseId = c.Id, Title = "Interrogation 1", Kind = AssessmentKind.Interrogation, Term = 1, Date = year.StartDate.AddDays(28), MaxScore = 20 };
                    db.Assessments.Add(assessment);
                    assessmentsByPromotion.TryAdd(promotion.Id, []);
                    assessmentsByPromotion[promotion.Id].Add(assessment);
                }
            }

            for (var i = 0; i < spec.Students; i++)
            {
                var female = random.Next(100) < 70;
                var student = new Student
                {
                    LastName = LastNames[random.Next(LastNames.Length)], MiddleName = MiddleNames[random.Next(MiddleNames.Length)],
                    FirstName = female ? FemaleFirst[random.Next(FemaleFirst.Length)] : MaleFirst[random.Next(MaleFirst.Length)],
                    Gender = female ? Gender.F : Gender.M, BirthDate = new DateOnly(today.Year - 18 - spec.Level - random.Next(0, 5), random.Next(1, 13), random.Next(1, 28)),
                    BirthPlace = new[] { "Kinshasa", "Lubumbashi", "Mbuji-Mayi", "Kananga", "Kisangani", "Matadi" }[random.Next(6)], Nationality = "Congolaise",
                    Address = $"Avenue {LastNames[random.Next(LastNames.Length)].ToLowerInvariant()} n° {random.Next(1, 200)}, Kinshasa",
                    GuardianName = $"{LastNames[random.Next(LastNames.Length)]} {MaleFirst[random.Next(MaleFirst.Length)]}", GuardianRelation = random.Next(2) == 0 ? "Père" : "Mère",
                    GuardianPhone = $"08{random.Next(1, 10)}{random.Next(1000000, 9999999)}",
                };
                counters[spec.Code] = counters.GetValueOrDefault(spec.Code) + 1;
                student.Matricule = $"{spec.Code}{counters[spec.Code]:00}";
                student.RefreshKeys();
                db.Students.Add(student);
                var enrollment = new Enrollment { StudentId = student.Id, AcademicYearId = year.Id, PromotionId = promotion.Id, EnrolledOn = year.StartDate.AddDays(random.Next(0, 20)), IsReEnrollment = spec.Level > 1 };
                db.Enrollments.Add(enrollment);

                // Paiements réalistes : la plupart à jour, certains en retard d'un à trois mois.
                var behaviour = random.Next(100);
                var dueLines = promotion.Fees.Where(f => f.DueDate <= today.AddDays(20)).OrderBy(f => f.DueDate).ThenBy(f => f.Order).ToList();
                var skip = behaviour < 60 ? 0 : behaviour < 80 ? 1 : behaviour < 92 ? 2 : 3;
                var payable = dueLines.Take(Math.Max(0, dueLines.Count - skip)).ToList();
                if (i % 9 == 4)
                {
                    var sponsorship = new Sponsorship { EnrollmentId = enrollment.Id, SponsorName = "Fondation Espoir Santé", Reason = "Bourse d'excellence", EffectiveFrom = year.StartDate };
                    foreach (var fee in promotion.Fees.Where(f => f.Kind == FeeKind.Mensualite)) sponsorship.Lines.Add(new SponsorshipLine { SponsorshipId = sponsorship.Id, FeeItemId = fee.Id, Percent = 50 });
                    db.Sponsorships.Add(sponsorship);
                    payable = [];
                    foreach (var fee in dueLines)
                        if (fee.Kind == FeeKind.Mensualite) AddReceipt(db, year, enrollment, student, [(fee, fee.Amount / 2)], fee.DueDate.AddDays(-random.Next(1, 10)), today, ref receiptNo, cashier, random);
                        else AddReceipt(db, year, enrollment, student, [(fee, fee.Amount)], fee.DueDate.AddDays(-3), today, ref receiptNo, cashier, random);
                }
                foreach (var fee in payable)
                {
                    var partial = behaviour >= 95 && fee == payable[^1];
                    AddReceipt(db, year, enrollment, student, [(fee, partial ? fee.Amount - 20000 : fee.Amount)], fee.DueDate.AddDays(-random.Next(0, 12)), today, ref receiptNo, cashier, random);
                }
                if (assessmentsByPromotion.TryGetValue(promotion.Id, out var assessments))
                    foreach (var a in assessments)
                        db.Grades.Add(new Grade { AssessmentId = a.Id, EnrollmentId = enrollment.Id, Score = random.Next(100) < 5 ? null : Math.Round((decimal)(8 + random.NextDouble() * 11), 1) });
                for (var d = 1; d <= 10; d++)
                {
                    var day = today.AddDays(-d);
                    if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || day < year.StartDate) continue;
                    var roll = random.Next(100);
                    db.Attendance.Add(new AttendanceRecord { EnrollmentId = enrollment.Id, Date = day, Status = roll < 88 ? AttendanceStatus.Present : roll < 94 ? AttendanceStatus.Retard : roll < 97 ? AttendanceStatus.AbsenceJustifiee : AttendanceStatus.Absent });
                }
            }
        }
        db.Counters.Add(new Counter { Name = $"RC-{year.FirstCalendarYear}", Value = receiptNo });

        var expenseNo = 0;
        foreach (var (category, beneficiary, description, amount, offset) in new[]
        {
            ("Fournitures", "Papeterie La Gombe", "Rames de papier et cartouches d'encre", 180000m, 40), ("Électricité et eau", "SNEL", "Facture d'électricité", 95000m, 30),
            ("Entretien et réparations", "Atelier Mwana", "Réparation des tables du laboratoire", 120000m, 20), ("Transport", "Chauffeur de l'école", "Carburant pour les stages", 60000m, 10),
            ("Communication", "Opérateur mobile", "Forfait internet du secrétariat", 45000m, 5),
        })
        {
            var date = today.AddDays(-offset);
            if (date < year.StartDate) date = year.StartDate;
            db.Expenses.Add(new Expense
            {
                Number = DocumentNumbers.Format("DP", year.FirstCalendarYear, ++expenseNo), Date = date, AcademicYearId = year.Id, Category = category,
                Beneficiary = beneficiary, Description = description, Amount = amount, Method = PaymentMethod.Especes, RecordedByName = cashier,
            });
        }
        db.Counters.Add(new Counter { Name = $"DP-{year.FirstCalendarYear}", Value = expenseNo });

        var staffNo = 0;
        foreach (var (last, first, gender, function, salary) in new[]
        {
            ("KASONGO", "Pierre", Gender.M, "Directeur", 850000m), ("MBUYI", "Thérèse", Gender.F, "Secrétaire", 450000m),
            ("ILUNGA", "Jean", Gender.M, "Enseignant", 520000m), ("NGOY", "Marie", Gender.F, "Enseignante", 520000m), ("BANZA", "Paul", Gender.M, "Comptable", 480000m),
        })
            db.Staff.Add(new StaffMember { Number = $"PER-{++staffNo:000}", LastName = last, MiddleName = "", FirstName = first, Gender = gender, Function = function, HireDate = year.StartDate.AddYears(-3), BaseSalary = salary, Phone = $"0810000{staffNo:000}" });
    }

    private static void AddReceipt(IAppDbContext db, AcademicYear year, Enrollment enrollment, Student student, (FeeItem Fee, decimal Amount)[] parts, DateOnly date,
        DateOnly today, ref long number, string cashier, Random random)
    {
        if (date > today) date = today.AddDays(-random.Next(1, 12));
        if (date < year.StartDate) date = year.StartDate;
        var receipt = new Receipt
        {
            Number = DocumentNumbers.Format("RC", year.FirstCalendarYear, ++number), VerificationCode = DocumentNumbers.NewVerificationCode(), Date = date,
            StudentId = student.Id, EnrollmentId = enrollment.Id, AcademicYearId = year.Id, Amount = parts.Sum(p => p.Amount),
            Method = random.Next(100) < 75 ? PaymentMethod.Especes : PaymentMethod.MobileMoney, CashierName = cashier,
        };
        if (receipt.Method == PaymentMethod.MobileMoney) receipt.Reference = $"MP{random.Next(10000000, 99999999)}";
        foreach (var (fee, amount) in parts) receipt.Allocations.Add(new ReceiptAllocation { ReceiptId = receipt.Id, FeeItemId = fee.Id, Amount = amount });
        db.Receipts.Add(receipt);
    }
}
