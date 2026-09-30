using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyBudget.App.ViewModels;
using MyBudget.Core;

namespace MyBudget.App;

public sealed partial class StatementImportDialog : ContentDialog
{
    private readonly StatementReviewRow[] _rows;
    public IReadOnlyList<BudgetTransaction> ReviewedEntries { get; private set; } = [];

    public StatementImportDialog(BankStatement statement, BudgetSnapshot snapshot)
    {
        InitializeComponent();
        Resources["ContentDialogMaxWidth"] = 1000d;
        Loaded += (_, _) =>
        {
            ReviewContent.Width = Math.Clamp(XamlRoot.Size.Width - 112, 640, 880);
            RowsList.Height = Math.Clamp(XamlRoot.Size.Height - 480, 160, 330);
        };
        _rows = statement.Entries.Select(entry => new StatementReviewRow(entry, snapshot)).ToArray();
        StatementSummary.Text = $"{statement.Month.FirstDay:MMMM yyyy} · {_rows.Length} verified rows · {_rows.Count(row => row.AlreadyImported)} already imported";
        RowsList.ItemsSource = _rows;
        foreach (var row in _rows) row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(StatementReviewRow.IsSelected)) UpdateCount();
        };
        UpdateCount();
    }

    private void UpdateCount() => SelectionCount.Text = $"{_rows.Count(row => row.IsSelected)} selected";
    private void SelectPurchases_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.IsSelected = row.IsCardPurchase && !row.PossibleDuplicate;
    }
    private void SelectNew_Click(object sender, RoutedEventArgs e)
    {
        // Potential manual/scheduled duplicates always require individual selection.
        foreach (var row in _rows) row.IsSelected = !row.PossibleDuplicate;
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.IsSelected = false;
    }
    private void Import_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (ReviewConfirmed.IsChecked != true || !_rows.Any(row => row.IsSelected))
        {
            ReviewError.Text = "Select at least one new row and confirm your review first.";
            args.Cancel = true;
            return;
        }
        try { ReviewedEntries = _rows.Where(row => row.IsSelected).Select(row => row.ToTransaction()).ToArray(); }
        catch (InvalidOperationException exception)
        {
            ReviewError.Text = exception.Message;
            args.Cancel = true;
        }
    }
}
