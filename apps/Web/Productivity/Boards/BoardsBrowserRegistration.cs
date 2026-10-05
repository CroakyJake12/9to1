using CakeOS.Cui.Language;
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
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "Boards.cui");
        if (parser.Diagnostics.Diagnostics.Any(error => error.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The browser Boards CUI document is invalid.");
        var feature = new BoardsBrowserFeature(repository,
            core => BoardsBrowserSurface.Create(document, core, reduceMotion), unknownOutcome: unknownOutcome);
        return registry.Register(feature, feature.Render, BrowserSurfaceScope.DeviceLocal);
    }
}
