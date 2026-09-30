using MyBudget.Core;

namespace MyBudget.Infrastructure.Tests;

[TestClass]
public sealed class StatementImportRepositoryTests
{
    private string _directory = null!;
    private SqliteBudgetRepository _repository = null!;
    private static readonly BudgetMonth Month = new(2026, 9);

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), "MyBudget.Infrastructure.Tests", Guid.NewGuid().ToString("N"));
        _repository = new SqliteBudgetRepository(Path.Combine(_directory, "mybudget.db"));
        await _repository.InitializeAsync();
    }
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [TestMethod]
    public async Task ReimportNeverDuplicatesOrOverwritesAnEditedEntry()
    {
        var row = Entry();
        Assert.AreEqual(new StatementImportResult(1, 0), await _repository.ImportStatementEntriesAsync([row]));
        await _repository.UpsertTransactionAsync(row with { Note = "Edited locally", Amount = 15 });
        Assert.AreEqual(new StatementImportResult(0, 2), await _repository.ImportStatementEntriesAsync([row, row]));
        var saved = Assert.ContainsSingle((await _repository.LoadAsync(Month)).Transactions);
        Assert.AreEqual("Edited locally", saved.Note);
        Assert.AreEqual(15m, saved.Amount);
    }

    [TestMethod]
    public async Task InvalidBatchLeavesEveryEntryUntouched()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _repository.ImportStatementEntriesAsync([
            Entry(), Entry() with { CategoryId = 8 }])); // Expense cannot use an income category.
        Assert.IsEmpty((await _repository.LoadAsync(Month)).Transactions);
    }

    [TestMethod]
    public async Task CancelledBatchAddsNothing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => _repository.ImportStatementEntriesAsync([Entry()], cancellation.Token));
        Assert.IsEmpty((await _repository.LoadAsync(Month)).Transactions);
    }

    [TestMethod]
    public async Task ReviewedSavingsUpdateGoalButNotBillsOrOpeningBalance()
    {
        await _repository.UpsertSavingsGoalAsync(new SavingsGoal(1, "Example goal", 500m, 20m));
        await _repository.UpsertRecurringBillAsync(new RecurringBill(1, "Example bill", 10m, 1, 7));
        await _repository.ImportStatementEntriesAsync([
            Entry() with { Type = TransactionType.Income, CategoryId = 9, Amount = 200 },
            Entry() with { Type = TransactionType.Savings, CategoryId = 6, SavingsGoalId = 1, Amount = 30 },
            Entry() with { Type = TransactionType.Transfer, CategoryId = null, Amount = 100 },
            Entry()]);
        var snapshot = await _repository.LoadAsync(Month);
        Assert.AreEqual(50m, Assert.ContainsSingle(snapshot.Goals).CurrentAmount);
        Assert.AreEqual(160m, BudgetCalculator.Calculate(snapshot).Available);
        Assert.AreEqual(0m, snapshot.CarryForward);
        Assert.AreEqual(160m, (await _repository.LoadAsync(Month.Next)).CarryForward);
        Assert.IsEmpty(snapshot.BillPayments);
    }

    [TestMethod]
    public async Task ConcurrentImportsRemainIdempotent()
    {
        var row = Entry();
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
            new SqliteBudgetRepository(_repository.DatabasePath).ImportStatementEntriesAsync([row]))));
        Assert.HasCount(1, (await _repository.LoadAsync(Month)).Transactions);
    }
    private static BudgetTransaction Entry() => new(Guid.NewGuid(), Month.FirstDay, TransactionType.Expense, 10m, 7, "Synthetic purchase");
}
