using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Core.Forms;

namespace HavenOS.Forms;

/// <summary>Orders canonical option IDs, including partial rankings; labels are presentation only.</summary>
internal sealed class FormNativeRankingInput : StackPanel, IDisposable
{
    private readonly FormChoiceOption[] _options;
    private readonly IReadOnlyDictionary<Guid, string> _labels;
    private readonly List<Guid> _ranked;
    private readonly Action<JsonElement> _changed;
    private readonly ListBox _list = new();
    private readonly ComboBox _available = new();
    private readonly Button _add = new() { Content = "Add to ranking" };
    private readonly Button _remove = new() { Content = "Remove from ranking" };
    private readonly Button _up = new() { Content = "Move up" };
    private readonly Button _down = new() { Content = "Move down" };
    private readonly Button _confirm = new() { Content = "Use ranking" };
    private FormChoiceOption[] _unranked = [];
    private bool _refreshing, _disposed;

    public FormNativeRankingInput(FormField field, JsonElement? answer, Action<JsonElement> changed)
    {
        if (field.Kind != FormFieldKind.Ranking || field.Options is not { Count: > 0 })
            throw new ArgumentException("A canonical ranking option schema is required.", nameof(field));
        _options = field.Options.ToArray(); _labels = _options.ToDictionary(option => option.OptionID, option => option.Label); _changed = changed;
        _ranked = answer is { ValueKind: JsonValueKind.Array } value
            ? value.EnumerateArray().Select(item => item.GetGuid()).ToList() : [];
        Spacing = 6;
        AutomationProperties.SetName(_list, "Ranked options");
        AutomationProperties.SetName(_available, "Unranked option");
        Children.Add(_list); Children.Add(_available);
        var buttons = new WrapPanel();
        foreach (var button in new[] { _add, _remove, _up, _down, _confirm }) buttons.Children.Add(button);
        Children.Add(buttons);
        _list.SelectionChanged += SelectionChanged; _available.SelectionChanged += SelectionChanged;
        _add.Click += Add; _remove.Click += Remove; _up.Click += Up; _down.Click += Down; _confirm.Click += Confirm;
        Refresh(_ranked.Count > 0 ? 0 : -1);
    }
    private bool Editable => !_disposed && IsEffectivelyEnabled;
    private void SelectionChanged(object? sender, SelectionChangedEventArgs args) { if (!_refreshing) RefreshButtons(); }
    private void Refresh(int selected)
    {
        _refreshing = true;
        _list.ItemsSource = _ranked.Select(id => _labels[id]).ToArray();
        _list.SelectedIndex = selected;
        var rankedIDs = _ranked.ToHashSet();
        _unranked = _options.Where(option => !rankedIDs.Contains(option.OptionID)).ToArray();
        _available.ItemsSource = _unranked.Select(option => option.Label).ToArray();
        _available.SelectedIndex = _unranked.Length > 0 ? 0 : -1;
        _refreshing = false; RefreshButtons();
    }
    private void RefreshButtons()
    {
        var index = _list.SelectedIndex;
        _add.IsEnabled = _available.SelectedIndex >= 0 && _available.SelectedIndex < _unranked.Length;
        _remove.IsEnabled = index >= 0 && index < _ranked.Count;
        _up.IsEnabled = index > 0 && index < _ranked.Count;
        _down.IsEnabled = index >= 0 && index < _ranked.Count - 1;
    }
    private void Add(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        var index = _available.SelectedIndex;
        if (!Editable || index < 0 || index >= _unranked.Length) return;
        _ranked.Add(_unranked[index].OptionID); Refresh(_ranked.Count - 1); Publish();
    }
    private void Remove(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        var index = _list.SelectedIndex;
        if (!Editable || index < 0 || index >= _ranked.Count) return;
        _ranked.RemoveAt(index); Refresh(Math.Min(index, _ranked.Count - 1)); Publish();
    }
    private void Up(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => Move(-1);
    private void Down(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => Move(1);
    private void Move(int delta)
    {
        var index = _list.SelectedIndex; var target = index + delta;
        if (!Editable || index < 0 || index >= _ranked.Count || target < 0 || target >= _ranked.Count) return;
        (_ranked[index], _ranked[target]) = (_ranked[target], _ranked[index]);
        Refresh(target); Publish();
    }
    private void Confirm(object? sender, Avalonia.Interactivity.RoutedEventArgs args) { if (Editable) Publish(); }
    private void Publish() => _changed(JsonSerializer.SerializeToElement(_ranked.ToArray()));
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _list.SelectionChanged -= SelectionChanged; _available.SelectionChanged -= SelectionChanged;
        _add.Click -= Add; _remove.Click -= Remove; _up.Click -= Up; _down.Click -= Down; _confirm.Click -= Confirm;
        IsEnabled = false;
    }
}
