namespace MyBudget.Core.Tests;

[TestClass]
public sealed class CashFlowCalculatorTests
{
    private static readonly BudgetMonth August = new(2026, 8);

    [TestMethod]
    public void CalculateCarryForward_AppliesEveryHistoricalCashFlowType()
    {
        var transactions = new[]
        {
            Transaction(new DateOnly(2026, 7, 1), TransactionType.Income, 5_000m),
            Transaction(new DateOnly(2026, 7, 2), TransactionType.Expense, 1_200m),
            Transaction(new DateOnly(2026, 7, 3), TransactionType.Savings, 500m),
            Transaction(new DateOnly(2026, 7, 4), TransactionType.Refund, 100m),
            Transaction(new DateOnly(2026, 7, 5), TransactionType.Transfer, 999m),
            Transaction(new DateOnly(2026, 8, 1), TransactionType.Expense, 800m),
        };

        var result = CashFlowCalculator.CalculateCarryForward(transactions, August, 250m);

        Assert.AreEqual(3_650m, result);
    }

    [TestMethod]
    public void CalculateCarryForward_UsesBaselineDateInclusively()
    {
        var transactions = new[]
        {
            Transaction(new DateOnly(2026, 6, 30), TransactionType.Income, 9_000m),
            Transaction(new DateOnly(2026, 7, 1), TransactionType.Income, 1_000m),
            Transaction(new DateOnly(2026, 7, 31), TransactionType.Expense, 200m),
        };

        var result = CashFlowCalculator.CalculateCarryForward(
            transactions,
            August,
            baselineAmount: 500m,
            baselineDate: new DateOnly(2026, 7, 1));

        Assert.AreEqual(1_300m, result);
    }

    [TestMethod]
    public void RecalculationNaturallyReflectsHistoricalEditAndDelete()
    {
        var income = Transaction(new DateOnly(2026, 7, 1), TransactionType.Income, 2_000m);
        var expense = Transaction(new DateOnly(2026, 7, 2), TransactionType.Expense, 600m);

        Assert.AreEqual(1_400m, CashFlowCalculator.CalculateCarryForward([income, expense], August));
        Assert.AreEqual(1_100m, CashFlowCalculator.CalculateCarryForward(
            [income, expense with { Amount = 900m }],
            August));
        Assert.AreEqual(2_000m, CashFlowCalculator.CalculateCarryForward([income], August));
    }

    [TestMethod]
    public void CalculateClosingBalance_AppliesOnlySelectedMonth()
    {
        var result = CashFlowCalculator.CalculateClosingBalance(
            800m,
            [
                Transaction(new DateOnly(2026, 7, 31), TransactionType.Income, 9_000m),
                Transaction(new DateOnly(2026, 8, 1), TransactionType.Income, 2_000m),
                Transaction(new DateOnly(2026, 8, 2), TransactionType.Expense, 300m),
            ],
            August);

        Assert.AreEqual(2_500m, result);
    }

    [TestMethod]
    public void CalculateCarryForward_RejectsBaselineAfterTargetMonthStarts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CashFlowCalculator.CalculateCarryForward(
                [],
                August,
                baselineDate: new DateOnly(2026, 8, 2)));
    }

    private static BudgetTransaction Transaction(
        DateOnly date,
        TransactionType type,
        decimal amount) => new(Guid.NewGuid(), date, type, amount, null);
}
