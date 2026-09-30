namespace MyBudget.Core;

/// <summary>A locally extracted bank row; its stable ID never contains account details.</summary>
public sealed record StatementEntry(
    Guid Id, DateOnly Date, decimal SignedAmount, decimal Balance, string Description)
{
    public decimal Amount => Math.Abs(SignedAmount);
    public bool IsCredit => SignedAmount > 0;
}

public sealed record BankStatement(
    BudgetMonth Month,
    IReadOnlyList<StatementEntry> Entries,
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal TotalDebits,
    decimal TotalCredits);

public sealed record StatementImportResult(int ImportedCount, int DuplicateCount);
