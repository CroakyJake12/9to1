using Haven.Application;
using CakeOS.Cui.Themes;

namespace CakeOS.Apps.Boards.App;

/// <summary>Host-owned access to the canonical Data artifact, never a copied row model.</summary>
public interface IBoardsDatabaseSource
{
    CuiThemePreferenceContext? DatabaseTheme => null;
    Task<DataRecordSession> CreateDatabaseAsync(string title, CancellationToken token = default);
    Task<DataRecordSession> OpenDatabaseAsync(Guid workbookID, CancellationToken token = default);
}
