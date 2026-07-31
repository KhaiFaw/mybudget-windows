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
    private BudgetMonth _selectedMonth = BudgetMonth.FromDate(DateOnly.FromDateTime(DateTime.Today));
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
    private string _billName = string.Empty;
    private double _billAmount;
    private double _billDueDay = 1;
    private CategoryOption? _selectedBillCategory;
    private string _goalName = string.Empty;
    private double _goalTargetAmount;
    private double _goalCurrentAmount;
    private DateTimeOffset _goalTargetDate = DateTimeOffset.Now.AddMonths(6);

    public MainPageViewModel(IBudgetRepository repository, string databasePath)
    {
        _repository = repository;
        DatabasePath = databasePath;
        _snapshot = BudgetSnapshot.Empty(_selectedMonth);
        _transactionDate = DefaultTransactionDate(_selectedMonth);

        TransactionTypes = Enum.GetValues<TransactionType>()
            .Select(type => new TransactionTypeOption(type, SplitWords(type.ToString())))
            .ToArray();
        SelectedTransactionType = TransactionTypes.First(option => option.Type == TransactionType.Expense);

        PreviousMonthCommand = new AsyncRelayCommand(() => ChangeMonthAsync(_selectedMonth.Previous));
        NextMonthCommand = new AsyncRelayCommand(() => ChangeMonthAsync(_selectedMonth.Next));
        CurrentMonthCommand = new AsyncRelayCommand(() => ChangeMonthAsync(BudgetMonth.FromDate(DateOnly.FromDateTime(DateTime.Today))));
        SavePlanCommand = new AsyncRelayCommand(SavePlanAsync);
        AddTransactionCommand = new AsyncRelayCommand(AddTransactionAsync);
        AddBillCommand = new AsyncRelayCommand(AddBillAsync);
        AddGoalCommand = new AsyncRelayCommand(AddGoalAsync);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        SeedDemoDataCommand = new AsyncRelayCommand(SeedDemoDataAsync);
    }

    public event EventHandler<bool>? ThemeRequested;

    public IAsyncRelayCommand PreviousMonthCommand { get; }
    public IAsyncRelayCommand NextMonthCommand { get; }
    public IAsyncRelayCommand CurrentMonthCommand { get; }
    public IAsyncRelayCommand SavePlanCommand { get; }
    public IAsyncRelayCommand AddTransactionCommand { get; }
    public IAsyncRelayCommand AddBillCommand { get; }
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

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(LocalSaveText));
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
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await RunAsync(async () =>
        {
            await _repository.InitializeAsync();
            await LoadMonthAsync();
            StatusText = "Saved locally";
        }, "We couldn't open your local budget.");
    }

    public async Task SetDarkModeAsync(bool isDarkMode)
    {
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

    public async Task DeleteBillAsync(long id) => await RunAndReloadAsync(
        () => _repository.DeleteRecurringBillAsync(id),
        "Bill deleted",
        "We couldn't delete that bill.");

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

    public async Task ImportCsvAsync(string path) => await RunAsync(async () =>
    {
        var result = await _repository.ImportTransactionsCsvAsync(path);
        await LoadMonthAsync();
        StatusText = $"Imported {result.ImportedCount}; skipped {result.SkippedCount}";
    }, "We couldn't import that CSV file.");

    private async Task ChangeMonthAsync(BudgetMonth month)
    {
        _selectedMonth = month;
        TransactionDate = DefaultTransactionDate(month);
        await RunAsync(LoadMonthAsync, "We couldn't load that month.");
    }

    private async Task LoadMonthAsync()
    {
        _snapshot = await _repository.LoadAsync(_selectedMonth);
        ApplySnapshot(_snapshot);
        await LoadTrendAsync();
    }

    private async Task LoadTrendAsync()
    {
        MonthlyTrend.Clear();
        var month = _selectedMonth;
        var snapshots = new List<BudgetSnapshot>();

        for (var index = 0; index < 6; index++)
        {
            snapshots.Add(month == _selectedMonth ? _snapshot : await _repository.LoadAsync(month));
            month = month.Previous;
        }

        foreach (var snapshot in snapshots.AsEnumerable().Reverse())
        {
            var summary = BudgetCalculator.Calculate(snapshot);
            MonthlyTrend.Add(new MonthlyTrendRow(
                new DateTime(snapshot.Month.Year, snapshot.Month.Month, 1).ToString("MMM", CultureInfo.CurrentCulture),
                FormatMoney(summary.Income),
                FormatMoney(summary.Spent),
                FormatMoney(summary.Saved)));
        }
    }

    private void ApplySnapshot(BudgetSnapshot snapshot)
    {
        var summary = BudgetCalculator.Calculate(snapshot);
        CurrencyCode = snapshot.Settings.CurrencyCode;
        IsDarkMode = snapshot.Settings.IsDarkMode;
        ThemeRequested?.Invoke(this, IsDarkMode);

        IncomeText = FormatMoney(summary.Income);
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
        SelectedBillCategory = BillCategoryOptions.FirstOrDefault();

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

        Replace(Bills, snapshot.Bills
            .Where(bill => bill.IsActive)
            .Select(bill => (Bill: bill, DueDate: RecurringDateCalculator.GetDueDate(bill, snapshot.Month)))
            .Where(item => item.DueDate is not null)
            .OrderBy(item => item.DueDate)
            .Select(item => new BillRow(
                item.Bill.Id,
                item.Bill.Name,
                $"Due {item.DueDate!.Value:dd MMM}",
                snapshot.Categories.FirstOrDefault(category => category.Id == item.Bill.CategoryId)?.Name ?? "Uncategorised",
                FormatMoney(item.Bill.Amount))));

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
        var allocations = CategoryRows
            .Select(row => new BudgetAllocation(row.CategoryId, _selectedMonth, ToMoney(Math.Max(0d, row.PlannedAmount))))
            .ToArray();

        await RunAndReloadAsync(
            () => _repository.SaveAllocationsAsync(_selectedMonth, allocations),
            "Monthly plan saved",
            "We couldn't save your plan.");
    }

    private async Task AddTransactionAsync()
    {
        if (TransactionAmount <= 0d || SelectedTransactionType is null)
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

        var transaction = new BudgetTransaction(
            Guid.NewGuid(),
            DateOnly.FromDateTime(TransactionDate.Date),
            SelectedTransactionType.Type,
            ToMoney(TransactionAmount),
            SelectedTransactionType.Type is TransactionType.Income or TransactionType.Transfer
                ? null
                : SelectedTransactionCategory?.Id,
            TransactionNote.Trim());

        await RunAndReloadAsync(
            () => _repository.UpsertTransactionAsync(transaction),
            "Transaction added",
            "We couldn't save that transaction.");

        TransactionAmount = 0d;
        TransactionNote = string.Empty;
    }

    private async Task AddBillAsync()
    {
        if (string.IsNullOrWhiteSpace(BillName) || BillAmount <= 0d || BillDueDay is < 1d or > 31d)
        {
            SetError("Enter a bill name, an amount greater than zero, and a due day from 1 to 31.");
            return;
        }

        var bill = new RecurringBill(
            0,
            BillName.Trim(),
            ToMoney(BillAmount),
            Convert.ToInt32(BillDueDay),
            SelectedBillCategory?.Id);

        await RunAndReloadAsync(
            () => _repository.UpsertRecurringBillAsync(bill),
            "Recurring bill added",
            "We couldn't save that recurring bill.");

        BillName = string.Empty;
        BillAmount = 0d;
        BillDueDay = 1d;
    }

    private async Task AddGoalAsync()
    {
        if (string.IsNullOrWhiteSpace(GoalName) || GoalTargetAmount <= 0d || GoalCurrentAmount < 0d)
        {
            SetError("Enter a goal name and a target amount greater than zero.");
            return;
        }

        var goal = new SavingsGoal(
            0,
            GoalName.Trim(),
            ToMoney(GoalTargetAmount),
            ToMoney(GoalCurrentAmount),
            DateOnly.FromDateTime(GoalTargetDate.Date));

        await RunAndReloadAsync(
            () => _repository.UpsertSavingsGoalAsync(goal),
            "Savings goal added",
            "We couldn't save that savings goal.");

        GoalName = string.Empty;
        GoalTargetAmount = 0d;
        GoalCurrentAmount = 0d;
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
        await RunAsync(async () =>
        {
            var added = await _repository.SeedDemoDataAsync(_selectedMonth);
            await LoadMonthAsync();
            StatusText = added ? "Example budget loaded" : "This month already has data";
        }, "We couldn't load the example budget.");
    }

    private async Task RunAndReloadAsync(Func<Task> action, string successMessage, string errorMessage)
    {
        await RunAsync(async () =>
        {
            await action();
            await LoadMonthAsync();
            StatusText = successMessage;
        }, errorMessage);
    }

    private async Task RunAsync(Func<Task> action, string errorMessage)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            SetError($"{errorMessage} {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetError(string message)
    {
        StatusText = message;
        StatusIsError = true;
    }

    private string FormatMoney(decimal amount) => $"{CurrencyPrefix(CurrencyCode)} {amount:N2}";

    private static decimal ToMoney(double amount) => decimal.Round(
        Convert.ToDecimal(amount),
        2,
        MidpointRounding.AwayFromZero);

    private static DateTimeOffset DefaultTransactionDate(BudgetMonth month)
    {
        var day = Math.Min(DateTime.Today.Day, DateTime.DaysInMonth(month.Year, month.Month));
        var localDate = new DateTime(month.Year, month.Month, day, 12, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));
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
        OnPropertyChanged(nameof(SelectedMonthText));
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
    }
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

public sealed record BillRow(long Id, string Name, string DueText, string CategoryName, string AmountText);

public sealed record GoalRow(
    long Id,
    string Name,
    string CurrentText,
    string TargetText,
    string DueText,
    double PercentComplete,
    string PercentText);

public sealed record MonthlyTrendRow(string MonthText, string IncomeText, string SpentText, string SavedText);
