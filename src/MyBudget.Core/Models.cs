namespace MyBudget.Core;

public enum TransactionType
{
    Income,
    Expense,
    Savings,
    Refund,
    Transfer,
}

public enum CategoryKind
{
    Expense,
    Savings,
    Income,
}

public sealed record BudgetCategory(
    long Id,
    string Name,
    CategoryKind Kind,
    string ColorHex,
    int SortOrder = 0,
    bool IsArchived = false);

public sealed record BudgetTransaction(
    Guid Id,
    DateOnly Date,
    TransactionType Type,
    decimal Amount,
    long? CategoryId,
    string Note = "");

public sealed record BudgetAllocation(
    long CategoryId,
    BudgetMonth Month,
    decimal PlannedAmount);

public sealed record RecurringBill(
    long Id,
    string Name,
    decimal Amount,
    int DueDay,
    long? CategoryId,
    bool IsActive = true,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null);

public sealed record SavingsGoal(
    long Id,
    string Name,
    decimal TargetAmount,
    decimal CurrentAmount,
    DateOnly? TargetDate = null,
    string ColorHex = "#14B8A6")
{
    public decimal RemainingAmount => Math.Max(0m, TargetAmount - CurrentAmount);

    public decimal PercentComplete => TargetAmount <= 0m
        ? 0m
        : Math.Max(0m, CurrentAmount) / TargetAmount * 100m;
}

public sealed record BudgetAccount(
    long Id,
    string Name,
    string AccountType = "Other",
    bool IsActive = true);

public sealed record AppSettings(
    string CurrencyCode = "MYR",
    bool IsDarkMode = false);

public sealed record BudgetSnapshot(
    BudgetMonth Month,
    IReadOnlyList<BudgetCategory> Categories,
    IReadOnlyList<BudgetTransaction> Transactions,
    IReadOnlyList<BudgetAllocation> Allocations,
    IReadOnlyList<RecurringBill> Bills,
    IReadOnlyList<SavingsGoal> Goals,
    IReadOnlyList<BudgetAccount> Accounts,
    AppSettings Settings)
{
    public static BudgetSnapshot Empty(BudgetMonth month, AppSettings? settings = null) => new(
        month,
        Array.Empty<BudgetCategory>(),
        Array.Empty<BudgetTransaction>(),
        Array.Empty<BudgetAllocation>(),
        Array.Empty<RecurringBill>(),
        Array.Empty<SavingsGoal>(),
        Array.Empty<BudgetAccount>(),
        settings ?? new AppSettings());
}

public sealed record CategoryProgress(
    BudgetCategory Category,
    decimal Planned,
    decimal Actual)
{
    public decimal Remaining => Planned - Actual;

    public decimal PercentUsed => Planned <= 0m
        ? 0m
        : Math.Max(0m, Actual) / Planned * 100m;

    public decimal ChartPercent => Math.Clamp(PercentUsed, 0m, 100m);

    public bool IsOverBudget => Planned >= 0m && Actual > Planned;
}

public sealed record BudgetSummary(
    BudgetMonth Month,
    decimal Income,
    decimal Planned,
    decimal Spent,
    decimal Saved,
    decimal Available,
    decimal RemainingToPlan,
    IReadOnlyList<CategoryProgress> Categories);

public sealed record CsvImportResult(
    int ImportedCount,
    int SkippedCount);
