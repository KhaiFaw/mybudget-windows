using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using MyBudget.Core;

namespace MyBudget.App.ViewModels;

public sealed class MainPageViewModel : ObservableObject
{
    private readonly IBudgetRepository _repository;
    private DateOnly _localToday;
    private BudgetMonth _selectedMonth;
    private BudgetSnapshot _snapshot;
    private bool _isBusy;
    private bool _isDarkMode;
    private bool _isInitialized;
    private string _currencyCode = "MYR";
    private string _statusText = "Opening your local budget…";
    private bool _statusIsError;
    private TransactionTypeOption? _selectedTransactionType;
    private CategoryOption? _selectedTransactionCategory;
    private DateTimeOffset _transactionDate;
    private double _transactionAmount;
    private string _transactionNote = string.Empty;
    private double _monthlyIncomeAmount;
    private string _monthlyIncomeEditorHintText = "Other income entries will stay untouched.";
    private string _billName = string.Empty;
    private double _billAmount;
    private double _billDueDay = 1;
    private CategoryOption? _selectedBillCategory;
    private RecurringBill? _billBeingEdited;
    private string _goalName = string.Empty;
    private double _goalTargetAmount;
    private double _goalCurrentAmount;
    private DateTimeOffset _goalTargetDate = DateTimeOffset.Now.AddMonths(6);

