namespace MyBudget.Core.Tests;

[TestClass]
public sealed class BudgetCalculatorTests
{
    private static readonly BudgetMonth July = new(2026, 7);

    private static readonly BudgetCategory Housing = new(
        1,
        "Housing",
        CategoryKind.Expense,
        "#14B8A6",
        1);

    private static readonly BudgetCategory Food = new(
        2,
        "Food",
        CategoryKind.Expense,
        "#F97316",
        2);

    private static readonly BudgetCategory EmergencyFund = new(
        3,
        "Emergency fund",
        CategoryKind.Savings,
        "#10B981",
        3);

    [TestMethod]
    public void Calculate_SeparatesIncomeSpendingAndSavings()
    {
        var snapshot = CreateSnapshot(
            transactions:
            [
                Transaction(TransactionType.Income, 6_200m),
                Transaction(TransactionType.Expense, 1_800m, Housing.Id),
                Transaction(TransactionType.Expense, 700m, Food.Id),
                Transaction(TransactionType.Savings, 800m, EmergencyFund.Id),
            ],
            allocations:
            [
                new BudgetAllocation(Housing.Id, July, 2_000m),
                new BudgetAllocation(Food.Id, July, 1_000m),
                new BudgetAllocation(EmergencyFund.Id, July, 800m),
            ]);

        var result = BudgetCalculator.Calculate(snapshot);

        Assert.AreEqual(6_200m, result.Income);
        Assert.AreEqual(3_800m, result.Planned);
        Assert.AreEqual(2_500m, result.Spent);
        Assert.AreEqual(800m, result.Saved);
        Assert.AreEqual(2_900m, result.Available);
        Assert.AreEqual(2_400m, result.RemainingToPlan);
    }

    [TestMethod]
    public void Calculate_RefundReducesSpendingAndCategoryActual()
    {
        var snapshot = CreateSnapshot(
            transactions:
            [
                Transaction(TransactionType.Income, 2_000m),
                Transaction(TransactionType.Expense, 650m, Food.Id),
                Transaction(TransactionType.Refund, 125m, Food.Id),
            ],
            allocations: [new BudgetAllocation(Food.Id, July, 700m)]);

        var result = BudgetCalculator.Calculate(snapshot);
        var food = result.Categories.Single(category => category.Category.Id == Food.Id);

        Assert.AreEqual(525m, result.Spent);
        Assert.AreEqual(1_475m, result.Available);
        Assert.AreEqual(525m, food.Actual);
        Assert.AreEqual(175m, food.Remaining);
        Assert.IsFalse(food.IsOverBudget);
    }

    [TestMethod]
    public void Calculate_TransferDoesNotChangeAnyMonthlyTotalOrCategoryActual()
    {
        var snapshot = CreateSnapshot(
            transactions:
            [
                Transaction(TransactionType.Income, 1_000m),
                Transaction(TransactionType.Transfer, 400m, Housing.Id),
            ]);

        var result = BudgetCalculator.Calculate(snapshot);

        Assert.AreEqual(1_000m, result.Income);
        Assert.AreEqual(0m, result.Spent);
        Assert.AreEqual(0m, result.Saved);
        Assert.AreEqual(1_000m, result.Available);
        Assert.AreEqual(0m, result.Categories.Single(category => category.Category.Id == Housing.Id).Actual);
    }

    [TestMethod]
    public void Calculate_DoesNotApplyMismatchedTransactionTypeToCategoryProgress()
    {
        var snapshot = CreateSnapshot(
            transactions:
            [
                Transaction(TransactionType.Income, 1_000m, Housing.Id),
                Transaction(TransactionType.Expense, 200m, EmergencyFund.Id),
                Transaction(TransactionType.Savings, 100m, Food.Id),
            ]);

        var result = BudgetCalculator.Calculate(snapshot);

        Assert.AreEqual(1_000m, result.Income);
        Assert.AreEqual(200m, result.Spent);
        Assert.AreEqual(100m, result.Saved);
        Assert.IsTrue(result.Categories.All(category => category.Actual == 0m));
    }

