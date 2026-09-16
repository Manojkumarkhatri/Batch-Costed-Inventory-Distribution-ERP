using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ShopApp.Services;

namespace ShopApp.UI.Views;

/// <summary>
/// Shown when something escapes every other handler.
///
/// It offers Continue rather than only Close because most escaped exceptions
/// are one bad screen, not a broken process - closing the app would lose
/// whatever else he had open for no reason. When the failure happened during
/// startup there is nothing left to continue into, and the button says Close.
/// </summary>
public partial class CrashDialog : Window
{
    public CrashDialog(Exception ex, bool fatal)
    {
        InitializeComponent();

        Summary.Text = fatal
            ? "The application cannot continue and will close. Start it again and it will "
              + "pick up exactly where your data left off."
            : "The screen you were on could not finish what it was doing. You can carry on "
              + "using the rest of the application.";

        if (fatal) ContinueButton.Content = "Close";

        Detail.Text = AppLog.Describe(ex);

        LogPathText.Text = string.IsNullOrEmpty(AppLog.Folder)
            ? ""
            : "Logged to " + AppLog.TodayFile;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Detail.Text);
            ((Button)sender).Content = "Copied";
        }
        catch
        {
            // The clipboard is occasionally locked by another process. Not
            // worth a second error dialog on top of the first.
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(AppLog.Folder)) return;

        try
        {
            Process.Start(new ProcessStartInfo(AppLog.Folder) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    private void Continue_Click(object sender, RoutedEventArgs e) => Close();
}
