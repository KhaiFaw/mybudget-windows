namespace MyBudget.Core;

/// <summary>
/// Defines which category kind can meaningfully receive each transaction type.
/// Transactions may remain uncategorized, but a selected category must agree with
/// the direction of the transaction.
/// </summary>
public static class TransactionCategoryRules
{
    public static bool IsCompatible(TransactionType transactionType, CategoryKind categoryKind) =>
        (transactionType, categoryKind) switch
        {
            (TransactionType.Income, CategoryKind.Income) => true,
            (TransactionType.Expense, CategoryKind.Expense) => true,
            (TransactionType.Refund, CategoryKind.Expense) => true,
            (TransactionType.Savings, CategoryKind.Savings) => true,
            _ => false,
        };
}
