using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShopApp.Domain.Entities;

/// <summary>
/// Minimal change notification, hand-rolled so the Domain project keeps its
/// zero external dependencies and stays testable without any UI packages.
///
/// Only fields that a live edit form needs to react to are written as full
/// properties. Everything else stays an auto-property.
/// </summary>
public abstract class ObservableEntity : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
