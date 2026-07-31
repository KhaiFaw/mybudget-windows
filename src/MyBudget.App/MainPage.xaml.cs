using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyBudget.App.ViewModels;
using MyBudget.Infrastructure;
using Windows.Storage.Pickers;

namespace MyBudget.App;

public sealed partial class MainPage : Page
{
    private bool _isApplyingTheme;

    public MainPageViewModel ViewModel { get; }

    public MainPage()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KhaiFaw",
            "MyBudget");
        var databasePath = Path.Combine(dataDirectory, "mybudget.db");

        ViewModel = new MainPageViewModel(
            new SqliteBudgetRepository(databasePath),
            databasePath);
        ViewModel.ThemeRequested += ViewModel_ThemeRequested;

        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ShowSection("Overview");
        await ViewModel.InitializeAsync();
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
        TransactionsView.Visibility = section == "Transactions" ? Visibility.Visible : Visibility.Collapsed;
        BillsView.Visibility = section == "Bills" ? Visibility.Visible : Visibility.Collapsed;
        GoalsView.Visibility = section == "Goals" ? Visibility.Visible : Visibility.Collapsed;
        ReportsView.Visibility = section == "Reports" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = section == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        SectionTitle.Text = section;
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
        }
    }

    private void GoToPlan_Click(object sender, RoutedEventArgs e) => NavigateTo("Plan");

    private void GoToBills_Click(object sender, RoutedEventArgs e) => NavigateTo("Bills");

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
            await ViewModel.DeleteTransactionAsync(id);
        }
    }

    private async void DeleteBill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long id })
        {
            await ViewModel.DeleteBillAsync(id);
        }
    }

    private async void DeleteGoal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long id })
        {
            await ViewModel.DeleteGoalAsync(id);
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
