using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ShopApp.Domain.Enums;
using ShopApp.Services;

namespace ShopApp.UI.Views;

/// <summary>
/// Records one payment. Shows what the chosen party currently owes and which
/// documents the money will settle, so he can see where it is going before he
/// commits it - the allocation itself is done by PaymentService, oldest first.
/// </summary>
public partial class PaymentDialog : Window
{
    private readonly IReadOnlyList<PartyRow> _parties;
    private readonly PaymentDirection _direction;
    private readonly PaymentService _payments;

    private decimal _outstanding;

    /// <summary>Filled in once the user confirms; null if they cancelled.</summary>
    public PaymentInput? Result { get; private set; }

    public PaymentDialog(IReadOnlyList<PartyRow> parties, PaymentDirection direction,
                         PaymentService payments)
    {
        InitializeComponent();
        _parties = parties;
        _direction = direction;
        _payments = payments;

        var incoming = direction == PaymentDirection.In;
        Title = incoming ? "Payment In" : "Payment Out";
        HeaderText.Text = incoming ? "Record money received" : "Record money paid";
        OutstandingCaption.Text = incoming ? "THIS PARTY OWES" : "OWED TO THIS PARTY";

        PartyBox.ItemsSource = parties;
        DateBox.SelectedDate = DateTime.Today;

        ModeBox.ItemsSource = Enum.GetValues<PaymentMode>().ToList();
        ModeBox.SelectedItem = PaymentMode.Cash;

        // Customers with something owing first - they are who a payment is
        // almost always from - but every party stays in the list.
        var owing = parties.FirstOrDefault(p => incoming ? p.Balance > 0 : p.Balance < 0);
        PartyBox.SelectedItem = owing ?? parties.FirstOrDefault();

        Loaded += (_, _) => AmountBox.Focus();
    }

    private void PartyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PartyBox.SelectedItem is not PartyRow party)
        {
            _outstanding = 0m;
            OutstandingValue.Text = "Rs 0";
            OpenDocsText.Text = "";
            return;
        }

        _outstanding = _payments.OutstandingFor(party.Id, _direction);
        OutstandingValue.Text = $"Rs {_outstanding:N2}";

        var open = _payments.OpenDocuments(party.Id, _direction);
        OpenDocsText.Text = open.Count == 0
            ? "Nothing outstanding. Anything received will sit on account until the next invoice."
            : $"Will settle oldest first: {string.Join(",   ", open.Take(4).Select(d => d.Display))}" +
              (open.Count > 4 ? $",   and {open.Count - 4} more." : ".");
    }

    private void ModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var isCheque = ModeBox.SelectedItem is PaymentMode.Cheque;
        ChequePanel.Visibility = isCheque ? Visibility.Visible : Visibility.Collapsed;

        ReferenceLabel.Text = ModeBox.SelectedItem switch
        {
            PaymentMode.Cheque => "Cheque number",
            PaymentMode.Bank => "Transfer reference",
            PaymentMode.Online => "Transaction id",
            _ => "Reference no"
        };
    }

    private void FullAmount_Click(object sender, RoutedEventArgs e) =>
        AmountBox.Text = _outstanding.ToString("0.00", CultureInfo.CurrentCulture);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (PartyBox.SelectedItem is not PartyRow party)
        {
            Warn("Choose the party this payment is from.");
            return;
        }

        if (!decimal.TryParse(AmountBox.Text, NumberStyles.Number,
                              CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        {
            Warn("Enter an amount greater than zero.");
            return;
        }

        if (ModeBox.SelectedItem is not PaymentMode mode) mode = PaymentMode.Cash;

        // Overpaying is allowed - a customer paying a round figure against a
        // running account is normal - but it should be a deliberate choice.
        if (amount > _outstanding && _outstanding >= 0)
        {
            var extra = amount - _outstanding;
            var confirm = MessageBox.Show(this,
                $"{party.Name} owes {_outstanding:N2}, and this is {amount:N2}.\n\n" +
                $"{extra:N2} will sit on account against future invoices. Carry on?",
                "More than is owed", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
        }

        Result = new PaymentInput(
            party.Id,
            _direction,
            DateBox.SelectedDate ?? DateTime.Today,
            amount,
            mode,
            ReferenceBox.Text,
            ModeBox.SelectedItem is PaymentMode.Cheque ? ChequeDueBox.SelectedDate : null,
            NotesBox.Text);

        DialogResult = true;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Check this first",
            MessageBoxButton.OK, MessageBoxImage.Warning);

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
