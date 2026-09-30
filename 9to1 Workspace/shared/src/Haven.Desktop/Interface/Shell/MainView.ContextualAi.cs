using Avalonia;
using Avalonia.Layout;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Write;
using Haven.Desktop.Views.Pages.Present;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Cui.AI;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private ContextualAiBar? _contextualAiBar;

    private void RefreshContextualAiBar(object? page)
    {
        if (_contextualAiBar is not null)
        {
            NativeOverlayLayer.Children.Remove(_contextualAiBar);
            _contextualAiBar.Dispose();
            _contextualAiBar = null;
        }
        var factory = App.Services?.GetService<IAppAiCoordinatorFactory>();
        if (factory is null) return;
        ArtifactAiContext? context = page switch
        {
            WritePage write => new("write", () => (write.Document?.Id.ToString("N"), write.Document)),
            PresentPage present => new("present", () => (present.Document?.Id.ToString("N"), present.Document)),
            _ => null
        };
        if (context is null) return;
        _contextualAiBar = new ContextualAiBar(factory.Create(context, context), App.Services?.GetService<IInvocationCatalogue>())
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(16),
            MaxHeight = 280
        };
        NativeOverlayLayer.Children.Add(_contextualAiBar);
    }
}
