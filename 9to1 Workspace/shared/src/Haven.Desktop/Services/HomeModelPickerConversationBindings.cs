using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Home;

namespace Haven.Desktop.Services;

/// <summary>Native input adapter for the Home-owned projection. Only its presentation search
/// is writable; model routes and participant changes still use the existing typed dispatcher.</summary>
internal sealed class HomeModelPickerConversationBindings(HomeModelPickerCuiSurface surface, Func<bool> isCurrent)
    : ICuiWritableBindingContext, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => surface.PropertyChanged += value;
        remove => surface.PropertyChanged -= value;
    }

    public bool TryGetValue(string path, out object? value) => surface.TryGetValue(path, out value);

    public bool TrySetValue(string path, object? value) => path == "CompactSpecialistQuery" && isCurrent() &&
        surface.SetSpecialistQuery(value?.ToString());

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        // Model/route/reasoning rows already implement the Core binding contract.
        // Keep that exact row projection, including repeat identities and typed arguments.
        if (item is ICuiBindingContext bindings) return bindings.TryGetValue(path, out value);
        value = null;
        return false;
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;
}
