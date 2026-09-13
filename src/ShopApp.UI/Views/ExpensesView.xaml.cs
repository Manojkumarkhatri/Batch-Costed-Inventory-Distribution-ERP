using System.Windows;
using System.Windows.Controls;
using ShopApp.Domain.Entities;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class ExpensesView : UserControl
{
    public ExpensesView(ExpensesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        vm.ShowEditor = ShowEditor;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) vm.Load();
        };
    }

    private bool ShowEditor(Expense expense, IReadOnlyList<string> categories)
    {
        var dialog = new ExpenseDialog(expense, categories) { Owner = Window.GetWindow(this) };
        return dialog.ShowDialog() == true;
    }
}
