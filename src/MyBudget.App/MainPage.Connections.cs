using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyBudget.Core;
using MyBudget.Infrastructure;
using Windows.Storage.Pickers;

namespace MyBudget.App;

public sealed partial class MainPage
{
    private readonly NotionConnectionStore _notionStore;
    private NotionConnection? _notionConnection;
    private bool _connectionActionBusy;

    private Task<ContentDialogResult> ShowMessageAsync(string title, string text) => new ContentDialog
    {
        XamlRoot = XamlRoot,
        Title = title,
        Content = text,
        CloseButtonText = "Close",
        RequestedTheme = RequestedTheme
    }.ShowAsync().AsTask();

    private async void ImportStatement_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionActionBusy || ViewModel.IsBusy) return;
        _connectionActionBusy = true;
        try
        {
            if (ViewModel.IsEditingTransaction)
            {
                await ShowMessageAsync("Finish your edit first", "Save or cancel the current transaction before importing a statement.");
                return;
            }
            if (ViewModel.CurrencyCode != "MYR")
            {
                await ShowMessageAsync("MYR statement", "Maybank statement imports use MYR. Switch your budget currency to MYR first; no currency conversion is performed.");
                return;
            }
            var pathBox = new TextBox { Header = "Statement PDF", PlaceholderText = "Choose a file or paste its full path", MaxLength = 1024 };
            var browse = new Button { Content = "Choose PDF…", HorizontalAlignment = HorizontalAlignment.Left };
            var readError = new TextBlock { TextWrapping = TextWrapping.Wrap };
            browse.Click += async (_, _) =>
            {
                browse.IsEnabled = false;
                try
                {
                    var picker = new FileOpenPicker { ViewMode = PickerViewMode.List };
                    picker.FileTypeFilter.Add(".pdf");
                    InitializePicker(picker);
                    var file = await picker.PickSingleFileAsync();
                    if (file is not null) pathBox.Text = file.Path;
                }
                catch (Exception) { readError.Text = "The file chooser couldn't open. Paste the PDF's full local path instead."; }
                finally { browse.IsEnabled = true; }
            };
            var password = new PasswordBox { Header = "PDF opening password (only if required)", PlaceholderText = "Leave blank if the file opens normally" };
            var explain = new StackPanel { Spacing = 12 };
            explain.Children.Add(new TextBlock
            {
                Text = "The PDF is read on this PC, without AI or uploads. A review screen opens next; no entries are saved yet. Use only PDFs you trust. Never enter your online-banking password here.",
                TextWrapping = TextWrapping.Wrap
            });
            explain.Children.Add(pathBox);
            explain.Children.Add(browse);
            explain.Children.Add(new Expander { Header = "Password-protected PDF?", Content = password, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            explain.Children.Add(readError);
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Read Maybank statement locally",
                Content = explain,
                PrimaryButtonText = "Read statement",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = RequestedTheme
            };
            confirm.PrimaryButtonClick += (_, args) =>
            {
                var selectedPath = pathBox.Text.Trim().Trim('"');
                if (!Path.IsPathFullyQualified(selectedPath) || selectedPath.StartsWith(@"\\", StringComparison.Ordinal) || !selectedPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || !File.Exists(selectedPath))
                {
                    readError.Text = "Choose a PDF saved on this PC, or paste its full local file path. Copy network files onto this PC first.";
                    args.Cancel = true;
                }
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            BankStatement statement;
            try { statement = await MaybankStatementReader.ReadAsync(pathBox.Text.Trim().Trim('"'), password.Password); }
            finally { password.Password = string.Empty; }
            var snapshot = await ViewModel.LoadImportMonthAsync(statement.Month);
            var review = new StatementImportDialog(statement, snapshot) { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme };
            if (await review.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.ImportStatementAsync(statement.Month, review.ReviewedEntries);
                NavigateTo("Transactions");
            }
        }
        catch (InvalidDataException exception) { await ShowMessageAsync("Statement not imported", exception.Message); }
        catch (Exception) { await ShowMessageAsync("Statement not imported", "We couldn't read this PDF. Check its opening password if needed, and use the original complete savings/current-account statement. No entries were added."); }
        finally { _connectionActionBusy = false; }
    }

    private async Task LoadNotionConnectionAsync()
    {
        try
        {
            _notionConnection = await _notionStore.LoadAsync();
            if (_notionConnection is not null)
            {
                NotionParentPage.Text = $"https://www.notion.so/{_notionConnection.ParentId:N}";
                NotionStatus.Text = "Connection saved on this PC. Nothing is sent until you preview and confirm.";
            }
        }
        catch (Exception)
        {
            NotionStatus.Text = "The saved connection couldn't be unlocked. Enter its page and token again on this Windows account.";
        }
    }

    private async void SaveNotion_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionActionBusy) return;
        _connectionActionBusy = true;
        try
        {
            var id = NotionSummaryClient.ParsePageId(NotionParentPage.Text);
            var token = string.IsNullOrWhiteSpace(NotionToken.Password) ? _notionConnection?.Token : NotionToken.Password.Trim();
            if (string.IsNullOrWhiteSpace(token) || token.Length > 512 || token.Any(char.IsWhiteSpace))
                throw new ArgumentException("Enter the connection token from Notion's developer portal.");
            var connection = new NotionConnection(id, token);
            await _notionStore.SaveAsync(connection);
            _notionConnection = connection;
            NotionToken.Password = string.Empty;
            NotionStatus.Text = "Saved encrypted for this Windows user. Preview a month to test the connection and send its summary.";
        }
        catch (ArgumentException exception) { NotionStatus.Text = exception.Message; }
        catch (Exception) { NotionStatus.Text = "The connection couldn't be saved. Your budget is unchanged."; }
        finally { _connectionActionBusy = false; }
    }

    private async void PreviewNotion_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionActionBusy || ViewModel.IsBusy) return;
        _connectionActionBusy = true;
        NotionPreviewButton.IsEnabled = false;
        try
        {
            var summary = MonthlySummary.Create(ViewModel.CurrentSnapshot);
            var content = new StackPanel { Spacing = 14 };
            content.Children.Add(new TextBlock
            {
                Text = _notionConnection is null ? "Preview only. Save a connection in Settings to enable sending." :
                    $"Destination: https://www.notion.so/{_notionConnection.ParentId:N}\nThis is everything that will be shared. Sending again updates this month's MyBudget summary block; other page content is left alone.",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock { Text = summary.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            var preview = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = summary.Title,
                Content = new ScrollViewer { Content = content, MaxHeight = 440 },
                PrimaryButtonText = _notionConnection is null ? "" : "Send to Notion",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = RequestedTheme
            };
            if (await preview.ShowAsync() != ContentDialogResult.Primary || _notionConnection is null) return;
            NotionStatus.Text = "Sending the approved summary…";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var client = new NotionSummaryClient();
            var page = await client.PublishAsync(_notionConnection.ParentId, _notionConnection.Token, summary, timeout.Token);
            NotionStatus.Text = $"Summary sent. {page}\nNo individual transactions or statement files were shared.";
        }
        catch (InvalidOperationException exception) { NotionStatus.Text = exception.Message; }
        catch (Exception) { NotionStatus.Text = "The send could not be confirmed. Check your connection, then preview and try again. Your local budget is unchanged."; }
        finally { NotionPreviewButton.IsEnabled = true; _connectionActionBusy = false; }
    }

    private async void ForgetNotion_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionActionBusy) return;
        _connectionActionBusy = true;
        try
        {
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Forget this Notion connection?",
                Content = "This removes its saved token from this PC. Existing Notion pages and your local budget are not deleted. You can revoke the token in Notion's settings.",
                PrimaryButtonText = "Forget connection",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = RequestedTheme
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            _notionStore.Forget();
            _notionConnection = null;
            NotionParentPage.Text = NotionToken.Password = string.Empty;
            NotionStatus.Text = "Connection removed from this PC. No further summaries can be sent until you connect again.";
        }
        catch (Exception) { NotionStatus.Text = "The saved connection could not be removed. Try again after closing any other MyBudget window."; }
        finally { _connectionActionBusy = false; }
    }
}
