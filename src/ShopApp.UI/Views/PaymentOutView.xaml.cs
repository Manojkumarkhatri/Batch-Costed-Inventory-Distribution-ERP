using System.Windows;
using System.Windows.Controls;
using ShopApp.Domain.Enums;
using ShopApp.Services;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class PaymentOutView : UserControl
{
    private readonly PaymentService _payments;

    public PaymentOutView(PaymentOutViewModel vm, PaymentService payments)
    {
        InitializeComponent();
        DataContext = vm;
        _payments = payments;

        vm.ShowPaymentForm = ShowPaymentForm;

        vm.AskPrintOptions = hasDescription =>
        {
            var dialog = new PrintOptionsDialog("Print payment voucher", hasDescription)
            {
                Owner = Window.GetWindow(this)
            };
            return dialog.ShowDialog() == true ? dialog.WithDescription : null;
        };

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) vm.Load();
        };
    }

    /// <summary>
    /// The same dialog Payment-In uses. It takes a direction and relabels
    /// itself, so there is one place where a payment gets entered rather than
    /// two that can drift apart.
    /// </summary>
    private PaymentInput? ShowPaymentForm(IReadOnlyList<PartyRow> parties)
    {
        var dialog = new PaymentDialog(parties, PaymentDirection.Out, _payments)
        {
            Owner = Window.GetWindow(this)
        };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }
}
