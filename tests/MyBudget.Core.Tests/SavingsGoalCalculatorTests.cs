namespace MyBudget.Core.Tests;

[TestClass]
public sealed class SavingsGoalCalculatorTests
{
    [TestMethod]
    public void ApplyLinkedSavings_RebuildsProgressFromGoalLinks()
    {
        var goals = new[]
        {
            new SavingsGoal(1, "Emergency", 10_000m, 1_000m, LinkedSavingsAmount: 99_999m),
            new SavingsGoal(2, "Travel", 2_000m, 100m),
        };
        var transactions = new[]
        {
            Savings(500m, goalId: 1),
            Savings(250m, goalId: 1),
            Savings(300m, goalId: 2),
            Savings(999m),
        };

        var result = SavingsGoalCalculator.ApplyLinkedSavings(goals, transactions);

        Assert.AreEqual(1_750m, result.Single(goal => goal.Id == 1).CurrentAmount);
        Assert.AreEqual(400m, result.Single(goal => goal.Id == 2).CurrentAmount);
    }

    [TestMethod]
    public void ApplyLinkedSavings_ReflectsRelinkAndDeleteWithoutManualGoalEdits()
    {
        var goals = new[]
        {
            new SavingsGoal(1, "Emergency", 1_000m, 0m),
            new SavingsGoal(2, "Travel", 1_000m, 0m),
        };
        var transaction = Savings(250m, goalId: 1);

        var before = SavingsGoalCalculator.ApplyLinkedSavings(goals, [transaction]);
        var relinked = SavingsGoalCalculator.ApplyLinkedSavings(
            goals,
            [transaction with { SavingsGoalId = 2 }]);
        var deleted = SavingsGoalCalculator.ApplyLinkedSavings(goals, []);

        Assert.AreEqual(250m, before.Single(goal => goal.Id == 1).CurrentAmount);
        Assert.AreEqual(0m, relinked.Single(goal => goal.Id == 1).CurrentAmount);
        Assert.AreEqual(250m, relinked.Single(goal => goal.Id == 2).CurrentAmount);
        Assert.IsTrue(deleted.All(goal => goal.CurrentAmount == 0m));
    }

    [TestMethod]
    public void ApplyLinkedSavings_RejectsGoalLinkOnNonSavingsTransaction()
    {
        var invalid = Savings(250m, goalId: 1) with { Type = TransactionType.Income };

        Assert.Throws<ArgumentException>(() =>
            SavingsGoalCalculator.ApplyLinkedSavings(
                [new SavingsGoal(1, "Emergency", 1_000m, 0m)],
                [invalid]));
    }

    private static BudgetTransaction Savings(decimal amount, long? goalId = null) => new(
        Guid.NewGuid(),
        new DateOnly(2026, 8, 2),
        TransactionType.Savings,
        amount,
        null,
        SavingsGoalId: goalId);
}
