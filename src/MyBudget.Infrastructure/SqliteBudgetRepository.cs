using System.Globalization;
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
    private const int CurrentSchemaVersion = 1;
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
        new(7, "Other", CategoryKind.Expense, "#64748B", 70)
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
        switch (schemaVersion)
        {
            case 0:
                await MigrateToVersion1Async(connection, transaction, cancellationToken);
                break;
            case CurrentSchemaVersion:
                break;
            default:
                throw new InvalidDataException(
                    $"This database uses schema version {schemaVersion}, but this version of MyBudget supports up to {CurrentSchemaVersion}.");
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

        return new BudgetSnapshot(
            month,
            categories,
            transactions,
            allocations,
            bills,
            goals,
            accounts,
            settings);
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
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Transactions
                (Id, Date, Type, Amount, CategoryId, Note, CreatedUtc, UpdatedUtc)
            VALUES
                ($id, $date, $type, $amount, $categoryId, $note, $now, $now)
            ON CONFLICT(Id) DO UPDATE SET
                Date = excluded.Date,
                Type = excluded.Type,
                Amount = excluded.Amount,
                CategoryId = excluded.CategoryId,
                Note = excluded.Note,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        Add(command, "$id", transaction.Id.ToString("D"));
        Add(command, "$date", FormatDate(transaction.Date));
        Add(command, "$type", (int)transaction.Type);
        Add(command, "$amount", FormatMoney(transaction.Amount));
        AddNullable(command, "$categoryId", transaction.CategoryId);
        Add(command, "$note", transaction.Note ?? string.Empty);
        Add(command, "$now", DateTimeOffset.UtcNow.ToString("O", Invariant));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteTransactionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await ExecuteDeleteAsync("Transactions", "Id", id.ToString("D"), cancellationToken);
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
        EnsureNonNegative(goal.CurrentAmount, nameof(goal.CurrentAmount));

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
        Add(command, "$current", FormatMoney(goal.CurrentAmount));
        AddNullable(command, "$targetDate", goal.TargetDate is null ? null : FormatDate(goal.TargetDate.Value));
        Add(command, "$color", goal.ColorHex);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteSavingsGoalAsync(long id, CancellationToken cancellationToken = default)
    {
        await ExecuteDeleteAsync("SavingsGoals", "Id", id, cancellationToken);
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
        await using (var connection = await OpenConnectionAsync(cancellationToken))
        {
            categories = await ReadCategoriesAsync(connection, cancellationToken);
            transactions = await ReadTransactionsAsync(connection, month, cancellationToken);
        }

        var namesById = categories.ToDictionary(category => category.Id, category => category.Name);
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
                await writer.WriteLineAsync("Id,Date,Type,Amount,Category,Note".AsMemory(), cancellationToken);
                foreach (var item in transactions.OrderBy(item => item.Date).ThenBy(item => item.Id))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var categoryName = item.CategoryId is long categoryId && namesById.TryGetValue(categoryId, out var name)
                        ? name
                        : string.Empty;
                    var row = string.Join(",",
                        CsvCodec.Escape(item.Id.ToString("D")),
                        CsvCodec.Escape(FormatDate(item.Date)),
                        CsvCodec.Escape(item.Type.ToString()),
                        CsvCodec.Escape(FormatMoney(item.Amount)),
                        CsvCodec.Escape(CsvCodec.NeutralizeSpreadsheetFormula(categoryName)),
                        CsvCodec.Escape(CsvCodec.NeutralizeSpreadsheetFormula(item.Note ?? string.Empty)));
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

            if (!TryCreateImportedTransaction(row, header, categoriesByName, out var item))
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
            SELECT Id, Date, Type, Amount, CategoryId, Note
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
                reader.GetString(5)));
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
        var result = new List<SavingsGoal>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, TargetAmount, CurrentAmount, TargetDate, ColorHex
            FROM SavingsGoals
            ORDER BY Name;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SavingsGoal(
                reader.GetInt64(0),
                reader.GetString(1),
                ParseMoney(reader.GetString(2)),
                ParseMoney(reader.GetString(3)),
                reader.IsDBNull(4) ? null : ParseDate(reader.GetString(4)),
                reader.GetString(5)));
        }

        return result;
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
                (Id, Date, Type, Amount, CategoryId, Note, CreatedUtc, UpdatedUtc)
            VALUES
                ($id, $date, $type, $amount, $categoryId, $note, $now, $now);
            """;
        Add(command, "$id", item.Id.ToString("D"));
        Add(command, "$date", FormatDate(item.Date));
        Add(command, "$type", (int)item.Type);
        Add(command, "$amount", FormatMoney(item.Amount));
        AddNullable(command, "$categoryId", item.CategoryId);
        Add(command, "$note", item.Note ?? string.Empty);
        Add(command, "$now", DateTimeOffset.UtcNow.ToString("O", Invariant));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool TryCreateImportedTransaction(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> header,
        IReadOnlyDictionary<string, BudgetCategory> categories,
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
            CsvCodec.RestoreNeutralizedSpreadsheetFormula(Field("Note", trim: false)));
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
