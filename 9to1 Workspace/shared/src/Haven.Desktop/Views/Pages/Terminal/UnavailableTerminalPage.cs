using Avalonia.Controls;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Views.Pages.Terminal;

/// <summary>Retains an unavailable restored tab's canonical snapshot without starting another backend.</summary>
internal sealed class UnavailableTerminalPage(string savedStateJson) : UserControl, IDisposable
{
    private readonly CuiSceneHost _scene = new();
    public string SavedStateJson { get; } = savedStateJson;
    public async Task InitializeAsync(string message, CancellationToken token)
    {
        var bindings = new CuiViewModel();
        var scene = new CuiNativeScene("9to1.Terminal", "Terminal", "Terminal", new CuiRichParser().Parse("<Cui><StackPanel /></Cui>"),
            bindings, bindings, new Unavailable(message));
        await _scene.ShowAsync(scene, token);
        Content = _scene;
    }
    public void Dispose() { Content = null; _scene.Dispose(); }
    private sealed class Unavailable(string message) : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable,
                "TerminalUnavailable", message));
        }
    }
}
