using System.Windows;
using System.Windows.Controls;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class SaleListView : UserControl
{
    private readonly SaleView _form;
    private readonly SaleViewModel _formVm;

    public SaleListView(SaleListViewModel vm, SaleView form)
    {
        InitializeComponent();
        DataContext = vm;

        _form = form;
        _formVm = (SaleViewModel)form.DataContext;

        vm.ShowSaleForm = ShowSaleForm;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) vm.Load();
        };
    }

    /// <summary>
    /// The invoice form in a window. It is the same control the app has always
    /// used, so nothing about how a sale is entered has changed - only where it
    /// appears. One instance is reused because SaveInternal already resets the
    /// form to a fresh invoice after every save.
    /// </summary>
    private void ShowSaleForm()
    {
        var window = new Window
        {
            Title = "Add Sale",
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 1120,
            Height = 720,
            ShowInTaskbar = false,
            Background = System.Windows.Media.Brushes.White
        };

        // Detach the form from any previous host before reparenting it.
        if (_form.Parent is Window previous) previous.Content = null;
        window.Content = _form;

        _formVm.LoadLookups();
        _formVm.OnSaved = window.Close;

        window.ShowDialog();

        _formVm.OnSaved = null;
        window.Content = null;
    }
}
