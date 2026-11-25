using Scolia.Domain.School;

namespace Scolia.Tests;

public sealed class LmdInstallmentPlanTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Repartition_decroissante_et_total_exact(int count)
    {
        var amounts = LmdInstallmentPlan.Split(601.37m, count);
        Assert.Equal(count, amounts.Count);
        Assert.Equal(601.37m, amounts.Sum());
        Assert.All(amounts, a => Assert.True(a > 0));
        Assert.All(amounts.Zip(amounts.Skip(1)), pair => Assert.True(pair.First > pair.Second));
    }

    [Fact]
    public void Refuse_un_total_insuffisant_ou_des_tranches_invalides()
    {
        Assert.Throws<ArgumentException>(() => LmdInstallmentPlan.Split(0.03m, 3));
        Assert.Throws<ArgumentException>(() => LmdInstallmentPlan.Split(500m, 7));
    }

    [Fact]
    public void Dates_dans_annee_et_repartition_differente_par_promotion()
    {
        var start = new DateOnly(2026, 10, 1);
        var end = new DateOnly(2027, 7, 31);
        var dates = LmdInstallmentPlan.DueDates(start, end, 3);
        Assert.Equal(start, dates[0]);
        Assert.Equal(end, dates[2]);
        Assert.True(dates[1] > start && dates[1] < end);
        Assert.Equal(new decimal[] { 300, 200, 100 }, LmdInstallmentPlan.Split(600, 3));
        Assert.Equal(new decimal[] { 600, 400, 200 }, LmdInstallmentPlan.Split(1200, 3));
    }
}
