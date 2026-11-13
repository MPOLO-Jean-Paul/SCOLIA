using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;
using Scolia.Domain.School;

namespace Scolia.Application.Services;

/// <summary>
/// Paramétrage non destructif des structures et maquettes du LMD.
/// Ce service n'émet ni relevé officiel, ni décision de jury, ni droit de perception.
/// </summary>
public sealed class LmdPlanningService(IAppDbContext db, IWriteGate gate, IAuditLog audit)
{
    private async Task RequireLmdAsync(CancellationToken ct)
    {
        var school = await db.Schools.AsNoTracking().OrderBy(s => s.CreatedAtUtc).FirstAsync(ct);
        if (InstitutionProfiles.Canonicalize(school.InstitutionType) != InstitutionProfiles.SuperieurLmd)
            throw new DomainException("lmd", "Ce module est réservé aux établissements supérieurs LMD.");
    }

    private static LmdStructureDto StructureDto(AcademicStructureUnit unit) =>
        new(unit.Id, unit.Kind.ToString(), unit.ParentId, unit.Code, unit.Name, unit.IsActive);

    private static LmdSemesterDto SemesterDto(PedagogicalPeriod p) =>
        new(p.Id, p.Code, p.Name, p.Order, p.StartDate, p.EndDate);

    private static LmdProgramItemDto ProgramDto(CurriculumItem item) =>
        new(item.Id, item.PromotionId, item.AcademicUnitId, item.ParentItemId,
            item.Kind.ToString(), item.Code, item.Name, item.Semester.ToString(),
            item.Credits, item.Coefficient, item.Hours);

    public async Task<IReadOnlyList<LmdStructureDto>> StructuresAsync(CancellationToken ct = default)
    {
        await RequireLmdAsync(ct);
        var units = await db.AcademicStructureUnits.AsNoTracking()
            .OrderBy(u => u.Kind).ThenBy(u => u.Code).ToListAsync(ct);
        return units.Select(StructureDto).ToList();
    }

