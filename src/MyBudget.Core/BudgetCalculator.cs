namespace MyBudget.Core;

/// <summary>
/// Keeps all monthly arithmetic in one testable place. Money uses decimal so
/// ordinary base-10 currency values are not subjected to binary rounding.
/// </summary>
public static class BudgetCalculator
{
    public static BudgetSummary Calculate(BudgetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        ValidateSnapshot(snapshot);
        snapshot.Month.EnsureValid(nameof(snapshot));

        var monthlyTransactions = snapshot.Transactions
            .Where(transaction => snapshot.Month.Contains(transaction.Date))
            .ToArray();

        var monthlyAllocations = snapshot.Allocations
            .Where(allocation => allocation.Month == snapshot.Month)
            .ToArray();

        var income = Sum(monthlyTransactions, TransactionType.Income);
        var spent = Sum(monthlyTransactions, TransactionType.Expense)
            - Sum(monthlyTransactions, TransactionType.Refund);
        var saved = Sum(monthlyTransactions, TransactionType.Savings);
        var planned = monthlyAllocations.Sum(allocation => allocation.PlannedAmount);

        // Transfers are deliberately absent: moving money between a person's own
        // accounts does not create income, spending, or savings.
        var available = income - spent - saved;
        var remainingToPlan = income - planned;

        var progress = snapshot.Categories
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(category => new CategoryProgress(
                category,
                monthlyAllocations
                    .Where(allocation => allocation.CategoryId == category.Id)
                    .Sum(allocation => allocation.PlannedAmount),
                monthlyTransactions
                    .Where(transaction => transaction.CategoryId == category.Id)
                    .Where(transaction => TransactionCategoryRules.IsCompatible(transaction.Type, category.Kind))
                    .Sum(GetCategoryImpact)))
            .ToArray();

        return new BudgetSummary(
            snapshot.Month,
            income,
            planned,
            spent,
            saved,
            available,
            remainingToPlan,
            progress);
    }

    private static decimal Sum(
        IEnumerable<BudgetTransaction> transactions,
        TransactionType type) => transactions
        .Where(transaction => transaction.Type == type)
        .Sum(transaction => transaction.Amount);

    private static decimal GetCategoryImpact(BudgetTransaction transaction) => transaction.Type switch
    {
        TransactionType.Income => transaction.Amount,
        TransactionType.Expense => transaction.Amount,
        TransactionType.Savings => transaction.Amount,
        TransactionType.Refund => -transaction.Amount,
        TransactionType.Transfer => 0m,
        _ => throw new ArgumentOutOfRangeException(
            nameof(transaction),
            transaction.Type,
            "Unknown transaction type."),
    };

    private static void ValidateSnapshot(BudgetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot.Categories);
        ArgumentNullException.ThrowIfNull(snapshot.Transactions);
        ArgumentNullException.ThrowIfNull(snapshot.Allocations);

        var negativeTransaction = snapshot.Transactions.FirstOrDefault(transaction => transaction.Amount < 0m);
        if (negativeTransaction is not null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(snapshot),
                negativeTransaction.Amount,
                "Transaction amounts must be zero or greater. Use the transaction type to represent its direction.");
        }

        var unknownTransaction = snapshot.Transactions.FirstOrDefault(
            transaction => !Enum.IsDefined(transaction.Type));
        if (unknownTransaction is not null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(snapshot),
                unknownTransaction.Type,
                "Every transaction must use a supported transaction type.");
        }

        var negativeAllocation = snapshot.Allocations.FirstOrDefault(allocation => allocation.PlannedAmount < 0m);
        if (negativeAllocation is not null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(snapshot),
                negativeAllocation.PlannedAmount,
                "Planned amounts must be zero or greater.");
        }
    }
}
