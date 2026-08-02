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

/// <summary>
/// Broad asset classes used for display and calculation. Providers and names
/// remain free-form so new Malaysian and international products can be added
/// without changing the application.
/// </summary>
public enum InvestmentKind
{
    SavingsFund,
    UnitTrust,
    Gold,
    Other,
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
    string Note = "",
    long? SavingsGoalId = null,
    long? InvestmentId = null,
    long? RecurringIncomeId = null);

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

/// <summary>
/// The next concrete occurrence of a recurring bill relative to a selected
/// local calendar date. DaysUntilDue is calendar based, so daylight-saving
/// and UTC offsets cannot change the countdown.
/// </summary>
public sealed record RecurringBillOccurrence(
    RecurringBill Bill,
    DateOnly DueDate,
    int DaysUntilDue)
{
    public bool IsDueToday => DaysUntilDue == 0;
}

public sealed record RecurringIncome(
    long Id,
    string Name,
    decimal Amount,
    int PayDay,
    long? CategoryId,
    bool IsActive = true,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null);

public sealed record RecurringIncomeOccurrence(
    RecurringIncome Income,
    DateOnly Date);

public sealed record SavingsGoal(
    long Id,
    string Name,
    decimal TargetAmount,
    decimal StartingAmount,
    DateOnly? TargetDate = null,
    string ColorHex = "#14B8A6",
    decimal LinkedSavingsAmount = 0m)
{
    /// <summary>
    /// Goal progress is derived from the user's initial amount and every
    /// savings transaction linked to this goal. Editing, relinking, or deleting
    /// a transaction therefore updates progress instead of leaving stale data.
    /// </summary>
    public decimal CurrentAmount => StartingAmount + LinkedSavingsAmount;

    public decimal RemainingAmount => Math.Max(0m, TargetAmount - CurrentAmount);

    public decimal PercentComplete => TargetAmount <= 0m
        ? 0m
        : Math.Max(0m, CurrentAmount) / TargetAmount * 100m;
}

public sealed record Investment(
    long Id,
    string Name,
    string Provider,
    InvestmentKind Kind,
    string UnitLabel,
    string ColorHex = "#14B8A6",
    bool IsArchived = false);

public sealed record InvestmentValuation(
    Guid Id,
    long InvestmentId,
    DateOnly Date,
    decimal MarketValue,
    decimal? Units = null,
    decimal? UnitPrice = null,
    string Note = "");

public sealed record InvestmentPosition(
    Investment Investment,
    decimal AllTimeContributions,
    decimal MonthlyContributions,
    decimal CurrentValue,
    decimal GainLoss,
    InvestmentValuation? LatestValuation);

public sealed record InvestmentPortfolioSummary(
    IReadOnlyList<InvestmentPosition> Positions,
    decimal AllTimeContributions,
    decimal MonthlyContributions,
    decimal CurrentValue,
    decimal GainLoss);

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
    AppSettings Settings,
    IReadOnlyList<RecurringIncome> RecurringIncomes,
    IReadOnlyList<Investment> Investments,
    IReadOnlyList<InvestmentValuation> InvestmentValuations,
    IReadOnlyList<InvestmentPosition> InvestmentPositions,
    decimal CarryForward)
{
    /// <summary>
    /// Backwards-compatible constructor for callers that do not yet need the
    /// recurring-income and investment views.
    /// </summary>
    public BudgetSnapshot(
        BudgetMonth month,
        IReadOnlyList<BudgetCategory> categories,
        IReadOnlyList<BudgetTransaction> transactions,
        IReadOnlyList<BudgetAllocation> allocations,
        IReadOnlyList<RecurringBill> bills,
        IReadOnlyList<SavingsGoal> goals,
        IReadOnlyList<BudgetAccount> accounts,
        AppSettings settings)
        : this(
            month,
            categories,
            transactions,
            allocations,
            bills,
            goals,
            accounts,
            settings,
            Array.Empty<RecurringIncome>(),
            Array.Empty<Investment>(),
            Array.Empty<InvestmentValuation>(),
            Array.Empty<InvestmentPosition>(),
            0m)
    {
    }

    public static BudgetSnapshot Empty(BudgetMonth month, AppSettings? settings = null) => new(
        month,
        Array.Empty<BudgetCategory>(),
        Array.Empty<BudgetTransaction>(),
        Array.Empty<BudgetAllocation>(),
        Array.Empty<RecurringBill>(),
        Array.Empty<SavingsGoal>(),
        Array.Empty<BudgetAccount>(),
        settings ?? new AppSettings(),
        Array.Empty<RecurringIncome>(),
        Array.Empty<Investment>(),
        Array.Empty<InvestmentValuation>(),
        Array.Empty<InvestmentPosition>(),
        0m);
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
    IReadOnlyList<CategoryProgress> Categories,
    decimal CarryForward = 0m);

public sealed record CsvImportResult(
    int ImportedCount,
    int SkippedCount);
