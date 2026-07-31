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
