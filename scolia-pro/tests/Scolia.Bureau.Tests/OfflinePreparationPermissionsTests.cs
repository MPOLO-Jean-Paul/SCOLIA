using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Le cache silencieux respecte les permissions du compte.</summary>
public sealed class OfflinePreparationPermissionsTests
{
    [Fact]
    public void Enseignant_sans_droit_aux_noms_charge_ses_feuilles_anonymes()
    {
        Assert.Equal(OfflinePreparationKind.AnonymousTeacherSheets,
            OfflinePreparation.KindForPermissions([Perm.MarksEncode]));
        Assert.Equal(OfflinePreparationKind.AnonymousTeacherSheets,
            OfflinePreparation.KindForPermissions([Perm.JuryEncode]));
    }

    [Fact]
    public void Secretaire_conserve_le_registre_local()
    {
        Assert.Equal(OfflinePreparationKind.StudentRecords,
            OfflinePreparation.KindForPermissions([Perm.StudentsRead, Perm.StudentsWrite, Perm.PaymentsRead]));
    }

    [Fact]
    public void Professeur_non_autorise_ne_recoit_aucune_fiche()
    {
        Assert.Equal(OfflinePreparationKind.None,
            OfflinePreparation.KindForPermissions([Perm.OwnResultsRead]));
        Assert.Equal(OfflinePreparationKind.None,
            OfflinePreparation.KindForPermissions(Array.Empty<string>()));
    }

    [Fact]
    public void Les_droits_sont_invariables_a_la_casse_et_la_lecture_nominative_prime()
    {
        Assert.Equal(OfflinePreparationKind.AnonymousTeacherSheets,
            OfflinePreparation.KindForPermissions(["COTES.ENCODER"]));
        Assert.Equal(OfflinePreparationKind.StudentRecords | OfflinePreparationKind.AnonymousTeacherSheets,
            OfflinePreparation.KindForPermissions([Perm.StudentsRead, Perm.MarksEncode]));
    }
}
