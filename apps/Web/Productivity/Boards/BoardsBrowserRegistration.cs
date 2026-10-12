using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Web.Productivity.Boards;

/// <summary>Connects the maintained notebook owner to this-origin storage and the original retained editor.</summary>
public static class BoardsBrowserRegistration
{
    public static HomeCoreOperationResult<bool> Register(BrowserSurfaceRegistry registry,
        INotesRepository repository, Func<bool> reduceMotion, Func<Exception, bool>? unknownOutcome = null)
    {
        using var stream = typeof(BoardsBrowserRegistration).Assembly.GetManifestResourceStream("NineToOne.Web.Boards.cui")
            ?? throw new InvalidOperationException("The browser Boards CUI resource is unavailable.");
        using var reader = new StreamReader(stream);
        var document = new CuiDocumentSource(reader.ReadToEnd(), "Boards.cui", deferParsing: true);
        var feature = new BoardsBrowserFeature(repository,
            core => BoardsBrowserSurface.Create(document.Document, core, reduceMotion), unknownOutcome: unknownOutcome);
        return registry.Register(feature, feature.Render, BrowserSurfaceScope.DeviceLocal);
    }
}
