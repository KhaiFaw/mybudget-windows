namespace MyBudget.Core.Tests;

[TestClass]
public sealed class BudgetMonthTests
{
    [TestMethod]
    public void Constructor_RejectsYearOutsideDateOnlyRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetMonth(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetMonth(10_000, 1));
    }

    [TestMethod]
    public void Constructor_RejectsInvalidMonth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetMonth(2026, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetMonth(2026, 13));
    }

    [TestMethod]
    public void DefaultValue_IsRejectedAtCalculationBoundaries()
    {
        var month = default(BudgetMonth);

        Assert.IsFalse(month.IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = month.FirstDay);
        Assert.Throws<ArgumentOutOfRangeException>(() => month.Contains(new DateOnly(2026, 7, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => month.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BudgetCalculator.Calculate(BudgetSnapshot.Empty(month)));
    }

    [TestMethod]
    public void FirstAndLastDay_UseCalendarBoundaries()
    {
        var month = new BudgetMonth(2024, 2);

        Assert.AreEqual(new DateOnly(2024, 2, 1), month.FirstDay);
        Assert.AreEqual(new DateOnly(2024, 2, 29), month.LastDay);
    }

    [TestMethod]
    public void NextAndPrevious_CrossYearBoundaries()
    {
        Assert.AreEqual(new BudgetMonth(2027, 1), new BudgetMonth(2026, 12).Next);
        Assert.AreEqual(new BudgetMonth(2025, 12), new BudgetMonth(2026, 1).Previous);
    }

    [TestMethod]
    public void Contains_MatchesOnlyDatesInsideMonth()
    {
        var month = new BudgetMonth(2026, 7);

        Assert.IsTrue(month.Contains(new DateOnly(2026, 7, 31)));
        Assert.IsFalse(month.Contains(new DateOnly(2026, 6, 30)));
    }

    [TestMethod]
    public void FromDateAndToString_UseStableYearMonthFormat()
    {
        var month = BudgetMonth.FromDate(new DateOnly(2026, 3, 18));

        Assert.AreEqual(new BudgetMonth(2026, 3), month);
        Assert.AreEqual("2026-03", month.ToString());
    }
}
