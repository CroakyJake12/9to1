using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.Controls.Templates;
using Haven.Application;

namespace HavenOS.Forms;

/// <summary>Optional native editor for an actual owning lookup session. Choices and returned
/// references are answer data. Missing/denied reads do not become empty selectable results.</summary>
internal sealed class FormNativeReferenceLookupInput : StackPanel, IDisposable
{
    private readonly Func<Func<bool>, CancellationToken, Task<IFormDataReferenceLookupSession?>> _open;
    private IFormDataReferenceLookupSession? _session;
    private readonly Action<JsonElement> _changed;
    private readonly TextBox _query = new() { MaxLength = 1024 };
    private readonly Button _search = new() { Content = "Find records" };
    private readonly ComboBox _choices = new();
    private readonly TextBlock _status = new() { Text = "Find an authorized record." };
    private readonly CancellationTokenSource _lifetime = new();
    private long _generation;
    private object _selectionInvocation = new();
    private bool _disposed;
    private bool _refreshing;
    public FormNativeReferenceLookupInput(
        Func<Func<bool>, CancellationToken, Task<IFormDataReferenceLookupSession?>> open, Action<JsonElement> changed, JsonElement retainedAnswer)
    {
        _open = open; _changed = changed; Spacing = 6;
        if (retainedAnswer.ValueKind == JsonValueKind.Object
            && retainedAnswer.TryGetProperty("tableID", out var table) && table.ValueKind == JsonValueKind.String && table.TryGetGuid(out var tableId) && tableId != Guid.Empty
            && retainedAnswer.TryGetProperty("recordID", out var record) && record.ValueKind == JsonValueKind.String && record.TryGetGuid(out var recordId) && recordId != Guid.Empty)
            _status.Text = "Saved reference retained. Find an authorized record to replace it.";
        _choices.ItemTemplate = new FuncDataTemplate<FormDataReferenceChoice>((choice, _) => new TextBlock { Text = choice?.Label });
        AutomationProperties.SetName(_query, "Reference record search");
        AutomationProperties.SetName(_choices, "Reference record choices");
        AutomationProperties.SetName(_status, "Reference lookup status");
        Children.Add(_query); Children.Add(_search); Children.Add(_choices); Children.Add(_status);
        _search.Click += Search; _choices.SelectionChanged += Select;
        _query.PropertyChanged += QueryChanged;
    }
    private void QueryChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
    {
        if (_disposed || args.Property != TextBox.TextProperty) return;
        Interlocked.Increment(ref _generation); _selectionInvocation = new(); ClearChoices(); _status.Text = "Find an authorized record.";
    }
    private void ClearChoices()
    { _refreshing = true; _choices.SelectedItem = null; _choices.ItemsSource = null; _refreshing = false; }
    private async void Search(object? sender, RoutedEventArgs args)
    {
        if (_disposed || !IsEffectivelyEnabled) return;
        _selectionInvocation = new();
        var generation = Interlocked.Increment(ref _generation); var query = _query.Text ?? "";
        ClearChoices(); _status.Text = "Reading authorized records…";
        try
        {
            _session?.Dispose(); _session = null;
            var actual = await _open(() => !Volatile.Read(ref _disposed) && generation == Interlocked.Read(ref _generation), _lifetime.Token);
            if (_disposed || generation != _generation || !IsEffectivelyEnabled) { actual?.Dispose(); return; }
            if (actual is null) { _status.Text = "Original record lookup unavailable."; return; }
            _session = actual;
            var read = await actual.ReadChoicesAsync(query, 0, 200, _lifetime.Token);
            if (_disposed || generation != _generation || !IsEffectivelyEnabled) return;
            if (!read.Success || read.Choices is null) { _status.Text = "Configured records are unavailable. Reopen this response or try again."; return; }
            _refreshing = true; _choices.ItemsSource = read.Choices; _refreshing = false;
            _status.Text = read.Choices.Count == 0 ? "No matching records." : read.HasMore
                ? "First 200 matching records. Refine the search to find another record." : "Choose an authorized record.";
        }
        catch (OperationCanceledException)
        { if (!_disposed && generation == _generation) _status.Text = "Records unavailable. Retry the original lookup."; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { if (!_disposed && generation == _generation) _status.Text = "Records unavailable. Retry the original lookup."; }
    }
    private async void Select(object? sender, SelectionChangedEventArgs args)
    {
        if (_disposed || _refreshing) return;
        var selectionInvocation = new object(); _selectionInvocation = selectionInvocation;
        if (!IsEffectivelyEnabled || _choices.SelectedItem is not FormDataReferenceChoice choice) return;
        var generation = _generation;
        var actual = _session;
        if (actual is null) return;
        try
        {
            var reference = await actual.SelectAsync(choice, _lifetime.Token);
            if (_disposed || generation != _generation || !IsEffectivelyEnabled || !ReferenceEquals(_selectionInvocation, selectionInvocation) || !ReferenceEquals(_choices.SelectedItem, choice)) return;
            if (reference is null) { ClearChoices(); _status.Text = "Original record unavailable. Find records again."; return; }
            _changed(reference.Value);
            if (!_disposed && generation == _generation && ReferenceEquals(_selectionInvocation, selectionInvocation)) _status.Text = "Selected: " + choice.Label;
        }
        catch (OperationCanceledException)
        { if (!_disposed && generation == _generation && ReferenceEquals(_selectionInvocation, selectionInvocation)) _status.Text = "Original record unavailable. Find records again."; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { if (!_disposed && generation == _generation && ReferenceEquals(_selectionInvocation, selectionInvocation)) _status.Text = "Original record unavailable. Find records again."; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _selectionInvocation = new(); Interlocked.Increment(ref _generation); _lifetime.Cancel();
        _search.Click -= Search; _choices.SelectionChanged -= Select; _query.PropertyChanged -= QueryChanged;
        ClearChoices(); _session?.Dispose();
    }
}
