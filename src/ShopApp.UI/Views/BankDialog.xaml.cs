using System.Globalization;
using System.Windows;
using ShopApp.Domain.Entities;

namespace ShopApp.UI.Views;

/// <summary>
/// Add or edit a bank account. Edits the object it is given, so a rejected
/// save reopens with the typing intact; the caller passes a detached copy.
/// </summary>
public partial class BankDialog : Window
{
    private readonly BankAccount _account;

    public BankDialog(BankAccount account)
    {
        InitializeComponent();
        _account = account;

        var isNew = account.Id == 0;
        Title = isNew ? "Add Bank Account" : "Edit Bank Account";
        HeaderText.Text = Title;

        NameBox.Text = account.Name;
        if (account.OpeningBalance != 0)
            OpeningBox.Text = account.OpeningBalance.ToString("0.##", CultureInfo.CurrentCulture);
        OpeningDateBox.SelectedDate = account.OpeningBalanceDate == default
            ? DateTime.Today : account.OpeningBalanceDate;

        BankNameBox.Text = account.BankName ?? "";
        AccountNumberBox.Text = account.AccountNumber ?? "";
        BranchBox.Text = account.BranchOrIban ?? "";
        HolderBox.Text = account.AccountHolder ?? "";

        PrintBox.IsChecked = account.PrintOnInvoice;
        ActiveBox.IsChecked = isNew || account.IsActive;

        Loaded += (_, _) => NameBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            Warn("Give the account a name you will recognise in a list.");
            return;
        }

        var opening = 0m;
        if (!string.IsNullOrWhiteSpace(OpeningBox.Text) &&
            !decimal.TryParse(OpeningBox.Text, NumberStyles.Number,
                              CultureInfo.CurrentCulture, out opening))
        {
            Warn("The opening balance is not a number. Leave it blank for zero.");
            return;
        }

        _account.Name = NameBox.Text.Trim();
        _account.OpeningBalance = opening;
        _account.OpeningBalanceDate = OpeningDateBox.SelectedDate ?? DateTime.Today;
        _account.BankName = BankNameBox.Text;
        _account.AccountNumber = AccountNumberBox.Text;
        _account.BranchOrIban = BranchBox.Text;
        _account.AccountHolder = HolderBox.Text;
        _account.PrintOnInvoice = PrintBox.IsChecked == true;
        _account.IsActive = ActiveBox.IsChecked == true;

        DialogResult = true;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Check this first",
            MessageBoxButton.OK, MessageBoxImage.Warning);

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
