namespace MyBudget.Core;

/// <summary>
/// Calculates liquid cash movement consistently for the dashboard and monthly
/// carry-forward. Savings are removed from spendable cash, while transfers
/// between the user's own accounts have no net effect.
/// </summary>
public static class CashFlowCalculator
{
    public static decimal GetCashImpact(BudgetTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ValidateTransaction(transaction);

        return transaction.Type switch
        {
            TransactionType.Income => transaction.Amount,
            TransactionType.Expense => -transaction.Amount,
            TransactionType.Savings => -transaction.Amount,
            TransactionType.Refund => transaction.Amount,
            TransactionType.Transfer => 0m,
            _ => throw new ArgumentOutOfRangeException(
                nameof(transaction),
                transaction.Type,
                "Unknown transaction type."),
        };
    }

    /// <summary>
    /// Derives the balance available at the start of a month. The optional
    /// baseline is a balance at the opening of <paramref name="baselineDate"/>;
    /// transactions on that date and later are then applied exactly once.
    /// </summary>
    public static decimal CalculateCarryForward(
        IEnumerable<BudgetTransaction> transactions,
        BudgetMonth targetMonth,
        decimal baselineAmount = 0m,
        DateOnly? baselineDate = null)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        targetMonth.EnsureValid(nameof(targetMonth));

        if (baselineDate is not null && baselineDate.Value > targetMonth.FirstDay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baselineDate),
                baselineDate,
                "The balance baseline cannot be later than the target month.");
        }

        return baselineAmount + transactions
            .Where(transaction => transaction.Date < targetMonth.FirstDay)
            .Where(transaction => baselineDate is null || transaction.Date >= baselineDate.Value)
            .Sum(GetCashImpact);
    }

    public static decimal CalculateClosingBalance(
        decimal carryForward,
        IEnumerable<BudgetTransaction> monthlyTransactions,
        BudgetMonth month)
    {
        ArgumentNullException.ThrowIfNull(monthlyTransactions);
        month.EnsureValid(nameof(month));

        return carryForward + monthlyTransactions
            .Where(transaction => month.Contains(transaction.Date))
            .Sum(GetCashImpact);
    }

    private static void ValidateTransaction(BudgetTransaction transaction)
    {
        if (transaction.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transaction),
                transaction.Amount,
                "Transaction amounts must be zero or greater. Use the transaction type to represent its direction.");
        }

        if (!Enum.IsDefined(transaction.Type))
        {
            throw new ArgumentOutOfRangeException(
                nameof(transaction),
                transaction.Type,
                "Every transaction must use a supported transaction type.");
        }

        TransactionDestinationRules.Validate(transaction);
    }
}
