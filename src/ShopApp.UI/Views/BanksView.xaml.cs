using System.Windows;
using System.Windows.Controls;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Services;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class BanksView : UserControl
{
    private readonly BankService _banks;

    public BanksView(BanksViewModel vm, BankService banks)
    {
        InitializeComponent();
        DataContext = vm;
        _banks = banks;

        vm.ShowEditor = ShowEditor;
        vm.ShowTransfer = ShowTransfer;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) vm.Load();
        };
    }

    private bool ShowEditor(BankAccount account)
    {
        var dialog = new BankDialog(account) { Owner = Window.GetWindow(this) };
        return dialog.ShowDialog() == true;
    }

    private BankTransferInput? ShowTransfer(BankTxnType type, BankAccount from,
                                            IReadOnlyList<BankAccountRow> accounts)
    {
        var dialog = new BankTransferDialog(type, from, _banks.BalanceOf(from.Id), accounts)
        {
            Owner = Window.GetWindow(this)
        };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }
}
