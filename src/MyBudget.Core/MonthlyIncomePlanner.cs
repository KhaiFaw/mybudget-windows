namespace MyBudget.Core;

public sealed record MonthlyIncomeAdjustment(
    BudgetTransaction? ManagedTransaction,
    decimal OtherIncomeTotal,
    decimal ManagedAmount)
{
    public bool ShouldDeleteManaged => ManagedTransaction is not null && ManagedAmount == 0m;
}

/// <summary>
/// Plans edits to a monthly income total without changing income transactions
/// that the user entered separately.
/// </summary>
public static class MonthlyIncomePlanner
{
    public const string ManagedTransactionNote = "Monthly income";

    public static MonthlyIncomeAdjustment Plan(
        IEnumerable<BudgetTransaction> monthlyTransactions,
        decimal desiredIncomeTotal)
    {
        ArgumentNullException.ThrowIfNull(monthlyTransactions);
        if (desiredIncomeTotal < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredIncomeTotal),
                desiredIncomeTotal,
                "Monthly income cannot be negative.");
        }

        var incomeTransactions = monthlyTransactions
            .Where(transaction => transaction.Type == TransactionType.Income)
            .ToArray();
        var managedTransaction = incomeTransactions
            .Where(IsManagedTransaction)
            .OrderBy(transaction => transaction.Date)
            .ThenBy(transaction => transaction.Id)
            .FirstOrDefault();
        var otherIncomeTotal = incomeTransactions
            .Where(transaction => managedTransaction is null || transaction.Id != managedTransaction.Id)
            .Sum(transaction => transaction.Amount);

        if (desiredIncomeTotal < otherIncomeTotal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredIncomeTotal),
                desiredIncomeTotal,
                $"Monthly income cannot be lower than the {otherIncomeTotal} already recorded in other income entries.");
        }

        return new MonthlyIncomeAdjustment(
            managedTransaction,
            otherIncomeTotal,
            desiredIncomeTotal - otherIncomeTotal);
    }

    public static bool IsManagedTransaction(BudgetTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return transaction.Type == TransactionType.Income
            && string.Equals(
                transaction.Note,
                ManagedTransactionNote,
                StringComparison.Ordinal);
    }
}
