using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ClutterFlock.Models;

// Keep the collection identity (and WPF sorting) while publishing complete filter results.
public sealed class ResultCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IReadOnlyList<T> results)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in results) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        // WPF does not support range Add notifications. One Reset sorts the whole list once.
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
