using System.Windows.Controls;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class PurchaseView : UserControl
{
    public PurchaseView(PurchaseViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) vm.LoadLookups();
        };
    }
}
