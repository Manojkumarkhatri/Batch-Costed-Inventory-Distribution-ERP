using System.Windows;

namespace ShopApp.UI.Views;

/// <summary>
/// Asks for the backup passphrase. In confirm mode it is typed twice and the
/// user has to tick that it is written down, because a passphrase that only
/// exists in someone's head is not a backup strategy - DPAPI dies with the PC.
/// </summary>
public partial class PassphraseDialog : Window
{
    private readonly bool _confirm;

    /// <summary>What was typed; null if cancelled.</summary>
    public string? Passphrase { get; private set; }

    public PassphraseDialog(string heading, bool confirm)
    {
        InitializeComponent();
        _confirm = confirm;

        HeaderText.Text = heading;
        Title = heading;

        if (!confirm)
        {
            // Entering an existing passphrase to open a backup: no second box,
            // and the warning does not apply.
            ConfirmPanel.Visibility = Visibility.Collapsed;
            WarningPanel.Visibility = Visibility.Collapsed;
            FirstLabel.Text = "Passphrase used when this backup was made";
            OkButton.Content = "Open backup";
            Height = 280;
        }
        else
        {
            OkButton.Content = "Set passphrase";
        }

        Loaded += (_, _) => FirstBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var first = FirstBox.Password;

        if (string.IsNullOrEmpty(first))
        {
            Warn("Type the passphrase.");
            return;
        }

        if (_confirm)
        {
            if (first.Length < 8)
            {
                Warn("Use at least 8 characters. Four ordinary words is a good choice.");
                return;
            }

            if (first != SecondBox.Password)
            {
                Warn("The two entries do not match. Type it again in both boxes.");
                SecondBox.Clear();
                SecondBox.Focus();
                return;
            }

            if (WrittenDownBox.IsChecked != true)
            {
                Warn("Write the passphrase on paper and tick the box. " +
                     "Without it the backups cannot be opened on a new PC.");
                return;
            }
        }

        Passphrase = first;
        DialogResult = true;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Passphrase",
            MessageBoxButton.OK, MessageBoxImage.Warning);

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
