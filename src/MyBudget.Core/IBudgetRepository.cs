namespace MyBudget.Core;

public interface IBudgetRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<BudgetSnapshot> LoadAsync(
        BudgetMonth month,
        CancellationToken cancellationToken = default);

    Task UpsertTransactionAsync(
        BudgetTransaction transaction,
        CancellationToken cancellationToken = default);

    Task DeleteTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);

    Task SaveAllocationsAsync(
        BudgetMonth month,
        IReadOnlyCollection<BudgetAllocation> allocations,
        CancellationToken cancellationToken = default);

    Task UpsertRecurringBillAsync(
        RecurringBill bill,
        CancellationToken cancellationToken = default);

    Task DeleteRecurringBillAsync(
        long billId,
        CancellationToken cancellationToken = default);

    Task UpsertSavingsGoalAsync(
        SavingsGoal goal,
        CancellationToken cancellationToken = default);

    Task DeleteSavingsGoalAsync(
        long goalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates an income source. The effective month controls when
    /// edits begin so already-recorded historical deposits stay unchanged.
    /// Returns the stored source identifier.
    /// </summary>
    Task<long> UpsertRecurringIncomeAsync(
        RecurringIncome income,
        BudgetMonth effectiveMonth,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates a recurring source while preserving generated deposits and
    /// their audit linkage.
    /// </summary>
    Task DeleteRecurringIncomeAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Materializes every due recurring deposit through the supplied PC-local
    /// date. Implementations must be idempotent.
    /// </summary>
    Task SynchronizeRecurringIncomeAsync(
        DateOnly throughDate,
        CancellationToken cancellationToken = default);

    Task<long> UpsertInvestmentAsync(
        Investment investment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates an investment and one of its dated valuations as a
    /// single operation. A zero valuation InvestmentId is replaced with the
    /// stored investment identifier.
    /// </summary>
    Task<long> UpsertInvestmentWithValuationAsync(
        Investment investment,
        InvestmentValuation valuation,
        CancellationToken cancellationToken = default);

    Task ArchiveInvestmentAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task UpsertInvestmentValuationAsync(
        InvestmentValuation valuation,
        CancellationToken cancellationToken = default);

    Task DeleteInvestmentValuationAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InvestmentPosition>> LoadInvestmentPortfolioAsync(
        BudgetMonth month,
        CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default);

    Task CreateBackupAsync(
        string destinationFilePath,
        CancellationToken cancellationToken = default);

    Task ExportTransactionsCsvAsync(
        BudgetMonth month,
        string destinationFilePath,
        CancellationToken cancellationToken = default);

    Task<CsvImportResult> ImportTransactionsCsvAsync(
        string sourceFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds synthetic example data when the selected month is empty.
    /// Returns false when existing data was left untouched.
    /// </summary>
    Task<bool> SeedDemoDataAsync(
        BudgetMonth month,
        CancellationToken cancellationToken = default);
}
