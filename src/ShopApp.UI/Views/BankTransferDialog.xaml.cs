using System.Globalization;
using System.Windows;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Services;

namespace ShopApp.UI.Views;

/// <summary>
/// One dialog for every kind of bank movement. The type decides the wording,
/// whether a second account is asked for, and which way the money goes - one
/// place to get the signs right rather than six.
/// </summary>
public partial class BankTransferDialog : Window
{
    private readonly BankTxnType _type;
    private readonly BankAccount _from;
    private readonly decimal _balance;

    /// <summary>Filled in once the user confirms; null if they cancelled.</summary>
    public BankTransferInput? Result { get; private set; }

    public BankTransferDialog(BankTxnType type, BankAccount from, decimal balance,
                              IReadOnlyList<BankAccountRow> accounts)
    {
        InitializeComponent();
        _type = type;
        _from = from;
        _balance = balance;

        (HeaderText.Text, HeaderHint.Text) = type switch
        {
            BankTxnType.Deposit => ("Deposit", "Cash paid into the account."),
            BankTxnType.Withdraw => ("Withdraw", "Cash taken out of the account."),
            BankTxnType.BankToCash => ("Bank to cash", "Moved from the account into the till."),
            BankTxnType.CashToBank => ("Cash to bank", "Moved from the till into the account."),
            BankTxnType.BankToBank => ("Bank to bank transfer", "Moved between two of his own accounts."),
            _ => ("Adjust balance", "Correct the book balance so it matches the bank statement.")
        };

        Title = HeaderText.Text;
        FromBox.Text = from.Name;
        DateBox.SelectedDate = DateTime.Today;

        // Only a transfer needs somewhere to go.
        if (type == BankTxnType.BankToBank)
        {
            var others = accounts.Where(a => a.Id != from.Id && a.IsActive).ToList();
            ToBox.ItemsSource = others;
            ToBox.SelectedItem = others.FirstOrDefault();
        }
        else
        {
            ToPanel.Visibility = Visibility.Collapsed;
            FromLabel.Text = "Account";
        }

        // Only an adjustment can go either way.
        if (type == BankTxnType.Adjustment)
            DirectionPanel.Visibility = Visibility.Visible;

        BalanceText.Text = $"Rs {balance:N2}";
        AmountBox.TextChanged += (_, _) => UpdateAfter();
        IncreaseRadio.Checked += (_, _) => UpdateAfter();
        DecreaseRadio.Checked += (_, _) => UpdateAfter();
        UpdateAfter();

        Loaded += (_, _) => AmountBox.Focus();
    }

    /// <summary>Shows where the balance lands, before he commits to it.</summary>
    private void UpdateAfter()
    {
        if (!decimal.TryParse(AmountBox.Text, NumberStyles.Number,
                              CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        {
            AfterText.Text = "";
            return;
        }

        var after = _balance + Signed(amount);
        AfterText.Text = after < 0
            ? $"Leaves {after:N2} - the account would be overdrawn."
            : $"Leaves {after:N2}.";
    }

    /// <summary>
    /// Positive puts money in, negative takes it out. Deposit and cash-to-bank
    /// are the only kinds that increase the balance; an adjustment follows the
    /// direction he picked.
    /// </summary>
    private decimal Signed(decimal amount) => _type switch
    {
        BankTxnType.Deposit or BankTxnType.CashToBank => amount,
        BankTxnType.Adjustment => IncreaseRadio.IsChecked == true ? amount : -amount,
        _ => -amount
    };

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(AmountBox.Text, NumberStyles.Number,
                              CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        {
            Warn("Enter an amount greater than zero.");
            return;
        }

        int? to = null;
        if (_type == BankTxnType.BankToBank)
        {
            if (ToBox.SelectedItem is not BankAccountRow target)
            {
                Warn("Choose the account the money is going to.");
                return;
            }
            to = target.Id;
        }

        // Overdrawing is allowed - a real account can go overdrawn - but it
        // should be a deliberate choice rather than a typing slip.
        var after = _balance + Signed(amount);
        if (after < 0)
        {
            var confirm = MessageBox.Show(this,
                $"{_from.Name} holds {_balance:N2}, and this would leave {after:N2}.\n\n" +
                "Carry on?",
                "That would overdraw the account", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
        }

        // The service expects a positive amount for everything except an
        // adjustment, which carries its own sign.
        var value = _type == BankTxnType.Adjustment ? Signed(amount) : amount;

        Result = new BankTransferInput(_from.Id, to, value,
            DateBox.SelectedDate ?? DateTime.Today,
            DescriptionBox.Text, _type);

        DialogResult = true;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Check this first",
            MessageBoxButton.OK, MessageBoxImage.Warning);

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
