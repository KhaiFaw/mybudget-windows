namespace MyBudget.Core.Tests;

[TestClass]
public sealed class TransactionDestinationRulesTests
{
    [TestMethod]
    public void Validate_AcceptsOneSavingsDestination()
    {
        TransactionDestinationRules.Validate(Transaction(TransactionType.Savings) with { SavingsGoalId = 1 });
        TransactionDestinationRules.Validate(Transaction(TransactionType.Savings) with { InvestmentId = 2 });
        TransactionDestinationRules.Validate(Transaction(TransactionType.Income) with { RecurringIncomeId = 3 });
    }

    [TestMethod]
    public void Validate_RejectsTwoSavingsDestinations()
    {
        var transaction = Transaction(TransactionType.Savings) with
        {
            SavingsGoalId = 1,
            InvestmentId = 2,
        };

        var error = Assert.Throws<ArgumentException>(() =>
            TransactionDestinationRules.Validate(transaction));

        StringAssert.Contains(error.Message, "cannot fund a goal and an investment");
    }

    [TestMethod]
    public void Validate_RejectsDestinationOnWrongTransactionType()
    {
        Assert.Throws<ArgumentException>(() =>
            TransactionDestinationRules.Validate(
                Transaction(TransactionType.Expense) with { SavingsGoalId = 1 }));

        Assert.Throws<ArgumentException>(() =>
            TransactionDestinationRules.Validate(
                Transaction(TransactionType.Savings) with { RecurringIncomeId = 1 }));
    }

    private static BudgetTransaction Transaction(TransactionType type) => new(
        Guid.NewGuid(),
        new DateOnly(2026, 8, 2),
        type,
        100m,
        null);
}
