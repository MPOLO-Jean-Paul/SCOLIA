using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>
/// Aperçu rapide d'un apprenant, ouvert au clic droit sur une ligne : l'essentiel de son dossier et de son compte
/// dans une petite boîte, sans quitter la liste. Un bouton ouvre la fiche complète.
/// </summary>
public static class QuickView
{
    public static async Task StudentAsync(ShellViewModel shell, Guid studentId)
    {
        var student = await shell.Api.GetAsync<StudentDto>($"api/apprenants/{studentId}");
        var enrollment = student.Enrollments.FirstOrDefault(e => e.Status == "Actif") ?? (student.Enrollments.Count > 0 ? student.Enrollments[0] : null);
        var currency = shell.Session?.CurrencySymbol ?? string.Empty;
        var lines = new List<string>
        {
            $"Matricule {student.Matricule}" + (enrollment is null ? " · sans inscription" : $" · {enrollment.PromotionCode} · {Labels.Status(enrollment.Status)}"),
            $"Parent : {(string.IsNullOrWhiteSpace(student.GuardianName) ? "non renseigné" : student.GuardianName)}"
                + $" · {student.GuardianPhone ?? student.GuardianPhone2 ?? "sans contact"}",
        };
        if (enrollment is not null && shell.Can(Perm.PaymentsRead))
        {
            var account = await shell.Api.GetAsync<AccountDto>($"api/inscriptions/{enrollment.Id}/compte");
            var months = account.Lines.Where(l => l.Kind == "Mensualite").ToList();
            var paid = months.Where(l => l.Remaining <= 0).Select(l => l.Label).ToList();
            var late = months.Where(l => l.IsOverdue).Select(l => l.Label).ToList();
            lines.Add(string.Empty);
            lines.Add($"Reste exigible : {Format.Money(account.Overdue, currency)}" + (late.Count > 0 ? $" ({string.Join(", ", late)})" : " · à jour"));
            lines.Add($"Reste sur l'année : {Format.Money(account.Balance, currency)} · déjà payé {Format.Money(account.TotalPaid, currency)}");
            lines.Add($"Mois réglés : {(paid.Count == 0 ? "aucun" : string.Join(", ", paid))}");
            if (account.Receipts.Count > 0)
            {
                var last = account.Receipts[0];
                lines.Add($"Dernier reçu : {last.Number} du {Format.Date(last.Date)}, {Format.Money(last.Amount, currency)}");
            }
        }
        var fullName = string.Join(' ', new[] { student.LastName, student.MiddleName, student.FirstName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var box = new FormViewModel(fullName, string.Empty, _ => Task.FromResult(true))
        {
            Message = string.Join('\n', lines), SubmitText = "Ouvrir la fiche", CancelText = "Fermer",
        };
        if (await shell.Ask(box))
            await shell.Open(() => new StudentDetailPage(shell, studentId));
    }
}
