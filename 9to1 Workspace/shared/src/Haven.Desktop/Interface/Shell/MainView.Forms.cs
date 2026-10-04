using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    public void OpenForms()
    {
        const string key = "haven-forms";
        var existing = OpenTabs.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        var services = App.Services;
        var submissions = services?.GetService<IFormsSubmissionStore>();
        if (submissions is null)
        {
            _notifications.Show(
                "Forms unavailable",
                "Forms is registered, but local response storage is not available in this image.",
                ToastKind.Warning,
                TimeSpan.FromSeconds(5));
            return;
        }

        var page = new FormsPage(submissions, _structuredFormTemplate, _genUiRouter, _genUiInstances);
        page.DataWorkbookRequested += OnDataWorkbookRequested;
        AddOrSelectTab(key, "Forms", page, closeable: true, surface: HavenSurface.Forms);
    }
}
