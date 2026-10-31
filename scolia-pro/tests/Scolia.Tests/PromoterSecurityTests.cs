using Scolia.Domain.Security;

namespace Scolia.Tests;

public sealed class PromoterSecurityTests
{
    [Fact]
    public void Promoter_role_is_read_only()
    {
        Assert.Contains(Role.Promoteur, Permissions.Assignable);
        var permissions = Permissions.For(Role.Promoteur);
        foreach (var read in new[] {
            Permissions.StudentsRead, Permissions.PaymentsRead,
            Permissions.ReportsRead, Permissions.AcademicsRead
        }) Assert.Contains(read, permissions);
        foreach (var write in new[] {
            Permissions.StudentsWrite, Permissions.PaymentsCreate,
            Permissions.PaymentsCancel, Permissions.ExpensesWrite,
            Permissions.CashClose, Permissions.SettingsWrite, Permissions.UsersManage,
            Permissions.BackupManage, Permissions.StaffManage, Permissions.AcademicsWrite,
            Permissions.DisciplineCancel, Permissions.AuditRead
        }) Assert.DoesNotContain(write, permissions);
    }

    [Fact]
    public void Persisted_legacy_role_numbers_are_never_changed()
    {
        Assert.Equal(2, (int)Role.Secretariat);
        Assert.Equal(3, (int)Role.Comptabilite);
        Assert.Equal(4, (int)Role.Enseignant);
        Assert.Equal(5, (int)Role.Promoteur);
    }
}
