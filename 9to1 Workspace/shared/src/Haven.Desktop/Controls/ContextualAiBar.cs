using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Haven.Desktop.HavenUI.Components;
using NineToOne.Cui.AI;

namespace Haven.Desktop.Controls;

/// <summary>Shared desktop renderer for Home's semantic AI compose state. No artifact content is owned here.</summary>
public sealed class ContextualAiBar : UserControl, IDisposable
{
    private readonly FloatingAiBarState _state;
    private readonly IInvocationCatalogue? _catalogue;
    private readonly HavenTextInput _prompt = new() { PlaceholderText = "Ask Dulche · @ to invoke", MinWidth = 120 };
    private readonly HavenComboBox _access = new() { ItemsSource = new[] { "Read-only", "Write mode" }, SelectedIndex = 0, MinWidth = 125 };
    private readonly HavenButton _send = new() { Content = "Send" };
    private readonly HavenButton _stop = new() { Content = "Stop", IsVisible = false };
    private readonly HavenButton _model = new() { Content = "Model" };
    private readonly TextBlock _context = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _response = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _invocations = new() { Spacing = 4 };
    private readonly WrapPanel _chips = new();
    private CancellationTokenSource? _searchCancellation;
    private bool _refreshing;
    private bool _disposed;

    public ContextualAiBar(FloatingAiBarState state, IInvocationCatalogue? catalogue = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _catalogue = catalogue;
        AutomationProperties.SetName(this, "Dulche contextual AI bar");
        AutomationProperties.SetName(_access, "AI access mode");
        AutomationProperties.SetName(_prompt, "Request with canonical app and file invocations");
        var commands = new WrapPanel();
        commands.Children.Add(_access);
        commands.Children.Add(_model);
        commands.Children.Add(_send);
        commands.Children.Add(_stop);
        Content = new StackPanel { Spacing = 6, Children = { _context, _chips, _prompt, _invocations, commands, _response } };
        _prompt.TextChanged += OnPromptChanged;
        _access.SelectionChanged += (_, _) => { if (!_refreshing) _state.SetAccessMode(_access.SelectedIndex == 1 ? AppAiAccessMode.Write : AppAiAccessMode.ReadOnly); };
        _send.Click += async (_, _) => await _state.SubmitAsync();
        _stop.Click += (_, _) => _state.Cancel();
        _model.Click += async (_, _) => await _state.SelectNextModelAsync();
        _state.Changed += OnStateChanged;
        DetachedFromVisualTree += (_, _) => Dispose();
        _state.Expand();
        Refresh();
    }

    private async void OnPromptChanged(object? sender, TextChangedEventArgs args)
    {
        if (_refreshing || _disposed) return;
        _state.Prompt = _prompt.Text ?? string.Empty;
        if (_catalogue is null) return;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = _searchCancellation = new();
        try { await _state.SearchInvocationsAsync(_catalogue, _prompt.CaretIndex, cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception) { _context.Text = "Invocations unavailable. Existing context remains permission-limited."; }
    }

    private void OnStateChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        if (_disposed) return;
        _refreshing = true;
        try
        {
            if (_prompt.Text != _state.Prompt) _prompt.Text = _state.Prompt;
            _access.SelectedIndex = _state.IsWriteMode ? 1 : 0;
            _context.Text = string.Join(" · ", new[] { _state.ContextLabel, _state.AccessModeLabel, _state.RequestStateLabel }.Where(value => !string.IsNullOrWhiteSpace(value)));
            _response.Text = _state.Error ?? _state.Response;
            _send.IsEnabled = _state.Mode != FloatingAiBarMode.Streaming;
            _stop.IsVisible = _state.Mode == FloatingAiBarMode.Streaming;
            _model.Content = _state.ModelPickerLabel;
            _model.IsEnabled = _state.ModelPickerAvailable;
            _chips.Children.Clear();
            foreach (var token in _state.Compose.Tokens)
            {
                var chip = new HavenButton { Content = token.Resource.Label + " ×" };
                AutomationProperties.SetName(chip, token.AccessibilityLabel + "; remove");
                chip.Click += (_, _) => _state.RemoveInvocation(token.TokenId);
                _chips.Children.Add(chip);
            }
            _invocations.Children.Clear();
            if (!_state.Compose.IsMenuOpen) return;
            foreach (var section in _state.InvocationSections)
            {
                var heading = new HavenButton { Content = section.Kind + (section.Collapsed ? " ▸" : " ▾") };
                heading.Click += (_, _) => { _state.Compose.SetCollapsed(section.Kind, !section.Collapsed); _ = SearchAgainAsync(); };
                _invocations.Children.Add(heading);
                if (section.Collapsed) continue;
                foreach (var resource in section.Resources)
                {
                    var option = new HavenButton { Content = resource.Label };
                    AutomationProperties.SetName(option, "Invoke " + resource.Kind + ": " + resource.Label);
                    option.Click += (_, _) => { _state.InsertInvocation(resource, _prompt.CaretIndex); _prompt.CaretIndex = _state.Compose.Tokens.Last().Start + _state.Compose.Tokens.Last().Length; _prompt.Focus(); };
                    _invocations.Children.Add(option);
                }
            }
        }
        finally { _refreshing = false; }
    }

    private async Task SearchAgainAsync()
    {
        if (_catalogue is not null) await _state.SearchInvocationsAsync(_catalogue, _prompt.CaretIndex);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _state.Changed -= OnStateChanged;
        _prompt.TextChanged -= OnPromptChanged;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _state.Cancel();
    }
}
