using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Tests;

public sealed class CentralSectionOfflineStoreTests
{
    private static readonly Uri Origin = new("https://central-staging.example/");
    private static readonly Guid SectionId = Guid.Parse("92e7af16-4ea5-4ffb-88d5-2c8e07ba5491");

    [Fact]
    public void Sections_et_operations_survivent_au_redemarrage_sans_perte_du_curseur()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var op = new CentralSectionChange(Guid.NewGuid(), SectionId,
                "INF", "Infirmière", true, 0);
            var one = new CentralSectionOfflineStore(Origin, "school-1", "teacher", folder);
            one.Enqueue(op);
            one.ApplyPage(0, new CentralSectionPage([
                new CentralSectionEvent(8, SectionId, "INF", "Infirmière", true),
            ], 8, false));

            var second = new CentralSectionOfflineStore(Origin, "school-1", "teacher", folder);
            Assert.Equal(8, second.Snapshot().Revision);
            Assert.Single(second.Snapshot().Sections);
            Assert.Equal(op.OperationId, Assert.Single(second.Snapshot().Pending).Change.OperationId);
            second.Confirm(new CentralSectionReceipt(op.OperationId, 8, false));

            var third = new CentralSectionOfflineStore(Origin, "school-1", "teacher", folder);
            Assert.Empty(third.Snapshot().Pending);
            Assert.Equal(8, third.Snapshot().Revision);

            // Les données de deux comptes ou établissements sont séparées.
            var another = new CentralSectionOfflineStore(Origin, "school-1", "other-user", folder);
            var differentSchool = new CentralSectionOfflineStore(Origin, "school-2", "teacher", folder);
            Assert.Equal(0, another.Snapshot().Revision);
            Assert.Equal(0, differentSchool.Snapshot().Revision);
            Assert.Empty(another.Snapshot().Sections);
            Assert.Empty(differentSchool.Snapshot().Sections);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Transaction_invalide_ne_fait_pas_avancer_le_curseur()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CentralSectionOfflineStore(Origin, "school-1", "teacher", folder);
            var op = new CentralSectionChange(Guid.NewGuid(), SectionId,
                "INF", "Infirmière", true, 0);
            store.Enqueue(op);
            Assert.Throws<InvalidDataException>(() => store.ApplyPage(0,
                new CentralSectionPage([
                    new CentralSectionEvent(6, SectionId, "INF", "Infirmière", true),
                ], 7, false)));
            Assert.Equal(0, store.Snapshot().Revision);
            Assert.Empty(store.Snapshot().Sections);
            Assert.Single(store.Snapshot().Pending);

            store.ApplyPage(0, new CentralSectionPage([
                new CentralSectionEvent(6, SectionId, "INF", "Infirmière", true),
            ], 6, false));
            Assert.Throws<InvalidOperationException>(() => store.ApplyPage(0,
                new CentralSectionPage([], 0, false)));
            Assert.Equal(6, store.Snapshot().Revision);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Conflit_ne_supprime_pas_l_operation_et_reste_apres_redemarrage()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var op = new CentralSectionChange(Guid.NewGuid(), SectionId,
                "ACC", "Accoucheuse", true, 0);
            var store = new CentralSectionOfflineStore(Origin, "school-1", "secretary", folder);
            store.Enqueue(op);
            store.MarkConflict(op.OperationId);
            var reopened = new CentralSectionOfflineStore(Origin, "school-1", "secretary", folder);
            Assert.True(Assert.Single(reopened.Snapshot().Pending).Conflict);
            Assert.Equal(op.OperationId, reopened.Snapshot().Pending[0].Change.OperationId);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
