namespace MyBudget.Core.Tests;

[TestClass]
public sealed class MonthlyIncomePlannerTests
{
    private static readonly DateOnly JulyFirst = new(2026, 7, 1);

    [TestMethod]
    public void Plan_CreatesManagedRemainderWithoutChangingOtherIncome()
    {
        var salary = Income(2_000m, "Salary");
        var freelance = Income(500m, "Freelance");

        var plan = MonthlyIncomePlanner.Plan([salary, freelance], 3_000m);

        Assert.IsNull(plan.ManagedTransaction);
        Assert.AreEqual(2_500m, plan.OtherIncomeTotal);
        Assert.AreEqual(500m, plan.ManagedAmount);
        Assert.IsFalse(plan.ShouldDeleteManaged);
    }

    [TestMethod]
    public void Plan_UpdatesOnlyTheManagedIncomeEntry()
    {
        var managed = Income(3_000m, MonthlyIncomePlanner.ManagedTransactionNote);
        var bonus = Income(400m, "Bonus");

        var plan = MonthlyIncomePlanner.Plan([managed, bonus], 4_000m);

        Assert.AreEqual(managed, plan.ManagedTransaction);
        Assert.AreEqual(400m, plan.OtherIncomeTotal);
        Assert.AreEqual(3_600m, plan.ManagedAmount);
    }

    [TestMethod]
    public void Plan_PreservesOneUnmarkedIncomeEntry()
    {
        var salary = Income(3_000m, "Salary");

        var plan = MonthlyIncomePlanner.Plan([salary], 3_500m);

        Assert.IsNull(plan.ManagedTransaction);
        Assert.AreEqual(3_000m, plan.OtherIncomeTotal);
        Assert.AreEqual(500m, plan.ManagedAmount);
    }

    [TestMethod]
    public void Plan_DeletesManagedEntryWhenOtherIncomeAlreadyMatchesDesiredTotal()
    {
        var managed = Income(3_000m, MonthlyIncomePlanner.ManagedTransactionNote);
        var replacement = Income(3_000m, "Imported salary");

        var plan = MonthlyIncomePlanner.Plan([managed, replacement], 3_000m);

        Assert.AreEqual(0m, plan.ManagedAmount);
        Assert.IsTrue(plan.ShouldDeleteManaged);
    }

    [TestMethod]
    public void Plan_RejectsTotalBelowUnrelatedIncome()
    {
        var otherIncome = Income(800m, "Side work");

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MonthlyIncomePlanner.Plan([otherIncome], 799.99m));

        StringAssert.Contains(exception.Message, "other income entries");
    }

    [TestMethod]
    public void Plan_TreatsOnlyExactAppManagedNoteAsManaged()
    {
        var exact = Income(1_000m, MonthlyIncomePlanner.ManagedTransactionNote);
        var similar = Income(200m, "monthly income");

        var plan = MonthlyIncomePlanner.Plan([similar, exact], 1_500m);

        Assert.AreEqual(exact.Id, plan.ManagedTransaction?.Id);
        Assert.AreEqual(200m, plan.OtherIncomeTotal);
        Assert.AreEqual(1_300m, plan.ManagedAmount);
    }

    private static BudgetTransaction Income(decimal amount, string note) => new(
        Guid.NewGuid(),
        JulyFirst,
        TransactionType.Income,
        amount,
        null,
        note);
}
