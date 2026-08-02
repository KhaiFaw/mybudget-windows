using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using MyBudget.Core;

namespace MyBudget.Infrastructure;

/// <summary>
/// Stores all MyBudget data in one local SQLite database. Money values are written as
/// invariant-culture text so that decimal values round-trip without binary rounding.
/// </summary>
public sealed class SqliteBudgetRepository : IBudgetRepository
{
    private const int CurrentSchemaVersion = 3;
    private const long MaximumCsvBytes = 10 * 1024 * 1024;
    private const int MaximumCsvRows = 50_000;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly BudgetCategory[] DefaultCategories =
    [
        new(1, "Housing", CategoryKind.Expense, "#2563EB", 10),
        new(2, "Food", CategoryKind.Expense, "#F59E0B", 20),
        new(3, "Transport", CategoryKind.Expense, "#8B5CF6", 30),
        new(4, "Utilities", CategoryKind.Expense, "#06B6D4", 40),
        new(5, "Lifestyle", CategoryKind.Expense, "#F97316", 50),
        new(6, "Savings", CategoryKind.Savings, "#14B8A6", 60),
        new(7, "Other", CategoryKind.Expense, "#64748B", 70),
        new(8, "Salary", CategoryKind.Income, "#10B981", 80),
        new(9, "Other income", CategoryKind.Income, "#22C55E", 90)
    ];

    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteBudgetRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            ForeignKeys = true
        }.ToString();
    }

    public string DatabasePath => _databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var schemaVersion = await ReadSchemaVersionAsync(connection, transaction, cancellationToken);
        if (schemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"This database uses schema version {schemaVersion}, but this version of MyBudget supports up to {CurrentSchemaVersion}.");
        }

        // Upgrades intentionally run in sequence. A preview database can still be
        // version zero, so it must pass through every intervening migration.
        if (schemaVersion < 1)
        {
            await MigrateToVersion1Async(connection, transaction, cancellationToken);
            schemaVersion = 1;
        }

        if (schemaVersion < 2)
        {
            await MigrateToVersion2Async(connection, transaction, cancellationToken);
            schemaVersion = 2;
        }

        if (schemaVersion < 3)
        {
            await MigrateToVersion3Async(connection, transaction, cancellationToken);
        }

        foreach (var category in DefaultCategories)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO Categories
                    (Id, Name, Kind, ColorHex, SortOrder, IsArchived)
                VALUES
                    ($id, $name, $kind, $color, $sortOrder, $isArchived);
                """;
            Add(command, "$id", category.Id);
            Add(command, "$name", category.Name);
            Add(command, "$kind", (int)category.Kind);
            Add(command, "$color", category.ColorHex);
            Add(command, "$sortOrder", category.SortOrder);
            Add(command, "$isArchived", category.IsArchived ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertSettingIfMissingAsync(connection, transaction, "CurrencyCode", "MYR", cancellationToken);
        await InsertSettingIfMissingAsync(connection, transaction, "IsDarkMode", "0", cancellationToken);

        await SeedDefaultInvestmentsAsync(connection, transaction, cancellationToken);

        await using (var accountCommand = connection.CreateCommand())
        {
            accountCommand.Transaction = transaction;
            accountCommand.CommandText = """
                INSERT INTO Accounts (Name, AccountType, IsActive)
                SELECT 'Main account', 'Other', 1
                WHERE NOT EXISTS (SELECT 1 FROM Accounts);
                """;
            await accountCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<BudgetSnapshot> LoadAsync(
        BudgetMonth month,
        CancellationToken cancellationToken = default)
    {
        month.EnsureValid(nameof(month));
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var categories = await ReadCategoriesAsync(connection, cancellationToken);
        var transactions = await ReadTransactionsAsync(connection, month, cancellationToken);
        var allocations = await ReadAllocationsAsync(connection, month, cancellationToken);
        var bills = await ReadRecurringBillsAsync(connection, cancellationToken);
        var goals = await ReadSavingsGoalsAsync(connection, cancellationToken);
        var accounts = await ReadAccountsAsync(connection, cancellationToken);
        var settings = await ReadSettingsAsync(connection, cancellationToken);
        var recurringIncomes = await ReadRecurringIncomesAsync(connection, cancellationToken);
        var investments = await ReadInvestmentsAsync(connection, cancellationToken);
        var valuations = await ReadInvestmentValuationsAsync(connection, month.LastDay, cancellationToken);
        var positions = await ReadInvestmentPositionsAsync(
            connection,
            month,
            investments,
            valuations,
            cancellationToken);
        var carryForward = await ReadCarryForwardAsync(connection, month, cancellationToken);

        return new BudgetSnapshot(
            month,
            categories,
            transactions,
            allocations,
            bills,
            goals,
            accounts,
            settings,
            recurringIncomes,
            investments,
            valuations,
            positions,
            carryForward);
    }

    public async Task UpsertTransactionAsync(
        BudgetTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        EnsureNonNegative(transaction.Amount, nameof(transaction.Amount));
        EnsureDefinedTransactionType(transaction.Type, nameof(transaction.Type));

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureTransactionCategoryCompatibleAsync(connection, transaction, cancellationToken);
        await EnsureTransactionDestinationsCompatibleAsync(connection, transaction, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Transactions
                (Id, Date, Type, Amount, CategoryId, Note, CreatedUtc, UpdatedUtc,
                 SavingsGoalId, InvestmentId, RecurringIncomeId, RecurringOccurrenceMonth)
            VALUES
                ($id, $date, $type, $amount, $categoryId, $note, $now, $now,
                 $savingsGoalId, $investmentId, $recurringIncomeId, $recurringOccurrenceMonth)
            ON CONFLICT(Id) DO UPDATE SET
                Date = excluded.Date,
                Type = excluded.Type,
                Amount = excluded.Amount,
                CategoryId = excluded.CategoryId,
                Note = excluded.Note,
                SavingsGoalId = excluded.SavingsGoalId,
                InvestmentId = excluded.InvestmentId,
                RecurringIncomeId = excluded.RecurringIncomeId,
                RecurringOccurrenceMonth = CASE
                    WHEN excluded.RecurringIncomeId IS NULL THEN NULL
                    WHEN Transactions.RecurringIncomeId = excluded.RecurringIncomeId
                    THEN COALESCE(Transactions.RecurringOccurrenceMonth, excluded.RecurringOccurrenceMonth)
                    ELSE excluded.RecurringOccurrenceMonth
                END,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        Add(command, "$id", transaction.Id.ToString("D"));
        Add(command, "$date", FormatDate(transaction.Date));
        Add(command, "$type", (int)transaction.Type);
        Add(command, "$amount", FormatMoney(transaction.Amount));
        AddNullable(command, "$categoryId", transaction.CategoryId);
        Add(command, "$note", transaction.Note ?? string.Empty);
        AddNullable(command, "$savingsGoalId", transaction.SavingsGoalId);
        AddNullable(command, "$investmentId", transaction.InvestmentId);
        AddNullable(command, "$recurringIncomeId", transaction.RecurringIncomeId);
        AddNullable(
            command,
            "$recurringOccurrenceMonth",
            transaction.RecurringIncomeId is null ? null : BudgetMonth.FromDate(transaction.Date).ToString());
        Add(command, "$now", DateTimeOffset.UtcNow.ToString("O", Invariant));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteTransactionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO RecurringIncomeOccurrenceSuppressions
                (RecurringIncomeId, OccurrenceMonth, CreatedUtc)
            SELECT
                RecurringIncomeId,
                COALESCE(RecurringOccurrenceMonth, substr(Date, 1, 7)),
                $now
            FROM Transactions
            WHERE Id = $id
              AND RecurringIncomeId IS NOT NULL;

            DELETE FROM Transactions
            WHERE Id = $id;
            """;
        Add(command, "$id", id.ToString("D"));
        Add(command, "$now", DateTimeOffset.UtcNow.ToString("O", Invariant));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveAllocationsAsync(
        BudgetMonth month,
        IReadOnlyCollection<BudgetAllocation> allocations,
        CancellationToken cancellationToken = default)
    {
        month.EnsureValid(nameof(month));
        ArgumentNullException.ThrowIfNull(allocations);

        foreach (var allocation in allocations)
        {
            if (allocation.Month != month)
            {
                throw new ArgumentException("Every allocation must belong to the requested month.", nameof(allocations));
            }

            EnsureNonNegative(allocation.PlannedAmount, nameof(allocation.PlannedAmount));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Allocations WHERE Year = $year AND Month = $month;";
            Add(delete, "$year", month.Year);
            Add(delete, "$month", month.Month);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var allocation in allocations)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO Allocations (CategoryId, Year, Month, PlannedAmount)
                VALUES ($categoryId, $year, $month, $amount);
                """;
            Add(insert, "$categoryId", allocation.CategoryId);
            Add(insert, "$year", month.Year);
            Add(insert, "$month", month.Month);
            Add(insert, "$amount", FormatMoney(allocation.PlannedAmount));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpsertRecurringBillAsync(
        RecurringBill bill,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bill);
        EnsureNonNegative(bill.Amount, nameof(bill.Amount));

        if (bill.DueDay is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(bill.DueDay), "Due day must be between 1 and 31.");
        }

        if (bill.StartDate is not null && bill.EndDate is not null && bill.StartDate.Value > bill.EndDate.Value)
        {
            throw new ArgumentException("A recurring bill's start date cannot be after its end date.", nameof(bill));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (bill.Id <= 0)
        {
            command.CommandText = """
                INSERT INTO RecurringBills
                    (Name, Amount, DueDay, CategoryId, IsActive, StartDate, EndDate)
                VALUES
                    ($name, $amount, $dueDay, $categoryId, $isActive, $startDate, $endDate);
                """;
        }
        else
        {
            command.CommandText = """
                INSERT INTO RecurringBills
                    (Id, Name, Amount, DueDay, CategoryId, IsActive, StartDate, EndDate)
                VALUES
                    ($id, $name, $amount, $dueDay, $categoryId, $isActive, $startDate, $endDate)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    Amount = excluded.Amount,
                    DueDay = excluded.DueDay,
                    CategoryId = excluded.CategoryId,
                    IsActive = excluded.IsActive,
                    StartDate = excluded.StartDate,
                    EndDate = excluded.EndDate;
                """;
            Add(command, "$id", bill.Id);
        }

        Add(command, "$name", bill.Name);
        Add(command, "$amount", FormatMoney(bill.Amount));
        Add(command, "$dueDay", bill.DueDay);
        AddNullable(command, "$categoryId", bill.CategoryId);
        Add(command, "$isActive", bill.IsActive ? 1 : 0);
        AddNullable(command, "$startDate", bill.StartDate is null ? null : FormatDate(bill.StartDate.Value));
        AddNullable(command, "$endDate", bill.EndDate is null ? null : FormatDate(bill.EndDate.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteRecurringBillAsync(long id, CancellationToken cancellationToken = default)
    {
        await ExecuteDeleteAsync("RecurringBills", "Id", id, cancellationToken);
    }

    public async Task UpsertSavingsGoalAsync(
        SavingsGoal goal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(goal);
        EnsureNonNegative(goal.TargetAmount, nameof(goal.TargetAmount));
        EnsureNonNegative(goal.StartingAmount, nameof(goal.StartingAmount));

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (goal.Id <= 0)
        {
            command.CommandText = """
                INSERT INTO SavingsGoals
                    (Name, TargetAmount, CurrentAmount, TargetDate, ColorHex)
                VALUES
                    ($name, $target, $current, $targetDate, $color);
                """;
        }
        else
        {
            command.CommandText = """
                INSERT INTO SavingsGoals
                    (Id, Name, TargetAmount, CurrentAmount, TargetDate, ColorHex)
                VALUES
                    ($id, $name, $target, $current, $targetDate, $color)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    TargetAmount = excluded.TargetAmount,
                    CurrentAmount = excluded.CurrentAmount,
                    TargetDate = excluded.TargetDate,
                    ColorHex = excluded.ColorHex;
                """;
            Add(command, "$id", goal.Id);
        }

        Add(command, "$name", goal.Name);
        Add(command, "$target", FormatMoney(goal.TargetAmount));
        Add(command, "$current", FormatMoney(goal.StartingAmount));
        AddNullable(command, "$targetDate", goal.TargetDate is null ? null : FormatDate(goal.TargetDate.Value));
        Add(command, "$color", goal.ColorHex);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteSavingsGoalAsync(long id, CancellationToken cancellationToken = default)
    {
        await ExecuteDeleteAsync("SavingsGoals", "Id", id, cancellationToken);
    }

    public async Task<long> UpsertRecurringIncomeAsync(
        RecurringIncome income,
        BudgetMonth effectiveMonth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(income);
        effectiveMonth.EnsureValid(nameof(effectiveMonth));
        EnsureNonNegative(income.Amount, nameof(income.Amount));
        if (string.IsNullOrWhiteSpace(income.Name))
        {
            throw new ArgumentException("An income source needs a name.", nameof(income));
        }

        if (income.PayDay is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(income.PayDay), "Pay day must be between 1 and 31.");
        }

        var effectiveStart = income.StartDate is null || income.StartDate.Value < effectiveMonth.FirstDay
            ? effectiveMonth.FirstDay
            : income.StartDate.Value;
        if (income.EndDate is not null && effectiveStart > income.EndDate.Value)
        {
            throw new ArgumentException("A recurring income's effective start cannot be after its end date.", nameof(income));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        if (income.CategoryId is long categoryId)
        {
            await EnsureCategoryKindAsync(
                connection,
                categoryId,
                CategoryKind.Income,
                "The selected recurring-income category",
                cancellationToken);
        }

        await using var command = connection.CreateCommand();
        var now = DateTimeOffset.UtcNow.ToString("O", Invariant);
        if (income.Id <= 0)
        {
            command.CommandText = """
                INSERT INTO RecurringIncomes
                    (Name, Amount, PayDay, CategoryId, IsActive, StartDate, EndDate, CreatedUtc, UpdatedUtc)
                VALUES
                    ($name, $amount, $payDay, $categoryId, $isActive, $startDate, $endDate, $now, $now)
                RETURNING Id;
                """;
        }
        else
        {
            command.CommandText = """
                INSERT INTO RecurringIncomes
                    (Id, Name, Amount, PayDay, CategoryId, IsActive, StartDate, EndDate, CreatedUtc, UpdatedUtc)
                VALUES
                    ($id, $name, $amount, $payDay, $categoryId, $isActive, $startDate, $endDate, $now, $now)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    Amount = excluded.Amount,
                    PayDay = excluded.PayDay,
                    CategoryId = excluded.CategoryId,
                    IsActive = excluded.IsActive,
                    StartDate = excluded.StartDate,
                    EndDate = excluded.EndDate,
                    UpdatedUtc = excluded.UpdatedUtc
                RETURNING Id;
                """;
            Add(command, "$id", income.Id);
        }

        Add(command, "$name", income.Name.Trim());
        Add(command, "$amount", FormatMoney(income.Amount));
        Add(command, "$payDay", income.PayDay);
        AddNullable(command, "$categoryId", income.CategoryId);
        Add(command, "$isActive", income.IsActive ? 1 : 0);
        Add(command, "$startDate", FormatDate(effectiveStart));
        AddNullable(command, "$endDate", income.EndDate is null ? null : FormatDate(income.EndDate.Value));
        Add(command, "$now", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), Invariant);
    }

    public async Task DeleteRecurringIncomeAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE RecurringIncomes
            SET IsActive = 0, UpdatedUtc = $now
            WHERE Id = $id;
            """;
        Add(command, "$id", id);
        Add(command, "$now", DateTimeOffset.UtcNow.ToString("O", Invariant));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SynchronizeRecurringIncomeAsync(
        DateOnly throughDate,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var incomes = await ReadRecurringIncomesAsync(connection, cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.ToString("O", Invariant);

        foreach (var income in incomes.Where(item => item.IsActive))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startDate = income.StartDate ?? throughDate;
            var finalDate = income.EndDate is null || income.EndDate.Value > throughDate
                ? throughDate
                : income.EndDate.Value;
            if (startDate > finalDate)
            {
                continue;
            }

            var month = BudgetMonth.FromDate(startDate);
            var lastMonth = BudgetMonth.FromDate(finalDate);
            while (true)
            {
                var dueDate = RecurringDateCalculator.GetDueDate(month, income.PayDay);
                if (dueDate >= startDate && dueDate <= finalDate)
                {
                    await InsertRecurringIncomeOccurrenceAsync(
                        connection,
                        transaction,
                        income,
                        month,
                        dueDate,
                        now,
                        cancellationToken);
                }

                if (month == lastMonth)
                {
                    break;
                }

                month = month.Next;
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<long> UpsertInvestmentAsync(
        Investment investment,
        CancellationToken cancellationToken = default)
    {
        ValidateInvestment(investment);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.ToString("O", Invariant);
        return await UpsertInvestmentRecordAsync(
            connection,
            transaction: null,
            investment,
            now,
            cancellationToken);
    }

    public async Task<long> UpsertInvestmentWithValuationAsync(
        Investment investment,
        InvestmentValuation valuation,
        CancellationToken cancellationToken = default)
    {
        ValidateInvestment(investment);
        ValidateInvestmentValuation(valuation);
        if (valuation.InvestmentId != 0 && valuation.InvestmentId != investment.Id)
        {
            throw new ArgumentException(
                "The valuation must belong to the investment being saved.",
                nameof(valuation));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.ToString("O", Invariant);
        var investmentId = await UpsertInvestmentRecordAsync(
            connection,
            transaction,
            investment,
            now,
            cancellationToken);
        await UpsertInvestmentValuationRecordAsync(
            connection,
            transaction,
            valuation with { InvestmentId = investmentId },
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return investmentId;
    }

    public async Task ArchiveInvestmentAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Investments
            SET IsArchived = 1, UpdatedUtc = $now
            WHERE Id = $id;
            """;
        Add(command, "$id", id);
        Add(command, "$now", DateTimeOffset.UtcNow.ToString("O", Invariant));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertInvestmentValuationAsync(
        InvestmentValuation valuation,
        CancellationToken cancellationToken = default)
    {
        ValidateInvestmentValuation(valuation);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureInvestmentExistsAsync(connection, valuation.InvestmentId, cancellationToken);
        await UpsertInvestmentValuationRecordAsync(
            connection,
            transaction: null,
            valuation,
            DateTimeOffset.UtcNow.ToString("O", Invariant),
            cancellationToken);
    }

    public async Task DeleteInvestmentValuationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await ExecuteDeleteAsync("InvestmentValuations", "Id", id.ToString("D"), cancellationToken);
    }

    public async Task<IReadOnlyList<InvestmentPosition>> LoadInvestmentPortfolioAsync(
        BudgetMonth month,
        CancellationToken cancellationToken = default)
    {
        month.EnsureValid(nameof(month));
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var investments = await ReadInvestmentsAsync(connection, cancellationToken);
        var valuations = await ReadInvestmentValuationsAsync(connection, month.LastDay, cancellationToken);
        return await ReadInvestmentPositionsAsync(connection, month, investments, valuations, cancellationToken);
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var currencyCode = settings.CurrencyCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Length != 3)
        {
            throw new ArgumentException("Currency code must contain three letters.", nameof(settings));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await UpsertSettingAsync(connection, transaction, "CurrencyCode", currencyCode, cancellationToken);
        await UpsertSettingAsync(connection, transaction, "IsDarkMode", settings.IsDarkMode ? "1" : "0", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CreateBackupAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        if (string.Equals(_databasePath, fullDestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The backup destination must differ from the live database.", nameof(destinationPath));
        }

        var destinationDirectory = Path.GetDirectoryName(fullDestinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        await using var source = await OpenConnectionAsync(cancellationToken);
        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullDestinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true
        }.ToString();
        await using var destination = new SqliteConnection(destinationConnectionString);
        await destination.OpenAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        source.BackupDatabase(destination);
    }

    public async Task ExportTransactionsCsvAsync(
        BudgetMonth month,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        month.EnsureValid(nameof(month));
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullPath = Path.GetFullPath(destinationPath);
        if (string.Equals(_databasePath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The CSV destination must differ from the live database.", nameof(destinationPath));
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        IReadOnlyList<BudgetCategory> categories;
        IReadOnlyList<BudgetTransaction> transactions;
        IReadOnlyList<SavingsGoal> goals;
        IReadOnlyList<Investment> investments;
        await using (var connection = await OpenConnectionAsync(cancellationToken))
        {
            categories = await ReadCategoriesAsync(connection, cancellationToken);
            transactions = await ReadTransactionsAsync(connection, month, cancellationToken);
            goals = await ReadSavingsGoalsAsync(connection, cancellationToken);
            investments = await ReadInvestmentsAsync(connection, cancellationToken);
        }

        var namesById = categories.ToDictionary(category => category.Id, category => category.Name);
        var goalNamesById = goals.ToDictionary(goal => goal.Id, goal => goal.Name);
        var investmentNamesById = investments.ToDictionary(investment => investment.Id, investment => investment.Name);
        var temporaryPath = Path.Combine(
            directory!,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16_384,
                useAsync: true))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteLineAsync("Id,Date,Type,Amount,Category,Note,Goal,Investment".AsMemory(), cancellationToken);
                foreach (var item in transactions.OrderBy(item => item.Date).ThenBy(item => item.Id))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var categoryName = item.CategoryId is long categoryId && namesById.TryGetValue(categoryId, out var name)
                        ? name
                        : string.Empty;
                    var goalName = item.SavingsGoalId is long goalId && goalNamesById.TryGetValue(goalId, out var matchedGoal)
                        ? matchedGoal
                        : string.Empty;
                    var investmentName = item.InvestmentId is long investmentId && investmentNamesById.TryGetValue(investmentId, out var matchedInvestment)
                        ? matchedInvestment
                        : string.Empty;
                    var row = string.Join(",",
                        CsvCodec.Escape(item.Id.ToString("D")),
                        CsvCodec.Escape(FormatDate(item.Date)),
                        CsvCodec.Escape(item.Type.ToString()),
                        CsvCodec.Escape(FormatMoney(item.Amount)),
                        CsvCodec.Escape(CsvCodec.NeutralizeSpreadsheetFormula(categoryName)),
                        CsvCodec.Escape(CsvCodec.NeutralizeSpreadsheetFormula(item.Note ?? string.Empty)),
                        CsvCodec.Escape(CsvCodec.NeutralizeSpreadsheetFormula(goalName)),
                        CsvCodec.Escape(CsvCodec.NeutralizeSpreadsheetFormula(investmentName)));
                    await writer.WriteLineAsync(row.AsMemory(), cancellationToken);
                }

                await writer.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ReplaceFileAtomically(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<CsvImportResult> ImportTransactionsCsvAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullPath = Path.GetFullPath(sourcePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The CSV file was not found.", fullPath);
        }

        if (file.Length > MaximumCsvBytes)
        {
            throw new InvalidDataException($"CSV files larger than {MaximumCsvBytes / 1024 / 1024} MB are not supported.");
        }

        var csv = await File.ReadAllTextAsync(fullPath, Encoding.UTF8, cancellationToken);
        var rows = CsvCodec.Parse(csv, MaximumCsvRows + 1);
        if (rows.Count == 0)
        {
            throw new InvalidDataException("The CSV file is empty.");
        }

        var headerEntries = rows[0]
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .ToArray();
        if (headerEntries
            .GroupBy(pair => pair.Name, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("The CSV header contains duplicate column names.");
        }

        var header = headerEntries.ToDictionary(
            pair => pair.Name,
            pair => pair.Index,
            StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[] { "Date", "Type", "Amount", "Category", "Note" })
        {
            if (!header.ContainsKey(required))
            {
                throw new InvalidDataException($"The CSV file is missing the required '{required}' column.");
            }
        }

        if (rows.Count - 1 > MaximumCsvRows)
        {
            throw new InvalidDataException($"CSV files may contain at most {MaximumCsvRows:N0} data rows.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var categories = await ReadCategoriesAsync(connection, cancellationToken);
        var categoriesByName = categories.ToDictionary(category => category.Name, StringComparer.OrdinalIgnoreCase);
        var goals = await ReadSavingsGoalsAsync(connection, cancellationToken);
        var goalsByName = goals
            .GroupBy(goal => goal.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
        var investments = await ReadInvestmentsAsync(connection, cancellationToken);
        var investmentsByName = investments
            .GroupBy(investment => investment.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var knownTransactionIds = await ReadTransactionIdsAsync(connection, transaction, cancellationToken);

        var imported = 0;
        var skipped = 0;
        for (var index = 1; index < rows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[index];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (!TryCreateImportedTransaction(
                    row,
                    header,
                    categoriesByName,
                    goalsByName,
                    investmentsByName,
                    out var item))
            {
                skipped++;
                continue;
            }

            var idKey = item!.Id.ToString("D");
            if (!knownTransactionIds.Add(idKey))
            {
                skipped++;
                continue;
            }

            await InsertTransactionWithinTransactionAsync(connection, transaction, item, cancellationToken);
            imported++;
        }

        await transaction.CommitAsync(cancellationToken);
        return new CsvImportResult(imported, skipped);
    }

    public async Task<bool> SeedDemoDataAsync(
        BudgetMonth month,
        CancellationToken cancellationToken = default)
    {
        month.EnsureValid(nameof(month));
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM Transactions WHERE Date >= $first AND Date <= $last) +
                    (SELECT COUNT(*) FROM Allocations WHERE Year = $year AND Month = $month);
                """;
            Add(check, "$first", FormatDate(month.FirstDay));
            Add(check, "$last", FormatDate(month.LastDay));
            Add(check, "$year", month.Year);
            Add(check, "$month", month.Month);
            var count = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken), Invariant);
            if (count > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        var day = (int requested) => Math.Min(requested, month.LastDay.Day);
        var demoTransactions = new[]
        {
            new BudgetTransaction(Guid.NewGuid(), new DateOnly(month.Year, month.Month, day(1)), TransactionType.Income, 6_200m, null, "Monthly income"),
            new BudgetTransaction(Guid.NewGuid(), new DateOnly(month.Year, month.Month, day(2)), TransactionType.Expense, 1_500m, 1, "Rent"),
            new BudgetTransaction(Guid.NewGuid(), new DateOnly(month.Year, month.Month, day(6)), TransactionType.Expense, 580m, 2, "Groceries and meals"),
            new BudgetTransaction(Guid.NewGuid(), new DateOnly(month.Year, month.Month, day(10)), TransactionType.Expense, 350m, 3, "Fuel and transport"),
            new BudgetTransaction(Guid.NewGuid(), new DateOnly(month.Year, month.Month, day(14)), TransactionType.Expense, 310m, 4, "Utilities"),
            new BudgetTransaction(Guid.NewGuid(), new DateOnly(month.Year, month.Month, day(18)), TransactionType.Expense, 300m, 5, "Personal spending"),
            new BudgetTransaction(Guid.NewGuid(), new DateOnly(month.Year, month.Month, day(3)), TransactionType.Savings, 700m, 6, "Emergency fund")
        };

        foreach (var item in demoTransactions)
        {
            await InsertTransactionWithinTransactionAsync(connection, transaction, item, cancellationToken);
        }

        var demoAllocations = new[]
        {
            new BudgetAllocation(1, month, 1_800m),
            new BudgetAllocation(2, month, 900m),
            new BudgetAllocation(3, month, 550m),
            new BudgetAllocation(4, month, 500m),
            new BudgetAllocation(5, month, 400m),
            new BudgetAllocation(6, month, 700m)
        };
        foreach (var allocation in demoAllocations)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Allocations (CategoryId, Year, Month, PlannedAmount)
                VALUES ($categoryId, $year, $month, $amount)
                ON CONFLICT(CategoryId, Year, Month) DO UPDATE SET
                    PlannedAmount = excluded.PlannedAmount;
                """;
            Add(command, "$categoryId", allocation.CategoryId);
            Add(command, "$year", month.Year);
            Add(command, "$month", month.Month);
            Add(command, "$amount", FormatMoney(allocation.PlannedAmount));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var bills = connection.CreateCommand())
        {
            bills.Transaction = transaction;
            bills.CommandText = """
                INSERT INTO RecurringBills (Name, Amount, DueDay, CategoryId, IsActive, StartDate, EndDate)
                SELECT 'Rent', '1500', 2, 1, 1, NULL, NULL
                WHERE NOT EXISTS (SELECT 1 FROM RecurringBills WHERE Name = 'Rent' COLLATE NOCASE);
                INSERT INTO RecurringBills (Name, Amount, DueDay, CategoryId, IsActive, StartDate, EndDate)
                SELECT 'Internet', '129', 12, 4, 1, NULL, NULL
                WHERE NOT EXISTS (SELECT 1 FROM RecurringBills WHERE Name = 'Internet' COLLATE NOCASE);
                """;
            await bills.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var goals = connection.CreateCommand())
        {
            goals.Transaction = transaction;
            goals.CommandText = """
                INSERT INTO SavingsGoals (Name, TargetAmount, CurrentAmount, TargetDate, ColorHex)
                SELECT 'Emergency fund', '12000', '3200', NULL, '#14B8A6'
                WHERE NOT EXISTS (SELECT 1 FROM SavingsGoals WHERE Name = 'Emergency fund' COLLATE NOCASE);
                """;
            await goals.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<IReadOnlyList<BudgetCategory>> ReadCategoriesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new List<BudgetCategory>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Kind, ColorHex, SortOrder, IsArchived
            FROM Categories
            ORDER BY SortOrder, Name;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new BudgetCategory(
                reader.GetInt64(0),
                reader.GetString(1),
                (CategoryKind)reader.GetInt32(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt64(5) != 0));
        }

        return result;
    }

    private static async Task<IReadOnlyList<BudgetTransaction>> ReadTransactionsAsync(
        SqliteConnection connection,
        BudgetMonth month,
        CancellationToken cancellationToken)
    {
        var result = new List<BudgetTransaction>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Date, Type, Amount, CategoryId, Note,
                   SavingsGoalId, InvestmentId, RecurringIncomeId
            FROM Transactions
            WHERE Date >= $first AND Date <= $last
            ORDER BY Date DESC, UpdatedUtc DESC;
            """;
        Add(command, "$first", FormatDate(month.FirstDay));
        Add(command, "$last", FormatDate(month.LastDay));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new BudgetTransaction(
                Guid.Parse(reader.GetString(0)),
                ParseDate(reader.GetString(1)),
                (TransactionType)reader.GetInt32(2),
                ParseMoney(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<BudgetAllocation>> ReadAllocationsAsync(
        SqliteConnection connection,
        BudgetMonth month,
        CancellationToken cancellationToken)
    {
        var result = new List<BudgetAllocation>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CategoryId, PlannedAmount
            FROM Allocations
            WHERE Year = $year AND Month = $month
            ORDER BY CategoryId;
            """;
        Add(command, "$year", month.Year);
        Add(command, "$month", month.Month);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new BudgetAllocation(reader.GetInt64(0), month, ParseMoney(reader.GetString(1))));
        }

        return result;
    }

    private static async Task<IReadOnlyList<RecurringBill>> ReadRecurringBillsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new List<RecurringBill>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Amount, DueDay, CategoryId, IsActive, StartDate, EndDate
            FROM RecurringBills
            ORDER BY DueDay, Name;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new RecurringBill(
                reader.GetInt64(0),
                reader.GetString(1),
                ParseMoney(reader.GetString(2)),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.GetInt64(5) != 0,
                reader.IsDBNull(6) ? null : ParseDate(reader.GetString(6)),
                reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7))));
        }

        return result;
    }

    private static async Task<IReadOnlyList<SavingsGoal>> ReadSavingsGoalsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var baselines = new List<SavingsGoal>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Id, Name, TargetAmount, CurrentAmount, TargetDate, ColorHex
                FROM SavingsGoals
                ORDER BY Name;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                // The v1 CurrentAmount column is intentionally retained as the
                // immutable starting amount so existing goal progress is preserved.
                baselines.Add(new SavingsGoal(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    ParseMoney(reader.GetString(2)),
                    ParseMoney(reader.GetString(3)),
                    reader.IsDBNull(4) ? null : ParseDate(reader.GetString(4)),
                    reader.GetString(5)));
            }
        }

        var linkedAmounts = new Dictionary<long, decimal>();
        await using (var contributionCommand = connection.CreateCommand())
        {
            contributionCommand.CommandText = """
                SELECT SavingsGoalId, Amount
                FROM Transactions
                WHERE Type = $savingsType AND SavingsGoalId IS NOT NULL;
                """;
            Add(contributionCommand, "$savingsType", (int)TransactionType.Savings);
            await using var reader = await contributionCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var goalId = reader.GetInt64(0);
                linkedAmounts[goalId] = linkedAmounts.GetValueOrDefault(goalId) + ParseMoney(reader.GetString(1));
            }
        }

        return baselines
            .Select(goal => goal with { LinkedSavingsAmount = linkedAmounts.GetValueOrDefault(goal.Id) })
            .ToArray();
    }

    private static async Task<IReadOnlyList<BudgetAccount>> ReadAccountsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new List<BudgetAccount>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, AccountType, IsActive
            FROM Accounts
            ORDER BY Name;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new BudgetAccount(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3) != 0));
        }

        return result;
    }

    private static async Task<IReadOnlyList<RecurringIncome>> ReadRecurringIncomesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new List<RecurringIncome>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Amount, PayDay, CategoryId, IsActive, StartDate, EndDate
            FROM RecurringIncomes
            ORDER BY IsActive DESC, PayDay, Name;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new RecurringIncome(
                reader.GetInt64(0),
                reader.GetString(1),
                ParseMoney(reader.GetString(2)),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.GetInt64(5) != 0,
                reader.IsDBNull(6) ? null : ParseDate(reader.GetString(6)),
                reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7))));
        }

        return result;
    }

    private static async Task<IReadOnlyList<Investment>> ReadInvestmentsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new List<Investment>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Provider, Kind, UnitLabel, ColorHex, IsArchived
            FROM Investments
            ORDER BY IsArchived, Name;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new Investment(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                (InvestmentKind)reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6) != 0));
        }

        return result;
    }

    private static async Task<IReadOnlyList<InvestmentValuation>> ReadInvestmentValuationsAsync(
        SqliteConnection connection,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        var result = new List<InvestmentValuation>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, InvestmentId, Date, MarketValue, Units, UnitPrice, Note
            FROM InvestmentValuations
            WHERE Date <= $throughDate
            ORDER BY Date DESC, UpdatedUtc DESC, Id;
            """;
        Add(command, "$throughDate", FormatDate(throughDate));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new InvestmentValuation(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt64(1),
                ParseDate(reader.GetString(2)),
                ParseMoney(reader.GetString(3)),
                reader.IsDBNull(4) ? null : ParseMoney(reader.GetString(4)),
                reader.IsDBNull(5) ? null : ParseMoney(reader.GetString(5)),
                reader.GetString(6)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<InvestmentPosition>> ReadInvestmentPositionsAsync(
        SqliteConnection connection,
        BudgetMonth month,
        IReadOnlyList<Investment> investments,
        IReadOnlyList<InvestmentValuation> valuations,
        CancellationToken cancellationToken)
    {
        var allTime = new Dictionary<long, decimal>();
        var monthly = new Dictionary<long, decimal>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT InvestmentId, Date, Amount
                FROM Transactions
                WHERE Type = $savingsType
                  AND InvestmentId IS NOT NULL
                  AND Date <= $lastDate;
                """;
            Add(command, "$savingsType", (int)TransactionType.Savings);
            Add(command, "$lastDate", FormatDate(month.LastDay));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var investmentId = reader.GetInt64(0);
                var date = ParseDate(reader.GetString(1));
                var amount = ParseMoney(reader.GetString(2));
                allTime[investmentId] = allTime.GetValueOrDefault(investmentId) + amount;
                if (month.Contains(date))
                {
                    monthly[investmentId] = monthly.GetValueOrDefault(investmentId) + amount;
                }
            }
        }

        var latestByInvestment = valuations
            .GroupBy(item => item.InvestmentId)
            .ToDictionary(group => group.Key, group => group.First());
        return investments
            .Select(investment =>
            {
                var contributions = allTime.GetValueOrDefault(investment.Id);
                latestByInvestment.TryGetValue(investment.Id, out var latest);
                var currentValue = latest?.MarketValue ?? contributions;
                return new InvestmentPosition(
                    investment,
                    contributions,
                    monthly.GetValueOrDefault(investment.Id),
                    currentValue,
                    currentValue - contributions,
                    latest);
            })
            .ToArray();
    }

    private static async Task<decimal> ReadCarryForwardAsync(
        SqliteConnection connection,
        BudgetMonth month,
        CancellationToken cancellationToken)
    {
        var carryForward = 0m;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Type, Amount
            FROM Transactions
            WHERE Date < $firstDate;
            """;
        Add(command, "$firstDate", FormatDate(month.FirstDay));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var type = (TransactionType)reader.GetInt32(0);
            var amount = ParseMoney(reader.GetString(1));
            carryForward += type switch
            {
                TransactionType.Income or TransactionType.Refund => amount,
                TransactionType.Expense or TransactionType.Savings => -amount,
                TransactionType.Transfer => 0m,
                _ => throw new InvalidDataException($"Unknown stored transaction type {(int)type}."),
            };
        }

        return carryForward;
    }

    private static async Task<AppSettings> ReadSettingsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var currencyCode = "MYR";
        var isDarkMode = false;

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM Settings;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            switch (reader.GetString(0))
            {
                case "CurrencyCode":
                    currencyCode = reader.GetString(1);
                    break;
                case "IsDarkMode":
                    isDarkMode = reader.GetString(1) == "1";
                    break;
            }
        }

        return new AppSettings(currencyCode, isDarkMode);
    }

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), Invariant);
    }

    private static async Task MigrateToVersion1Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Categories (
                Id          INTEGER PRIMARY KEY,
                Name        TEXT NOT NULL COLLATE NOCASE UNIQUE,
                Kind        INTEGER NOT NULL CHECK (Kind IN (0, 1, 2)),
                ColorHex    TEXT NOT NULL,
                SortOrder   INTEGER NOT NULL DEFAULT 0,
                IsArchived  INTEGER NOT NULL DEFAULT 0 CHECK (IsArchived IN (0, 1))
            );

            CREATE TABLE IF NOT EXISTS Transactions (
                Id          TEXT PRIMARY KEY,
                Date        TEXT NOT NULL,
                Type        INTEGER NOT NULL CHECK (Type IN (0, 1, 2, 3, 4)),
                Amount      TEXT NOT NULL CHECK (length(trim(Amount)) > 0 AND CAST(Amount AS NUMERIC) >= 0),
                CategoryId  INTEGER NULL,
                Note        TEXT NOT NULL DEFAULT '',
                CreatedUtc  TEXT NOT NULL,
                UpdatedUtc  TEXT NOT NULL,
                FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Transactions_Date
                ON Transactions(Date);
            CREATE INDEX IF NOT EXISTS IX_Transactions_CategoryId
                ON Transactions(CategoryId);

            CREATE TABLE IF NOT EXISTS Allocations (
                CategoryId    INTEGER NOT NULL,
                Year          INTEGER NOT NULL CHECK (Year BETWEEN 1 AND 9999),
                Month         INTEGER NOT NULL CHECK (Month BETWEEN 1 AND 12),
                PlannedAmount TEXT NOT NULL CHECK (length(trim(PlannedAmount)) > 0 AND CAST(PlannedAmount AS NUMERIC) >= 0),
                PRIMARY KEY (CategoryId, Year, Month),
                FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS RecurringBills (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Name        TEXT NOT NULL,
                Amount      TEXT NOT NULL CHECK (length(trim(Amount)) > 0 AND CAST(Amount AS NUMERIC) >= 0),
                DueDay      INTEGER NOT NULL CHECK (DueDay BETWEEN 1 AND 31),
                CategoryId  INTEGER NULL,
                IsActive    INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1)),
                StartDate   TEXT NULL,
                EndDate     TEXT NULL,
                CHECK (StartDate IS NULL OR EndDate IS NULL OR StartDate <= EndDate),
                FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS SavingsGoals (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                Name          TEXT NOT NULL,
                TargetAmount  TEXT NOT NULL CHECK (length(trim(TargetAmount)) > 0 AND CAST(TargetAmount AS NUMERIC) >= 0),
                CurrentAmount TEXT NOT NULL CHECK (length(trim(CurrentAmount)) > 0 AND CAST(CurrentAmount AS NUMERIC) >= 0),
                TargetDate    TEXT NULL,
                ColorHex      TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Accounts (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Name        TEXT NOT NULL,
                AccountType TEXT NOT NULL,
                IsActive    INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1))
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            -- These triggers also protect databases created by the earlier,
            -- unversioned preview schema, whose existing tables lack CHECK clauses.
            CREATE TRIGGER IF NOT EXISTS TR_Transactions_Validate_Insert
            BEFORE INSERT ON Transactions
            WHEN NEW.Type NOT IN (0, 1, 2, 3, 4)
                 OR length(trim(NEW.Amount)) = 0
                 OR CAST(NEW.Amount AS NUMERIC) < 0
                 OR (NEW.CategoryId IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM Categories AS category
                        WHERE category.Id = NEW.CategoryId
                          AND ((NEW.Type = 0 AND category.Kind = 2)
                               OR (NEW.Type IN (1, 3) AND category.Kind = 0)
                               OR (NEW.Type = 2 AND category.Kind = 1))))
            BEGIN
                SELECT RAISE(ABORT, 'Invalid transaction values.');
            END;

            CREATE TRIGGER IF NOT EXISTS TR_Transactions_Validate_Update
            BEFORE UPDATE ON Transactions
            WHEN NEW.Type NOT IN (0, 1, 2, 3, 4)
                 OR length(trim(NEW.Amount)) = 0
                 OR CAST(NEW.Amount AS NUMERIC) < 0
                 OR (NEW.CategoryId IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM Categories AS category
                        WHERE category.Id = NEW.CategoryId
                          AND ((NEW.Type = 0 AND category.Kind = 2)
                               OR (NEW.Type IN (1, 3) AND category.Kind = 0)
                               OR (NEW.Type = 2 AND category.Kind = 1))))
            BEGIN
                SELECT RAISE(ABORT, 'Invalid transaction values.');
            END;

            CREATE TRIGGER IF NOT EXISTS TR_Allocations_Validate_Insert
            BEFORE INSERT ON Allocations
            WHEN NEW.Year NOT BETWEEN 1 AND 9999
                 OR NEW.Month NOT BETWEEN 1 AND 12
                 OR length(trim(NEW.PlannedAmount)) = 0
                 OR CAST(NEW.PlannedAmount AS NUMERIC) < 0
            BEGIN
                SELECT RAISE(ABORT, 'Invalid allocation values.');
            END;

            CREATE TRIGGER IF NOT EXISTS TR_Allocations_Validate_Update
            BEFORE UPDATE ON Allocations
            WHEN NEW.Year NOT BETWEEN 1 AND 9999
                 OR NEW.Month NOT BETWEEN 1 AND 12
                 OR length(trim(NEW.PlannedAmount)) = 0
                 OR CAST(NEW.PlannedAmount AS NUMERIC) < 0
            BEGIN
                SELECT RAISE(ABORT, 'Invalid allocation values.');
            END;

            CREATE TRIGGER IF NOT EXISTS TR_RecurringBills_Validate_Insert
            BEFORE INSERT ON RecurringBills
            WHEN NEW.DueDay NOT BETWEEN 1 AND 31
                 OR length(trim(NEW.Amount)) = 0
                 OR CAST(NEW.Amount AS NUMERIC) < 0
                 OR NEW.IsActive NOT IN (0, 1)
                 OR (NEW.StartDate IS NOT NULL AND NEW.EndDate IS NOT NULL AND NEW.StartDate > NEW.EndDate)
            BEGIN
                SELECT RAISE(ABORT, 'Invalid recurring bill values.');
            END;

            CREATE TRIGGER IF NOT EXISTS TR_RecurringBills_Validate_Update
            BEFORE UPDATE ON RecurringBills
            WHEN NEW.DueDay NOT BETWEEN 1 AND 31
                 OR length(trim(NEW.Amount)) = 0
                 OR CAST(NEW.Amount AS NUMERIC) < 0
                 OR NEW.IsActive NOT IN (0, 1)
                 OR (NEW.StartDate IS NOT NULL AND NEW.EndDate IS NOT NULL AND NEW.StartDate > NEW.EndDate)
            BEGIN
                SELECT RAISE(ABORT, 'Invalid recurring bill values.');
            END;

            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MigrateToVersion2Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE RecurringIncomes (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Name        TEXT NOT NULL,
                Amount      TEXT NOT NULL CHECK (length(trim(Amount)) > 0 AND CAST(Amount AS NUMERIC) >= 0),
                PayDay      INTEGER NOT NULL CHECK (PayDay BETWEEN 1 AND 31),
                CategoryId  INTEGER NULL,
                IsActive    INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1)),
                StartDate   TEXT NULL,
                EndDate     TEXT NULL,
                CreatedUtc  TEXT NOT NULL,
                UpdatedUtc  TEXT NOT NULL,
                CHECK (StartDate IS NULL OR EndDate IS NULL OR StartDate <= EndDate),
                FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE SET NULL
            );

            CREATE TABLE Investments (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                PresetKey   TEXT NULL COLLATE NOCASE UNIQUE,
                Name        TEXT NOT NULL,
                Provider    TEXT NOT NULL DEFAULT '',
                Kind        INTEGER NOT NULL CHECK (Kind IN (0, 1, 2, 3)),
                UnitLabel   TEXT NOT NULL DEFAULT '',
                ColorHex    TEXT NOT NULL,
                IsArchived  INTEGER NOT NULL DEFAULT 0 CHECK (IsArchived IN (0, 1)),
                CreatedUtc  TEXT NOT NULL,
                UpdatedUtc  TEXT NOT NULL
            );

            CREATE TABLE InvestmentValuations (
                Id            TEXT PRIMARY KEY,
                InvestmentId  INTEGER NOT NULL,
                Date          TEXT NOT NULL,
                MarketValue   TEXT NOT NULL CHECK (length(trim(MarketValue)) > 0 AND CAST(MarketValue AS NUMERIC) >= 0),
                Units         TEXT NULL CHECK (Units IS NULL OR (length(trim(Units)) > 0 AND CAST(Units AS NUMERIC) >= 0)),
                UnitPrice     TEXT NULL CHECK (UnitPrice IS NULL OR (length(trim(UnitPrice)) > 0 AND CAST(UnitPrice AS NUMERIC) >= 0)),
                Note          TEXT NOT NULL DEFAULT '',
                CreatedUtc    TEXT NOT NULL,
                UpdatedUtc    TEXT NOT NULL,
                FOREIGN KEY (InvestmentId) REFERENCES Investments(Id) ON DELETE CASCADE
            );

            ALTER TABLE Transactions
                ADD COLUMN SavingsGoalId INTEGER NULL REFERENCES SavingsGoals(Id) ON DELETE SET NULL;
            ALTER TABLE Transactions
                ADD COLUMN InvestmentId INTEGER NULL REFERENCES Investments(Id) ON DELETE SET NULL;
            ALTER TABLE Transactions
                ADD COLUMN RecurringIncomeId INTEGER NULL REFERENCES RecurringIncomes(Id) ON DELETE SET NULL;
            ALTER TABLE Transactions
                ADD COLUMN RecurringOccurrenceMonth TEXT NULL;

            CREATE INDEX IX_Transactions_SavingsGoalId
                ON Transactions(SavingsGoalId);
            CREATE INDEX IX_Transactions_InvestmentId
                ON Transactions(InvestmentId);
            CREATE INDEX IX_Transactions_RecurringIncomeId
                ON Transactions(RecurringIncomeId);
            CREATE UNIQUE INDEX UX_Transactions_RecurringIncomeMonth
                ON Transactions(RecurringIncomeId, RecurringOccurrenceMonth)
                WHERE RecurringIncomeId IS NOT NULL AND RecurringOccurrenceMonth IS NOT NULL;
            CREATE INDEX IX_InvestmentValuations_InvestmentDate
                ON InvestmentValuations(InvestmentId, Date DESC);

            CREATE TRIGGER TR_Transactions_Destination_Validate_Insert
            BEFORE INSERT ON Transactions
            WHEN (NEW.SavingsGoalId IS NOT NULL AND NEW.InvestmentId IS NOT NULL)
                 OR ((NEW.SavingsGoalId IS NOT NULL OR NEW.InvestmentId IS NOT NULL) AND NEW.Type <> 2)
                 OR (NEW.RecurringIncomeId IS NOT NULL AND NEW.Type <> 0)
            BEGIN
                SELECT RAISE(ABORT, 'Invalid transaction destination.');
            END;

            CREATE TRIGGER TR_Transactions_Destination_Validate_Update
            BEFORE UPDATE ON Transactions
            WHEN (NEW.SavingsGoalId IS NOT NULL AND NEW.InvestmentId IS NOT NULL)
                 OR ((NEW.SavingsGoalId IS NOT NULL OR NEW.InvestmentId IS NOT NULL) AND NEW.Type <> 2)
                 OR (NEW.RecurringIncomeId IS NOT NULL AND NEW.Type <> 0)
            BEGIN
                SELECT RAISE(ABORT, 'Invalid transaction destination.');
            END;

            CREATE TRIGGER TR_RecurringIncomes_Validate_Insert
            BEFORE INSERT ON RecurringIncomes
            WHEN NEW.PayDay NOT BETWEEN 1 AND 31
                 OR length(trim(NEW.Amount)) = 0
                 OR CAST(NEW.Amount AS NUMERIC) < 0
                 OR NEW.IsActive NOT IN (0, 1)
                 OR (NEW.StartDate IS NOT NULL AND NEW.EndDate IS NOT NULL AND NEW.StartDate > NEW.EndDate)
                 OR (NEW.CategoryId IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM Categories AS category
                        WHERE category.Id = NEW.CategoryId AND category.Kind = 2))
            BEGIN
                SELECT RAISE(ABORT, 'Invalid recurring income values.');
            END;

            CREATE TRIGGER TR_RecurringIncomes_Validate_Update
            BEFORE UPDATE ON RecurringIncomes
            WHEN NEW.PayDay NOT BETWEEN 1 AND 31
                 OR length(trim(NEW.Amount)) = 0
                 OR CAST(NEW.Amount AS NUMERIC) < 0
                 OR NEW.IsActive NOT IN (0, 1)
                 OR (NEW.StartDate IS NOT NULL AND NEW.EndDate IS NOT NULL AND NEW.StartDate > NEW.EndDate)
                 OR (NEW.CategoryId IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM Categories AS category
                        WHERE category.Id = NEW.CategoryId AND category.Kind = 2))
            BEGIN
                SELECT RAISE(ABORT, 'Invalid recurring income values.');
            END;

            PRAGMA user_version = 2;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MigrateToVersion3Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE RecurringIncomeOccurrenceSuppressions (
                RecurringIncomeId INTEGER NOT NULL,
                OccurrenceMonth   TEXT NOT NULL
                    CHECK (length(OccurrenceMonth) = 7
                           AND OccurrenceMonth GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]'
                           AND substr(OccurrenceMonth, 5, 1) = '-'
                           AND CAST(substr(OccurrenceMonth, 1, 4) AS INTEGER) BETWEEN 1 AND 9999
                           AND CAST(substr(OccurrenceMonth, 6, 2) AS INTEGER) BETWEEN 1 AND 12),
                CreatedUtc        TEXT NOT NULL,
                PRIMARY KEY (RecurringIncomeId, OccurrenceMonth),
                FOREIGN KEY (RecurringIncomeId) REFERENCES RecurringIncomes(Id) ON DELETE CASCADE
            );

            PRAGMA user_version = 3;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ExecuteDeleteAsync(
        string table,
        string idColumn,
        object value,
        CancellationToken cancellationToken)
    {
        // Table and column names are constants supplied only by this class; the value remains parameterized.
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE {idColumn} = $id;";
        Add(command, "$id", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SeedDefaultInvestmentsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", Invariant);
        var defaults = new[]
        {
            (Key: "tabung-haji", Name: "Tabung Haji", Provider: "Lembaga Tabung Haji", Kind: InvestmentKind.SavingsFund, Unit: "RM", Color: "#14B8A6"),
            (Key: "asb", Name: "ASB", Provider: "Amanah Saham Nasional Berhad", Kind: InvestmentKind.UnitTrust, Unit: "units", Color: "#3B82F6"),
            (Key: "maybank-gold", Name: "Maybank Gold", Provider: "Maybank", Kind: InvestmentKind.Gold, Unit: "g", Color: "#F59E0B"),
        };

        foreach (var item in defaults)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Investments
                    (PresetKey, Name, Provider, Kind, UnitLabel, ColorHex, IsArchived, CreatedUtc, UpdatedUtc)
                SELECT
                    $key, $name, $provider, $kind, $unit, $color, 0, $now, $now
                WHERE NOT EXISTS (
                    SELECT 1 FROM Investments WHERE PresetKey = $key COLLATE NOCASE);
                """;
            Add(command, "$key", item.Key);
            Add(command, "$name", item.Name);
            Add(command, "$provider", item.Provider);
            Add(command, "$kind", (int)item.Kind);
            Add(command, "$unit", item.Unit);
            Add(command, "$color", item.Color);
            Add(command, "$now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertRecurringIncomeOccurrenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RecurringIncome income,
        BudgetMonth occurrenceMonth,
        DateOnly dueDate,
        string now,
        CancellationToken cancellationToken)
    {
        if (await IsRecurringIncomeOccurrenceSuppressedAsync(
                connection,
                transaction,
                income.Id,
                occurrenceMonth,
                cancellationToken))
        {
            return;
        }

        if (await TryAdoptManagedIncomeOccurrenceAsync(
                connection,
                transaction,
                income,
                occurrenceMonth,
                dueDate,
                now,
                cancellationToken))
        {
            return;
        }

        var transactionId = CreateRecurringTransactionId(income.Id, occurrenceMonth);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO Transactions
                (Id, Date, Type, Amount, CategoryId, Note, CreatedUtc, UpdatedUtc,
                 SavingsGoalId, InvestmentId, RecurringIncomeId, RecurringOccurrenceMonth)
            VALUES
                ($id, $date, $type, $amount, $categoryId, $note, $now, $now,
                 NULL, NULL, $recurringIncomeId, $occurrenceMonth);
            """;
        Add(command, "$id", transactionId.ToString("D"));
        Add(command, "$date", FormatDate(dueDate));
        Add(command, "$type", (int)TransactionType.Income);
        Add(command, "$amount", FormatMoney(income.Amount));
        AddNullable(command, "$categoryId", income.CategoryId);
        Add(command, "$note", income.Name);
        Add(command, "$now", now);
        Add(command, "$recurringIncomeId", income.Id);
        Add(command, "$occurrenceMonth", occurrenceMonth.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> IsRecurringIncomeOccurrenceSuppressedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long recurringIncomeId,
        BudgetMonth occurrenceMonth,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM RecurringIncomeOccurrenceSuppressions
                WHERE RecurringIncomeId = $recurringIncomeId
                  AND OccurrenceMonth = $occurrenceMonth);
            """;
        Add(command, "$recurringIncomeId", recurringIncomeId);
        Add(command, "$occurrenceMonth", occurrenceMonth.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), Invariant) != 0;
    }

    private static async Task<bool> TryAdoptManagedIncomeOccurrenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RecurringIncome income,
        BudgetMonth occurrenceMonth,
        DateOnly dueDate,
        string now,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                income.Name,
                MonthlyIncomePlanner.ManagedTransactionNote,
                StringComparison.Ordinal))
        {
            return false;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Transactions
            SET Date = $date,
                Amount = $amount,
                CategoryId = $categoryId,
                RecurringIncomeId = $recurringIncomeId,
                RecurringOccurrenceMonth = $occurrenceMonth,
                UpdatedUtc = $now
            WHERE Id = (
                SELECT Id
                FROM Transactions
                WHERE Type = $incomeType
                  AND RecurringIncomeId IS NULL
                  AND Note = $managedNote
                  AND Date >= $firstDay
                  AND Date <= $lastDay
                ORDER BY Date, CreatedUtc, Id
                LIMIT 1)
              AND NOT EXISTS (
                  SELECT 1
                  FROM Transactions
                  WHERE RecurringIncomeId = $recurringIncomeId
                    AND RecurringOccurrenceMonth = $occurrenceMonth);
            """;
        Add(command, "$date", FormatDate(dueDate));
        Add(command, "$amount", FormatMoney(income.Amount));
        AddNullable(command, "$categoryId", income.CategoryId);
        Add(command, "$recurringIncomeId", income.Id);
        Add(command, "$occurrenceMonth", occurrenceMonth.ToString());
        Add(command, "$now", now);
        Add(command, "$incomeType", (int)TransactionType.Income);
        Add(command, "$managedNote", MonthlyIncomePlanner.ManagedTransactionNote);
        Add(command, "$firstDay", FormatDate(occurrenceMonth.FirstDay));
        Add(command, "$lastDay", FormatDate(occurrenceMonth.LastDay));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static Guid CreateRecurringTransactionId(long recurringIncomeId, BudgetMonth month)
    {
        var key = Encoding.UTF8.GetBytes($"MyBudget/RecurringIncome/{recurringIncomeId}/{month}");
        var bytes = SHA256.HashData(key)[..16];
        // Mark the deterministic value as an RFC 4122 version-5-style UUID.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static async Task<long> UpsertInvestmentRecordAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Investment investment,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (investment.Id <= 0)
        {
            command.CommandText = """
                INSERT INTO Investments
                    (Name, Provider, Kind, UnitLabel, ColorHex, IsArchived, CreatedUtc, UpdatedUtc)
                VALUES
                    ($name, $provider, $kind, $unitLabel, $color, $isArchived, $now, $now)
                RETURNING Id;
                """;
        }
        else
        {
            command.CommandText = """
                INSERT INTO Investments
                    (Id, Name, Provider, Kind, UnitLabel, ColorHex, IsArchived, CreatedUtc, UpdatedUtc)
                VALUES
                    ($id, $name, $provider, $kind, $unitLabel, $color, $isArchived, $now, $now)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    Provider = excluded.Provider,
                    Kind = excluded.Kind,
                    UnitLabel = excluded.UnitLabel,
                    ColorHex = excluded.ColorHex,
                    IsArchived = excluded.IsArchived,
                    UpdatedUtc = excluded.UpdatedUtc
                RETURNING Id;
                """;
            Add(command, "$id", investment.Id);
        }

        Add(command, "$name", investment.Name.Trim());
        Add(command, "$provider", investment.Provider?.Trim() ?? string.Empty);
        Add(command, "$kind", (int)investment.Kind);
        Add(command, "$unitLabel", investment.UnitLabel?.Trim() ?? string.Empty);
        Add(command, "$color", string.IsNullOrWhiteSpace(investment.ColorHex) ? "#14B8A6" : investment.ColorHex.Trim());
        Add(command, "$isArchived", investment.IsArchived ? 1 : 0);
        Add(command, "$now", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), Invariant);
    }

    private static async Task UpsertInvestmentValuationRecordAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        InvestmentValuation valuation,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO InvestmentValuations
                (Id, InvestmentId, Date, MarketValue, Units, UnitPrice, Note, CreatedUtc, UpdatedUtc)
            VALUES
                ($id, $investmentId, $date, $marketValue, $units, $unitPrice, $note, $now, $now)
            ON CONFLICT(Id) DO UPDATE SET
                InvestmentId = excluded.InvestmentId,
                Date = excluded.Date,
                MarketValue = excluded.MarketValue,
                Units = excluded.Units,
                UnitPrice = excluded.UnitPrice,
                Note = excluded.Note,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        Add(command, "$id", valuation.Id.ToString("D"));
        Add(command, "$investmentId", valuation.InvestmentId);
        Add(command, "$date", FormatDate(valuation.Date));
        Add(command, "$marketValue", FormatMoney(valuation.MarketValue));
        AddNullable(command, "$units", valuation.Units is null ? null : FormatMoney(valuation.Units.Value));
        AddNullable(command, "$unitPrice", valuation.UnitPrice is null ? null : FormatMoney(valuation.UnitPrice.Value));
        Add(command, "$note", valuation.Note ?? string.Empty);
        Add(command, "$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureTransactionDestinationsCompatibleAsync(
        SqliteConnection connection,
        BudgetTransaction transaction,
        CancellationToken cancellationToken)
    {
        TransactionDestinationRules.Validate(transaction);

        if (transaction.SavingsGoalId is long goalId)
        {
            await EnsureRowExistsAsync(
                connection,
                "SavingsGoals",
                goalId,
                "The selected savings goal does not exist.",
                nameof(transaction),
                cancellationToken);
        }

        if (transaction.InvestmentId is long investmentId)
        {
            await EnsureRowExistsAsync(
                connection,
                "Investments",
                investmentId,
                "The selected investment does not exist.",
                nameof(transaction),
                cancellationToken);
        }

        if (transaction.RecurringIncomeId is long recurringIncomeId)
        {
            await EnsureRowExistsAsync(
                connection,
                "RecurringIncomes",
                recurringIncomeId,
                "The selected recurring income source does not exist.",
                nameof(transaction),
                cancellationToken);
        }
    }

    private static async Task EnsureInvestmentExistsAsync(
        SqliteConnection connection,
        long investmentId,
        CancellationToken cancellationToken)
    {
        await EnsureRowExistsAsync(
            connection,
            "Investments",
            investmentId,
            "The selected investment does not exist.",
            nameof(investmentId),
            cancellationToken);
    }

    private static async Task EnsureRowExistsAsync(
        SqliteConnection connection,
        string table,
        long id,
        string message,
        string parameterName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 FROM {table} WHERE Id = $id;";
        Add(command, "$id", id);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    private static async Task EnsureCategoryKindAsync(
        SqliteConnection connection,
        long categoryId,
        CategoryKind expectedKind,
        string subject,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Kind FROM Categories WHERE Id = $categoryId;";
        Add(command, "$categoryId", categoryId);
        var storedKind = await command.ExecuteScalarAsync(cancellationToken);
        if (storedKind is null or DBNull)
        {
            throw new ArgumentException($"{subject} does not exist.", nameof(categoryId));
        }

        var actualKind = (CategoryKind)Convert.ToInt32(storedKind, Invariant);
        if (actualKind != expectedKind)
        {
            throw new ArgumentException($"{subject} must be a {expectedKind} category.", nameof(categoryId));
        }
    }

    private static async Task EnsureTransactionCategoryCompatibleAsync(
        SqliteConnection connection,
        BudgetTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (transaction.CategoryId is null)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Kind FROM Categories WHERE Id = $categoryId;";
        Add(command, "$categoryId", transaction.CategoryId.Value);
        var storedKind = await command.ExecuteScalarAsync(cancellationToken);
        if (storedKind is null or DBNull)
        {
            throw new ArgumentException("The selected transaction category does not exist.", nameof(transaction));
        }

        var categoryKind = (CategoryKind)Convert.ToInt32(storedKind, Invariant);
        if (!Enum.IsDefined(categoryKind) ||
            !TransactionCategoryRules.IsCompatible(transaction.Type, categoryKind))
        {
            throw new ArgumentException(
                $"A {transaction.Type} transaction cannot use a {categoryKind} category.",
                nameof(transaction));
        }
    }

    private static async Task<HashSet<string>> ReadTransactionIdsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id FROM Transactions;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static async Task InsertSettingIfMissingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO Settings (Key, Value) VALUES ($key, $value);";
        Add(command, "$key", key);
        Add(command, "$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertSettingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Settings (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        Add(command, "$key", key);
        Add(command, "$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertTransactionWithinTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BudgetTransaction item,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Transactions
                (Id, Date, Type, Amount, CategoryId, Note, CreatedUtc, UpdatedUtc,
                 SavingsGoalId, InvestmentId, RecurringIncomeId, RecurringOccurrenceMonth)
            VALUES
                ($id, $date, $type, $amount, $categoryId, $note, $now, $now,
                 $savingsGoalId, $investmentId, NULL, NULL);
            """;
        Add(command, "$id", item.Id.ToString("D"));
        Add(command, "$date", FormatDate(item.Date));
        Add(command, "$type", (int)item.Type);
        Add(command, "$amount", FormatMoney(item.Amount));
        AddNullable(command, "$categoryId", item.CategoryId);
        Add(command, "$note", item.Note ?? string.Empty);
        AddNullable(command, "$savingsGoalId", item.SavingsGoalId);
        AddNullable(command, "$investmentId", item.InvestmentId);
        Add(command, "$now", DateTimeOffset.UtcNow.ToString("O", Invariant));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool TryCreateImportedTransaction(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> header,
        IReadOnlyDictionary<string, BudgetCategory> categories,
        IReadOnlyDictionary<string, SavingsGoal> goals,
        IReadOnlyDictionary<string, Investment> investments,
        out BudgetTransaction? transaction)
    {
        transaction = null;
        string Field(string name, bool trim = true)
        {
            var index = header[name];
            if (index >= row.Count)
            {
                return string.Empty;
            }

            return trim ? row[index].Trim() : row[index];
        }

        string OptionalField(string name, bool trim = true)
        {
            if (!header.TryGetValue(name, out var index) || index >= row.Count)
            {
                return string.Empty;
            }

            return trim ? row[index].Trim() : row[index];
        }

        if (!DateOnly.TryParseExact(Field("Date"), "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var date) ||
            !Enum.TryParse<TransactionType>(Field("Type"), ignoreCase: true, out var type) ||
            !Enum.IsDefined(type) ||
            !decimal.TryParse(Field("Amount"), NumberStyles.Number, Invariant, out var amount) ||
            amount < 0)
        {
            return false;
        }

        var categoryName = CsvCodec.RestoreNeutralizedSpreadsheetFormula(Field("Category"));
        long? categoryId = null;
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            if (!categories.TryGetValue(categoryName, out var category))
            {
                return false;
            }

            if (!TransactionCategoryRules.IsCompatible(type, category.Kind))
            {
                return false;
            }

            categoryId = category.Id;
        }

        long? goalId = null;
        var goalName = CsvCodec.RestoreNeutralizedSpreadsheetFormula(OptionalField("Goal"));
        if (!string.IsNullOrWhiteSpace(goalName))
        {
            if (!goals.TryGetValue(goalName, out var goal))
            {
                return false;
            }

            goalId = goal.Id;
        }

        long? investmentId = null;
        var investmentName = CsvCodec.RestoreNeutralizedSpreadsheetFormula(OptionalField("Investment"));
        if (!string.IsNullOrWhiteSpace(investmentName))
        {
            if (!investments.TryGetValue(investmentName, out var investment))
            {
                return false;
            }

            investmentId = investment.Id;
        }

        if ((goalId is not null || investmentId is not null) && type != TransactionType.Savings
            || goalId is not null && investmentId is not null)
        {
            return false;
        }

        var id = Guid.NewGuid();
        if (header.TryGetValue("Id", out var idIndex) && idIndex < row.Count)
        {
            var idText = row[idIndex].Trim();
            if (!string.IsNullOrEmpty(idText) && !Guid.TryParse(idText, out id))
            {
                return false;
            }
        }

        transaction = new BudgetTransaction(
            id,
            date,
            type,
            amount,
            categoryId,
            CsvCodec.RestoreNeutralizedSpreadsheetFormula(Field("Note", trim: false)),
            goalId,
            investmentId);
        return true;
    }

    private static void ReplaceFileAtomically(string temporaryPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return;
        }

        File.Move(temporaryPath, destinationPath);
    }

    private static void Add(SqliteCommand command, string name, object value)
    {
        command.Parameters.AddWithValue(name, value);
    }

    private static void AddNullable(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static void EnsureNonNegative(decimal amount, string parameterName)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Money values cannot be negative.");
        }
    }

    private static void ValidateInvestment(Investment investment)
    {
        ArgumentNullException.ThrowIfNull(investment);
        if (string.IsNullOrWhiteSpace(investment.Name))
        {
            throw new ArgumentException("An investment needs a name.", nameof(investment));
        }

        if (!Enum.IsDefined(investment.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(investment.Kind), investment.Kind, "Unknown investment kind.");
        }
    }

    private static void ValidateInvestmentValuation(InvestmentValuation valuation)
    {
        ArgumentNullException.ThrowIfNull(valuation);
        if (valuation.Id == Guid.Empty)
        {
            throw new ArgumentException("An investment valuation needs a stable identifier.", nameof(valuation));
        }

        EnsureNonNegative(valuation.MarketValue, nameof(valuation.MarketValue));
        EnsureNullableNonNegative(valuation.Units, nameof(valuation.Units));
        EnsureNullableNonNegative(valuation.UnitPrice, nameof(valuation.UnitPrice));
    }

    private static void EnsureNullableNonNegative(decimal? amount, string parameterName)
    {
        if (amount is < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Values cannot be negative.");
        }
    }

    private static void EnsureDefinedTransactionType(TransactionType type, string parameterName)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(parameterName, type, "The transaction type is not supported.");
        }
    }

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", Invariant);

    private static DateOnly ParseDate(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", Invariant);

    private static string FormatMoney(decimal amount) => amount.ToString(Invariant);

    private static decimal ParseMoney(string value) => decimal.Parse(value, NumberStyles.Number, Invariant);
}
