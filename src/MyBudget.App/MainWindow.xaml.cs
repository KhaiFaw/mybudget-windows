using Microsoft.UI.Xaml;

namespace MyBudget.App;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "MyBudget.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
        var display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
            AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
        var width = display is null ? 1480 : Math.Min(1480, Math.Max(640, display.WorkArea.Width - 32));
        var height = display is null ? 920 : Math.Min(920, Math.Max(480, display.WorkArea.Height - 32));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }
}
