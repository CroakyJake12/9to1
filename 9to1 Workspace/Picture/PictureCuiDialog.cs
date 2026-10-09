using Avalonia.Controls;
using CakeOS.Cui.Runtime;

namespace HavenOS.Images;

/// <summary>Platform windowing for an authored CUI dialogue, with the same supplied Home readiness.</summary>
internal sealed class PictureCuiDialog : IDisposable
{
    private readonly Window _window;
    private readonly CuiSceneHost _scene = new();
    private readonly CuiNativeScene _definition;
    private bool _retiring;

    internal PictureCuiDialog(string title, string document, ICuiSceneReadiness readiness, CuiViewModel model)
    {
        _window = new Window { Title = title, Width = 440, Height = 360, MinWidth = 320, MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = _scene };
        foreach (var result in new[] { "save", "discard", "cancel" })
        {
            var captured = result;
            model.On("picture.confirm." + result, _ => _window.Close(captured));
        }
        model.On("picture.metadata.close", _ => _window.Close("close"));
        _definition = new CuiNativeScene("picture", title, "Imagine", PictureCuiDocuments.Load(document),
            model, model, readiness) { IsPublicationCurrent = () => !_retiring };
    }

    internal async Task<string?> OpenAsync(Window owner)
    {
        string? result = null;
        Exception? originalFailure = null;
        try
        {
            var availability = await _scene.ShowAsync(_definition);
            if (availability.State != CuiSceneAvailabilityState.Ready)
                throw new UnauthorizedAccessException(availability.Message);
            result = await _window.ShowDialog<string?>(owner);
        }
        catch (Exception error) { originalFailure = error; }
        _retiring = true;
        try
        {
            // The dialogue action returns from Close before the original
            // accepted action pipeline is joined by this external owner.
            await _scene.CloseOriginalAsync();
        }
        catch (Exception closeFailure)
        {
            if (originalFailure is not null)
                throw new AggregateException("Picture dialogue retained both its source and close failures.", originalFailure, closeFailure);
            throw;
        }
        if (originalFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(originalFailure).Throw();
        return result;
    }

    public void Dispose() => _retiring = true;
}