    public Task<LmdStructureDto> AddStructureAsync(LmdStructureInput input,
        CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        await RequireLmdAsync(ct);
        if (!Enum.TryParse<AcademicUnitKind>(input.Kind, true, out var kind) || !Enum.IsDefined(kind))
            throw new DomainException("lmd", "Type de structure LMD inconnu.");
        var nodes = await db.AcademicStructureUnits.AsNoTracking().ToListAsync(ct);
        var unit = new AcademicStructureUnit
        {
            ParentId = input.ParentId, Kind = kind,
            Code = (input.Code ?? string.Empty).Trim().ToUpperInvariant(),
            Name = (input.Name ?? string.Empty).Trim(),
        };
        CurriculumStructureRules.ValidateHierarchy(InstitutionProfiles.SuperieurLmd, [..nodes, unit]);
        db.AcademicStructureUnits.Add(unit);
        audit.Record("lmd.structure.creee", "UniteAcademique", unit.Id.ToString(),
            $"Structure {unit.Kind} {unit.Code} : {unit.Name}.");
        await db.SaveChangesAsync(ct);
        return StructureDto(unit);
    }, ct);

    public async Task<IReadOnlyList<LmdSemesterDto>> SemestersAsync(CancellationToken ct = default)
    {
        await RequireLmdAsync(ct);
        var year = await YearContext.CurrentAsync(db, ct);
        var list = await db.PedagogicalPeriods.AsNoTracking()
            .Where(p => p.AcademicYearId == year.Id)
            .OrderBy(p => p.Order).ToListAsync(ct);
        return list.Select(SemesterDto).ToList();
    }

    public Task<IReadOnlyList<LmdSemesterDto>> SaveSemestersAsync(
        LmdSemesterCalendarInput input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        await RequireLmdAsync(ct);
        var year = await YearContext.CurrentAsync(db, ct);
        if (year.Status != AcademicYearStatus.Open)
            throw new DomainException("lmd", "Le calendrier d'une année clôturée ne peut pas changer.");
        var existing = await db.PedagogicalPeriods
            .Where(p => p.AcademicYearId == year.Id).ToListAsync(ct);
        if (existing.Count > 2 || existing.Any(p => p.Code is not ("S1" or "S2")))
            throw new DomainException("lmd", "Un ancien calendrier incompatible doit être vérifié avant sa mise à jour.");

        PedagogicalPeriod Period(string code, int order, string name, DateOnly start, DateOnly end)
        {
            var row = existing.FirstOrDefault(p => p.Code == code);
            if (row is null)
            {
                row = new PedagogicalPeriod { AcademicYearId = year.Id };
                db.PedagogicalPeriods.Add(row);
            }
            row.Code = code;
            row.Order = order;
            row.Name = name;
            row.Kind = PedagogicalPeriodKind.Semester;
            row.StartDate = start;
            row.EndDate = end;
            return row;
        }
        var first = Period("S1", 1, "Premier semestre", input.FirstStart, input.FirstEnd);
        var second = Period("S2", 2, "Deuxième semestre", input.SecondStart, input.SecondEnd);
        PedagogicalCalendarRules.Validate(InstitutionProfiles.SuperieurLmd, year, [first, second]);
        audit.Record("lmd.semestres.configures", "CalendrierLmd", year.Id.ToString(),
            "Calendrier S1 et S2 configuré séparément des mois de paiement.");
        await db.SaveChangesAsync(ct);
        return new[] { SemesterDto(first), SemesterDto(second) };
    }, ct);

    public async Task<IReadOnlyList<LmdProgramItemDto>> ProgramAsync(Guid promotionId,
        CancellationToken ct = default)
    {
        await RequireLmdAsync(ct);
        var year = await YearContext.CurrentAsync(db, ct);
        if (!await db.Promotions.AsNoTracking()
                .AnyAsync(p => p.Id == promotionId && p.AcademicYearId == year.Id, ct))
            throw new NotFoundException("Promotion LMD introuvable pour l'année courante.");
        var rows = await db.CurriculumItems.AsNoTracking()
            .Where(i => i.AcademicYearId == year.Id && i.PromotionId == promotionId)
            .OrderBy(i => i.Semester).ThenBy(i => i.Kind).ThenBy(i => i.Code).ToListAsync(ct);
        return rows.Select(ProgramDto).ToList();
    }

    public Task<IReadOnlyList<LmdProgramItemDto>> AddTeachingUnitAsync(
        Guid promotionId, LmdTeachingUnitInput input, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
        {
            await RequireLmdAsync(ct);
            var year = await YearContext.CurrentAsync(db, ct);
            if (year.Status != AcademicYearStatus.Open)
                throw new DomainException("lmd", "Le programme d'une année clôturée ne peut pas changer.");
            if (!await db.Promotions.AnyAsync(p => p.Id == promotionId && p.AcademicYearId == year.Id && p.IsActive, ct))
                throw new NotFoundException("Promotion LMD active introuvable.");
            if (!Enum.TryParse<CourseSemester>(input.Semester, true, out var semester)
                || semester is not (CourseSemester.S1 or CourseSemester.S2))
                throw new DomainException("lmd", "Une UE appartient au semestre S1 ou S2.");
            var nodes = await db.AcademicStructureUnits.AsNoTracking().ToListAsync(ct);
            var existing = await db.CurriculumItems.AsNoTracking()
                .Where(i => i.PromotionId == promotionId && i.AcademicYearId == year.Id).ToListAsync(ct);
            var ue = new CurriculumItem
            {
                AcademicYearId = year.Id, PromotionId = promotionId,
                AcademicUnitId = input.AcademicUnitId, Kind = CurriculumItemKind.TeachingUnit,
                Semester = semester, Code = (input.Code ?? string.Empty).Trim().ToUpperInvariant(),
                Name = (input.Name ?? string.Empty).Trim(), Credits = input.Credits,
                Hours = input.Hours, Coefficient = 1m,
            };
            var ec = new CurriculumItem
            {
                AcademicYearId = year.Id, PromotionId = promotionId,
                AcademicUnitId = input.AcademicUnitId, Kind = CurriculumItemKind.TeachingElement,
                ParentItemId = ue.Id, Semester = semester,
                Code = (input.ElementCode ?? string.Empty).Trim().ToUpperInvariant(),
                Name = (input.ElementName ?? string.Empty).Trim(), Credits = 0,
                Hours = input.ElementHours, Coefficient = 1m,
            };
            CurriculumStructureRules.ValidateCurriculum(InstitutionProfiles.SuperieurLmd,
                year, [..existing, ue, ec], nodes);
            db.CurriculumItems.AddRange(ue, ec);
            audit.Record("lmd.ue.creee", "ProgrammeLmd", ue.Id.ToString(),
                $"UE {ue.Code} / EC {ec.Code} {semester} créée sans délibération officielle.");
            await db.SaveChangesAsync(ct);
            return (IReadOnlyList<LmdProgramItemDto>)new[] { ProgramDto(ue), ProgramDto(ec) };
        }, ct);

    public Task<LmdProgramItemDto> AddTeachingElementAsync(
        Guid promotionId, LmdTeachingElementInput input, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
        {
            await RequireLmdAsync(ct);
            var year = await YearContext.CurrentAsync(db, ct);
            if (year.Status != AcademicYearStatus.Open)
                throw new DomainException("lmd", "Le programme d'une année clôturée ne peut pas changer.");
            var existing = await db.CurriculumItems.AsNoTracking()
                .Where(i => i.AcademicYearId == year.Id && i.PromotionId == promotionId).ToListAsync(ct);
            var ue = existing.FirstOrDefault(i => i.Id == input.ParentItemId && i.Kind == CurriculumItemKind.TeachingUnit)
                ?? throw new NotFoundException("UE parente introuvable.");
            var nodes = await db.AcademicStructureUnits.AsNoTracking().ToListAsync(ct);
            var ec = new CurriculumItem
            {
                AcademicYearId = year.Id, PromotionId = promotionId,
                AcademicUnitId = ue.AcademicUnitId, ParentItemId = ue.Id,
                Kind = CurriculumItemKind.TeachingElement, Semester = ue.Semester,
                Code = (input.Code ?? string.Empty).Trim().ToUpperInvariant(),
                Name = (input.Name ?? string.Empty).Trim(), Credits = 0,
                Hours = input.Hours, Coefficient = 1m,
            };
            CurriculumStructureRules.ValidateCurriculum(InstitutionProfiles.SuperieurLmd,
                year, [..existing, ec], nodes);
            db.CurriculumItems.Add(ec);
            audit.Record("lmd.ec.cree", "ProgrammeLmd", ec.Id.ToString(),
                $"EC {ec.Code} ajouté à l'UE {ue.Code}.");
            await db.SaveChangesAsync(ct);
            return ProgramDto(ec);
        }, ct);
}
