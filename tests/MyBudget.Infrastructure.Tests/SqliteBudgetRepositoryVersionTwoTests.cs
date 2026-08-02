using System.Text;
using Microsoft.Data.Sqlite;
using MyBudget.Core;

namespace MyBudget.Infrastructure.Tests;

[TestClass]
public sealed class SqliteBudgetRepositoryVersionTwoTests
{
    private static readonly BudgetMonth January = new(2026, 1);
    private static readonly BudgetMonth February = new(2026, 2);
    private static readonly BudgetMonth June = new(2026, 6);
    private static readonly BudgetMonth July = new(2026, 7);
    private static readonly BudgetMonth August = new(2026, 8);

    private string _temporaryDirectory = null!;
    private string _databasePath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "MyBudget.Infrastructure.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporaryDirectory);
        _databasePath = Path.Combine(_temporaryDirectory, "mybudget.db");
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
    public async Task Initialize_MigratesVersionOneDataWithoutLossAndRemainsIdempotent()
    {
        var transactionId = Guid.NewGuid();
        await CreateVersionOneDatabaseAsync(_databasePath, transactionId);

        var repository = new SqliteBudgetRepository(_databasePath);
        await repository.InitializeAsync();
        await repository.InitializeAsync();

        await using (var connection = await OpenAsync(_databasePath))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version;";
            Assert.AreEqual(2L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }

        var snapshot = await repository.LoadAsync(July);

        var category = snapshot.Categories.Single(item => item.Id == 42);
        Assert.AreEqual("Legacy food", category.Name);
        var transaction = snapshot.Transactions.Single(item => item.Id == transactionId);
        Assert.AreEqual(new DateOnly(2026, 7, 8), transaction.Date);
        Assert.AreEqual(12.34m, transaction.Amount);
        Assert.AreEqual("Legacy lunch", transaction.Note);
        Assert.IsNull(transaction.SavingsGoalId);
        Assert.IsNull(transaction.InvestmentId);
        Assert.IsNull(transaction.RecurringIncomeId);
        Assert.AreEqual(90m, snapshot.Allocations.Single(item => item.CategoryId == 42).PlannedAmount);
        Assert.AreEqual("Legacy bill", Assert.ContainsSingle(snapshot.Bills).Name);

        var goal = snapshot.Goals.Single(item => item.Id == 77);
        Assert.AreEqual(25m, goal.StartingAmount);
        Assert.AreEqual(0m, goal.LinkedSavingsAmount);
        Assert.AreEqual(25m, goal.CurrentAmount);
        Assert.AreEqual("Legacy account", snapshot.Accounts.Single(item => item.Id == 12).Name);
        Assert.AreEqual("SGD", snapshot.Settings.CurrencyCode);
        Assert.IsTrue(snapshot.Settings.IsDarkMode);

        Assert.IsEmpty(snapshot.RecurringIncomes);
        Assert.HasCount(3, snapshot.Investments);
        Assert.AreEqual(3, snapshot.Investments.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.IsTrue(snapshot.Categories.Any(item => item.Kind == CategoryKind.Income));
    }

    [TestMethod]
    public async Task RecurringIncome_SynchronizesClampedDueDatesExactlyOnceUnderConcurrency()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var salaryCategory = (await repository.LoadAsync(January)).Categories
            .Single(item => item.Kind == CategoryKind.Income && item.Name == "Salary");
        var sourceId = await repository.UpsertRecurringIncomeAsync(
            new RecurringIncome(
                0,
                "Monthly salary",
                3_500m,
                31,
                salaryCategory.Id,
                StartDate: new DateOnly(2026, 1, 1)),
            January);

        await repository.SynchronizeRecurringIncomeAsync(new DateOnly(2026, 2, 27));
        Assert.IsEmpty((await repository.LoadAsync(February)).Transactions);

        var concurrentRepositories = Enumerable.Range(0, 8)
            .Select(_ => new SqliteBudgetRepository(_databasePath))
            .ToArray();
        await Task.WhenAll(concurrentRepositories.Select(item =>
            item.SynchronizeRecurringIncomeAsync(new DateOnly(2026, 2, 28))));
        await repository.SynchronizeRecurringIncomeAsync(new DateOnly(2026, 2, 28));

        var januaryDeposit = Assert.ContainsSingle((await repository.LoadAsync(January)).Transactions);
        Assert.AreEqual(new DateOnly(2026, 1, 31), januaryDeposit.Date);
        Assert.AreEqual(sourceId, januaryDeposit.RecurringIncomeId);
        Assert.AreEqual(TransactionType.Income, januaryDeposit.Type);
        Assert.AreEqual(salaryCategory.Id, januaryDeposit.CategoryId);

        var februaryDeposit = Assert.ContainsSingle((await repository.LoadAsync(February)).Transactions);
        Assert.AreEqual(new DateOnly(2026, 2, 28), februaryDeposit.Date);
        Assert.AreEqual(sourceId, februaryDeposit.RecurringIncomeId);
        Assert.AreEqual(3_500m, februaryDeposit.Amount);
    }

