using System.Text;
using Microsoft.Data.Sqlite;
using MyBudget.Core;

namespace MyBudget.Infrastructure.Tests;

[TestClass]
public sealed class SqliteBudgetRepositoryTests
{
    private static readonly BudgetMonth July = new(2026, 7);

    private string _temporaryDirectory = null!;
    private SqliteBudgetRepository _repository = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "MyBudget.Infrastructure.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporaryDirectory);

        _repository = new SqliteBudgetRepository(Path.Combine(_temporaryDirectory, "mybudget.db"));
        await _repository.InitializeAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Initialize_IsIdempotentAndCreatesLocalDefaults()
    {
        await _repository.InitializeAsync();

        var snapshot = await _repository.LoadAsync(July);

        CollectionAssert.AreEqual(
            new[] { "Housing", "Food", "Transport", "Utilities", "Lifestyle", "Savings", "Other" },
            snapshot.Categories.Select(category => category.Name).ToArray());
        Assert.AreEqual("MYR", snapshot.Settings.CurrencyCode);
        Assert.IsFalse(snapshot.Settings.IsDarkMode);
        Assert.HasCount(1, snapshot.Accounts);
        Assert.AreEqual("Main account", snapshot.Accounts[0].Name);
    }

    [TestMethod]
    public async Task Initialize_UsesVersionedSchemaAndRejectsANewerDatabase()
    {
        await using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _repository.DatabasePath, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.AreEqual(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));

