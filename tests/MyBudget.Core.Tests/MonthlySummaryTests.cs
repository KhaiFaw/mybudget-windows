namespace MyBudget.Core.Tests;

[TestClass]
public sealed class MonthlySummaryTests
{
    [TestMethod]
    public void SharesOnlySelectedMonthAggregates()
    {
        var month = new BudgetMonth(2026, 9);
        var snapshot = BudgetSnapshot.Empty(month) with
        {
            CarryForward = 10,
            Transactions = [
                new(Guid.NewGuid(), month.FirstDay, TransactionType.Income, 100, null, "PRIVATE-INCOME"),
                new(Guid.NewGuid(), month.FirstDay, TransactionType.Expense, 25, null, "PRIVATE-MERCHANT"),
                new(Guid.NewGuid(), month.FirstDay, TransactionType.Refund, 5, null, "PRIVATE-REFUND"),
                new(Guid.NewGuid(), month.FirstDay, TransactionType.Savings, 20, null, "PRIVATE-GOAL"),
                new(Guid.NewGuid(), month.FirstDay, TransactionType.Transfer, 400, null, "PRIVATE-ACCOUNT"),
                new(Guid.NewGuid(), month.Next.FirstDay, TransactionType.Income, 999, null, "FUTURE")],
            Goals = [new(1, "SECRET GOAL", 1500, 20)],
            Bills = [new(1, "SECRET BILL", 50, 5, null)],
            BillPayments = [new(1, month, month.FirstDay.AddDays(4), month.FirstDay, 50)]
        };
        var summary = MonthlySummary.Create(snapshot);
        StringAssert.Contains(summary.Text, "Income recorded: MYR 100.00");
        StringAssert.Contains(summary.Text, "Spending, less refunds: MYR 20.00");
        StringAssert.Contains(summary.Text, "Available in budget: MYR 70.00");
        StringAssert.Contains(summary.Text, "Bills checked off: 1 of 1");
        Assert.IsFalse(summary.Text.Contains("PRIVATE", StringComparison.Ordinal));
        Assert.IsFalse(summary.Text.Contains("SECRET", StringComparison.Ordinal));
        Assert.IsFalse(summary.Text.Contains("999", StringComparison.Ordinal));
        Assert.IsLessThan(1900, summary.Text.Length);
    }

    [TestMethod]
    public void StatementReviewSeparatesDirectionsAndWarnsAboutPostedSalary()
    {
        var date = new DateOnly(2026, 9, 5);
        var credit = new StatementEntry(Guid.NewGuid(), date, 100, 120, "Deposit");
        Assert.IsTrue(StatementReviewRules.IsPossibleDuplicate(credit, [new(Guid.NewGuid(), date.AddDays(-2), TransactionType.Income, 100, 8, RecurringIncomeId: 1)]));
        Assert.IsFalse(StatementReviewRules.IsPossibleDuplicate(credit, [new(Guid.NewGuid(), date, TransactionType.Expense, 100, 7)]));
        Assert.DoesNotContain(TransactionType.Expense, StatementReviewRules.AllowedTypes(credit));
        Assert.DoesNotContain(TransactionType.Income, StatementReviewRules.AllowedTypes(credit with { SignedAmount = -100 }));
        Assert.IsTrue(StatementReviewRules.IsCardPurchase(credit with { SignedAmount = -100, Description = "SALE DEBIT · Sample Shop" }));
    }
}