    [TestMethod]
    public async Task RecurringIncome_EditUsesEffectiveMonthAndDeactivateKeepsHistory()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var salaryCategoryId = (await repository.LoadAsync(January)).Categories
            .Single(item => item.Kind == CategoryKind.Income && item.Name == "Salary")
            .Id;
        var sourceId = await repository.UpsertRecurringIncomeAsync(
            new RecurringIncome(0, "Salary", 1_000m, 31, salaryCategoryId, StartDate: new DateOnly(2026, 1, 1)),
            January);
        await repository.SynchronizeRecurringIncomeAsync(new DateOnly(2026, 1, 31));

        await repository.UpsertRecurringIncomeAsync(
            new RecurringIncome(sourceId, "Salary", 1_200m, 15, salaryCategoryId, StartDate: new DateOnly(2026, 1, 1)),
            January);
        await repository.SynchronizeRecurringIncomeAsync(new DateOnly(2026, 1, 31));
        await repository.SynchronizeRecurringIncomeAsync(new DateOnly(2026, 2, 15));

        var januaryDeposit = Assert.ContainsSingle((await repository.LoadAsync(January)).Transactions);
        Assert.AreEqual(new DateOnly(2026, 1, 31), januaryDeposit.Date);
        Assert.AreEqual(1_000m, januaryDeposit.Amount);
        var februaryDeposit = Assert.ContainsSingle((await repository.LoadAsync(February)).Transactions);
        Assert.AreEqual(new DateOnly(2026, 2, 15), februaryDeposit.Date);
        Assert.AreEqual(1_200m, februaryDeposit.Amount);

        await repository.DeleteRecurringIncomeAsync(sourceId);
        await repository.SynchronizeRecurringIncomeAsync(new DateOnly(2026, 3, 31));

