namespace MyBudget.Core;

public static class SavingsGoalCalculator
{
    /// <summary>
    /// Rebuilds goal progress from transaction links. Existing linked totals
    /// are replaced, not accumulated, so refreshing the same snapshot is safe.
    /// </summary>
    public static IReadOnlyList<SavingsGoal> ApplyLinkedSavings(
        IEnumerable<SavingsGoal> goals,
        IEnumerable<BudgetTransaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentNullException.ThrowIfNull(transactions);

        var linkedTransactions = transactions
            .Where(transaction => transaction.SavingsGoalId is not null)
            .ToArray();

        foreach (var transaction in linkedTransactions)
        {
            if (transaction.Amount < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(transactions),
                    transaction.Amount,
                    "Savings amounts must be zero or greater.");
            }

            TransactionDestinationRules.Validate(transaction, nameof(transactions));
        }

        var totals = linkedTransactions
            .GroupBy(transaction => transaction.SavingsGoalId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(transaction => transaction.Amount));

        return goals
            .Select(goal => goal with
            {
                LinkedSavingsAmount = totals.GetValueOrDefault(goal.Id),
            })
            .ToArray();
    }
}
