using Scolia.Domain.Security;

namespace Scolia.Tests;

/// <summary>Refus des accès inutiles pour la direction : vérifié indépendamment des boutons.</summary>
public sealed class PromoterRoleTests
{
    [Fact]
    public void Promoteur_est_un_troisieme_role_sans_renumeroter_les_anciens()
    {
        Assert.Equal(2, (int)Role.Secretariat);
        Assert.Equal(3, (int)Role.Comptabilite);
        Assert.Equal(5, (int)Role.Promoteur);
        Assert.Contains(Role.Promoteur, Permissions.Assignable);
        Assert.Equal("Promoteur / Responsable", Permissions.Label(Role.Promoteur));
    }

    [Fact]
    public void Promoteur_ne_peut_lire_que_les_syntheses_agregees()
    {
        var permissions = Permissions.For(Role.Promoteur);
        Assert.Single(permissions);
        Assert.Contains(Permissions.PilotageRead, permissions);

        Assert.DoesNotContain(Permissions.UsersManage, permissions);
        Assert.DoesNotContain(Permissions.SettingsWrite, permissions);
        Assert.DoesNotContain(Permissions.PaymentsRead, permissions);
        Assert.DoesNotContain(Permissions.PaymentsCreate, permissions);
        Assert.DoesNotContain(Permissions.PaymentsCancel, permissions);
        Assert.DoesNotContain(Permissions.StudentsRead, permissions);
        Assert.DoesNotContain(Permissions.StudentsWrite, permissions);
        Assert.DoesNotContain(Permissions.ReportsRead, permissions);
        Assert.DoesNotContain(Permissions.BackupManage, permissions);
        Assert.DoesNotContain(Permissions.StaffManage, permissions);
        Assert.DoesNotContain(Permissions.AcademicsWrite, permissions);
    }

    [Fact]
    public void Secretariat_garde_le_droit_de_consulter_la_synthese()
    {
        Assert.Contains(Permissions.PilotageRead, Permissions.For(Role.Secretariat));
    }
}