        var march = await repository.LoadAsync(new BudgetMonth(2026, 3));
        Assert.IsEmpty(march.Transactions);
        var source = Assert.ContainsSingle(march.RecurringIncomes);
        Assert.AreEqual(sourceId, source.Id);
        Assert.IsFalse(source.IsActive);
        Assert.AreEqual(1_000m, Assert.ContainsSingle((await repository.LoadAsync(January)).Transactions).Amount);
    }

    [TestMethod]
    public async Task RecurringIncome_ConvertedLegacyMonthlyIncomeKeepsOneOccurrence()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var salaryCategoryId = (await repository.LoadAsync(January)).Categories
            .Single(item => item.Kind == CategoryKind.Income && item.Name == "Salary")
            .Id;
        var legacyTransactionId = Guid.NewGuid();
        await repository.UpsertTransactionAsync(new BudgetTransaction(
            legacyTransactionId,
            new DateOnly(2026, 1, 1),
            TransactionType.Income,
            1_000m,
            salaryCategoryId,
            MonthlyIncomePlanner.ManagedTransactionNote));
        var sourceId = await repository.UpsertRecurringIncomeAsync(
            new RecurringIncome(
                0,
                MonthlyIncomePlanner.ManagedTransactionNote,
                1_000m,
                31,
                salaryCategoryId,
                StartDate: January.FirstDay),
            January);

        await repository.UpsertTransactionAsync(new BudgetTransaction(
            legacyTransactionId,
            new DateOnly(2026, 1, 31),
            TransactionType.Income,
            1_000m,
            salaryCategoryId,
            MonthlyIncomePlanner.ManagedTransactionNote,
            RecurringIncomeId: sourceId));
        await repository.SynchronizeRecurringIncomeAsync(new DateOnly(2026, 1, 31));

        var transaction = Assert.ContainsSingle((await repository.LoadAsync(January)).Transactions);
        Assert.AreEqual(legacyTransactionId, transaction.Id);
        Assert.AreEqual(sourceId, transaction.RecurringIncomeId);
        Assert.AreEqual(new DateOnly(2026, 1, 31), transaction.Date);
        Assert.AreEqual(1_000m, transaction.Amount);
    }

    [TestMethod]
    public async Task RecurringIncome_SynchronizationAdoptsUnlinkedLegacyMonthlyIncome()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var salaryCategoryId = (await repository.LoadAsync(January)).Categories
            .Single(item => item.Kind == CategoryKind.Income && item.Name == "Salary")
            .Id;
        var legacyTransactionId = Guid.NewGuid();
        await repository.UpsertTransactionAsync(new BudgetTransaction(
            legacyTransactionId,
            new DateOnly(2026, 1, 1),
            TransactionType.Income,
            900m,
            salaryCategoryId,
            MonthlyIncomePlanner.ManagedTransactionNote));
        var sourceId = await repository.UpsertRecurringIncomeAsync(
            new RecurringIncome(
                0,
                MonthlyIncomePlanner.ManagedTransactionNote,
                1_200m,
                15,
                salaryCategoryId,
                StartDate: January.FirstDay),
            January);

        await repository.SynchronizeRecurringIncomeAsync(new DateOnly(2026, 1, 15));
        await repository.SynchronizeRecurringIncomeAsync(new DateOnly(2026, 1, 31));

        var transaction = Assert.ContainsSingle((await repository.LoadAsync(January)).Transactions);
        Assert.AreEqual(legacyTransactionId, transaction.Id);
        Assert.AreEqual(sourceId, transaction.RecurringIncomeId);
        Assert.AreEqual(new DateOnly(2026, 1, 15), transaction.Date);
        Assert.AreEqual(1_200m, transaction.Amount);
    }

    [TestMethod]
    public async Task CarryForward_RecalculatesAfterHistoricalTransactionsAreEditedOrDeleted()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var categories = (await repository.LoadAsync(June)).Categories;
        var salaryCategoryId = categories.Single(item => item.Name == "Salary").Id;
        var foodCategoryId = categories.Single(item => item.Name == "Food").Id;
        var savingsCategoryId = categories.Single(item => item.Kind == CategoryKind.Savings).Id;
        var expenseId = Guid.NewGuid();
        var savingsId = Guid.NewGuid();

        await repository.UpsertTransactionAsync(new BudgetTransaction(
            Guid.NewGuid(), new DateOnly(2026, 6, 1), TransactionType.Income, 1_000m, salaryCategoryId, "Income"));
        await repository.UpsertTransactionAsync(new BudgetTransaction(
            Guid.NewGuid(), new DateOnly(2026, 6, 2), TransactionType.Refund, 50m, foodCategoryId, "Refund"));
        await repository.UpsertTransactionAsync(new BudgetTransaction(
            expenseId, new DateOnly(2026, 6, 3), TransactionType.Expense, 200m, foodCategoryId, "Expense"));
        await repository.UpsertTransactionAsync(new BudgetTransaction(
            savingsId, new DateOnly(2026, 6, 4), TransactionType.Savings, 100m, savingsCategoryId, "Savings"));
        await repository.UpsertTransactionAsync(new BudgetTransaction(
            Guid.NewGuid(), new DateOnly(2026, 6, 5), TransactionType.Transfer, 999m, null, "Neutral transfer"));

        Assert.AreEqual(750m, (await repository.LoadAsync(July)).CarryForward);

        await repository.UpsertTransactionAsync(new BudgetTransaction(
            expenseId, new DateOnly(2026, 6, 3), TransactionType.Expense, 250m, foodCategoryId, "Edited expense"));
        Assert.AreEqual(700m, (await repository.LoadAsync(July)).CarryForward);

        await repository.DeleteTransactionAsync(savingsId);
        Assert.AreEqual(800m, (await repository.LoadAsync(July)).CarryForward);

        await repository.UpsertTransactionAsync(new BudgetTransaction(
            Guid.NewGuid(), new DateOnly(2026, 7, 1), TransactionType.Expense, 50m, foodCategoryId, "July expense"));
        Assert.AreEqual(800m, (await repository.LoadAsync(July)).CarryForward);
        Assert.AreEqual(750m, (await repository.LoadAsync(August)).CarryForward);
    }

    [TestMethod]
    public async Task GoalProgress_FollowsSavingsTransactionEditsRelinksAndDeletes()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var savingsCategoryId = (await repository.LoadAsync(July)).Categories
            .Single(item => item.Kind == CategoryKind.Savings)
            .Id;
        await repository.UpsertSavingsGoalAsync(new SavingsGoal(101, "Emergency", 5_000m, 50m));
        await repository.UpsertSavingsGoalAsync(new SavingsGoal(102, "Holiday", 2_000m, 20m));
        var transactionId = Guid.NewGuid();

        await repository.UpsertTransactionAsync(new BudgetTransaction(
            transactionId,
            new DateOnly(2026, 7, 10),
            TransactionType.Savings,
            100m,
            savingsCategoryId,
            "First goal",
            SavingsGoalId: 101));

        var first = await repository.LoadAsync(July);
        Assert.AreEqual(150m, first.Goals.Single(item => item.Id == 101).CurrentAmount);
        Assert.AreEqual(20m, first.Goals.Single(item => item.Id == 102).CurrentAmount);

        // Saving an already-loaded goal must not fold the derived contribution
        // into its starting amount and count the same transaction twice.
        await repository.UpsertSavingsGoalAsync(first.Goals.Single(item => item.Id == 101) with
        {
            Name = "Emergency edited",
        });
        var afterGoalEdit = await repository.LoadAsync(July);
        var editedGoal = afterGoalEdit.Goals.Single(item => item.Id == 101);
        Assert.AreEqual(50m, editedGoal.StartingAmount);
        Assert.AreEqual(100m, editedGoal.LinkedSavingsAmount);
        Assert.AreEqual(150m, editedGoal.CurrentAmount);

        await repository.UpsertTransactionAsync(new BudgetTransaction(
            transactionId,
            new DateOnly(2026, 7, 11),
            TransactionType.Savings,
            175m,
            savingsCategoryId,
            "Relinked goal",
            SavingsGoalId: 102));

        var relinked = await repository.LoadAsync(July);
        Assert.AreEqual(50m, relinked.Goals.Single(item => item.Id == 101).CurrentAmount);
        Assert.AreEqual(195m, relinked.Goals.Single(item => item.Id == 102).CurrentAmount);
        Assert.AreEqual(102L, Assert.ContainsSingle(relinked.Transactions).SavingsGoalId);

        await repository.DeleteTransactionAsync(transactionId);

        var deleted = await repository.LoadAsync(July);
        Assert.AreEqual(50m, deleted.Goals.Single(item => item.Id == 101).CurrentAmount);
        Assert.AreEqual(20m, deleted.Goals.Single(item => item.Id == 102).CurrentAmount);
    }

    [TestMethod]
    public async Task SavingsDestination_RejectsMissingIncompatibleAndAmbiguousLinks()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var snapshot = await repository.LoadAsync(July);
        var savingsCategoryId = snapshot.Categories.Single(item => item.Kind == CategoryKind.Savings).Id;
        var expenseCategoryId = snapshot.Categories.Single(item => item.Name == "Food").Id;
        var investmentId = snapshot.Investments[0].Id;
        await repository.UpsertSavingsGoalAsync(new SavingsGoal(201, "Goal", 1_000m, 0m));

        await Assert.ThrowsAsync<ArgumentException>(() => repository.UpsertTransactionAsync(new BudgetTransaction(
            Guid.NewGuid(), new DateOnly(2026, 7, 1), TransactionType.Savings, 10m, savingsCategoryId,
            SavingsGoalId: 201, InvestmentId: investmentId)));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.UpsertTransactionAsync(new BudgetTransaction(
            Guid.NewGuid(), new DateOnly(2026, 7, 1), TransactionType.Expense, 10m, expenseCategoryId,
            SavingsGoalId: 201)));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.UpsertTransactionAsync(new BudgetTransaction(
            Guid.NewGuid(), new DateOnly(2026, 7, 1), TransactionType.Savings, 10m, savingsCategoryId,
            SavingsGoalId: 999_999)));

        Assert.IsEmpty((await repository.LoadAsync(July)).Transactions);
    }

    [TestMethod]
    public async Task VersionTwoSchema_RejectsInvalidDestinationsEvenForDirectSqlWrites()
    {
        var repository = await CreateInitializedRepositoryAsync();
        await repository.UpsertSavingsGoalAsync(new SavingsGoal(205, "Emergency", 2_000m, 0m));
        var snapshot = await repository.LoadAsync(July);
        var savingsCategoryId = snapshot.Categories.Single(item => item.Name == "Savings").Id;
        var investmentId = snapshot.Investments.Single(item => item.Name == "ASB").Id;

        await using var connection = await OpenAsync(_databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Transactions
                (Id, Date, Type, Amount, CategoryId, Note, CreatedUtc, UpdatedUtc,
                 SavingsGoalId, InvestmentId, RecurringIncomeId, RecurringOccurrenceMonth)
            VALUES
                ($id, '2026-07-01', 2, '10', $categoryId, '', 'now', 'now',
                 205, $investmentId, NULL, NULL);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$categoryId", savingsCategoryId);
        command.Parameters.AddWithValue("$investmentId", investmentId);

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    [TestMethod]
    public async Task DeleteGoal_UnlinksItsSavingsWithoutDeletingTheTransaction()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var savingsCategoryId = (await repository.LoadAsync(July)).Categories
            .Single(item => item.Kind == CategoryKind.Savings)
            .Id;
        await repository.UpsertSavingsGoalAsync(new SavingsGoal(211, "Temporary goal", 500m, 25m));
        var transactionId = Guid.NewGuid();
        await repository.UpsertTransactionAsync(new BudgetTransaction(
            transactionId,
            new DateOnly(2026, 7, 2),
            TransactionType.Savings,
            75m,
            savingsCategoryId,
            "Keep this savings entry",
            SavingsGoalId: 211));

        await repository.DeleteSavingsGoalAsync(211);

        var snapshot = await repository.LoadAsync(July);
        Assert.IsFalse(snapshot.Goals.Any(item => item.Id == 211));
        var transaction = Assert.ContainsSingle(snapshot.Transactions);
        Assert.AreEqual(transactionId, transaction.Id);
        Assert.IsNull(transaction.SavingsGoalId);
        Assert.AreEqual(75m, transaction.Amount);
    }

    [TestMethod]
    public async Task Investments_SeedOnceAndPortfolioUsesContributionsAndLatestHistoricalValuation()
    {
        var repository = await CreateInitializedRepositoryAsync();
        await repository.InitializeAsync();
        var initial = await repository.LoadAsync(July);

        CollectionAssert.AreEquivalent(
            new[] { "Tabung Haji", "ASB", "Maybank Gold" },
            initial.Investments.Select(item => item.Name).ToArray());
        Assert.AreEqual(3, initial.Investments.Select(item => item.Id).Distinct().Count());

        var investment = initial.Investments.Single(item => item.Name == "Tabung Haji");
        var savingsCategoryId = initial.Categories.Single(item => item.Kind == CategoryKind.Savings).Id;
        await repository.UpsertTransactionAsync(new BudgetTransaction(
            Guid.NewGuid(), new DateOnly(2026, 6, 20), TransactionType.Savings, 500m, savingsCategoryId,
            "June contribution", InvestmentId: investment.Id));
        await repository.UpsertTransactionAsync(new BudgetTransaction(
            Guid.NewGuid(), new DateOnly(2026, 7, 20), TransactionType.Savings, 250m, savingsCategoryId,
            "July contribution", InvestmentId: investment.Id));

        var juneValuationId = Guid.NewGuid();
        var julyValuationId = Guid.NewGuid();
        await repository.UpsertInvestmentValuationAsync(new InvestmentValuation(
            juneValuationId, investment.Id, new DateOnly(2026, 6, 30), 525m, Note: "June close"));
        await repository.UpsertInvestmentValuationAsync(new InvestmentValuation(
            julyValuationId, investment.Id, new DateOnly(2026, 7, 31), 800m, Note: "July close"));
        await repository.UpsertInvestmentValuationAsync(new InvestmentValuation(
            Guid.NewGuid(), investment.Id, new DateOnly(2026, 8, 31), 900m, Note: "Future value"));

        var junePosition = (await repository.LoadInvestmentPortfolioAsync(June))
            .Single(item => item.Investment.Id == investment.Id);
        Assert.AreEqual(500m, junePosition.AllTimeContributions);
        Assert.AreEqual(500m, junePosition.MonthlyContributions);
        Assert.AreEqual(525m, junePosition.CurrentValue);
        Assert.AreEqual(25m, junePosition.GainLoss);
        Assert.AreEqual(juneValuationId, junePosition.LatestValuation?.Id);

        var julyPosition = (await repository.LoadInvestmentPortfolioAsync(July))
            .Single(item => item.Investment.Id == investment.Id);
        Assert.AreEqual(750m, julyPosition.AllTimeContributions);
        Assert.AreEqual(250m, julyPosition.MonthlyContributions);
        Assert.AreEqual(800m, julyPosition.CurrentValue);
        Assert.AreEqual(50m, julyPosition.GainLoss);
        Assert.AreEqual(julyValuationId, julyPosition.LatestValuation?.Id);

        await repository.UpsertInvestmentValuationAsync(new InvestmentValuation(
            julyValuationId,
            investment.Id,
            new DateOnly(2026, 7, 31),
            825m,
            Units: 825m,
            UnitPrice: 1m,
            Note: "Corrected July close"));
        var corrected = (await repository.LoadInvestmentPortfolioAsync(July))
            .Single(item => item.Investment.Id == investment.Id);
        Assert.AreEqual(825m, corrected.CurrentValue);
        Assert.AreEqual(75m, corrected.GainLoss);
        Assert.AreEqual(825m, corrected.LatestValuation?.Units);
        Assert.AreEqual("Corrected July close", corrected.LatestValuation?.Note);

        await repository.DeleteInvestmentValuationAsync(julyValuationId);
        var afterDelete = (await repository.LoadInvestmentPortfolioAsync(July))
            .Single(item => item.Investment.Id == investment.Id);
        Assert.AreEqual(juneValuationId, afterDelete.LatestValuation?.Id);
        Assert.AreEqual(525m, afterDelete.CurrentValue);
        Assert.AreEqual(-225m, afterDelete.GainLoss);
    }

    [TestMethod]
    public async Task Investments_CustomUpsertAndArchiveRoundTrip()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var id = await repository.UpsertInvestmentAsync(new Investment(
            0,
            "Global ETF",
            "Broker",
            InvestmentKind.Other,
            "units",
            "#123456"));
        await repository.UpsertInvestmentAsync(new Investment(
            id,
            "Global ETF edited",
            "New broker",
            InvestmentKind.UnitTrust,
            "shares",
            "#654321"));

        var saved = (await repository.LoadAsync(July)).Investments.Single(item => item.Id == id);
        Assert.AreEqual("Global ETF edited", saved.Name);
        Assert.AreEqual("New broker", saved.Provider);
        Assert.AreEqual(InvestmentKind.UnitTrust, saved.Kind);
        Assert.AreEqual("shares", saved.UnitLabel);
        Assert.IsFalse(saved.IsArchived);

        await repository.ArchiveInvestmentAsync(id);

        var archived = (await repository.LoadAsync(July)).Investments.Single(item => item.Id == id);
        Assert.IsTrue(archived.IsArchived);
    }

    [TestMethod]
    public async Task InvestmentWithValuation_CommitsBothRecordsTogether()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var valuationId = Guid.NewGuid();

        var investmentId = await repository.UpsertInvestmentWithValuationAsync(
            new Investment(0, "Global ETF", "Broker", InvestmentKind.Other, "units", "#123456"),
            new InvestmentValuation(
                valuationId,
                0,
                new DateOnly(2026, 7, 20),
                1_234.56m,
                Units: 10m,
                UnitPrice: 123.456m,
                Note: "Opening value"));

        var snapshot = await repository.LoadAsync(July);
        var investment = snapshot.Investments.Single(item => item.Id == investmentId);
        var valuation = snapshot.InvestmentValuations.Single(item => item.Id == valuationId);
        Assert.AreEqual("Global ETF", investment.Name);
        Assert.AreEqual(investmentId, valuation.InvestmentId);
        Assert.AreEqual(1_234.56m, valuation.MarketValue);
        Assert.AreEqual("Opening value", valuation.Note);
    }

    [TestMethod]
    public async Task InvestmentWithValuation_RollsBackInvestmentWhenValuationWriteFails()
    {
        var repository = await CreateInitializedRepositoryAsync();
        await using (var connection = await OpenAsync(_databasePath))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER TR_Test_RejectInvestmentValuation
                BEFORE INSERT ON InvestmentValuations
                BEGIN
                    SELECT RAISE(ABORT, 'Forced valuation failure.');
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<SqliteException>(() => repository.UpsertInvestmentWithValuationAsync(
            new Investment(0, "Must roll back", "Broker", InvestmentKind.Other, "units", "#123456"),
            new InvestmentValuation(
                Guid.NewGuid(),
                0,
                new DateOnly(2026, 7, 20),
                500m)));

        var snapshot = await repository.LoadAsync(July);
        Assert.IsFalse(snapshot.Investments.Any(item => item.Name == "Must roll back"));
        Assert.IsEmpty(snapshot.InvestmentValuations);
    }

    [TestMethod]
    public async Task CsvExportAndImport_RoundTripsGoalAndInvestmentDestinationsByName()
    {
        var source = await CreateInitializedRepositoryAsync();
        var sourceSnapshot = await source.LoadAsync(July);
        var savingsCategoryId = sourceSnapshot.Categories.Single(item => item.Kind == CategoryKind.Savings).Id;
        var sourceInvestment = sourceSnapshot.Investments.Single(item => item.Name == "ASB");
        await source.UpsertSavingsGoalAsync(new SavingsGoal(301, "New home", 50_000m, 1_000m));
        var goalTransactionId = Guid.NewGuid();
        var investmentTransactionId = Guid.NewGuid();
        await source.UpsertTransactionAsync(new BudgetTransaction(
            goalTransactionId,
            new DateOnly(2026, 7, 3),
            TransactionType.Savings,
            125m,
            savingsCategoryId,
            "Goal contribution",
            SavingsGoalId: 301));
        await source.UpsertTransactionAsync(new BudgetTransaction(
            investmentTransactionId,
            new DateOnly(2026, 7, 4),
            TransactionType.Savings,
            225m,
            savingsCategoryId,
            "Investment contribution",
            InvestmentId: sourceInvestment.Id));

        var csvPath = Path.Combine(_temporaryDirectory, "destinations.csv");
        await source.ExportTransactionsCsvAsync(July, csvPath);

        var header = (await File.ReadAllLinesAsync(csvPath))[0].TrimStart('\uFEFF');
        Assert.AreEqual("Id,Date,Type,Amount,Category,Note,Goal,Investment", header);

        var importedRepository = new SqliteBudgetRepository(Path.Combine(_temporaryDirectory, "imported.db"));
        await importedRepository.InitializeAsync();
        await importedRepository.UpsertSavingsGoalAsync(new SavingsGoal(401, "New home", 50_000m, 0m));
        var importedInvestment = (await importedRepository.LoadAsync(July)).Investments
            .Single(item => item.Name == "ASB");

        var result = await importedRepository.ImportTransactionsCsvAsync(csvPath);
        var imported = await importedRepository.LoadAsync(July);
        var transactions = imported.Transactions.ToDictionary(item => item.Id);

        Assert.AreEqual(2, result.ImportedCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.AreEqual(401L, transactions[goalTransactionId].SavingsGoalId);
        Assert.IsNull(transactions[goalTransactionId].InvestmentId);
        Assert.AreEqual(importedInvestment.Id, transactions[investmentTransactionId].InvestmentId);
        Assert.IsNull(transactions[investmentTransactionId].SavingsGoalId);
        Assert.IsNull(transactions[goalTransactionId].RecurringIncomeId);
        Assert.AreEqual(125m, imported.Goals.Single(item => item.Id == 401).LinkedSavingsAmount);
    }

    [TestMethod]
    public async Task CsvImport_AcceptsLegacyHeaderWithoutDestinationColumns()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var csvPath = Path.Combine(_temporaryDirectory, "legacy.csv");
        var csv = """
            Id,Date,Type,Amount,Category,Note
            ,2026-07-01,Income,100,Salary,Legacy salary
            ,2026-07-02,Expense,20,Food,Legacy lunch
            ,2026-07-03,Savings,25,Savings,Unassigned savings
            """;
        await File.WriteAllTextAsync(csvPath, csv, new UTF8Encoding(false));

        var result = await repository.ImportTransactionsCsvAsync(csvPath);
        var imported = await repository.LoadAsync(July);

        Assert.AreEqual(3, result.ImportedCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.HasCount(3, imported.Transactions);
        Assert.IsTrue(imported.Transactions.All(item => item.SavingsGoalId is null));
        Assert.IsTrue(imported.Transactions.All(item => item.InvestmentId is null));
        Assert.IsTrue(imported.Transactions.All(item => item.RecurringIncomeId is null));
    }

    [TestMethod]
    public async Task CsvImport_SkipsUnknownAmbiguousAndIncompatibleDestinations()
    {
        var repository = await CreateInitializedRepositoryAsync();
        await repository.UpsertSavingsGoalAsync(new SavingsGoal(501, "Emergency", 5_000m, 0m));
        var csvPath = Path.Combine(_temporaryDirectory, "invalid-destinations.csv");
        var csv = """
            Id,Date,Type,Amount,Category,Note,Goal,Investment
            ,2026-07-01,Savings,10,Savings,Valid,Emergency,
            ,2026-07-02,Savings,20,Savings,Unknown goal,Missing,
            ,2026-07-03,Savings,30,Savings,Two destinations,Emergency,ASB
            ,2026-07-04,Expense,40,Food,Expense destination,Emergency,
            """;
        await File.WriteAllTextAsync(csvPath, csv, new UTF8Encoding(false));

        var result = await repository.ImportTransactionsCsvAsync(csvPath);
        var imported = await repository.LoadAsync(July);

        Assert.AreEqual(1, result.ImportedCount);
        Assert.AreEqual(3, result.SkippedCount);
        var transaction = Assert.ContainsSingle(imported.Transactions);
        Assert.AreEqual(501L, transaction.SavingsGoalId);
        Assert.AreEqual("Valid", transaction.Note);
    }

    private async Task<SqliteBudgetRepository> CreateInitializedRepositoryAsync()
    {
        var repository = new SqliteBudgetRepository(_databasePath);
        await repository.InitializeAsync();
        return repository;
    }

    private static async Task<SqliteConnection> OpenAsync(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private static async Task CreateVersionOneDatabaseAsync(string path, Guid transactionId)
    {
        await using var connection = await OpenAsync(path);
        await using var command = connection.CreateCommand();
        command.CommandText = $$"""
            CREATE TABLE Categories (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                Kind INTEGER NOT NULL,
                ColorHex TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsArchived INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE Transactions (
                Id TEXT PRIMARY KEY,
                Date TEXT NOT NULL,
                Type INTEGER NOT NULL,
                Amount TEXT NOT NULL,
                CategoryId INTEGER NULL,
                Note TEXT NOT NULL DEFAULT '',
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE SET NULL);
            CREATE TABLE Allocations (
                CategoryId INTEGER NOT NULL,
                Year INTEGER NOT NULL,
                Month INTEGER NOT NULL,
                PlannedAmount TEXT NOT NULL,
                PRIMARY KEY (CategoryId, Year, Month),
                FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE CASCADE);
            CREATE TABLE RecurringBills (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Amount TEXT NOT NULL,
                DueDay INTEGER NOT NULL,
                CategoryId INTEGER NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                StartDate TEXT NULL,
                EndDate TEXT NULL,
                FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE SET NULL);
            CREATE TABLE SavingsGoals (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                TargetAmount TEXT NOT NULL,
                CurrentAmount TEXT NOT NULL,
                TargetDate TEXT NULL,
                ColorHex TEXT NOT NULL);
            CREATE TABLE Accounts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                AccountType TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);

            INSERT INTO Categories VALUES (42, 'Legacy food', 0, '#111111', 420, 0);
            INSERT INTO Transactions VALUES (
                '{{transactionId:D}}', '2026-07-08', 1, '12.34', 42, 'Legacy lunch',
                '2026-07-08T01:02:03.0000000+00:00', '2026-07-08T01:02:03.0000000+00:00');
            INSERT INTO Allocations VALUES (42, 2026, 7, '90');
            INSERT INTO RecurringBills VALUES (66, 'Legacy bill', '19.95', 9, 42, 1, '2026-01-01', NULL);
            INSERT INTO SavingsGoals VALUES (77, 'Legacy goal', '100', '25', '2026-12-31', '#222222');
            INSERT INTO Accounts VALUES (12, 'Legacy account', 'Cash', 1);
            INSERT INTO Settings VALUES ('CurrencyCode', 'SGD');
            INSERT INTO Settings VALUES ('IsDarkMode', '1');
            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync();
    }
}
