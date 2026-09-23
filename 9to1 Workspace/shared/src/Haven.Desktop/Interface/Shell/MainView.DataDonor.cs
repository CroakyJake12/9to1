using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Data;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private void OnDataWorkbookRequested(Guid workbookId) => _ = OpenDataWorkbookAsync(workbookId);

    private async Task OpenDataWorkbookAsync(Guid workbookId)
    {
        try
        {
            var mode = await _modeRegistry.GetModeByKeyAsync("data", CancellationToken.None);
            if (mode is null)
            {
                _notifications.Show("Data unavailable", "The Data app is not registered in this image.",
                    ToastKind.Warning, TimeSpan.FromSeconds(5));
                return;
            }

            await OpenModeWorkspaceAsync(mode, HavenSurface.Data, forceNewTab: false);
            if (!_documentWorkspaces.TryGetValue(mode.Key, out var workspace) || workspace is not DataPage page
                || !await page.OpenWorkbookAsync(workbookId))
            {
                _notifications.Show("Workbook unavailable", "The app-owned workbook could not be opened in Data.",
                    ToastKind.Warning, TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception failure) when (failure is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _notifications.Show("Data unavailable", failure.Message, ToastKind.Warning, TimeSpan.FromSeconds(5));
        }
    }
}
