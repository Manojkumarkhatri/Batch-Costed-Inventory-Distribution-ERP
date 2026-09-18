using System.Windows;
using System.Windows.Controls;
using ShopApp.Services;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class PurchaseListView : UserControl
{
    private readonly PurchaseView _form;
    private readonly PurchaseViewModel _formVm;
    private readonly PurchaseService _purchases;

    public PurchaseListView(PurchaseListViewModel vm, PurchaseView form,
                            PurchaseService purchases)
    {
        InitializeComponent();
        DataContext = vm;

        _form = form;
        _formVm = (PurchaseViewModel)form.DataContext;
        _purchases = purchases;

        vm.ShowPurchaseForm = ShowPurchaseForm;
        vm.ShowPurchaseFormForEdit = ShowPurchaseFormForEdit;

        vm.AskPrintOptions = hasDescription =>
        {
            var dialog = new PrintOptionsDialog("Print purchase bill", hasDescription)
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
    /// The entry form in a window. Same control as before, so nothing about how
    /// a purchase is recorded has changed - only where it appears. One instance
    /// is reused because the form already resets itself after every save.
    /// </summary>
    /// <summary>Same window, loaded with an existing bill.</summary>
    private void ShowPurchaseFormForEdit(int purchaseId)
    {
        var bill = _purchases.GetById(purchaseId);
        if (bill is null) return;

        _formVm.LoadForEdit(bill);
        ShowForm("Edit Purchase Bill");
    }

    private void ShowPurchaseForm()
    {
        _formVm.LoadLookups();
        ShowForm("Add Purchase");
    }

    /// <summary>
    /// The entry form in a window. One instance is reused for both adding and
    /// editing, because the form resets itself after every save and there is
    /// no reason to keep two.
    /// </summary>
    private void ShowForm(string title)
    {
        var window = new Window
        {
            Title = title,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 1120,
            Height = 720,
            ShowInTaskbar = false,
            Background = System.Windows.Media.Brushes.White
        };

        if (_form.Parent is Window previous) previous.Content = null;
        window.Content = _form;

        _formVm.OnSaved = window.Close;
        window.ShowDialog();

        _formVm.OnSaved = null;
        window.Content = null;
    }
}
