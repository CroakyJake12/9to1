using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

internal sealed class NativeHomeAccentEditor : ICuiActionDispatcher, ICuiActionAvailability, IDisposable
{
    private readonly NativeHomeThemePreferences _source;
    private readonly CuiViewModel _host;
    private readonly Func<object, CancellationToken, Task> _apply;
    private readonly CuiViewModel _draft = new();
    private readonly CuiControlLoader _loader = new();
    private readonly Flyout _flyout = new();
    private Button? _trigger;
    private TextBox? _hexInput;
    private object? _target;
    private Task? _retirement;
    private bool _settingColour, _saving, _disposed;

    internal NativeHomeAccentEditor(NativeHomeThemePreferences source, CuiViewModel host,
        Func<object, CancellationToken, Task> apply)
    {
        _source = source;
        _host = host;
        _apply = apply;
        host.Set("HomeAccentAppIndex", 0);
        host.PropertyChanged += OnHostPropertyChanged;
        _draft.PropertyChanged += OnDraftPropertyChanged;
        CuiPopupPresentation.RequestTransparency(_flyout.Popup);
        _flyout.Opening += (_, _) => Open();
        _flyout.Closed += (_, _) => _target = null;
        _loader.SetThemePreferenceContext(source.Context);
        _loader.SetBindingContext(_draft);
        _loader.SetActionDispatcher(this);
    }

    internal void Register(CuiControlLoader loader) =>
        loader.BindAuthoredControl("home-edit-accent", _ =>
        {
            return _trigger = new Button { Flyout = _flyout };
        });

    public bool? IsActionAvailable(string command) => !_disposed && !_saving && _target is not null &&
        (command == "CancelAccentColour" || command == "SaveAccentColour" && _draft.Get("AccentColourValid") is true);

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (IsActionAvailable(command) != true) return;
        if (command == "CancelAccentColour")
        {
            Close();
            return;
        }

