using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using MyBudget.App.ViewModels;
using MyBudget.Infrastructure;
using Windows.Storage.Pickers;
using System.ComponentModel;

namespace MyBudget.App;

public sealed partial class MainPage : Page
{
    private bool _isApplyingTheme;
    private bool _isRefreshingLocalDate;
    private DispatcherQueueTimer? _localDateRefreshTimer;

    public MainPageViewModel ViewModel { get; }
    public PlanningViewModel Planning { get; }

    public MainPage()
    {
        var configuredDataDirectory = Environment.GetEnvironmentVariable("MYBUDGET_DATA_DIRECTORY");
        var dataDirectory = string.IsNullOrWhiteSpace(configuredDataDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KhaiFaw",
                "MyBudget")
            : Path.GetFullPath(configuredDataDirectory);
        var databasePath = Path.Combine(dataDirectory, "mybudget.db");
        _notionStore = new NotionConnectionStore(dataDirectory);

        ViewModel = new MainPageViewModel(
            new SqliteBudgetRepository(databasePath),
            databasePath);
        Planning = new PlanningViewModel(new PlanningProfileStore(Path.Combine(dataDirectory, "planning-profile.json")));
        ViewModel.ThemeRequested += ViewModel_ThemeRequested;

        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        UpdatePaneFooter(RootNavigation.IsPaneOpen);
        UpdateBillEditState();
        UpdateTransactionEditState();
        ShowSection("Overview");
        await ViewModel.InitializeAsync();
        await Planning.LoadAsync();
        await LoadNotionConnectionAsync();
        StartLocalDateRefreshTimer();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e) =>
        _localDateRefreshTimer?.Stop();

    private void StartLocalDateRefreshTimer()
    {
        if (_localDateRefreshTimer is null)
        {
            _localDateRefreshTimer = DispatcherQueue.CreateTimer();
            _localDateRefreshTimer.Interval = TimeSpan.FromMinutes(1);
            _localDateRefreshTimer.IsRepeating = true;
            _localDateRefreshTimer.Tick += LocalDateRefreshTimer_Tick;
        }

        _localDateRefreshTimer.Start();
    }

