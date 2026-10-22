using CakeOS.Cui;

namespace NineToOne.Web.Productivity.Boards;

/// <summary>Browser presentation adapter over the SAME portable Boards CUI owner.</summary>
public static class BoardsBrowserSurface
{
    public static BrowserCuiSurface Create(CuiDocument document, BoardsBrowserCore core, Func<bool> reduceMotion)
    {
        var lifetime = new BoardsCuiWorkspacePresentation(core, reduceMotion);
        try
        {
            return new(document, core, core, lifetime, lifetime.ControlRegistry);
        }
        catch (Exception primary)
        {
            try { lifetime.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Boards surface acquisition and cleanup failed.", primary, cleanup); }
            throw;
        }
    }
}
