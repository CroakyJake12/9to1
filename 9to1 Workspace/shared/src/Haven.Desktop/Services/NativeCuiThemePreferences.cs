using System.Security.Cryptography;
using System.Text;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Desktop.HavenUI.Tokens;

namespace Haven.Desktop.Services;

/// <summary>Read-only CUI observation of the existing native preference owner.
/// This is explicitly local profile presentation, not an authenticated account
/// preference or a new persistence/synchronisation authority.</summary>
internal sealed class NativeCuiThemePreferences : ICuiThemePreferenceSource, IDisposable
{
    private readonly UserPreferencesService _owner;
    private readonly string _scope;
    private long _revision;
    private bool _disposed;

    public NativeCuiThemePreferences(UserPreferencesService owner, IAppPaths paths)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ArgumentNullException.ThrowIfNull(paths);
        _scope = "native-local-profile:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(paths.DataDirectory))));
        Current = Observe();
        _owner.AppearanceChanged += OnChanged;
    }

    public CuiThemePreferenceSnapshot Current { get; private set; }
    public event EventHandler? Changed;

    private CuiThemePreferenceSnapshot Observe()
    {
        var appearance = Enum.Parse<CuiAppearance>(_owner.Appearance.ToString());
        var accent = CuiAccentPreference.Default;
        if (_owner.OverrideAccentColour && AccentColourCatalog.Parse(_owner.AccentColourSelection) is { } selection)
        {
            var actual = AccentColourCatalog.Resolve(selection, _owner.Appearance).Primary;
            accent = new(CuiAccentOverrideMode.AllApps, actual.Length == 9 ? "#" + actual[3..] : actual);
        }
        return new(_owner.Theme.ToString(), _revision, _scope, false,
            new() { InterfaceFontFamilyOverride = _owner.FontPreference }, appearance, accent);
    }

    private void OnChanged(object? sender, EventArgs args)
    {
        if (_disposed) return;
        var next = Observe();
        if (next == Current) return;
        _revision = checked(_revision + 1);
        Current = next with { Revision = _revision };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.AppearanceChanged -= OnChanged;
        Changed = null;
    }
}
