using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;
using Scolia.Domain.School;

namespace Scolia.Tests;

public sealed class CurriculumStructureTests
{
    private static AcademicYear Year() => new()
    {
        Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1),
        EndDate = new DateOnly(2027, 6, 30),
    };

    [Fact]
    public void Existing_itm_cursus_stays_untouched_by_new_program_models()
    {
        var year = Year();
        CurriculumStructureRules.ValidateCurriculum(InstitutionProfiles.ItmIm,
            year, Array.Empty<CurriculumItem>(), Array.Empty<AcademicStructureUnit>());

        var item = Annual(year, Guid.NewGuid());
        Assert.Throws<DomainException>(() => CurriculumStructureRules.ValidateCurriculum(
            InstitutionProfiles.ItmIm, year, new[] { item }, Array.Empty<AcademicStructureUnit>()));
    }

    [Theory]
    [InlineData("Primaire")]
    [InlineData("Secondaire")]
    public void Primary_and_secondary_use_only_annual_subjects(string profile)
    {
        var year = Year();
        var promotion = Guid.NewGuid();
        var subject = Annual(year, promotion);
        CurriculumStructureRules.ValidateCurriculum(profile, year, new[] { subject }, []);

        subject.Semester = CourseSemester.S1;
        Assert.Throws<DomainException>(() =>
            CurriculumStructureRules.ValidateCurriculum(profile, year, new[] { subject }, []));
        subject.Semester = CourseSemester.Annuel;
        subject.Credits = 3;
        Assert.Throws<DomainException>(() =>
            CurriculumStructureRules.ValidateCurriculum(profile, year, new[] { subject }, []));
    }

    [Fact]
    public void University_has_faculty_department_program_and_ue_ec_by_semester()
    {
        var year = Year();
        var promotion = Guid.NewGuid();
        var (units, filiere) = Hierarchy();
        var ue = Unit(year, promotion, filiere.Id, CourseSemester.S1, credits: 6);
        var ec1 = Element(ue, "EC01", 2);
        var ec2 = Element(ue, "EC02", 4);

        CurriculumStructureRules.ValidateCurriculum(InstitutionProfiles.SuperieurLmd,
            year, new[] { ue, ec1, ec2 }, units);

        Assert.Throws<DomainException>(() => CurriculumStructureRules.ValidateCurriculum(
            InstitutionProfiles.Primaire, year, new[] { ue, ec1, ec2 }, units));
    }

    [Fact]
    public void Lmd_refuses_annual_courses_and_invalid_ue_credits()
    {
        var year = Year();
        var (units, filiere) = Hierarchy();
        var ue = Unit(year, Guid.NewGuid(), filiere.Id, CourseSemester.S1, credits: 6);
        var ec = Element(ue, "EC01", 5);
        Assert.Throws<DomainException>(() =>
            CurriculumStructureRules.ValidateCurriculum(InstitutionProfiles.SuperieurLmd, year,
                new[] { ue, ec }, units));

        ec.Credits = 6;
        ue.Semester = CourseSemester.Annuel;
        Assert.Throws<DomainException>(() =>
            CurriculumStructureRules.ValidateCurriculum(InstitutionProfiles.SuperieurLmd, year,
                new[] { ue, ec }, units));
    }

    [Fact]
    public void Lmd_rejects_orphan_element_or_cross_semester_element()
    {
        var year = Year();
        var (units, filiere) = Hierarchy();
        var ue = Unit(year, Guid.NewGuid(), filiere.Id, CourseSemester.S1, credits: 6);
        var ec = Element(ue, "EC01", 6);

        ec.ParentItemId = Guid.NewGuid();
        Assert.Throws<DomainException>(() =>
            CurriculumStructureRules.ValidateCurriculum(InstitutionProfiles.SuperieurLmd, year,
                new[] { ue, ec }, units));
        ec.ParentItemId = ue.Id;
        ec.Semester = CourseSemester.S2;
        Assert.Throws<DomainException>(() =>
            CurriculumStructureRules.ValidateCurriculum(InstitutionProfiles.SuperieurLmd, year,
                new[] { ue, ec }, units));
    }

    [Fact]
    public void Hierarchy_rejects_filiere_without_department_and_duplicate_codes()
    {
        var (nodes, filiere) = Hierarchy();
        filiere.ParentId = nodes[0].Id;
        Assert.Throws<DomainException>(() =>
            CurriculumStructureRules.ValidateHierarchy(InstitutionProfiles.SuperieurLmd, nodes));

        filiere.ParentId = nodes[1].Id;
        nodes.Add(new AcademicStructureUnit
        {
            Kind = AcademicUnitKind.Filiere, ParentId = filiere.ParentId,
            Code = filiere.Code, Name = "Filière en doublon",
        });
        Assert.Throws<DomainException>(() =>
            CurriculumStructureRules.ValidateHierarchy(InstitutionProfiles.SuperieurLmd, nodes));
    }

    private static CurriculumItem Annual(AcademicYear year, Guid promotion) => new()
    {
        AcademicYearId = year.Id, PromotionId = promotion, Code = "MAT",
        Name = "Mathématiques", Kind = CurriculumItemKind.AnnualSubject,
        Semester = CourseSemester.Annuel, Coefficient = 2,
        Hours = 120, Credits = 0,
    };

    private static (List<AcademicStructureUnit> Nodes, AcademicStructureUnit Filiere) Hierarchy()
    {
        var faculty = new AcademicStructureUnit
        {
            Kind = AcademicUnitKind.Faculty, Code = "SCI", Name = "Sciences",
        };
        var department = new AcademicStructureUnit
        {
            Kind = AcademicUnitKind.Department, Code = "INFO",
            Name = "Informatique", ParentId = faculty.Id,
        };
        var filiere = new AcademicStructureUnit
        {
            Kind = AcademicUnitKind.Filiere, Code = "MSI",
            Name = "Management des systèmes d'information", ParentId = department.Id,
        };
        return (new List<AcademicStructureUnit> { faculty, department, filiere }, filiere);
    }

    private static CurriculumItem Unit(AcademicYear year, Guid promotion, Guid filiere,
        CourseSemester semester, decimal credits) => new()
    {
        AcademicYearId = year.Id, PromotionId = promotion, AcademicUnitId = filiere,
        Code = "UE01", Name = "Fondements informatiques",
        Kind = CurriculumItemKind.TeachingUnit, Semester = semester,
        Credits = credits, Coefficient = 1, Hours = 60,
    };

    private static CurriculumItem Element(CurriculumItem ue, string code, decimal credits) => new()
    {
        AcademicYearId = ue.AcademicYearId, PromotionId = ue.PromotionId,
        AcademicUnitId = ue.AcademicUnitId, ParentItemId = ue.Id,
        Code = code, Name = "Élément de cours", Kind = CurriculumItemKind.TeachingElement,
        Semester = ue.Semester, Credits = credits, Hours = 30,
    };
}
