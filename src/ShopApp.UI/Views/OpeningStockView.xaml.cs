using System.Windows.Controls;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class OpeningStockView : UserControl
{
    public OpeningStockView(OpeningStockViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) vm.Load();
        };
    }
}
