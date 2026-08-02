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
    private BudgetTransaction? _transactionBeingEdited;
    private SavingsDestinationOption? _selectedSavingsDestination;
    private double _monthlyIncomeAmount;
    private double _monthlyIncomePayDay = 1;
    private bool _isMonthlyIncomeActive = true;
    private RecurringIncome? _monthlyIncomeSchedule;
    private string _monthlyIncomeScheduleText = "Choose a payday and save once to repeat automatically.";
    private string _billName = string.Empty;
    private double _billAmount;
    private double _billDueDay = 1;
    private CategoryOption? _selectedBillCategory;
    private RecurringBill? _billBeingEdited;
    private string _goalName = string.Empty;
    private double _goalTargetAmount;
    private double _goalCurrentAmount;
    private DateTimeOffset _goalTargetDate = DateTimeOffset.Now.AddMonths(6);
    private Investment? _investmentBeingEdited;
    private string _investmentName = string.Empty;
    private string _investmentProvider = string.Empty;
    private string _investmentKind = "Other";
    private double _investmentCurrentValue = double.NaN;
    private double _investmentUnits = double.NaN;
    private double _investmentUnitPrice = double.NaN;
    private DateTimeOffset _investmentValuationDate = DateTimeOffset.Now;
    private string _investmentNote = string.Empty;

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
        CancelTransactionEditCommand = new RelayCommand(ResetTransactionForm, CanRunCommand);
        SaveMonthlyIncomeCommand = new AsyncRelayCommand(SaveMonthlyIncomeAsync, CanRunCommand);
        AddBillCommand = new AsyncRelayCommand(AddBillAsync, CanRunCommand);
        CancelBillEditCommand = new RelayCommand(ResetBillForm, CanRunCommand);
        AddGoalCommand = new AsyncRelayCommand(AddGoalAsync, CanRunCommand);
        SaveInvestmentCommand = new AsyncRelayCommand(SaveInvestmentAsync, CanRunCommand);
        CancelInvestmentEditCommand = new RelayCommand(ResetInvestmentForm, CanRunCommand);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, CanRunCommand);
        SeedDemoDataCommand = new AsyncRelayCommand(SeedDemoDataAsync, CanRunCommand);
    }

    public event EventHandler<bool>? ThemeRequested;

    public IAsyncRelayCommand PreviousMonthCommand { get; }
    public IAsyncRelayCommand NextMonthCommand { get; }
    public IAsyncRelayCommand CurrentMonthCommand { get; }
    public IAsyncRelayCommand SavePlanCommand { get; }
    public IAsyncRelayCommand AddTransactionCommand { get; }
    public IRelayCommand CancelTransactionEditCommand { get; }
    public IAsyncRelayCommand SaveMonthlyIncomeCommand { get; }
    public IAsyncRelayCommand AddBillCommand { get; }
    public IRelayCommand CancelBillEditCommand { get; }
    public IAsyncRelayCommand AddGoalCommand { get; }
    public IAsyncRelayCommand SaveInvestmentCommand { get; }
    public IRelayCommand CancelInvestmentEditCommand { get; }
    public IAsyncRelayCommand SaveSettingsCommand { get; }
    public IAsyncRelayCommand SeedDemoDataCommand { get; }

    public IReadOnlyList<TransactionTypeOption> TransactionTypes { get; }
    public IReadOnlyList<string> CurrencyCodes { get; } = ["MYR", "USD", "SGD", "EUR", "GBP", "AUD"];
    public IReadOnlyList<string> InvestmentKindOptions { get; } = ["Savings fund", "Unit trust", "Gold", "Other"];
    public ObservableCollection<CategoryOption> CategoryOptions { get; } = [];
    public ObservableCollection<CategoryOption> AvailableTransactionCategories { get; } = [];
    public ObservableCollection<CategoryOption> BillCategoryOptions { get; } = [];
    public ObservableCollection<CategoryBudgetRow> CategoryRows { get; } = [];
    public ObservableCollection<TransactionRow> Transactions { get; } = [];
    public ObservableCollection<SavingsDestinationOption> SavingsDestinations { get; } = [];
    public ObservableCollection<BillRow> Bills { get; } = [];
    public ObservableCollection<GoalRow> Goals { get; } = [];
    public ObservableCollection<InvestmentRow> InvestmentRows { get; } = [];
    public ObservableCollection<ArchivedInvestmentRow> ArchivedInvestmentRows { get; } = [];
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
    public string CarryForwardText { get; private set; } = "RM 0.00";
    public string CarryForwardContextText { get; private set; } = "Nothing carried from last month";
    public string PlannedText { get; private set; } = "RM 0.00";
    public string SpentText { get; private set; } = "RM 0.00";
    public string SavedText { get; private set; } = "RM 0.00";
    public string AvailableText { get; private set; } = "RM 0.00";
    public string RemainingToPlanText { get; private set; } = "RM 0.00";
    public string BudgetHealthText { get; private set; } = "Add income to begin your plan.";
    public string SpendingPercentText { get; private set; } = "0% used";
    public double SpendingPercent { get; private set; }
    public string TransactionCountText => Transactions.Count == 1 ? "1 entry" : $"{Transactions.Count} entries";
    public bool IsTransactionCategoryEnabled => SelectedTransactionType?.Type != TransactionType.Transfer;
    public Visibility SavingsDestinationVisibility => SelectedTransactionType?.Type == TransactionType.Savings
        ? Visibility.Visible
        : Visibility.Collapsed;
    public string LocalSaveText => IsBusy ? "Saving…" : StatusText;
    public Visibility EmptyTransactionsVisibility => Transactions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyBillsVisibility => Bills.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyGoalsVisibility => Goals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StatusErrorVisibility => StatusIsError ? Visibility.Visible : Visibility.Collapsed;
    public bool IsEditingBill => _billBeingEdited is not null;
    public string BillFormTitle => IsEditingBill ? "Edit recurring bill" : "Add recurring bill";
    public string BillSubmitText => IsEditingBill ? "Save bill changes" : "Add recurring bill";
    public bool IsEditingTransaction => _transactionBeingEdited is not null;
    public string TransactionFormTitle => IsEditingTransaction ? "Edit transaction" : "Daily money entry";
    public string TransactionSubmitText => IsEditingTransaction ? "Save changes" : "Add transaction";
    public bool IsEditingInvestment => _investmentBeingEdited is not null;
    public string InvestmentFormTitle => IsEditingInvestment ? "Update investment" : "Add an investment";
    public string InvestmentSubmitText => IsEditingInvestment ? "Save investment update" : "Add investment";
    public string PortfolioValueText { get; private set; } = "RM 0.00";
    public string PortfolioContributedText { get; private set; } = "RM 0.00";
    public string PortfolioGainLossText { get; private set; } = "RM 0.00";
    public string PortfolioMonthContributionText { get; private set; } = "RM 0.00";
    public Visibility ArchivedInvestmentsVisibility => ArchivedInvestmentRows.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

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
                OnPropertyChanged(nameof(SavingsDestinationVisibility));
            }
        }
    }

    public CategoryOption? SelectedTransactionCategory
    {
        get => _selectedTransactionCategory;
        set => SetProperty(ref _selectedTransactionCategory, value);
    }

    public SavingsDestinationOption? SelectedSavingsDestination
    {
        get => _selectedSavingsDestination;
        set => SetProperty(ref _selectedSavingsDestination, value);
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

    public double MonthlyIncomePayDay
    {
        get => _monthlyIncomePayDay;
        set => SetProperty(ref _monthlyIncomePayDay, value);
    }

    public bool IsMonthlyIncomeActive
    {
        get => _isMonthlyIncomeActive;
        set => SetProperty(ref _isMonthlyIncomeActive, value);
    }

    public string MonthlyIncomeScheduleText
    {
        get => _monthlyIncomeScheduleText;
        private set => SetProperty(ref _monthlyIncomeScheduleText, value);
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

    public string InvestmentName
    {
        get => _investmentName;
        set => SetProperty(ref _investmentName, value);
    }

    public string InvestmentProvider
    {
        get => _investmentProvider;
        set => SetProperty(ref _investmentProvider, value);
    }

    public string InvestmentKind
    {
        get => _investmentKind;
        set => SetProperty(ref _investmentKind, value);
    }

    public double InvestmentCurrentValue
    {
        get => _investmentCurrentValue;
        set => SetProperty(ref _investmentCurrentValue, value);
    }

    public double InvestmentUnits
    {
        get => _investmentUnits;
        set => SetProperty(ref _investmentUnits, value);
    }

    public double InvestmentUnitPrice
    {
        get => _investmentUnitPrice;
        set => SetProperty(ref _investmentUnitPrice, value);
    }

    public DateTimeOffset InvestmentValuationDate
    {
        get => _investmentValuationDate;
        set => SetProperty(ref _investmentValuationDate, value);
    }

    public string InvestmentNote
    {
        get => _investmentNote;
        set => SetProperty(ref _investmentNote, value);
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
            var loadedMonth = await ReadMonthAsync(currentMonth, localToday);

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

        if (_transactionBeingEdited is not null)
        {
            SetError("Save or cancel the transaction changes so MyBudget can refresh the new local day.");
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

        if (_isInitialized)
        {
            await RunAsync(async () =>
            {
                var loadedMonth = await ReadMonthAsync(targetMonth, refreshedToday);

                _localToday = refreshedToday;
                _selectedMonth = targetMonth;
                TransactionDate = ToLocalDateTimeOffset(refreshedTransactionDate);
                ApplyLoadedMonth(loadedMonth);
            }, "We couldn't refresh the new local day.");
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

    public void BeginEditTransaction(Guid id)
    {
        if (IsBusy)
        {
            return;
        }

        var transaction = _snapshot.Transactions.FirstOrDefault(item => item.Id == id);
        if (transaction is null)
        {
            SetError("That transaction is no longer available.");
            return;
        }

        _transactionBeingEdited = transaction;
        RefreshSavingsDestinations(_snapshot);
        SelectedTransactionType = TransactionTypes.First(option => option.Type == transaction.Type);
        SelectedTransactionCategory = AvailableTransactionCategories.FirstOrDefault(option => option.Id == transaction.CategoryId);
        SelectedSavingsDestination = SavingsDestinations.FirstOrDefault(option =>
            option.GoalId == transaction.SavingsGoalId && option.InvestmentId == transaction.InvestmentId)
            ?? SavingsDestinations.FirstOrDefault();
        TransactionDate = ToLocalDateTimeOffset(transaction.Date);
        TransactionAmount = (double)transaction.Amount;
        TransactionNote = transaction.Note;
        NotifyTransactionEditorChanged();
    }

    public void PrepareSavingsForGoal(long id)
    {
        var goal = _snapshot.Goals.FirstOrDefault(item => item.Id == id);
        if (goal is null || IsBusy)
        {
            return;
        }

        ResetTransactionForm();
        SelectedTransactionType = TransactionTypes.First(option => option.Type == TransactionType.Savings);
        SelectedSavingsDestination = SavingsDestinations.FirstOrDefault(option => option.GoalId == id);
        TransactionNote = $"Savings for {goal.Name}";
    }

    public void PrepareSavingsForInvestment(long id)
    {
        var investment = _snapshot.Investments.FirstOrDefault(item => item.Id == id && !item.IsArchived);
        if (investment is null || IsBusy)
        {
            return;
        }

        ResetTransactionForm();
        SelectedTransactionType = TransactionTypes.First(option => option.Type == TransactionType.Savings);
        SelectedSavingsDestination = SavingsDestinations.FirstOrDefault(option => option.InvestmentId == id);
        TransactionNote = $"Investment contribution · {investment.Name}";
    }

    public void BeginInvestmentTemplate(string template)
    {
        if (IsBusy)
        {
            return;
        }

        var existing = _snapshot.Investments.FirstOrDefault(investment =>
            !investment.IsArchived && string.Equals(investment.Name, template, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            BeginEditInvestment(existing.Id);
            return;
        }

        ResetInvestmentForm();
        (InvestmentName, InvestmentProvider, InvestmentKind) = template switch
        {
            "Tabung Haji" => ("Tabung Haji", "Lembaga Tabung Haji", "Savings fund"),
            "ASB" => ("ASB", "Amanah Saham Nasional Berhad", "Unit trust"),
            "Maybank Gold" => ("Maybank Gold", "Maybank", "Gold"),
            _ => (string.Empty, string.Empty, "Other"),
        };
    }

    public void BeginEditInvestment(long id)
    {
        if (IsBusy)
        {
            return;
        }

        var investment = _snapshot.Investments.FirstOrDefault(item => item.Id == id && !item.IsArchived);
        if (investment is null)
        {
            SetError("That investment is no longer available.");
            return;
        }

        var position = _snapshot.InvestmentPositions.FirstOrDefault(item => item.Investment.Id == id);
        _investmentBeingEdited = investment;
        InvestmentName = investment.Name;
        InvestmentProvider = investment.Provider;
        InvestmentKind = FormatInvestmentKind(investment.Kind);
        InvestmentCurrentValue = position?.LatestValuation is null
            ? double.NaN
            : (double)position.LatestValuation.MarketValue;
        InvestmentUnits = position?.LatestValuation?.Units is decimal units
            ? (double)units
            : double.NaN;
        InvestmentUnitPrice = position?.LatestValuation?.UnitPrice is decimal unitPrice
            ? (double)unitPrice
            : double.NaN;
        InvestmentValuationDate = ToLocalDateTimeOffset(GetDefaultInvestmentValuationDate());
        InvestmentNote = string.Empty;
        NotifyInvestmentEditorChanged();
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

    public async Task DeleteTransactionAsync(Guid id)
    {
        var transaction = _snapshot.Transactions.FirstOrDefault(item => item.Id == id);
        if (transaction?.RecurringIncomeId is not null)
        {
            SetError("Recurring income entries are protected. Pause or update the income schedule instead.");
            return;
        }

        var deleted = await RunAndReloadAsync(
            () => _repository.DeleteTransactionAsync(id),
            "Transaction deleted",
            "We couldn't delete that transaction.");
        if (deleted && _transactionBeingEdited?.Id == id)
        {
            ResetTransactionForm();
        }
    }

    public async Task ArchiveInvestmentAsync(long id)
    {
        var archived = await RunAndReloadAsync(
            () => _repository.ArchiveInvestmentAsync(id),
            "Investment archived; linked transactions were kept",
            "We couldn't archive that investment.");
        if (archived && _investmentBeingEdited?.Id == id)
        {
            ResetInvestmentForm();
        }
    }

    public async Task RestoreInvestmentAsync(long id)
    {
        var investment = _snapshot.Investments.FirstOrDefault(item => item.Id == id && item.IsArchived);
        if (investment is null)
        {
            SetError("That archived investment is no longer available.");
            return;
        }

        await RunAndReloadAsync(
            () => _repository.UpsertInvestmentAsync(investment with { IsArchived = false }),
            "Investment restored",
            "We couldn't restore that investment.");
    }

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

        if (_transactionBeingEdited is not null)
        {
            SetError("Save or cancel the transaction changes before moving to another month.");
            return;
        }

        var localToday = resolvedLocalToday ?? BudgetDateSelection.GetLocalToday();
        var selectedDate = DateOnly.FromDateTime(TransactionDate.Date);
        var targetTransactionDate = month.Contains(localToday)
            ? localToday
            : BudgetDateSelection.MoveToMonth(selectedDate, month);

        await RunAsync(async () =>
        {
            var loadedMonth = await ReadMonthAsync(month, localToday);

            _localToday = localToday;
            _selectedMonth = month;
            TransactionDate = ToLocalDateTimeOffset(targetTransactionDate);
            ApplyLoadedMonth(loadedMonth);
        }, "We couldn't load that month.");
    }

    private async Task<LoadedMonth> ReadMonthAsync(BudgetMonth selectedMonth, DateOnly? throughDate = null)
    {
        await _repository.SynchronizeRecurringIncomeAsync(throughDate ?? _localToday);
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
        var editedTransactionCategoryId = _transactionBeingEdited is null
            ? null
            : SelectedTransactionCategory?.Id;
        CurrencyCode = snapshot.Settings.CurrencyCode;
        IsDarkMode = snapshot.Settings.IsDarkMode;
        ThemeRequested?.Invoke(this, IsDarkMode);

        IncomeText = FormatMoney(summary.Income);
        CarryForwardText = FormatMoney(summary.CarryForward);
        CarryForwardContextText = GetCarryForwardContext(summary.CarryForward, snapshot.Month);
        PlannedText = FormatMoney(summary.Planned);
        SpentText = FormatMoney(summary.Spent);
        SavedText = FormatMoney(summary.Saved);
        AvailableText = FormatMoney(summary.Available);
        RemainingToPlanText = FormatMoney(summary.RemainingToPlan);
        SpendingPercent = summary.Planned <= 0m ? 0d : (double)Math.Clamp(summary.Spent / summary.Planned * 100m, 0m, 100m);
        SpendingPercentText = summary.Planned <= 0m ? "No plan yet" : $"{summary.Spent / summary.Planned:P0} used";
        BudgetHealthText = summary.CarryForward + summary.Income <= 0m
            ? "Add income or a starting balance to see your monthly breathing room."
            : summary.Available < 0m
                ? $"You're {FormatMoney(Math.Abs(summary.Available))} over available cash."
                : $"You still have {FormatMoney(summary.Available)} available this month.";

        _monthlyIncomeSchedule = snapshot.RecurringIncomes
            .OrderByDescending(income => income.IsActive)
            .ThenBy(income => income.Id)
            .FirstOrDefault();
        var legacyManagedIncome = snapshot.Transactions.FirstOrDefault(MonthlyIncomePlanner.IsManagedTransaction);
        MonthlyIncomeAmount = (double)(_monthlyIncomeSchedule?.Amount ?? legacyManagedIncome?.Amount ?? 0m);
        MonthlyIncomePayDay = _monthlyIncomeSchedule?.PayDay ?? legacyManagedIncome?.Date.Day ?? 1;
        // A new schedule should repeat by default. Existing schedules always
        // keep the user's saved active/paused choice.
        IsMonthlyIncomeActive = _monthlyIncomeSchedule?.IsActive ?? true;
        MonthlyIncomeScheduleText = BuildIncomeScheduleText(_monthlyIncomeSchedule);

        Replace(CategoryOptions, snapshot.Categories
            .Where(category => !category.IsArchived)
            .Select(category => new CategoryOption(category.Id, category.Name, category.Kind)));
        Replace(BillCategoryOptions, CategoryOptions.Where(option => option.Kind == CategoryKind.Expense));
        RefreshSavingsDestinations(snapshot);
        UpdateTransactionCategoryOptions();
        if (editedTransactionCategoryId is long categoryId)
        {
            SelectedTransactionCategory = AvailableTransactionCategories.FirstOrDefault(option => option.Id == categoryId)
                ?? SelectedTransactionCategory;
        }
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
                GetTransactionDestinationLabel(transaction, snapshot),
                FormatSignedMoney(transaction),
                transaction.Type is TransactionType.Expense ? "Expense" : "Positive",
                transaction.RecurringIncomeId is null ? "Edit" : "Edit entry",
                transaction.RecurringIncomeId is null)));

        RefreshBills(snapshot);

        Replace(Goals, snapshot.Goals
            .OrderBy(goal => goal.TargetDate)
            .Select(goal =>
            {
                var monthContribution = snapshot.Transactions
                    .Where(transaction => transaction.Type == TransactionType.Savings)
                    .Where(transaction => transaction.SavingsGoalId == goal.Id)
                    .Sum(transaction => transaction.Amount);
                return new GoalRow(
                    goal.Id,
                    goal.Name,
                    FormatMoney(goal.CurrentAmount),
                    $"of {FormatMoney(goal.TargetAmount)}",
                    goal.TargetDate is null ? "No target date" : $"Target {goal.TargetDate:dd MMM yyyy}",
                    (double)Math.Clamp(goal.PercentComplete, 0m, 100m),
                    $"{goal.PercentComplete:N0}%",
                    $"Started with {FormatMoney(goal.StartingAmount)}",
                    $"{FormatMoney(goal.LinkedSavingsAmount)} from transactions",
                    monthContribution > 0m
                        ? $"{FormatMoney(monthContribution)} added in {SelectedMonthText}"
                        : $"No linked savings in {SelectedMonthText}");
            }));

        var activePositions = snapshot.InvestmentPositions
            .Where(position => !position.Investment.IsArchived)
            .OrderBy(position => position.Investment.Name)
            .ToArray();
        var portfolioValue = activePositions.Sum(position => position.CurrentValue);
        var portfolioContributed = activePositions.Sum(position => position.AllTimeContributions);
        var portfolioMonthContribution = activePositions.Sum(position => position.MonthlyContributions);
        PortfolioValueText = FormatMoney(portfolioValue);
        PortfolioContributedText = FormatMoney(portfolioContributed);
        PortfolioGainLossText = FormatSignedDifference(portfolioValue - portfolioContributed);
        PortfolioMonthContributionText = FormatMoney(portfolioMonthContribution);
        Replace(InvestmentRows, activePositions.Select(position =>
        {
            var valuation = position.LatestValuation;
            var unitsText = valuation?.Units is > 0m
                ? $" · {valuation.Units:N4} {position.Investment.UnitLabel}"
                : string.Empty;
            var valuationText = valuation is null
                ? "No manual valuation yet; value currently follows contributions"
                : $"Valued {valuation.Date:dd MMM yyyy}{unitsText}";
            return new InvestmentRow(
                position.Investment.Id,
                position.Investment.Name,
                $"{FormatInvestmentKind(position.Investment.Kind)} · {position.Investment.Provider}",
                FormatMoney(position.CurrentValue),
                FormatMoney(position.AllTimeContributions),
                FormatSignedDifference(position.GainLoss),
                position.MonthlyContributions > 0m
                    ? $"+{FormatMoney(position.MonthlyContributions)} this month"
                    : "No contribution this month",
                valuationText,
                position.AllTimeContributions <= 0m
                    ? 0d
                    : (double)Math.Clamp(position.CurrentValue / position.AllTimeContributions * 100m, 0m, 100m));
        }));
        Replace(ArchivedInvestmentRows, snapshot.Investments
            .Where(investment => investment.IsArchived)
            .OrderBy(investment => investment.Name)
            .Select(investment => new ArchivedInvestmentRow(
                investment.Id,
                investment.Name,
                $"{FormatInvestmentKind(investment.Kind)} · {investment.Provider}")));
        OnPropertyChanged(nameof(ArchivedInvestmentsVisibility));

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

        if (SelectedTransactionType.Type != TransactionType.Transfer
            && SelectedTransactionCategory is null)
        {
            SetError("Choose a matching category for this transaction type.");
            return;
        }

        var original = _transactionBeingEdited;
        var transactionDate = DateOnly.FromDateTime(TransactionDate.Date);
        var transactionMonth = BudgetMonth.FromDate(transactionDate);
        if (original?.RecurringIncomeId is not null
            && (SelectedTransactionType.Type != TransactionType.Income
                || BudgetMonth.FromDate(original.Date) != transactionMonth))
        {
            SetError("A recurring income entry must remain Income and stay in its original month. Update the monthly schedule for future deposits.");
            return;
        }

        var destination = SelectedTransactionType.Type == TransactionType.Savings
            ? SelectedSavingsDestination
            : null;
        var transaction = new BudgetTransaction(
            original?.Id ?? Guid.NewGuid(),
            transactionDate,
            SelectedTransactionType.Type,
            transactionAmount,
            SelectedTransactionType.Type == TransactionType.Transfer ? null : SelectedTransactionCategory?.Id,
            TransactionNote.Trim(),
            destination?.GoalId,
            destination?.InvestmentId,
            original?.RecurringIncomeId);

        var changedMonth = transactionMonth != _selectedMonth;
        var saved = await RunAndReloadAsync(
            () => _repository.UpsertTransactionAsync(transaction),
            changedMonth
                ? $"Transaction saved; now viewing {FormatMonth(transactionMonth)}"
                : original is null ? "Transaction added" : "Transaction updated",
            "We couldn't save that transaction.",
            transactionMonth);

        if (saved)
        {
            ResetTransactionForm();
        }
    }

    private async Task SaveMonthlyIncomeAsync()
    {
        if (!TryToMoney(MonthlyIncomeAmount, out var amount)
            || amount <= 0m
            || !double.IsFinite(MonthlyIncomePayDay)
            || MonthlyIncomePayDay != Math.Truncate(MonthlyIncomePayDay)
            || MonthlyIncomePayDay is < 1d or > 31d)
        {
            SetError("Enter a monthly income amount greater than zero and a pay day from 1 to 31.");
            return;
        }

        var salaryCategory = _snapshot.Categories.FirstOrDefault(category =>
            category.Kind == CategoryKind.Income
            && string.Equals(category.Name, "Salary", StringComparison.OrdinalIgnoreCase))
            ?? _snapshot.Categories.FirstOrDefault(category => category.Kind == CategoryKind.Income);
        if (salaryCategory is null)
        {
            SetError("The Salary category is unavailable. Restart MyBudget so its local database can finish updating.");
            return;
        }

        var effectiveMonth = BudgetMonth.FromDate(_localToday);
        var schedule = new RecurringIncome(
            _monthlyIncomeSchedule?.Id ?? 0,
            _monthlyIncomeSchedule?.Name ?? MonthlyIncomePlanner.ManagedTransactionNote,
            amount,
            Convert.ToInt32(MonthlyIncomePayDay),
            salaryCategory.Id,
            IsMonthlyIncomeActive,
            _monthlyIncomeSchedule?.StartDate ?? effectiveMonth.FirstDay,
            _monthlyIncomeSchedule?.EndDate);

        await RunAndReloadAsync(async () =>
        {
            var storedId = await _repository.UpsertRecurringIncomeAsync(schedule, effectiveMonth);
            if (_monthlyIncomeSchedule is null && _selectedMonth == effectiveMonth)
            {
                var legacy = _snapshot.Transactions.FirstOrDefault(transaction =>
                    transaction.RecurringIncomeId is null
                    && MonthlyIncomePlanner.IsManagedTransaction(transaction));
                if (legacy is not null)
                {
                    await _repository.UpsertTransactionAsync(legacy with
                    {
                        Date = RecurringDateCalculator.GetDueDate(effectiveMonth, schedule.PayDay),
                        Amount = amount,
                        CategoryId = salaryCategory.Id,
                        RecurringIncomeId = storedId,
                    });
                }
            }

            await _repository.SynchronizeRecurringIncomeAsync(_localToday);
        },
        IsMonthlyIncomeActive ? "Monthly income schedule saved" : "Monthly income schedule paused",
        "We couldn't save your monthly income schedule.");
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

    private async Task SaveInvestmentAsync()
    {
        if (string.IsNullOrWhiteSpace(InvestmentName)
            || !TryToOptionalDecimal(InvestmentCurrentValue, 2, out var currentValue)
            || !TryToOptionalDecimal(InvestmentUnits, 4, out var units)
            || !TryToOptionalDecimal(InvestmentUnitPrice, 2, out var unitPrice))
        {
            SetError("Enter an investment name and valid non-negative valuation details.");
            return;
        }

        var kind = ParseInvestmentKind(InvestmentKind);
        decimal? storedUnits = units > 0m ? units : null;
        decimal? storedUnitPrice = unitPrice > 0m ? unitPrice : null;
        if (storedUnits is not null && storedUnitPrice is not null)
        {
            currentValue = decimal.Round(storedUnits.Value * storedUnitPrice.Value, 2, MidpointRounding.AwayFromZero);
            InvestmentCurrentValue = (double)currentValue.Value;
        }
        else if (currentValue is null && (storedUnits is not null || storedUnitPrice is not null))
        {
            SetError("Enter both units and price, or enter a current value. Leave all three blank to add the holding without a valuation.");
            return;
        }

        var original = _investmentBeingEdited;
        var unitLabel = original is not null && original.Kind == kind
            ? original.UnitLabel
            : kind switch
            {
                MyBudget.Core.InvestmentKind.SavingsFund => "RM",
                MyBudget.Core.InvestmentKind.Gold => "g",
                _ => "units",
            };
        var investment = new Investment(
            original?.Id ?? 0,
            InvestmentName.Trim(),
            string.IsNullOrWhiteSpace(InvestmentProvider) ? "Self-managed" : InvestmentProvider.Trim(),
            kind,
            unitLabel,
            original?.ColorHex ?? GetInvestmentColor(kind),
            false);
        var valuationDate = DateOnly.FromDateTime(InvestmentValuationDate.Date);
        if (currentValue is not null
            && (valuationDate > _localToday || valuationDate > _selectedMonth.LastDay))
        {
            SetError("Choose a valuation date no later than today and no later than the end of the month being viewed.");
            return;
        }

        Func<Task> saveAction;
        if (currentValue is decimal storedCurrentValue)
        {
            var valuationId = original is null
                ? Guid.NewGuid()
                : _snapshot.InvestmentValuations
                    .Where(item => item.InvestmentId == original.Id && item.Date == valuationDate)
                    .OrderByDescending(item => item.Id)
                    .Select(item => (Guid?)item.Id)
                    .FirstOrDefault()
                    ?? Guid.NewGuid();
            var valuation = new InvestmentValuation(
                valuationId,
                original?.Id ?? 0,
                valuationDate,
                storedCurrentValue,
                storedUnits,
                storedUnitPrice,
                InvestmentNote.Trim());
            saveAction = () => _repository.UpsertInvestmentWithValuationAsync(investment, valuation);
        }
        else
        {
            saveAction = async () => await _repository.UpsertInvestmentAsync(investment);
        }

        var saved = await RunAndReloadAsync(
            saveAction,
            original is null
                ? "Investment added"
                : currentValue is null ? "Investment details updated" : "Investment value updated",
            "We couldn't save that investment.");

        if (saved)
        {
            ResetInvestmentForm();
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

    private void ResetTransactionForm()
    {
        _transactionBeingEdited = null;
        RefreshSavingsDestinations(_snapshot);
        SelectedTransactionType = TransactionTypes.First(option => option.Type == TransactionType.Expense);
        TransactionDate = ToLocalDateTimeOffset(BudgetDateSelection.GetDefaultDate(_selectedMonth, _localToday));
        TransactionAmount = 0d;
        TransactionNote = string.Empty;
        SelectedSavingsDestination = SavingsDestinations.FirstOrDefault();
        NotifyTransactionEditorChanged();
    }

    private void ResetInvestmentForm()
    {
        _investmentBeingEdited = null;
        InvestmentName = string.Empty;
        InvestmentProvider = string.Empty;
        InvestmentKind = "Other";
        InvestmentCurrentValue = double.NaN;
        InvestmentUnits = double.NaN;
        InvestmentUnitPrice = double.NaN;
        InvestmentValuationDate = ToLocalDateTimeOffset(GetDefaultInvestmentValuationDate());
        InvestmentNote = string.Empty;
        NotifyInvestmentEditorChanged();
    }

    private void NotifyBillEditorChanged()
    {
        OnPropertyChanged(nameof(IsEditingBill));
        OnPropertyChanged(nameof(BillFormTitle));
        OnPropertyChanged(nameof(BillSubmitText));
    }

    private void NotifyTransactionEditorChanged()
    {
        OnPropertyChanged(nameof(IsEditingTransaction));
        OnPropertyChanged(nameof(TransactionFormTitle));
        OnPropertyChanged(nameof(TransactionSubmitText));
    }

    private void NotifyInvestmentEditorChanged()
    {
        OnPropertyChanged(nameof(IsEditingInvestment));
        OnPropertyChanged(nameof(InvestmentFormTitle));
        OnPropertyChanged(nameof(InvestmentSubmitText));
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
        CancelTransactionEditCommand.NotifyCanExecuteChanged();
        SaveMonthlyIncomeCommand.NotifyCanExecuteChanged();
        AddBillCommand.NotifyCanExecuteChanged();
        CancelBillEditCommand.NotifyCanExecuteChanged();
        AddGoalCommand.NotifyCanExecuteChanged();
        SaveInvestmentCommand.NotifyCanExecuteChanged();
        CancelInvestmentEditCommand.NotifyCanExecuteChanged();
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

    private static bool TryToOptionalDecimal(double amount, int decimals, out decimal? value)
    {
        value = null;
        if (double.IsNaN(amount))
        {
            return true;
        }

        if (!double.IsFinite(amount) || amount < 0d)
        {
            return false;
        }

        try
        {
            value = decimal.Round(Convert.ToDecimal(amount), decimals, MidpointRounding.AwayFromZero);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static string FormatMonth(BudgetMonth month) =>
        new DateTime(month.Year, month.Month, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    private DateOnly GetDefaultInvestmentValuationDate() =>
        _localToday <= _selectedMonth.LastDay ? _localToday : _selectedMonth.LastDay;

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

    private void RefreshSavingsDestinations(BudgetSnapshot snapshot)
    {
        var selectedGoalId = SelectedSavingsDestination?.GoalId;
        var selectedInvestmentId = SelectedSavingsDestination?.InvestmentId;
        var options = new List<SavingsDestinationOption>
        {
            new("General savings", null, null),
        };
        options.AddRange(snapshot.Goals
            .OrderBy(goal => goal.Name)
            .Select(goal => new SavingsDestinationOption($"Goal · {goal.Name}", goal.Id, null)));
        options.AddRange(snapshot.Investments
            .Where(investment => !investment.IsArchived)
            .OrderBy(investment => investment.Name)
            .Select(investment => new SavingsDestinationOption($"Investment · {investment.Name}", null, investment.Id)));
        if (_transactionBeingEdited?.InvestmentId is long editedInvestmentId
            && options.All(option => option.InvestmentId != editedInvestmentId))
        {
            var archivedInvestment = snapshot.Investments.FirstOrDefault(investment => investment.Id == editedInvestmentId);
            if (archivedInvestment is not null)
            {
                options.Add(new SavingsDestinationOption(
                    $"Investment · {archivedInvestment.Name} (archived)",
                    null,
                    archivedInvestment.Id));
            }
        }
        Replace(SavingsDestinations, options);
        SelectedSavingsDestination = SavingsDestinations.FirstOrDefault(option =>
            option.GoalId == selectedGoalId && option.InvestmentId == selectedInvestmentId)
            ?? SavingsDestinations.FirstOrDefault();
    }

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
            TransactionType.Income => CategoryKind.Income,
            _ => (CategoryKind?)null,
        };

        Replace(
            AvailableTransactionCategories,
            kind is null
                ? Array.Empty<CategoryOption>()
                : CategoryOptions.Where(option => option.Kind == kind));
        SelectedTransactionCategory = AvailableTransactionCategories.FirstOrDefault();
        if (SelectedTransactionType.Type != TransactionType.Savings)
        {
            SelectedSavingsDestination = SavingsDestinations.FirstOrDefault();
        }
    }

    private string GetTransactionDestinationLabel(BudgetTransaction transaction, BudgetSnapshot snapshot)
    {
        if (transaction.SavingsGoalId is long goalId)
        {
            var goalName = snapshot.Goals.FirstOrDefault(goal => goal.Id == goalId)?.Name ?? "Archived goal";
            return $"Goal · {goalName}";
        }

        if (transaction.InvestmentId is long investmentId)
        {
            var investmentName = snapshot.Investments.FirstOrDefault(investment => investment.Id == investmentId)?.Name
                ?? "Archived investment";
            return $"Investment · {investmentName}";
        }

        var categoryName = snapshot.Categories.FirstOrDefault(category => category.Id == transaction.CategoryId)?.Name;
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            return transaction.RecurringIncomeId is null ? categoryName : $"{categoryName} · Recurring";
        }

        return transaction.Type switch
        {
            TransactionType.Income => "Other income",
            TransactionType.Savings => "General savings",
            TransactionType.Transfer => "Transfer",
            _ => "Uncategorised",
        };
    }

    private string BuildIncomeScheduleText(RecurringIncome? schedule)
    {
        if (schedule is null)
        {
            return "Not scheduled yet. Choose a payday and save once to repeat automatically; days 29–31 use month-end when needed.";
        }

        if (!schedule.IsActive)
        {
            return "Paused. Income already recorded in earlier months remains untouched.";
        }

        var nextSearchDate = _localToday == DateOnly.MaxValue
            ? _localToday
            : _localToday.AddDays(1);
        var nextDate = RecurringIncomeSchedule.GetNextDepositDate(schedule, nextSearchDate);
        var nextText = nextDate is null
            ? "The schedule has ended."
            : $"Next deposit: {nextDate.Value:dd MMM yyyy}.";
        var clampText = schedule.PayDay >= 29 ? " Shorter months use their last day." : string.Empty;
        return $"Paid on day {schedule.PayDay} every month. {nextText}{clampText} Changes apply to deposits that have not been posted yet; use Edit entry for an existing deposit.";
    }

    private string GetCarryForwardContext(decimal amount, BudgetMonth month)
    {
        if (month.Year == 1 && month.Month == 1)
        {
            return "Opening balance before recorded history";
        }

        var previousText = FormatMonth(month.Previous);
        return amount switch
        {
            > 0m => $"Left over after {previousText}",
            < 0m => $"Shortfall brought from {previousText}",
            _ => $"No balance left from {previousText}",
        };
    }

    private string FormatSignedDifference(decimal amount) => amount switch
    {
        > 0m => $"+{FormatMoney(amount)}",
        < 0m => $"−{FormatMoney(Math.Abs(amount))}",
        _ => FormatMoney(0m),
    };

    private static string FormatInvestmentKind(MyBudget.Core.InvestmentKind kind) => kind switch
    {
        MyBudget.Core.InvestmentKind.SavingsFund => "Savings fund",
        MyBudget.Core.InvestmentKind.UnitTrust => "Unit trust",
        MyBudget.Core.InvestmentKind.Gold => "Gold",
        _ => "Other",
    };

    private static MyBudget.Core.InvestmentKind ParseInvestmentKind(string value) => value switch
    {
        "Savings fund" => MyBudget.Core.InvestmentKind.SavingsFund,
        "Unit trust" => MyBudget.Core.InvestmentKind.UnitTrust,
        "Gold" => MyBudget.Core.InvestmentKind.Gold,
        _ => MyBudget.Core.InvestmentKind.Other,
    };

    private static string GetInvestmentColor(MyBudget.Core.InvestmentKind kind) => kind switch
    {
        MyBudget.Core.InvestmentKind.Gold => "#D6A632",
        MyBudget.Core.InvestmentKind.UnitTrust => "#7C3AED",
        MyBudget.Core.InvestmentKind.SavingsFund => "#0B8F75",
        _ => "#64748B",
    };

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
        OnPropertyChanged(nameof(CarryForwardText));
        OnPropertyChanged(nameof(CarryForwardContextText));
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
        OnPropertyChanged(nameof(PortfolioValueText));
        OnPropertyChanged(nameof(PortfolioContributedText));
        OnPropertyChanged(nameof(PortfolioGainLossText));
        OnPropertyChanged(nameof(PortfolioMonthContributionText));
        NotifyBillEditorChanged();
        NotifyTransactionEditorChanged();
        NotifyInvestmentEditorChanged();
    }

    private sealed record LoadedMonth(
        BudgetSnapshot Snapshot,
        IReadOnlyList<BudgetSnapshot> TrendSnapshots);
}

public sealed record TransactionTypeOption(TransactionType Type, string Label);

public sealed record CategoryOption(long Id, string Name, CategoryKind Kind);

public sealed record SavingsDestinationOption(string Label, long? GoalId, long? InvestmentId);

public sealed record ArchivedInvestmentRow(long Id, string Name, string DetailText);

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
    string Tone,
    string ActionText,
    bool CanDelete);

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
    string PercentText,
    string StartingText,
    string ContributionText,
    string MonthContributionText);

public sealed record InvestmentRow(
    long Id,
    string Name,
    string DetailText,
    string CurrentValueText,
    string ContributedText,
    string GainLossText,
    string MonthContributionText,
    string ValuationText,
    double ContributionValueRatio);

public sealed record MonthlyTrendRow(string MonthText, string IncomeText, string SpentText, string SavedText);
