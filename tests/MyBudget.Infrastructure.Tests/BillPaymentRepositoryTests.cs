using Microsoft.Data.Sqlite;
using MyBudget.Core;

namespace MyBudget.Infrastructure.Tests;

[TestClass]
public sealed class BillPaymentRepositoryTests
{
    private static readonly BudgetMonth September = new(2026, 9);
    private string _directory = null!;
    private SqliteBudgetRepository _repository = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), "MyBudget.Infrastructure.Tests", Guid.NewGuid().ToString("N"));
        _repository = new SqliteBudgetRepository(Path.Combine(_directory, "mybudget.db"));
        await _repository.InitializeAsync();
        await _repository.UpsertRecurringBillAsync(new RecurringBill(1, "Phone", 45m, 14, 4)
        {
            PaymentTrackingStart = September.FirstDay
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public async Task MarkPaid_SurvivesRestartWithoutAddingAnExpenseOrChangingBalances()
    {
        var income = new BudgetTransaction(Guid.NewGuid(), September.FirstDay, TransactionType.Income, 1_000m, 8);
        await _repository.UpsertTransactionAsync(income);
        await _repository.MarkBillPaidAsync(1, September, new DateOnly(2026, 9, 10));
        var reopened = new SqliteBudgetRepository(_repository.DatabasePath);
        await reopened.InitializeAsync();
        var snapshot = await reopened.LoadAsync(September);
        var payment = Assert.ContainsSingle(snapshot.BillPayments);
        Assert.AreEqual(new BillPayment(1, September, new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 10), 45m), payment);
        Assert.AreEqual(income, Assert.ContainsSingle(snapshot.Transactions));
        Assert.AreEqual(1_000m, (await reopened.LoadAsync(September.Next)).CarryForward);
        Assert.AreEqual(September.FirstDay, Assert.ContainsSingle(snapshot.Bills).PaymentTrackingStart);
    }

    [TestMethod]
    public async Task RepeatedMark_IsIdempotentAndPreservesFirstPaidDate()
    {
        await _repository.MarkBillPaidAsync(1, September, new DateOnly(2026, 9, 10));
        await _repository.MarkBillPaidAsync(1, September, new DateOnly(2026, 9, 14));
        var payment = Assert.ContainsSingle((await _repository.LoadAsync(September)).BillPayments);
        Assert.AreEqual(new DateOnly(2026, 9, 10), payment.PaidOn);
    }

    [TestMethod]
    public async Task ConcurrentMark_CreatesOnlyOnePayment()
    {
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            new SqliteBudgetRepository(_repository.DatabasePath).MarkBillPaidAsync(1, September, September.FirstDay))));
        Assert.HasCount(1, (await _repository.LoadAsync(September)).BillPayments);
    }

    [TestMethod]
    public async Task Undo_OnlyChangesChosenBillAndDueMonth()
    {
        await _repository.UpsertRecurringBillAsync(new RecurringBill(2, "Internet", 100m, 12, 4));
        await _repository.MarkBillPaidAsync(1, September, September.FirstDay);
        await _repository.MarkBillPaidAsync(1, September.Next, September.FirstDay);
        await _repository.MarkBillPaidAsync(2, September, September.FirstDay);
        await _repository.UndoBillPaymentAsync(1, September);
        await _repository.UndoBillPaymentAsync(1, September);
        var snapshot = await _repository.LoadAsync(September);
        Assert.HasCount(2, snapshot.BillPayments);
        Assert.IsTrue(snapshot.BillPayments.Any(payment => payment.BillId == 1 && payment.Month == September.Next));
        Assert.IsTrue(snapshot.BillPayments.Any(payment => payment.BillId == 2 && payment.Month == September));
        Assert.IsFalse(BillPaymentCalculator.GetMonth(snapshot.Bills, snapshot.BillPayments, September)
            .Single(item => item.Bill.Id == 1).IsPaid);
        Assert.AreEqual(new DateOnly(2026, 9, 14), BillPaymentCalculator.GetNextUnpaidBills(
            snapshot.Bills, snapshot.BillPayments, September.FirstDay).Single(item => item.Bill.Id == 1).DueDate);
    }

    [TestMethod]
    public async Task BillEdit_PreservesPaymentSnapshotAndTrackingStart()
    {
        await _repository.MarkBillPaidAsync(1, September, September.FirstDay);
        await _repository.UpsertRecurringBillAsync(new RecurringBill(1, "New phone plan", 80m, 31, 4)
        {
            PaymentTrackingStart = September.Next.FirstDay
        });
        var snapshot = await _repository.LoadAsync(September.Next);
        var payment = Assert.ContainsSingle(snapshot.BillPayments);
        Assert.AreEqual(45m, payment.Amount);
        Assert.AreEqual(new DateOnly(2026, 9, 14), payment.DueDate);
        Assert.AreEqual(September.FirstDay, Assert.ContainsSingle(snapshot.Bills).PaymentTrackingStart);
        Assert.AreEqual(80m, Assert.ContainsSingle(snapshot.Bills).Amount);
    }

    [TestMethod]
    public async Task Backup_PreservesPaymentHistoryAndCountdown()
    {
        await _repository.MarkBillPaidAsync(1, September, September.FirstDay);
        var backup = Path.Combine(_directory, "backup.db");
        await _repository.CreateBackupAsync(backup);
        var restored = new SqliteBudgetRepository(backup);
        await restored.InitializeAsync();
        var snapshot = await restored.LoadAsync(September);
        Assert.HasCount(1, snapshot.BillPayments);
        Assert.AreEqual(new DateOnly(2026, 10, 14), Assert.ContainsSingle(
            BillPaymentCalculator.GetNextUnpaidBills(snapshot.Bills, snapshot.BillPayments, September.LastDay)).DueDate);
    }

    [TestMethod]
    public async Task DeleteBill_RemovesItsMarkersButNotTransactionsOrOtherBills()
    {
        var expense = new BudgetTransaction(Guid.NewGuid(), September.FirstDay, TransactionType.Expense, 45m, 4, "Phone paid");
        await _repository.UpsertTransactionAsync(expense);
        await _repository.UpsertRecurringBillAsync(new RecurringBill(2, "Other", 10m, 1, 4));
        await _repository.MarkBillPaidAsync(1, September, September.FirstDay);
        await _repository.MarkBillPaidAsync(2, September, September.FirstDay);
        await _repository.DeleteRecurringBillAsync(1);
        var snapshot = await _repository.LoadAsync(September);
        Assert.AreEqual(2L, Assert.ContainsSingle(snapshot.BillPayments).BillId);
        Assert.AreEqual(2L, Assert.ContainsSingle(snapshot.Bills).Id);
        Assert.AreEqual(expense, Assert.ContainsSingle(snapshot.Transactions));
    }

    [TestMethod]
    public async Task InvalidPayment_RejectsUnknownInactiveUnscheduledBillAndInvalidMonth()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.MarkBillPaidAsync(999, September, September.FirstDay));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _repository.MarkBillPaidAsync(1, default, September.FirstDay));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _repository.UndoBillPaymentAsync(1, default));
        await _repository.UpsertRecurringBillAsync(new RecurringBill(1, "Inactive", 45m, 14, 4, IsActive: false));
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.MarkBillPaidAsync(1, September, September.FirstDay));
        await _repository.UpsertRecurringBillAsync(new RecurringBill(1, "Future", 45m, 14, 4, StartDate: September.Next.FirstDay));
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.MarkBillPaidAsync(1, September, September.FirstDay));
        Assert.IsEmpty((await _repository.LoadAsync(September)).BillPayments);
    }

    [TestMethod]
    public async Task MigrationFromV3_PreservesLedgerAndStartsTrackingThisMonthWithoutInventingOldDebt()
    {
        var expense = new BudgetTransaction(Guid.NewGuid(), September.FirstDay, TransactionType.Expense, 21m, 2, "Keep me");
        await _repository.UpsertTransactionAsync(expense);
        await _repository.SaveAllocationsAsync(September, [new BudgetAllocation(2, September, 100m)]);
        await _repository.SaveSettingsAsync(new AppSettings("MYR", true));
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _repository.DatabasePath,
            Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE BillPayments;
                ALTER TABLE RecurringBills DROP COLUMN PaymentTrackingStart;
                PRAGMA user_version = 3;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await _repository.InitializeAsync();
        await _repository.InitializeAsync();
        var snapshot = await _repository.LoadAsync(September);
        Assert.AreEqual(expense, Assert.ContainsSingle(snapshot.Transactions));
        Assert.AreEqual(100m, Assert.ContainsSingle(snapshot.Allocations).PlannedAmount);
        Assert.IsTrue(snapshot.Settings.IsDarkMode);
        Assert.IsEmpty(snapshot.BillPayments);
        var bill = Assert.ContainsSingle(snapshot.Bills);
        Assert.AreEqual("Phone", bill.Name);
        Assert.AreEqual(45m, bill.Amount);
        Assert.AreEqual(BudgetMonth.FromDate(BudgetDateSelection.GetLocalToday()).FirstDay, bill.PaymentTrackingStart);
        await _repository.MarkBillPaidAsync(1, September, September.FirstDay);
        Assert.HasCount(1, (await _repository.LoadAsync(September)).BillPayments);
    }
}
