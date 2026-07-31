namespace MyBudget.Core.Tests;

[TestClass]
public sealed class ModelTests
{
    [TestMethod]
    public void SavingsGoal_ComputesProgressAndDoesNotReportNegativeRemainder()
    {
        var inProgress = new SavingsGoal(1, "Emergency fund", 10_000m, 2_500m);
        var exceeded = new SavingsGoal(2, "Holiday", 1_000m, 1_200m);

        Assert.AreEqual(7_500m, inProgress.RemainingAmount);
        Assert.AreEqual(25m, inProgress.PercentComplete);
        Assert.AreEqual(0m, exceeded.RemainingAmount);
        Assert.AreEqual(120m, exceeded.PercentComplete);
    }

    [TestMethod]
    public void CategoryProgress_HandlesNoPlanWithoutDividingByZero()
    {
        var category = new BudgetCategory(1, "Food", CategoryKind.Expense, "#F97316");
        var progress = new CategoryProgress(category, 0m, 50m);

        Assert.AreEqual(0m, progress.PercentUsed);
        Assert.AreEqual(0m, progress.ChartPercent);
        Assert.IsTrue(progress.IsOverBudget);
    }

    [TestMethod]
    public void EmptySnapshot_UsesSafeDefaults()
    {
        var snapshot = BudgetSnapshot.Empty(new BudgetMonth(2026, 7));

        Assert.IsEmpty(snapshot.Categories);
        Assert.IsEmpty(snapshot.Transactions);
        Assert.AreEqual("MYR", snapshot.Settings.CurrencyCode);
        Assert.IsFalse(snapshot.Settings.IsDarkMode);
    }
}