        _saving = true;
        SetSaveState();
        try
        {
            var result = await _source.SaveAccentDraftAsync(_target, (string)_draft.Get("AccentColourHex")!, _apply, cancellationToken);
            if (result.Succeeded) Close();
            else _draft.Set("AccentColourStatus", result.Diagnostic ?? "The colour could not be saved.");
        }
        finally
        {
            _saving = false;
            if (!_disposed) SetSaveState();
        }
    }

    private void Open()
    {
        if (_disposed || _saving || _host.Get("HomeAppearanceAvailable") is not true) return;
        _target = _host.Get("HomeAccentEditorTarget");
        _draft.Set("AccentColourTitle", _host.Get("HomeAccentScopeName") is string app ? app + " accent colour" : "Global accent colour");
        _draft.Set("AccentColourHex", _host.Get("HomeAccentSampleColour"));
        UpdateColourFromText();
        if (_flyout.Content is null)
        {
            _loader.BindAuthoredControl("exact-accent-colour", _ => _hexInput = new TextBox());
            const string markup = """
                <Cui><Theme Name="user_preference"><Page AccessibleName="Accent colour picker">
                  <Container Type="Vertical Stack" Width="266" CellGap="8" Padding="12" Background="{Resource CuiSurfaceMaterial}" CornerRadius="14">
                    <Text Text="{Binding AccentColourTitle}" FontSize="17" FontWeight="700" />
                    <Container Type="Grid" ColumnSizes="Auto 1fr" CellGap="8">
                      <Container Type="Overlay" Width="30" Height="32" CornerRadius="6" Background="{Binding AccentColourSwatch}" />
                      <Input id="exact-accent-colour" Column="1" Type="Text" AccessibleName="Hexadecimal accent colour" Value="{Binding AccentColourHex, mode=TwoWay, type=string}" />
                    </Container>
                    <Container Type="Grid" ColumnSizes="Auto 1fr Auto" RowSizes="Auto Auto Auto" CellGap="6">
                      <Text Text="Red" /><Input Column="1" Type="Slider" Minimum="0" Maximum="255" AccessibleName="Red" Value="{Binding AccentColourRed, mode=TwoWay, type=double}" /><Text Column="2" Text="{Binding AccentColourRed}" />
                      <Text Row="1" Text="Green" /><Input Row="1" Column="1" Type="Slider" Minimum="0" Maximum="255" AccessibleName="Green" Value="{Binding AccentColourGreen, mode=TwoWay, type=double}" /><Text Row="1" Column="2" Text="{Binding AccentColourGreen}" />
                      <Text Row="2" Text="Blue" /><Input Row="2" Column="1" Type="Slider" Minimum="0" Maximum="255" AccessibleName="Blue" Value="{Binding AccentColourBlue, mode=TwoWay, type=double}" /><Text Row="2" Column="2" Text="{Binding AccentColourBlue}" />
                    </Container>
                    <Text Text="{Binding AccentColourStatus}" TextWrapping="Wrap" Foreground="{Resource CuiTextSoftBrush}" />
                    <Container Type="Horizontal Stack" CellGap="8" HorizontalAlignment="Right">
                      <Button Action="CancelAccentColour" Content="Cancel" />
                      <Button Action="SaveAccentColour" Content="Apply colour" IsEnabled="{Binding AccentColourCanSave, fallback=false}" />
                    </Container>
                  </Container>
                </Page></Theme></Cui>
                """;
            var (root, diagnostics) = _loader.LoadMarkup(markup, "NativeAccentEditor.cui");
            if (root is null || diagnostics.Any(item => item.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("The accent colour picker could not render.");
            root.AddHandler(InputElement.KeyDownEvent, OnPickerKey, RoutingStrategies.Tunnel);
            _loader.WireBindings(root);
            _flyout.Content = root;
        }
        _loader.RefreshBindings();
        Dispatcher.UIThread.Post(() => { if (_target is not null && _hexInput?.Focus() == true) _hexInput.SelectAll(); },
            DispatcherPriority.Loaded);
    }

    private void OnPickerKey(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || _saving) return;
        args.Handled = true;
        Close();
    }

    private void Close()
    {
        if (!_flyout.IsOpen) return;
        _flyout.Hide();
        Dispatcher.UIThread.Post(() => { if (!_disposed && _trigger?.IsEffectivelyVisible == true) _trigger.Focus(); },
            DispatcherPriority.Loaded);
    }

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == "HomeAccentAppIndex" && _host.Get("HomeAccentAppIndex") is int index && index >= 0)
            _source.ApplyEditorBindings(_host);
    }

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_settingColour) return;
        if (args.PropertyName == "AccentColourHex") UpdateColourFromText();
        if (args.PropertyName is "AccentColourRed" or "AccentColourGreen" or "AccentColourBlue")
        {
            int ReadChannel(string name) => (int)Math.Round((double?)_draft.Get("AccentColour" + name) ?? 0);
            _draft.Set("AccentColourHex", $"#{ReadChannel("Red"):X2}{ReadChannel("Green"):X2}{ReadChannel("Blue"):X2}");
        }
    }

    private void UpdateColourFromText()
    {
        var raw = _draft.Get("AccentColourHex") as string;
        _settingColour = true;
        try
        {
            string? canonical = null;
            try { canonical = HomeThemeAccentPreference.CanonicalColour(raw?.Trim() ?? ""); }
            catch (ArgumentException) { }
            _draft.Set("AccentColourValid", canonical is not null);
            _draft.Set("AccentColourStatus", canonical is null ? "Enter a colour in #RRGGBB format." : "");
            if (canonical is { } hex)
            {
                var colour = Color.Parse(hex);
                _draft.Set("AccentColourRed", (double)colour.R);
                _draft.Set("AccentColourGreen", (double)colour.G);
                _draft.Set("AccentColourBlue", (double)colour.B);
                _draft.Set("AccentColourSwatch", new SolidColorBrush(colour));
            }
        }
        finally { _settingColour = false; }
        SetSaveState();
    }

    private void SetSaveState() =>
        _draft.Set("AccentColourCanSave", IsActionAvailable("SaveAccentColour") == true);

    internal Task WhenIdleAsync() => _loader.WhenActionsIdleAsync();

    internal Task CloseAndDrainAsync() => _retirement ??= RetireAsync();

    private async Task RetireAsync()
    {
        try { await WhenIdleAsync(); }
        finally { Dispose(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.PropertyChanged -= OnHostPropertyChanged;
        _draft.PropertyChanged -= OnDraftPropertyChanged;
        _flyout.Hide();
        _loader.Dispose();
    }
}
