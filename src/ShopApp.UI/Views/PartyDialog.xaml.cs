using System.Windows;
using ShopApp.Domain.Entities;

namespace ShopApp.UI.Views;

/// <summary>
/// Add / edit one party. Edits the Party it is given directly, so a rejected
/// save can reopen the dialog with everything the user typed still in place.
/// The caller passes a detached copy, which is what makes Cancel safe.
/// </summary>
public partial class PartyDialog : Window
{
    private readonly Party _party;

    public PartyDialog(Party party, IReadOnlyList<string> groups)
    {
        InitializeComponent();
        _party = party;
        DataContext = party;

        GroupBox.ItemsSource = groups;

        var isNew = party.Id == 0;
        Title = isNew ? "Add Party" : "Edit Party";
        HeaderText.Text = isNew ? "Add Party" : $"Edit {party.Name}";

        // The two radios are one boolean. Binding both to it would need an
        // inverting converter, so the second is set from the first instead.
        ToPayRadio.IsChecked = !party.OpeningIsReceivable;

        Loaded += (_, _) => NameBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _party.OpeningIsReceivable = ToPayRadio.IsChecked != true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
