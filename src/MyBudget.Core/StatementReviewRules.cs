namespace MyBudget.Core;

public static class StatementReviewRules
{
    public static bool IsPossibleDuplicate(StatementEntry row, IEnumerable<BudgetTransaction> existing) => existing.Any(item =>
        item.Id != row.Id && item.Amount == row.Amount &&
        ((item.Date == row.Date && (row.IsCredit
            ? item.Type is TransactionType.Income or TransactionType.Refund or TransactionType.Transfer
            : item.Type is TransactionType.Expense or TransactionType.Savings or TransactionType.Transfer)) ||
         (row.IsCredit && item.RecurringIncomeId is not null && BudgetMonth.FromDate(item.Date) == BudgetMonth.FromDate(row.Date))));

    public static IReadOnlyList<TransactionType> AllowedTypes(StatementEntry row) => row.IsCredit
        ? [TransactionType.Income, TransactionType.Refund, TransactionType.Transfer]
        : [TransactionType.Expense, TransactionType.Savings, TransactionType.Transfer];

    public static bool IsCardPurchase(StatementEntry row) => !row.IsCredit &&
        row.Description.StartsWith("SALE DEBIT", StringComparison.OrdinalIgnoreCase);
}
