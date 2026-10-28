using CakeOS.Apps.Boards.App;
using Haven.Application;
using CakeOS.Cui.Themes;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

internal sealed partial class NativeCanvasBoardEmbeddedArtifactHost : IBoardsDatabaseSource
{
    public CuiThemePreferenceContext? DatabaseTheme => _theme;
    public Task<DataRecordSession> CreateDatabaseAsync(string title, CancellationToken token = default) =>
        _provider.GetRequiredService<DataLocalRecordSessionFactory>()
            .CreateAsync(_profileId, title, token, _tabLifetime);

    public Task<DataRecordSession> OpenDatabaseAsync(Guid workbookID, CancellationToken token = default) =>
        _provider.GetRequiredService<DataLocalRecordSessionFactory>()
            .OpenAsync(_profileId, workbookID, token, _tabLifetime);
}
