using Scolia.Domain.Common;

namespace Scolia.Domain.Staff;

public sealed class StaffMember : Entity
{
    public string Number { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public Students.Gender Gender { get; set; }
    public string Function { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateOnly HireDate { get; set; }
    /// <summary>Fin de la période d'activité, vide si l'agent est toujours en fonction.</summary>
    public DateOnly? ExitDate { get; set; }
    public decimal BaseSalary { get; set; }
    public bool IsActive { get; set; } = true;

    public string FullName => string.Join(' ', new[] { LastName, MiddleName, FirstName }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public enum PayrollStatus { Brouillon = 0, Valide = 1, Paye = 2 }

/// <summary>Bulletin de paie mensuel.</summary>
public sealed class PayrollEntry : Entity
{
    public Guid StaffMemberId { get; set; }
    /// <summary>Mois de paie au format AAAA-MM.</summary>
    public string Period { get; set; } = string.Empty;
    public decimal BaseSalary { get; set; }
    public decimal Allowances { get; set; }
    public decimal Deductions { get; set; }
    public decimal Advances { get; set; }
    public decimal Net { get; set; }
    public PayrollStatus Status { get; set; }
    public DateOnly? PaidOn { get; set; }
    public string? Note { get; set; }

    public void Recompute()
    {
        if (BaseSalary < 0 || Allowances < 0 || Deductions < 0 || Advances < 0)
            throw new DomainException("paie", "Les montants de paie ne peuvent pas être négatifs.");
        Net = BaseSalary + Allowances - Deductions - Advances;
        if (Net < 0) throw new DomainException("paie", "Le net à payer ne peut pas être négatif.");
    }
}
