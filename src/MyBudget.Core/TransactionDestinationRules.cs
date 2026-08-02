namespace MyBudget.Core;

/// <summary>
/// Protects the meaning of optional transaction links. A savings deposit may
/// fund a goal or an investment, but never both; generated recurring-income
/// links belong only to income transactions.
/// </summary>
public static class TransactionDestinationRules
{
    public static bool IsValid(BudgetTransaction transaction) =>
        GetValidationError(transaction) is null;

    public static string? GetValidationError(BudgetTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (transaction.SavingsGoalId is not null && transaction.InvestmentId is not null)
        {
            return "A savings transaction cannot fund a goal and an investment at the same time.";
        }

        if ((transaction.SavingsGoalId is not null || transaction.InvestmentId is not null)
            && transaction.Type != TransactionType.Savings)
        {
            return "Only savings transactions can be linked to a goal or investment.";
        }

        if (transaction.RecurringIncomeId is not null && transaction.Type != TransactionType.Income)
        {
            return "Only income transactions can be linked to a recurring income source.";
        }

        return null;
    }

    public static void Validate(BudgetTransaction transaction, string? parameterName = null)
    {
        var error = GetValidationError(transaction);
        if (error is not null)
        {
            throw new ArgumentException(error, parameterName ?? nameof(transaction));
        }
    }
}