    public MainPageViewModel(IBudgetRepository repository, string databasePath)
    {
        _repository = repository;
        DatabasePath = databasePath;
        _localToday = BudgetDateSelection.GetLocalToday();
        _selectedMonth = BudgetMonth.FromDate(_localToday);
        _snapshot = BudgetSnapshot.Empty(_selectedMonth);
        _transactionDate = ToLocalDateTimeOffset(_localToday);

        TransactionTypes = Enum.GetValues<TransactionType>()
            .Select(type => new TransactionTypeOption(type, SplitWords(type.ToString())))
            .ToArray();
        SelectedTransactionType = TransactionTypes.First(option => option.Type == TransactionType.Expense);

        PreviousMonthCommand = new AsyncRelayCommand(
            () => ChangeMonthAsync(_selectedMonth.Previous),
            CanRunCommand);
        NextMonthCommand = new AsyncRelayCommand(
            () => ChangeMonthAsync(_selectedMonth.Next),
            CanRunCommand);
        CurrentMonthCommand = new AsyncRelayCommand(ChangeToCurrentMonthAsync, CanRunCommand);
        SavePlanCommand = new AsyncRelayCommand(SavePlanAsync, CanRunCommand);
        AddTransactionCommand = new AsyncRelayCommand(AddTransactionAsync, CanRunCommand);
        SaveMonthlyIncomeCommand = new AsyncRelayCommand(SaveMonthlyIncomeAsync, CanRunCommand);
        AddBillCommand = new AsyncRelayCommand(AddBillAsync, CanRunCommand);
        CancelBillEditCommand = new RelayCommand(ResetBillForm, CanRunCommand);
        AddGoalCommand = new AsyncRelayCommand(AddGoalAsync, CanRunCommand);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, CanRunCommand);
        SeedDemoDataCommand = new AsyncRelayCommand(SeedDemoDataAsync, CanRunCommand);
    }

    public event EventHandler<bool>? ThemeRequested;

    public IAsyncRelayCommand PreviousMonthCommand { get; }
    public IAsyncRelayCommand NextMonthCommand { get; }
    public IAsyncRelayCommand CurrentMonthCommand { get; }
    public IAsyncRelayCommand SavePlanCommand { get; }
    public IAsyncRelayCommand AddTransactionCommand { get; }
    public IAsyncRelayCommand SaveMonthlyIncomeCommand { get; }
    public IAsyncRelayCommand AddBillCommand { get; }
    public IRelayCommand CancelBillEditCommand { get; }
    public IAsyncRelayCommand AddGoalCommand { get; }
    public IAsyncRelayCommand SaveSettingsCommand { get; }
    public IAsyncRelayCommand SeedDemoDataCommand { get; }

    public IReadOnlyList<TransactionTypeOption> TransactionTypes { get; }
    public IReadOnlyList<string> CurrencyCodes { get; } = ["MYR", "USD", "SGD", "EUR", "GBP", "AUD"];
    public ObservableCollection<CategoryOption> CategoryOptions { get; } = [];
    public ObservableCollection<CategoryOption> AvailableTransactionCategories { get; } = [];
    public ObservableCollection<CategoryOption> BillCategoryOptions { get; } = [];
    public ObservableCollection<CategoryBudgetRow> CategoryRows { get; } = [];
    public ObservableCollection<TransactionRow> Transactions { get; } = [];
    public ObservableCollection<BillRow> Bills { get; } = [];
    public ObservableCollection<GoalRow> Goals { get; } = [];
    public ObservableCollection<MonthlyTrendRow> MonthlyTrend { get; } = [];

    public string DatabasePath { get; }
    public string SelectedMonthText => new DateTime(_selectedMonth.Year, _selectedMonth.Month, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture);
    public string TodayHeadingText => _localToday
        .ToDateTime(TimeOnly.MinValue)
        .ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture);
    public string TodayContextText => _selectedMonth.Contains(_localToday)
        ? "Viewing the current month. New entries default to your PC's local date."
        : $"Today is {_localToday:dd MMM yyyy}; viewing {SelectedMonthText}.";
    public string IncomeText { get; private set; } = "RM 0.00";
    public string PlannedText { get; private set; } = "RM 0.00";
    public string SpentText { get; private set; } = "RM 0.00";
    public string SavedText { get; private set; } = "RM 0.00";
    public string AvailableText { get; private set; } = "RM 0.00";
    public string RemainingToPlanText { get; private set; } = "RM 0.00";
    public string BudgetHealthText { get; private set; } = "Add income to begin your plan.";
    public string SpendingPercentText { get; private set; } = "0% used";
    public double SpendingPercent { get; private set; }
    public string TransactionCountText => Transactions.Count == 1 ? "1 entry" : $"{Transactions.Count} entries";
    public bool IsTransactionCategoryEnabled => SelectedTransactionType?.Type is not (TransactionType.Income or TransactionType.Transfer);
    public string LocalSaveText => IsBusy ? "Saving…" : StatusText;
    public Visibility EmptyTransactionsVisibility => Transactions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyBillsVisibility => Bills.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyGoalsVisibility => Goals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StatusErrorVisibility => StatusIsError ? Visibility.Visible : Visibility.Collapsed;
    public bool IsEditingBill => _billBeingEdited is not null;
    public string BillFormTitle => IsEditingBill ? "Edit recurring bill" : "Add recurring bill";
    public string BillSubmitText => IsEditingBill ? "Save bill changes" : "Add recurring bill";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(LocalSaveText));
                NotifyCommandCanExecuteChanged();
            }
        }
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        private set => SetProperty(ref _isDarkMode, value);
    }

    public string CurrencyCode
    {
        get => _currencyCode;
        set => SetProperty(ref _currencyCode, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(LocalSaveText));
            }
        }
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set
        {
            if (SetProperty(ref _statusIsError, value))
            {
                OnPropertyChanged(nameof(StatusErrorVisibility));
            }
        }
    }

    public TransactionTypeOption? SelectedTransactionType
    {
        get => _selectedTransactionType;
        set
        {
            if (SetProperty(ref _selectedTransactionType, value))
            {
                UpdateTransactionCategoryOptions();
                OnPropertyChanged(nameof(IsTransactionCategoryEnabled));
            }
        }
    }

    public CategoryOption? SelectedTransactionCategory
    {
        get => _selectedTransactionCategory;
        set => SetProperty(ref _selectedTransactionCategory, value);
    }

    public DateTimeOffset TransactionDate
    {
        get => _transactionDate;
        set => SetProperty(ref _transactionDate, value);
    }

    public double TransactionAmount
    {
        get => _transactionAmount;
        set => SetProperty(ref _transactionAmount, value);
    }

    public string TransactionNote
    {
        get => _transactionNote;
        set => SetProperty(ref _transactionNote, value);
    }

    public double MonthlyIncomeAmount
    {
        get => _monthlyIncomeAmount;
        set => SetProperty(ref _monthlyIncomeAmount, value);
    }

    public string MonthlyIncomeEditorHintText
    {
        get => _monthlyIncomeEditorHintText;
        private set => SetProperty(ref _monthlyIncomeEditorHintText, value);
    }

    public string BillName
    {
        get => _billName;
        set => SetProperty(ref _billName, value);
    }

    public double BillAmount
    {
        get => _billAmount;
        set => SetProperty(ref _billAmount, value);
    }

    public double BillDueDay
    {
        get => _billDueDay;
        set => SetProperty(ref _billDueDay, value);
    }

    public CategoryOption? SelectedBillCategory
    {
        get => _selectedBillCategory;
        set => SetProperty(ref _selectedBillCategory, value);
    }

    public string GoalName
    {
        get => _goalName;
        set => SetProperty(ref _goalName, value);
    }

    public double GoalTargetAmount
    {
        get => _goalTargetAmount;
        set => SetProperty(ref _goalTargetAmount, value);
    }

    public double GoalCurrentAmount
    {
        get => _goalCurrentAmount;
        set => SetProperty(ref _goalCurrentAmount, value);
    }

    public DateTimeOffset GoalTargetDate
    {
        get => _goalTargetDate;
        set => SetProperty(ref _goalTargetDate, value);
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized || IsBusy)
        {
            return;
        }

        var localToday = BudgetDateSelection.GetLocalToday();
        var currentMonth = BudgetMonth.FromDate(localToday);
        var initialized = await RunAsync(async () =>
        {
            await _repository.InitializeAsync();
            var loadedMonth = await ReadMonthAsync(currentMonth);

            _localToday = localToday;
            _selectedMonth = currentMonth;
            TransactionDate = ToLocalDateTimeOffset(localToday);
            ApplyLoadedMonth(loadedMonth);
            StatusText = "Saved locally";
        }, "We couldn't open your local budget.");

        _isInitialized = initialized;
    }

    /// <summary>
    /// Refreshes local-date dependent state after the PC clock crosses midnight.
    /// When the user is following the current month, a month boundary also loads
    /// the new month. Manually browsed historical months remain selected.
    /// </summary>
    public async Task RefreshForLocalDateAsync()
    {
        var refreshedToday = BudgetDateSelection.GetLocalToday();
        if (refreshedToday == _localToday || IsBusy)
        {
            return;
        }

        var previousToday = _localToday;
        var previousLocalMonth = BudgetMonth.FromDate(previousToday);
        var currentMonth = BudgetMonth.FromDate(refreshedToday);
        var targetMonth = _selectedMonth == previousLocalMonth
            ? currentMonth
            : _selectedMonth;
        var monthChanged = targetMonth != _selectedMonth;

        var selectedTransactionDate = DateOnly.FromDateTime(TransactionDate.Date);
        var transactionWasFollowingToday = selectedTransactionDate == previousToday;
        var refreshedTransactionDate = transactionWasFollowingToday
            ? refreshedToday
            : monthChanged
                ? BudgetDateSelection.MoveToMonth(selectedTransactionDate, targetMonth)
                : selectedTransactionDate;

        if (monthChanged && _isInitialized)
        {
            await RunAsync(async () =>
            {
                var loadedMonth = await ReadMonthAsync(targetMonth);

                _localToday = refreshedToday;
                _selectedMonth = targetMonth;
                TransactionDate = ToLocalDateTimeOffset(refreshedTransactionDate);
                ApplyLoadedMonth(loadedMonth);
            }, "We couldn't load the new local month.");
            return;
        }

        _localToday = refreshedToday;
        if (monthChanged)
        {
            _selectedMonth = targetMonth;
        }
        TransactionDate = ToLocalDateTimeOffset(refreshedTransactionDate);

        RefreshBills(_snapshot);
        NotifyDateContextChanged();
    }

    public void UseTodayForTransaction()
    {
        if (IsBusy)
        {
            return;
        }

        // Resolve the PC date at click time. Leave _localToday unchanged here so
        // RefreshForLocalDateAsync can still detect and process a midnight/month
        // boundary if the timer has not fired yet.
        TransactionDate = ToLocalDateTimeOffset(BudgetDateSelection.GetLocalToday());
        NotifyDateContextChanged();
    }

    public void BeginEditBill(long id)
    {
        if (IsBusy)
        {
            return;
        }

        var bill = _snapshot.Bills.FirstOrDefault(item => item.Id == id);
        if (bill is null)
        {
            SetError("That recurring bill is no longer available.");
            return;
        }

        _billBeingEdited = bill;
        BillName = bill.Name;
        BillAmount = (double)bill.Amount;
        BillDueDay = bill.DueDay;
        SelectedBillCategory = BillCategoryOptions.FirstOrDefault(option => option.Id == bill.CategoryId);
        NotifyBillEditorChanged();
    }

    public async Task SetDarkModeAsync(bool isDarkMode)
    {
        if (IsBusy)
        {
            ThemeRequested?.Invoke(this, IsDarkMode);
            return;
        }

        IsDarkMode = isDarkMode;
        ThemeRequested?.Invoke(this, isDarkMode);

        if (!_isInitialized)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _repository.SaveSettingsAsync(new AppSettings(CurrencyCode, isDarkMode));
            _snapshot = _snapshot with { Settings = new AppSettings(CurrencyCode, isDarkMode) };
            StatusText = isDarkMode ? "Dark mode saved locally" : "Light mode saved locally";
        }, "The theme changed, but the preference could not be saved.");
    }

    public async Task DeleteTransactionAsync(Guid id) => await RunAndReloadAsync(
        () => _repository.DeleteTransactionAsync(id),
        "Transaction deleted",
        "We couldn't delete that transaction.");

    public async Task DeleteBillAsync(long id)
    {
        var deleted = await RunAndReloadAsync(
            () => _repository.DeleteRecurringBillAsync(id),
            "Bill deleted",
            "We couldn't delete that bill.");

        if (deleted && _billBeingEdited?.Id == id)
        {
            ResetBillForm();
        }
    }

    public async Task DeleteGoalAsync(long id) => await RunAndReloadAsync(
        () => _repository.DeleteSavingsGoalAsync(id),
        "Goal deleted",
        "We couldn't delete that goal.");

    public async Task CreateBackupAsync(string path) => await RunAsync(async () =>
    {
        await _repository.CreateBackupAsync(path);
        StatusText = "Backup created";
    }, "We couldn't create the backup.");

    public async Task ExportCsvAsync(string path) => await RunAsync(async () =>
    {
        await _repository.ExportTransactionsCsvAsync(_selectedMonth, path);
        StatusText = "Transactions exported";
    }, "We couldn't export the transactions.");

    public async Task ImportCsvAsync(string path)
    {
        CsvImportResult? importResult = null;
        var imported = await RunAndReloadAsync(async () =>
        {
            importResult = await _repository.ImportTransactionsCsvAsync(path);
        }, "Transactions imported", "We couldn't import that CSV file.");

        if (imported && !StatusIsError && importResult is not null)
        {
            StatusText = $"Imported {importResult.ImportedCount}; skipped {importResult.SkippedCount}";
        }
    }

    private async Task ChangeToCurrentMonthAsync()
    {
        var localToday = BudgetDateSelection.GetLocalToday();
        await ChangeMonthAsync(BudgetMonth.FromDate(localToday), localToday);
    }

    private async Task ChangeMonthAsync(BudgetMonth month, DateOnly? resolvedLocalToday = null)
    {
        if (IsBusy)
        {
            return;
        }

        var localToday = resolvedLocalToday ?? BudgetDateSelection.GetLocalToday();
        var selectedDate = DateOnly.FromDateTime(TransactionDate.Date);
        var targetTransactionDate = month.Contains(localToday)
            ? localToday
            : BudgetDateSelection.MoveToMonth(selectedDate, month);

        await RunAsync(async () =>
        {
            var loadedMonth = await ReadMonthAsync(month);

            _localToday = localToday;
            _selectedMonth = month;
            TransactionDate = ToLocalDateTimeOffset(targetTransactionDate);
            ApplyLoadedMonth(loadedMonth);
        }, "We couldn't load that month.");
    }

    private async Task<LoadedMonth> ReadMonthAsync(BudgetMonth selectedMonth)
    {
        var selectedSnapshot = await _repository.LoadAsync(selectedMonth);
        var month = selectedMonth;
        var snapshots = new List<BudgetSnapshot> { selectedSnapshot };

        for (var index = 1; index < 6; index++)
        {
            if (month.Year == 1 && month.Month == 1)
            {
                break;
            }

            month = month.Previous;
            snapshots.Add(await _repository.LoadAsync(month));
        }

        return new LoadedMonth(selectedSnapshot, snapshots.AsEnumerable().Reverse().ToArray());
    }

    private void ApplyLoadedMonth(LoadedMonth loadedMonth)
    {
        _snapshot = loadedMonth.Snapshot;
        ApplySnapshot(_snapshot);
        Replace(MonthlyTrend, loadedMonth.TrendSnapshots.Select(snapshot =>
        {
            var summary = BudgetCalculator.Calculate(snapshot);
            return new MonthlyTrendRow(
                new DateTime(snapshot.Month.Year, snapshot.Month.Month, 1).ToString("MMM", CultureInfo.CurrentCulture),
                FormatMoney(summary.Income),
                FormatMoney(summary.Spent),
                FormatMoney(summary.Saved));
        }));
    }

    private void ApplySnapshot(BudgetSnapshot snapshot)
    {
        var summary = BudgetCalculator.Calculate(snapshot);
        CurrencyCode = snapshot.Settings.CurrencyCode;
        IsDarkMode = snapshot.Settings.IsDarkMode;
        ThemeRequested?.Invoke(this, IsDarkMode);

        IncomeText = FormatMoney(summary.Income);
        MonthlyIncomeAmount = (double)summary.Income;
        var incomeAdjustment = MonthlyIncomePlanner.Plan(snapshot.Transactions, summary.Income);
        MonthlyIncomeEditorHintText = incomeAdjustment.OtherIncomeTotal > 0m
            ? $"{FormatMoney(incomeAdjustment.OtherIncomeTotal)} is recorded in other income entries and will stay untouched."
            : "Set the monthly total here; other income entries will stay untouched.";
        PlannedText = FormatMoney(summary.Planned);
        SpentText = FormatMoney(summary.Spent);
        SavedText = FormatMoney(summary.Saved);
        AvailableText = FormatMoney(summary.Available);
        RemainingToPlanText = FormatMoney(summary.RemainingToPlan);
        SpendingPercent = summary.Planned <= 0m ? 0d : (double)Math.Clamp(summary.Spent / summary.Planned * 100m, 0m, 100m);
        SpendingPercentText = summary.Planned <= 0m ? "No plan yet" : $"{summary.Spent / summary.Planned:P0} used";
        BudgetHealthText = summary.Income <= 0m
            ? "Add income to see your monthly breathing room."
            : summary.Available < 0m
                ? $"You're {FormatMoney(Math.Abs(summary.Available))} over available cash."
                : $"You still have {FormatMoney(summary.Available)} available this month.";

        Replace(CategoryOptions, snapshot.Categories
            .Where(category => !category.IsArchived)
            .Select(category => new CategoryOption(category.Id, category.Name, category.Kind)));
        Replace(BillCategoryOptions, CategoryOptions.Where(option => option.Kind == CategoryKind.Expense));
        UpdateTransactionCategoryOptions();
        SelectedBillCategory = _billBeingEdited is null
            ? BillCategoryOptions.FirstOrDefault()
            : BillCategoryOptions.FirstOrDefault(option => option.Id == _billBeingEdited.CategoryId);

        Replace(CategoryRows, summary.Categories
            .Where(progress => !progress.Category.IsArchived && progress.Category.Kind != CategoryKind.Income)
            .Select(progress => new CategoryBudgetRow(
                progress.Category.Id,
                progress.Category.Name,
                progress.Category.ColorHex,
                (double)progress.Planned,
                FormatMoney(progress.Actual),
                FormatMoney(progress.Remaining),
                (double)progress.ChartPercent,
                progress.IsOverBudget)));

        Replace(Transactions, snapshot.Transactions
            .OrderByDescending(transaction => transaction.Date)
            .ThenByDescending(transaction => transaction.Amount)
            .Select(transaction => new TransactionRow(
                transaction.Id,
                transaction.Date.ToString("dd MMM yyyy", CultureInfo.CurrentCulture),
                SplitWords(transaction.Type.ToString()),
                transaction.Note.Length == 0 ? "No note" : transaction.Note,
                snapshot.Categories.FirstOrDefault(category => category.Id == transaction.CategoryId)?.Name ?? "Uncategorised",
                FormatSignedMoney(transaction),
                transaction.Type is TransactionType.Expense ? "Expense" : "Positive")));

        RefreshBills(snapshot);

        Replace(Goals, snapshot.Goals
            .OrderBy(goal => goal.TargetDate)
            .Select(goal => new GoalRow(
                goal.Id,
                goal.Name,
                FormatMoney(goal.CurrentAmount),
                $"of {FormatMoney(goal.TargetAmount)}",
                goal.TargetDate is null ? "No target date" : $"Target {goal.TargetDate:dd MMM yyyy}",
                (double)Math.Clamp(goal.PercentComplete, 0m, 100m),
                $"{goal.PercentComplete:N0}%")));

        NotifyDashboardChanged();
    }

    private async Task SavePlanAsync()
    {
        var allocations = new List<BudgetAllocation>(CategoryRows.Count);
        foreach (var row in CategoryRows)
        {
            if (!double.IsFinite(row.PlannedAmount)
                || !TryToMoney(Math.Max(0d, row.PlannedAmount), out var plannedAmount))
            {
                SetError($"Enter a valid planned amount for {row.Name}.");
                return;
            }

            allocations.Add(new BudgetAllocation(row.CategoryId, _selectedMonth, plannedAmount));
        }

        await RunAndReloadAsync(
            () => _repository.SaveAllocationsAsync(_selectedMonth, allocations),
            "Monthly plan saved",
            "We couldn't save your plan.");
    }

    private async Task AddTransactionAsync()
    {
        if (!TryToMoney(TransactionAmount, out var transactionAmount)
            || transactionAmount <= 0m
            || SelectedTransactionType is null)
        {
            SetError("Enter an amount greater than zero and choose a transaction type.");
            return;
        }

        if (SelectedTransactionType.Type is TransactionType.Expense or TransactionType.Refund or TransactionType.Savings
            && SelectedTransactionCategory is null)
        {
            SetError("Choose a matching category for this transaction type.");
            return;
        }

        var transactionDate = DateOnly.FromDateTime(TransactionDate.Date);
        var transactionMonth = BudgetMonth.FromDate(transactionDate);
        var transaction = new BudgetTransaction(
            Guid.NewGuid(),
            transactionDate,
            SelectedTransactionType.Type,
            transactionAmount,
            SelectedTransactionType.Type is TransactionType.Income or TransactionType.Transfer
                ? null
                : SelectedTransactionCategory?.Id,
            TransactionNote.Trim());

        var changedMonth = transactionMonth != _selectedMonth;
        var saved = await RunAndReloadAsync(
            () => _repository.UpsertTransactionAsync(transaction),
            changedMonth
                ? $"Transaction added; now viewing {FormatMonth(transactionMonth)}"
                : "Transaction added",
            "We couldn't save that transaction.",
            transactionMonth);

        if (saved)
        {
            TransactionAmount = 0d;
            TransactionNote = string.Empty;
        }
    }

    private async Task SaveMonthlyIncomeAsync()
    {
        if (!TryToMoney(MonthlyIncomeAmount, out var desiredTotal))
        {
            SetError("Enter a monthly income total of zero or more.");
            return;
        }

        MonthlyIncomeAdjustment adjustment;
        try
        {
            adjustment = MonthlyIncomePlanner.Plan(_snapshot.Transactions, desiredTotal);
        }
        catch (ArgumentOutOfRangeException exception)
            when (exception.ParamName == "desiredIncomeTotal")
        {
            SetError("Monthly income cannot be lower than your separately recorded income entries.");
            return;
        }

        if (adjustment.ManagedAmount == 0m && adjustment.ManagedTransaction is null)
        {
            StatusIsError = false;
            StatusText = "Monthly income already matches your other income entries";
            return;
        }

        await RunAndReloadAsync(async () =>
        {
            if (adjustment.ShouldDeleteManaged)
            {
                await _repository.DeleteTransactionAsync(adjustment.ManagedTransaction!.Id);
                return;
            }

            var transaction = new BudgetTransaction(
                adjustment.ManagedTransaction?.Id ?? Guid.NewGuid(),
                adjustment.ManagedTransaction?.Date
                    ?? BudgetDateSelection.GetDefaultDate(_selectedMonth, _localToday),
                TransactionType.Income,
                adjustment.ManagedAmount,
                null,
                MonthlyIncomePlanner.ManagedTransactionNote);
            await _repository.UpsertTransactionAsync(transaction);
        },
        "Monthly income saved",
        "We couldn't save your monthly income.");
    }

    private async Task AddBillAsync()
    {
        if (string.IsNullOrWhiteSpace(BillName)
            || !TryToMoney(BillAmount, out var billAmount)
            || billAmount <= 0m
            || !double.IsFinite(BillDueDay)
            || BillDueDay != Math.Truncate(BillDueDay)
            || BillDueDay is < 1d or > 31d)
        {
            SetError("Enter a bill name, an amount greater than zero, and a due day from 1 to 31.");
            return;
        }

        var originalBill = _billBeingEdited;
        var bill = new RecurringBill(
            originalBill?.Id ?? 0,
            BillName.Trim(),
            billAmount,
            Convert.ToInt32(BillDueDay),
            SelectedBillCategory?.Id,
            originalBill?.IsActive ?? true,
            originalBill?.StartDate,
            originalBill?.EndDate);

        var saved = await RunAndReloadAsync(
            () => _repository.UpsertRecurringBillAsync(bill),
            originalBill is null ? "Recurring bill added" : "Recurring bill updated",
            "We couldn't save that recurring bill.");

        if (saved)
        {
            ResetBillForm();
        }
    }

    private async Task AddGoalAsync()
    {
        if (string.IsNullOrWhiteSpace(GoalName)
            || !TryToMoney(GoalTargetAmount, out var targetAmount)
            || targetAmount <= 0m
            || !TryToMoney(GoalCurrentAmount, out var currentAmount))
        {
            SetError("Enter a goal name and a target amount greater than zero.");
            return;
        }

        var goal = new SavingsGoal(
            0,
            GoalName.Trim(),
            targetAmount,
            currentAmount,
            DateOnly.FromDateTime(GoalTargetDate.Date));

        var saved = await RunAndReloadAsync(
            () => _repository.UpsertSavingsGoalAsync(goal),
            "Savings goal added",
            "We couldn't save that savings goal.");

        if (saved)
        {
            GoalName = string.Empty;
            GoalTargetAmount = 0d;
            GoalCurrentAmount = 0d;
        }
    }

    private async Task SaveSettingsAsync()
    {
        await RunAsync(async () =>
        {
            await _repository.SaveSettingsAsync(new AppSettings(CurrencyCode, IsDarkMode));
            _snapshot = _snapshot with { Settings = new AppSettings(CurrencyCode, IsDarkMode) };
            ApplySnapshot(_snapshot);
            StatusText = "Preferences saved locally";
        }, "We couldn't save your preferences.");
    }

    private async Task SeedDemoDataAsync()
    {
        var added = false;
        var saved = await RunAndReloadAsync(async () =>
        {
            added = await _repository.SeedDemoDataAsync(_selectedMonth);
        }, "Example budget loaded", "We couldn't load the example budget.");

        if (saved && !StatusIsError)
        {
            StatusText = added ? "Example budget loaded" : "This month already has data";
        }
    }

    private async Task<bool> RunAndReloadAsync(
        Func<Task> action,
        string successMessage,
        string errorMessage,
        BudgetMonth? monthToShow = null)
    {
        var targetMonth = monthToShow ?? _selectedMonth;
        var writeSucceeded = false;
        var operationCompleted = await RunAsync(async () =>
        {
            await action();
            writeSucceeded = true;

            try
            {
                var loadedMonth = await ReadMonthAsync(targetMonth);
                _selectedMonth = targetMonth;
                ApplyLoadedMonth(loadedMonth);
                StatusText = successMessage;
            }
            catch (Exception exception)
            {
                SetError($"{successMessage}. Your change was saved, but the screen could not refresh. {exception.Message}");
            }
        }, errorMessage);

        return operationCompleted && writeSucceeded;
    }

    private async Task<bool> RunAsync(Func<Task> action, string errorMessage)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        StatusIsError = false;
        try
        {
            await action();
            return true;
        }
        catch (Exception exception)
        {
            SetError($"{errorMessage} {exception.Message}");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshBills(BudgetSnapshot snapshot)
    {
        var upcomingBills = RecurringDateCalculator.GetUpcomingBills(snapshot.Bills, _localToday);
        Replace(Bills, upcomingBills.Select(item => new BillRow(
            item.Bill.Id,
            item.Bill.Name,
            $"Due {item.DueDate:dd MMM yyyy}",
            snapshot.Categories.FirstOrDefault(category => category.Id == item.Bill.CategoryId)?.Name ?? "Uncategorised",
            FormatMoney(item.Bill.Amount),
            FormatCountdown(item.DaysUntilDue))));
        OnPropertyChanged(nameof(EmptyBillsVisibility));
    }

    private void ResetBillForm()
    {
        _billBeingEdited = null;
        BillName = string.Empty;
        BillAmount = 0d;
        BillDueDay = 1d;
        SelectedBillCategory = BillCategoryOptions.FirstOrDefault();
        NotifyBillEditorChanged();
    }

    private void NotifyBillEditorChanged()
    {
        OnPropertyChanged(nameof(IsEditingBill));
        OnPropertyChanged(nameof(BillFormTitle));
        OnPropertyChanged(nameof(BillSubmitText));
    }

    private void NotifyDateContextChanged()
    {
        OnPropertyChanged(nameof(SelectedMonthText));
        OnPropertyChanged(nameof(TodayHeadingText));
        OnPropertyChanged(nameof(TodayContextText));
    }

    private bool CanRunCommand() => !IsBusy;

    private void NotifyCommandCanExecuteChanged()
    {
        PreviousMonthCommand.NotifyCanExecuteChanged();
        NextMonthCommand.NotifyCanExecuteChanged();
        CurrentMonthCommand.NotifyCanExecuteChanged();
        SavePlanCommand.NotifyCanExecuteChanged();
        AddTransactionCommand.NotifyCanExecuteChanged();
        SaveMonthlyIncomeCommand.NotifyCanExecuteChanged();
        AddBillCommand.NotifyCanExecuteChanged();
        CancelBillEditCommand.NotifyCanExecuteChanged();
        AddGoalCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
        SeedDemoDataCommand.NotifyCanExecuteChanged();
    }

    private void SetError(string message)
    {
        StatusText = message;
        StatusIsError = true;
    }

    private string FormatMoney(decimal amount) => $"{CurrencyPrefix(CurrencyCode)} {amount:N2}";

    private static bool TryToMoney(double amount, out decimal money)
    {
        money = 0m;
        if (!double.IsFinite(amount) || amount < 0d)
        {
            return false;
        }

        try
        {
            money = decimal.Round(
                Convert.ToDecimal(amount),
                2,
                MidpointRounding.AwayFromZero);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static string FormatMonth(BudgetMonth month) =>
        new DateTime(month.Year, month.Month, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    private static DateTimeOffset ToLocalDateTimeOffset(DateOnly date)
    {
        var localDate = date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        return new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));
    }

    private static string FormatCountdown(int daysUntilDue) => daysUntilDue switch
    {
        < 0 => $"Overdue by {Math.Abs(daysUntilDue)} day{(daysUntilDue == -1 ? string.Empty : "s")}",
        0 => "Due today",
        1 => "Due tomorrow",
        _ => $"Due in {daysUntilDue} days",
    };

    private void UpdateTransactionCategoryOptions()
    {
        if (SelectedTransactionType is null)
        {
            return;
        }

        var kind = SelectedTransactionType.Type switch
        {
            TransactionType.Savings => CategoryKind.Savings,
            TransactionType.Expense or TransactionType.Refund => CategoryKind.Expense,
            _ => (CategoryKind?)null,
        };

        Replace(
            AvailableTransactionCategories,
            kind is null
                ? Array.Empty<CategoryOption>()
                : CategoryOptions.Where(option => option.Kind == kind));
        SelectedTransactionCategory = AvailableTransactionCategories.FirstOrDefault();
    }

    private string FormatSignedMoney(BudgetTransaction transaction)
    {
        var sign = transaction.Type is TransactionType.Expense or TransactionType.Savings ? "−" : "+";
        return $"{sign}{FormatMoney(transaction.Amount)}";
    }

    private static string CurrencyPrefix(string code) => code switch
    {
        "MYR" => "RM",
        "USD" => "$",
        "SGD" => "S$",
        "EUR" => "€",
        "GBP" => "£",
        "AUD" => "A$",
        _ => code,
    };

    private static string SplitWords(string value) => string.Concat(value.Select((character, index) =>
        index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private void NotifyDashboardChanged()
    {
        NotifyDateContextChanged();
        OnPropertyChanged(nameof(IncomeText));
        OnPropertyChanged(nameof(PlannedText));
        OnPropertyChanged(nameof(SpentText));
        OnPropertyChanged(nameof(SavedText));
        OnPropertyChanged(nameof(AvailableText));
        OnPropertyChanged(nameof(RemainingToPlanText));
        OnPropertyChanged(nameof(BudgetHealthText));
        OnPropertyChanged(nameof(SpendingPercentText));
        OnPropertyChanged(nameof(SpendingPercent));
        OnPropertyChanged(nameof(TransactionCountText));
        OnPropertyChanged(nameof(EmptyTransactionsVisibility));
        OnPropertyChanged(nameof(EmptyBillsVisibility));
        OnPropertyChanged(nameof(EmptyGoalsVisibility));
        NotifyBillEditorChanged();
    }

    private sealed record LoadedMonth(
        BudgetSnapshot Snapshot,
        IReadOnlyList<BudgetSnapshot> TrendSnapshots);
}

public sealed record TransactionTypeOption(TransactionType Type, string Label);

public sealed record CategoryOption(long Id, string Name, CategoryKind Kind);

public sealed class CategoryBudgetRow : ObservableObject
{
    private double _plannedAmount;

    public CategoryBudgetRow(
        long categoryId,
        string name,
        string colorHex,
        double plannedAmount,
        string actualText,
        string remainingText,
        double progressPercent,
        bool isOverBudget)
    {
        CategoryId = categoryId;
        Name = name;
        ColorHex = colorHex;
        _plannedAmount = plannedAmount;
        ActualText = actualText;
        RemainingText = remainingText;
        ProgressPercent = progressPercent;
        IsOverBudget = isOverBudget;
    }

    public long CategoryId { get; }
    public string Name { get; }
    public string ColorHex { get; }
    public string ActualText { get; }
    public string RemainingText { get; }
    public double ProgressPercent { get; }
    public bool IsOverBudget { get; }

    public double PlannedAmount
    {
        get => _plannedAmount;
        set => SetProperty(ref _plannedAmount, value);
    }
}

public sealed record TransactionRow(
    Guid Id,
    string DateText,
    string TypeText,
    string Note,
    string CategoryName,
    string AmountText,
    string Tone);

public sealed record BillRow(
    long Id,
    string Name,
    string DueText,
    string CategoryName,
    string AmountText,
    string CountdownText);

public sealed record GoalRow(
    long Id,
    string Name,
    string CurrentText,
    string TargetText,
    string DueText,
    double PercentComplete,
    string PercentText);

public sealed record MonthlyTrendRow(string MonthText, string IncomeText, string SpentText, string SavedText);