    [TestMethod]
    public void Calculate_IgnoresTransactionsAndAllocationsFromOtherMonths()
    {
        var snapshot = CreateSnapshot(
            transactions:
            [
                Transaction(TransactionType.Income, 3_000m),
                Transaction(TransactionType.Expense, 999m, Housing.Id, new DateOnly(2026, 6, 30)),
            ],
            allocations:
            [
                new BudgetAllocation(Housing.Id, July, 1_000m),
                new BudgetAllocation(Housing.Id, new BudgetMonth(2026, 6), 999m),
            ]);

        var result = BudgetCalculator.Calculate(snapshot);

        Assert.AreEqual(0m, result.Spent);
        Assert.AreEqual(1_000m, result.Planned);
    }

    [TestMethod]
    public void Calculate_EmptySnapshotProducesZeroTotals()
    {
        var result = BudgetCalculator.Calculate(BudgetSnapshot.Empty(July));

        Assert.AreEqual(0m, result.Income);
        Assert.AreEqual(0m, result.Planned);
        Assert.AreEqual(0m, result.Spent);
        Assert.AreEqual(0m, result.Saved);
        Assert.AreEqual(0m, result.Available);
        Assert.AreEqual(0m, result.RemainingToPlan);
        Assert.IsEmpty(result.Categories);
    }

    [TestMethod]
    public void Calculate_BuildsSortedCategoryProgressAndCapsChartPercent()
    {
        var snapshot = CreateSnapshot(
            transactions:
            [
                Transaction(TransactionType.Expense, 600m, Food.Id),
                Transaction(TransactionType.Expense, 1_250m, Housing.Id),
            ],
            allocations:
            [
                new BudgetAllocation(Food.Id, July, 600m),
                new BudgetAllocation(Housing.Id, July, 1_000m),
            ]);

        var result = BudgetCalculator.Calculate(snapshot);

        CollectionAssert.AreEqual(
            new[] { Housing.Id, Food.Id, EmergencyFund.Id },
            result.Categories.Select(category => category.Category.Id).ToArray());

        var housing = result.Categories[0];
        Assert.AreEqual(125m, housing.PercentUsed);
        Assert.AreEqual(100m, housing.ChartPercent);
        Assert.AreEqual(-250m, housing.Remaining);
        Assert.IsTrue(housing.IsOverBudget);
    }

    [TestMethod]
    public void Calculate_RejectsNegativeTransactions()
    {
        var snapshot = CreateSnapshot(
            transactions: [Transaction(TransactionType.Expense, -1m, Food.Id)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => BudgetCalculator.Calculate(snapshot));
    }

    [TestMethod]
    public void Calculate_RejectsNegativeAllocations()
    {
        var snapshot = CreateSnapshot(
            allocations: [new BudgetAllocation(Food.Id, July, -1m)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => BudgetCalculator.Calculate(snapshot));
    }

    [TestMethod]
    public void Calculate_RejectsUnknownTransactionTypes()
    {
        var snapshot = CreateSnapshot(
            transactions: [Transaction((TransactionType)999, 1m)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => BudgetCalculator.Calculate(snapshot));
    }

    private static BudgetSnapshot CreateSnapshot(
        IReadOnlyList<BudgetTransaction>? transactions = null,
        IReadOnlyList<BudgetAllocation>? allocations = null) => new(
        July,
        [Housing, Food, EmergencyFund],
        transactions ?? [],
        allocations ?? [],
        [],
        [],
        [],
        new AppSettings());

    private static BudgetTransaction Transaction(
        TransactionType type,
        decimal amount,
        long? categoryId = null,
        DateOnly? date = null) => new(
        Guid.NewGuid(),
        date ?? new DateOnly(2026, 7, 15),
        type,
        amount,
        categoryId,
        "Test transaction");
}
