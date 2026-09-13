using System.Windows;
using System.Windows.Controls;

namespace ShopApp.UI.Views;

/// <summary>
/// The placeholder that replaces an empty table. Constructed in XAML with no
/// dependencies, so it can sit in any screen without a DI registration.
/// </summary>
public partial class EmptyState : UserControl
{
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(EmptyState),
            new PropertyMetadata("No records found"));

    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(EmptyState),
            new PropertyMetadata(""));

    /// <summary>The headline. Keep it factual - this is not an error.</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Optional second line saying what would put rows here.</summary>
    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public EmptyState() => InitializeComponent();
}
