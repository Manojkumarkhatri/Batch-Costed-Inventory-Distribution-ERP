using System.Windows;

namespace ShopApp.UI.Views;

/// <summary>Asks whether the description goes on the printout.</summary>
public partial class PrintOptionsDialog : Window
{
    public bool WithDescription { get; private set; } = true;

    public PrintOptionsDialog(string heading, bool hasDescription)
    {
        InitializeComponent();
        HeaderText.Text = heading;

        // Saying "with or without" about something that is not there wastes a
        // decision, so the dialog admits it rather than pretending.
        if (!hasDescription)
        {
            NoDescNote.Visibility = Visibility.Visible;
            WithBox.IsEnabled = false;
            WithoutBox.IsEnabled = false;
        }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        WithDescription = WithBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
