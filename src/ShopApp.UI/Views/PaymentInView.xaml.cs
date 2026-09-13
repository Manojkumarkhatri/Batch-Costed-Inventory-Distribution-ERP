using System.Windows;
using System.Windows.Controls;
using ShopApp.Domain.Enums;
using ShopApp.Services;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class PaymentInView : UserControl
{
    private readonly PaymentService _payments;

    public PaymentInView(PaymentInViewModel vm, PaymentService payments)
    {
        InitializeComponent();
        DataContext = vm;
        _payments = payments;

        vm.ShowPaymentForm = ShowPaymentForm;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) vm.Load();
        };
    }

    private PaymentInput? ShowPaymentForm(IReadOnlyList<PartyRow> parties)
    {
        var dialog = new PaymentDialog(parties, PaymentDirection.In, _payments)
        {
            Owner = Window.GetWindow(this)
        };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }
}
