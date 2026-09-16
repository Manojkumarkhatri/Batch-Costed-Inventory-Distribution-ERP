using System.Windows;
using System.Windows.Controls;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class PurchaseListView : UserControl
{
    private readonly PurchaseView _form;
    private readonly PurchaseViewModel _formVm;

    public PurchaseListView(PurchaseListViewModel vm, PurchaseView form)
    {
        InitializeComponent();
        DataContext = vm;

        _form = form;
        _formVm = (PurchaseViewModel)form.DataContext;

        vm.ShowPurchaseForm = ShowPurchaseForm;

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
    private void ShowPurchaseForm()
    {
        var window = new Window
        {
            Title = "Add Purchase",
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 1120,
            Height = 720,
            ShowInTaskbar = false,
            Background = System.Windows.Media.Brushes.White
        };

        if (_form.Parent is Window previous) previous.Content = null;
        window.Content = _form;

        _formVm.LoadLookups();
        _formVm.OnSaved = window.Close;

        window.ShowDialog();

        _formVm.OnSaved = null;
        window.Content = null;
    }
}