            command.CommandText = "PRAGMA user_version = 0;";
            await command.ExecuteNonQueryAsync();
        }

        await _repository.InitializeAsync();

        var futurePath = Path.Combine(_temporaryDirectory, "future.db");
        await using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = futurePath, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 2;";
            await command.ExecuteNonQueryAsync();
        }

        var futureRepository = new SqliteBudgetRepository(futurePath);
        await Assert.ThrowsAsync<InvalidDataException>(() => futureRepository.InitializeAsync());
    }

    [TestMethod]
    public async Task SaveAllocations_RejectsDefaultMonthBeforeWriting()
    {
        var invalidMonth = default(BudgetMonth);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _repository.SaveAllocationsAsync(
                invalidMonth,
                [new BudgetAllocation(1, invalidMonth, 100m)]));

        var snapshot = await _repository.LoadAsync(July);
        Assert.IsEmpty(snapshot.Allocations);
    }

    [TestMethod]
    public async Task VersionOneSchema_EnforcesMonthAndCategoryCompatibilityConstraints()
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _repository.DatabasePath, Pooling = false }.ToString());
        await connection.OpenAsync();

        await using (var invalidMonth = connection.CreateCommand())
        {
            invalidMonth.CommandText = """
                INSERT INTO Allocations (CategoryId, Year, Month, PlannedAmount)
                VALUES (1, 0, 0, '100');
                """;
            await Assert.ThrowsAsync<SqliteException>(() => invalidMonth.ExecuteNonQueryAsync());
        }

        await using (var invalidCategory = connection.CreateCommand())
        {
            invalidCategory.CommandText = """
                INSERT INTO Transactions
                    (Id, Date, Type, Amount, CategoryId, Note, CreatedUtc, UpdatedUtc)
                VALUES
                    ('11111111-1111-1111-1111-111111111111', '2026-07-01', 0, '100', 1, '', 'now', 'now');
                """;
            await Assert.ThrowsAsync<SqliteException>(() => invalidCategory.ExecuteNonQueryAsync());
        }
    }

    [TestMethod]
    public async Task SaveAndLoad_RoundTripsMoneyThemeAndNotesExactly()
    {
        var transaction = new BudgetTransaction(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 8),
            TransactionType.Expense,
            1_234.5678m,
            2,
            "Lunch, coffee and a note");

        await _repository.UpsertTransactionAsync(transaction);
        await _repository.SaveAllocationsAsync(
            July,
            [new BudgetAllocation(2, July, 1_999.9901m)]);
        await _repository.SaveSettingsAsync(new AppSettings("sgd", IsDarkMode: true));

        var snapshot = await _repository.LoadAsync(July);

        Assert.AreEqual(transaction, Assert.ContainsSingle(snapshot.Transactions));
        Assert.AreEqual(1_999.9901m, Assert.ContainsSingle(snapshot.Allocations).PlannedAmount);
        Assert.AreEqual("SGD", snapshot.Settings.CurrencyCode);
        Assert.IsTrue(snapshot.Settings.IsDarkMode);
    }

    [TestMethod]
    public async Task Crud_UpdatesAndDeletesTransactionsBillsAndGoals()
    {
        var id = Guid.NewGuid();
        await _repository.UpsertTransactionAsync(new BudgetTransaction(
            id,
            new DateOnly(2026, 7, 2),
            TransactionType.Expense,
            10m,
            2,
            "Before"));
        await _repository.UpsertTransactionAsync(new BudgetTransaction(
            id,
            new DateOnly(2026, 7, 3),
            TransactionType.Expense,
            15m,
            2,
            "After"));
        await _repository.UpsertRecurringBillAsync(new RecurringBill(41, "Internet", 129m, 12, 4));
        await _repository.UpsertSavingsGoalAsync(new SavingsGoal(77, "Laptop", 5_000m, 600m));

        var saved = await _repository.LoadAsync(July);
        var savedTransaction = Assert.ContainsSingle(saved.Transactions);
        Assert.AreEqual(15m, savedTransaction.Amount);
        Assert.AreEqual("After", savedTransaction.Note);
        Assert.AreEqual("Internet", Assert.ContainsSingle(saved.Bills).Name);
        Assert.AreEqual("Laptop", Assert.ContainsSingle(saved.Goals).Name);

        await _repository.DeleteTransactionAsync(id);
        await _repository.DeleteRecurringBillAsync(41);
        await _repository.DeleteSavingsGoalAsync(77);

        var deleted = await _repository.LoadAsync(July);
        Assert.IsEmpty(deleted.Transactions);
        Assert.IsEmpty(deleted.Bills);
        Assert.IsEmpty(deleted.Goals);
    }

    [TestMethod]
    public async Task Upserts_CanChangeIncomeAndEveryEditableBillField()
    {
        var incomeId = Guid.NewGuid();
        await _repository.UpsertTransactionAsync(new BudgetTransaction(
            incomeId,
            new DateOnly(2026, 7, 1),
            TransactionType.Income,
            3_000m,
            null,
            "Original income"));
        await _repository.UpsertTransactionAsync(new BudgetTransaction(
            incomeId,
            new DateOnly(2026, 7, 2),
            TransactionType.Income,
            3_500m,
            null,
            "Updated income"));

        await _repository.UpsertRecurringBillAsync(new RecurringBill(
            51,
            "Internet",
            100m,
            10,
            4));
        var updatedBill = new RecurringBill(
            51,
            "Fibre internet",
            129.90m,
            31,
            1,
            IsActive: false,
            StartDate: new DateOnly(2026, 8, 1),
            EndDate: new DateOnly(2027, 7, 31));
        await _repository.UpsertRecurringBillAsync(updatedBill);

        var snapshot = await _repository.LoadAsync(July);

        var income = Assert.ContainsSingle(snapshot.Transactions);
        Assert.AreEqual(incomeId, income.Id);
        Assert.AreEqual(new DateOnly(2026, 7, 2), income.Date);
        Assert.AreEqual(3_500m, income.Amount);
        Assert.AreEqual("Updated income", income.Note);
        Assert.AreEqual(updatedBill, Assert.ContainsSingle(snapshot.Bills));
    }

    [TestMethod]
    public async Task Writes_RejectMismatchedTransactionCategoryAndInvalidBillRange()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.UpsertTransactionAsync(
            new BudgetTransaction(
                Guid.NewGuid(),
                new DateOnly(2026, 7, 2),
                TransactionType.Income,
                100m,
                1,
                "Income cannot be housing")));

        await Assert.ThrowsAsync<ArgumentException>(() => _repository.UpsertRecurringBillAsync(
            new RecurringBill(
                0,
                "Invalid range",
                50m,
                15,
                4,
                StartDate: new DateOnly(2026, 8, 1),
                EndDate: new DateOnly(2026, 7, 31))));

        var snapshot = await _repository.LoadAsync(July);
        Assert.IsEmpty(snapshot.Transactions);
        Assert.IsEmpty(snapshot.Bills);
    }

    [TestMethod]
    public async Task Load_FiltersTransactionsAndAllocationsToSelectedMonth()
    {
        var june = new BudgetMonth(2026, 6);
        await _repository.UpsertTransactionAsync(new BudgetTransaction(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 30),
            TransactionType.Expense,
            40m,
            3,
            "June"));
        await _repository.UpsertTransactionAsync(new BudgetTransaction(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 1),
            TransactionType.Expense,
            50m,
            3,
            "July"));
        await _repository.SaveAllocationsAsync(june, [new BudgetAllocation(3, june, 400m)]);
        await _repository.SaveAllocationsAsync(July, [new BudgetAllocation(3, July, 500m)]);

        var snapshot = await _repository.LoadAsync(July);

        Assert.AreEqual("July", Assert.ContainsSingle(snapshot.Transactions).Note);
        Assert.AreEqual(500m, Assert.ContainsSingle(snapshot.Allocations).PlannedAmount);
    }

    [TestMethod]
    public async Task SeedDemoData_AddsReconciledExampleDataOnlyOnce()
    {
        var firstSeed = await _repository.SeedDemoDataAsync(July);
        var secondSeed = await _repository.SeedDemoDataAsync(July);
        var snapshot = await _repository.LoadAsync(July);
        var summary = BudgetCalculator.Calculate(snapshot);

        Assert.IsTrue(firstSeed);
        Assert.IsFalse(secondSeed);
        Assert.HasCount(7, snapshot.Transactions);
        Assert.AreEqual(6_200m, summary.Income);
        Assert.AreEqual(4_850m, summary.Planned);
        Assert.AreEqual(3_040m, summary.Spent);
        Assert.AreEqual(700m, summary.Saved);
        Assert.AreEqual(2_460m, summary.Available);
        Assert.IsNotEmpty(snapshot.Bills);
        Assert.IsNotEmpty(snapshot.Goals);
    }

    [TestMethod]
    public async Task SeedDemoData_RefusesAllocationOnlyMonthWithoutChangingPlan()
    {
        await _repository.SaveAllocationsAsync(
            July,
            [new BudgetAllocation(1, July, 321m)]);

        var seeded = await _repository.SeedDemoDataAsync(July);
        var snapshot = await _repository.LoadAsync(July);

        Assert.IsFalse(seeded);
        Assert.AreEqual(321m, Assert.ContainsSingle(snapshot.Allocations).PlannedAmount);
        Assert.IsEmpty(snapshot.Transactions);
        Assert.IsEmpty(snapshot.Bills);
        Assert.IsEmpty(snapshot.Goals);
    }

    [TestMethod]
    public async Task SeedDemoData_DoesNotDuplicateGlobalBillsOrGoalsAcrossMonths()
    {
        Assert.IsTrue(await _repository.SeedDemoDataAsync(July));
        Assert.IsTrue(await _repository.SeedDemoDataAsync(new BudgetMonth(2026, 8)));

        var snapshot = await _repository.LoadAsync(new BudgetMonth(2026, 8));

        Assert.HasCount(2, snapshot.Bills);
        Assert.AreEqual(2, snapshot.Bills.Select(bill => bill.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.HasCount(1, snapshot.Goals);
        Assert.AreEqual("Emergency fund", snapshot.Goals[0].Name);
    }

    [TestMethod]
    public async Task Backup_CreatesReadableIndependentDatabase()
    {
        var transaction = new BudgetTransaction(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 9),
            TransactionType.Income,
            2_500m,
            null,
            "Contract work");
        await _repository.UpsertTransactionAsync(transaction);

        var backupPath = Path.Combine(_temporaryDirectory, "backups", "july.db");
        await _repository.CreateBackupAsync(backupPath);
        var backupRepository = new SqliteBudgetRepository(backupPath);
        var backup = await backupRepository.LoadAsync(July);

        Assert.IsTrue(File.Exists(backupPath));
        Assert.AreEqual(transaction, Assert.ContainsSingle(backup.Transactions));
    }

    [TestMethod]
    public async Task CsvExportAndImport_RoundTripsQuotedMultilineFields()
    {
        var transaction = new BudgetTransaction(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 11),
            TransactionType.Expense,
            42.75m,
            2,
            "Lunch, with \"Sam\"\nSecond line");
        await _repository.UpsertTransactionAsync(transaction);

        var csvPath = Path.Combine(_temporaryDirectory, "exports", "july.csv");
        await _repository.ExportTransactionsCsvAsync(July, csvPath);

        var importedRepository = new SqliteBudgetRepository(Path.Combine(_temporaryDirectory, "imported.db"));
        await importedRepository.InitializeAsync();
        var result = await importedRepository.ImportTransactionsCsvAsync(csvPath);
        var imported = await importedRepository.LoadAsync(July);

        Assert.AreEqual(1, result.ImportedCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.AreEqual(transaction, Assert.ContainsSingle(imported.Transactions));
    }

    [TestMethod]
    public async Task CsvExportAndImport_PreservesWhitespaceAndLeadingApostrophe()
    {
        var padded = new BudgetTransaction(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 12),
            TransactionType.Expense,
            10m,
            2,
            "  keep my surrounding spaces  ");
        var formulaLiteral = new BudgetTransaction(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 13),
            TransactionType.Expense,
            11m,
            2,
            "'=SUM(A1:A2)");
        await _repository.UpsertTransactionAsync(padded);
        await _repository.UpsertTransactionAsync(formulaLiteral);

        var csvPath = Path.Combine(_temporaryDirectory, "fidelity.csv");
        await _repository.ExportTransactionsCsvAsync(July, csvPath);
        var importedRepository = new SqliteBudgetRepository(Path.Combine(_temporaryDirectory, "fidelity-import.db"));
        await importedRepository.InitializeAsync();
        var result = await importedRepository.ImportTransactionsCsvAsync(csvPath);
        var imported = await importedRepository.LoadAsync(July);
        var notesById = imported.Transactions.ToDictionary(item => item.Id, item => item.Note);

        Assert.AreEqual(2, result.ImportedCount);
        Assert.AreEqual(padded.Note, notesById[padded.Id]);
        Assert.AreEqual(formulaLiteral.Note, notesById[formulaLiteral.Id]);
    }

    [TestMethod]
    public async Task CsvImport_SkipsExistingAndDuplicateIdsWithoutOverwriting()
    {
        var existingId = Guid.NewGuid();
        var duplicateId = Guid.NewGuid();
        await _repository.UpsertTransactionAsync(new BudgetTransaction(
            existingId,
            new DateOnly(2026, 7, 1),
            TransactionType.Expense,
            5m,
            2,
            "Keep this"));

        var csvPath = Path.Combine(_temporaryDirectory, "collisions.csv");
        var csv = $"""
            Id,Date,Type,Amount,Category,Note
            {existingId:D},2026-07-02,Expense,99,Food,Must not overwrite
            {duplicateId:D},2026-07-03,Expense,10,Food,First duplicate row
            {duplicateId:D},2026-07-04,Expense,20,Food,Second duplicate row
            """;
        await File.WriteAllTextAsync(csvPath, csv, new UTF8Encoding(false));

        var result = await _repository.ImportTransactionsCsvAsync(csvPath);
        var snapshot = await _repository.LoadAsync(July);
        var transactions = snapshot.Transactions.ToDictionary(item => item.Id);

        Assert.AreEqual(1, result.ImportedCount);
        Assert.AreEqual(2, result.SkippedCount);
        Assert.AreEqual("Keep this", transactions[existingId].Note);
        Assert.AreEqual(5m, transactions[existingId].Amount);
        Assert.AreEqual("First duplicate row", transactions[duplicateId].Note);
        Assert.HasCount(2, snapshot.Transactions);
    }

    [TestMethod]
    public async Task CsvImport_SkipsTypeAndCategoryMismatches()
    {
        var csvPath = Path.Combine(_temporaryDirectory, "category-mismatch.csv");
        var csv = """
            Id,Date,Type,Amount,Category,Note
            ,2026-07-01,Income,100,Housing,Income in an expense category
            ,2026-07-02,Expense,50,Savings,Expense in a savings category
            ,2026-07-03,Expense,20,Food,Valid expense
            """;
        await File.WriteAllTextAsync(csvPath, csv, new UTF8Encoding(false));

        var result = await _repository.ImportTransactionsCsvAsync(csvPath);
        var snapshot = await _repository.LoadAsync(July);

        Assert.AreEqual(1, result.ImportedCount);
        Assert.AreEqual(2, result.SkippedCount);
        Assert.AreEqual("Valid expense", Assert.ContainsSingle(snapshot.Transactions).Note);
    }

    [TestMethod]
    public async Task CsvExport_PreservesExistingDestinationWhenAtomicReplaceFails()
    {
        var csvPath = Path.Combine(_temporaryDirectory, "locked.csv");
        await File.WriteAllTextAsync(csvPath, "previous export", new UTF8Encoding(false));

        await using (var locked = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAsync<IOException>(
                () => _repository.ExportTransactionsCsvAsync(July, csvPath));
            Assert.AreEqual("previous export", await File.ReadAllTextAsync(csvPath));
        }

        Assert.IsEmpty(Directory.GetFiles(
            _temporaryDirectory,
            $".{Path.GetFileName(csvPath)}.*.tmp"));
    }

    [TestMethod]
    public async Task CsvImport_SkipsInvalidRowsAndRejectsUnknownCategories()
    {
        var csvPath = Path.Combine(_temporaryDirectory, "mixed.csv");
        var csv = """
            Id,Date,Type,Amount,Category,Note
            ,2026-07-05,Expense,18.50,Food,Valid row
            ,not-a-date,Expense,20,Food,Bad date
            ,2026-07-06,Expense,20,Does not exist,Bad category
            """;
        await File.WriteAllTextAsync(csvPath, csv, new UTF8Encoding(false));

        var result = await _repository.ImportTransactionsCsvAsync(csvPath);
        var snapshot = await _repository.LoadAsync(July);

        Assert.AreEqual(1, result.ImportedCount);
        Assert.AreEqual(2, result.SkippedCount);
        Assert.AreEqual("Valid row", Assert.ContainsSingle(snapshot.Transactions).Note);
    }
}