    private async void LocalDateRefreshTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_isRefreshingLocalDate)
        {
            return;
        }

        _isRefreshingLocalDate = true;
        try
        {
            await ViewModel.RefreshForLocalDateAsync();
        }
        finally
        {
            _isRefreshingLocalDate = false;
        }
    }

    private void RootNavigation_PaneClosing(
        NavigationView sender,
        NavigationViewPaneClosingEventArgs args) => UpdatePaneFooter(false);

    private void RootNavigation_PaneOpening(NavigationView sender, object args) =>
        UpdatePaneFooter(true);

    private void UpdatePaneFooter(bool isPaneOpen)
    {
        var expandedVisibility = isPaneOpen ? Visibility.Visible : Visibility.Collapsed;

        ExpandedPrivacyFooter.Visibility = expandedVisibility;
        FooterDivider.Visibility = expandedVisibility;
        ExpandedCreatorAttribution.Visibility = expandedVisibility;
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var section = args.IsSettingsSelected
            ? "Settings"
            : (args.SelectedItemContainer as NavigationViewItem)?.Tag?.ToString() ?? "Overview";
        ShowSection(section);
    }

    private void ShowSection(string section)
    {
        if (OverviewView is null)
        {
            return;
        }

        OverviewView.Visibility = section == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        PlanView.Visibility = section == "Plan" ? Visibility.Visible : Visibility.Collapsed;
        FuturePlanView.Visibility = section == "Future plan" ? Visibility.Visible : Visibility.Collapsed;
        TransactionsView.Visibility = section == "Transactions" ? Visibility.Visible : Visibility.Collapsed;
        BillsView.Visibility = section == "Bills" ? Visibility.Visible : Visibility.Collapsed;
        GoalsView.Visibility = section == "Goals" ? Visibility.Visible : Visibility.Collapsed;
        ReportsView.Visibility = section == "Reports" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = section == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        SectionTitle.Text = section switch { "Overview" => "Today", "Transactions" => "Activity", "Plan" => "Budget", "Reports" => "Insights", _ => section };
        MonthNavigation.Visibility = section == "Future plan" ? Visibility.Collapsed : Visibility.Visible;
        SectionDescription.Text = section switch
        {
            "Overview" => "A little clarity for your everyday money.",
            "Transactions" => "Your money in and out, all in one place.",
            "Bills" => "Check it off. Keep the next due date in sight.",
            "Plan" => "Give your money a simple monthly plan.",
            "Goals" => "Small steps toward the things that matter.",
            "Future plan" => "Estimates and possibilities — separate from money already received or spent.",
            "Reports" => "Understand the patterns behind your spending.",
            _ => "Your preferences, connections and local data."
        };
        ResizeDailyViews();
        if (section == "Future plan") ResizeFuturePlan();
        if (section == "Bills") ResizeBills();
    }

    private void FuturePlanView_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeFuturePlan();

    private void BillsView_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeBills();

    private void DailyView_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeDailyViews();
    private void ActivityView_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeDailyViews();
    private void GoalsView_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeDailyViews();

    private void ResizeDailyViews()
    {
        if (DailyContent is not null) DailyContent.Width = Math.Clamp(OverviewView.ActualWidth - 64, 760, 1160);
        if (ActivityContent is not null) ActivityContent.Width = Math.Clamp(TransactionsView.ActualWidth - 64, 760, 1160);
        if (GoalsContent is not null) GoalsContent.Width = Math.Clamp(GoalsView.ActualWidth - 64, 760, 1160);
    }

    private void ResizeBills()
    {
        if (BillsContent is not null && BillsView is not null)
            BillsContent.Width = Math.Clamp(BillsView.ActualWidth - 64, 760, 1160);
    }

    private void ResizeFuturePlan()
    {
        if (FuturePlanContent is not null && FuturePlanView is not null)
            FuturePlanContent.Width = Math.Max(960, FuturePlanView.ActualWidth - 64);
    }

    private void NavigateTo(string tag)
    {
        foreach (var menuItem in RootNavigation.MenuItems.OfType<NavigationViewItem>())
        {
            if (string.Equals(menuItem.Tag?.ToString(), tag, StringComparison.Ordinal))
            {
                RootNavigation.SelectedItem = menuItem;
                return;
            }
            foreach (var child in menuItem.MenuItems.OfType<NavigationViewItem>())
            {
                if (string.Equals(child.Tag?.ToString(), tag, StringComparison.Ordinal))
                {
                    menuItem.IsExpanded = true;
                    RootNavigation.SelectedItem = child;
                    return;
                }
            }
        }
    }

    private void GoToPlan_Click(object sender, RoutedEventArgs e) => NavigateTo("Plan");

    private void GoToBills_Click(object sender, RoutedEventArgs e) => NavigateTo("Bills");

    private void GoToTransactions_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.UseTodayForTransaction();
        TransactionEntryExpander.IsExpanded = true;
        NavigateTo("Transactions");
    }

    private void ViewActivity_Click(object sender, RoutedEventArgs e) => NavigateTo("Transactions");

    private void UseToday_Click(object sender, RoutedEventArgs e) => ViewModel.UseTodayForTransaction();

    private async void SavePlanning_Click(object sender, RoutedEventArgs e)
    {
        await Task.Yield();
        await Planning.SaveCommand.ExecuteAsync(null);
    }

    private async void ImportPlanning_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add(".json");
        InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is not null) await Planning.ImportAsync(file.Path);
    }

    private async void SaveMonthlyIncome_Click(object sender, RoutedEventArgs e)
    {
        // NumberBox commits typed text after focus leaves its internal TextBox.
        // Yield once so a single Update click saves the value the user can see.
        await Task.Yield();
        await ViewModel.SaveMonthlyIncomeCommand.ExecuteAsync(null);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainPageViewModel.IsEditingBill))
        {
            UpdateBillEditState();
            BillEntryExpander.IsExpanded = ViewModel.IsEditingBill;
        }

        if (args.PropertyName == nameof(MainPageViewModel.IsEditingTransaction))
        {
            UpdateTransactionEditState();
            TransactionEntryExpander.IsExpanded = ViewModel.IsEditingTransaction;
        }

    }

    private void UpdateBillEditState()
    {
        if (CancelBillEditButton is not null)
        {
            CancelBillEditButton.Visibility = ViewModel.IsEditingBill
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void UpdateTransactionEditState()
    {
        if (CancelTransactionEditButton is not null)
        {
            CancelTransactionEditButton.Visibility = ViewModel.IsEditingTransaction
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private async void ThemeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isApplyingTheme && sender is ToggleSwitch toggle)
        {
            await ViewModel.SetDarkModeAsync(toggle.IsOn);
        }
    }

    private async void SettingsThemeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isApplyingTheme && sender is ToggleSwitch toggle)
        {
            await ViewModel.SetDarkModeAsync(toggle.IsOn);
        }
    }

    private void ViewModel_ThemeRequested(object? sender, bool useDarkMode)
    {
        _isApplyingTheme = true;
        RequestedTheme = useDarkMode ? ElementTheme.Dark : ElementTheme.Light;

        if (ThemeToggle is not null)
        {
            ThemeToggle.IsOn = useDarkMode;
        }

        if (SettingsThemeToggle is not null)
        {
            SettingsThemeToggle.IsOn = useDarkMode;
        }

        _isApplyingTheme = false;
    }

    private async void DeleteTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
        {
            var isPostedIncome = ViewModel.IsPostedRecurringIncome(id);
            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = isPostedIncome ? "Delete this income entry?" : "Delete this transaction?",
                Content = isPostedIncome
                    ? "Only this posted deposit will be removed. Your monthly schedule and future deposits will continue."
                    : "This permanently removes only this transaction.",
                PrimaryButtonText = "Delete entry",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };

            if (await confirmation.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteTransactionAsync(id);
            }
        }
    }

    private void EditTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
        {
            ViewModel.BeginEditTransaction(id);
            NavigateTo("Transactions");
        }
    }

    private async void DeleteBill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long id })
        {
            await ViewModel.DeleteBillAsync(id);
        }
    }

    private async void ToggleBillPayment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BillRow row })
        {
            await ViewModel.ToggleBillPaymentAsync(row);
        }
    }

    private void EditBill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long id })
        {
            ViewModel.BeginEditBill(id);
            NavigateTo("Bills");
        }
    }

    private async void DeleteGoal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long id })
        {
            await ViewModel.DeleteGoalAsync(id);
        }
    }

    private void AddMoneyToGoal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long id })
        {
            ViewModel.PrepareSavingsForGoal(id);
            TransactionEntryExpander.IsExpanded = true;
            NavigateTo("Transactions");
        }
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"mybudget-backup-{DateTime.Now:yyyy-MM-dd}",
        };
        picker.FileTypeChoices.Add("SQLite database", [".db"]);
        InitializePicker(picker);

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await ViewModel.CreateBackupAsync(file.Path);
        }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"mybudget-transactions-{DateTime.Now:yyyy-MM}",
        };
        picker.FileTypeChoices.Add("CSV spreadsheet", [".csv"]);
        InitializePicker(picker);

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await ViewModel.ExportCsvAsync(file.Path);
        }
    }

    private async void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".csv");
        InitializePicker(picker);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await ViewModel.ImportCsvAsync(file.Path);
        }
    }

    private static void InitializePicker(object picker) =>
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
}
