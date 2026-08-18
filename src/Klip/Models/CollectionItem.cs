using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Klip.Models;

public sealed class CollectionItem : INotifyPropertyChanged
{
    private string _name = "";
    private int _count;

    public event PropertyChangedEventHandler? PropertyChanged;

    public long Id { get; set; }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public DateTime CreatedAt { get; set; }

    public int Count
    {
        get => _count;
        set => Set(ref _count, value);
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
