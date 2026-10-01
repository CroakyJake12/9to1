using Avalonia.Controls;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Forms;
using HavenOS.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private readonly SemaphoreSlim _formsOpen = new(1, 1);

    public async void OpenForms()
    {
        try { await OpenFormsAsync(CancellationToken.None); }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException
            or NotSupportedException or OperationCanceledException)
        {
            if (!IsDisposed) _notifications.Show("Forms unavailable", error.Message,
                ToastKind.Warning, TimeSpan.FromSeconds(5));
        }
    }

    public async Task OpenFormsAsync(CancellationToken token, Guid? selectedForm = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var services = App.Services ?? throw new InvalidOperationException("The actual Forms host is unavailable.");
        var actors = services.GetRequiredService<IAuthenticatedResourceActorSource>();
        // Capture before queueing; never adopt another session after waiting for an existing open.
        var originalActor = await actors.GetCurrentAsync(token)
            ?? throw new UnauthorizedAccessException("Forms requires the original authenticated actor.");
        await _formsOpen.WaitAsync(token);
        FormsNativeWorkspaceHost? candidate = null;
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (await actors.GetCurrentAsync(token) != originalActor)
                throw new UnauthorizedAccessException("The original Forms actor changed.");
            var key = selectedForm is { } formID ? $"haven-forms-{formID:N}" : "haven-forms";
            var existing = OpenTabs.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                if (existing.Page is not FormsNativeWorkspaceHost mounted)
                    throw new InvalidOperationException("The existing Forms tab is not the owning native workspace.");
                await mounted.RequireCurrentAsync(token);
                SelectedTab = existing;
                return;
            }
            var mountedWorkspace = await CreateFormsDocumentWorkspaceAsync(services, originalActor,
                selectedForm, () => TopLevel.GetTopLevel(this) as Window, () => !IsDisposed, token);
            candidate = mountedWorkspace.Host;
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            AddOrSelectTab(key, "Forms", candidate, closeable: true, surface: HavenSurface.Forms);
            candidate = null;
        }
        finally { candidate?.Dispose(); _formsOpen.Release(); }
    }

    public static async Task<(FormsNativeWorkspaceHost Host, FormsCuiWorkspace Workspace)> CreateFormsDocumentWorkspaceAsync(
        IServiceProvider services, AuthenticatedResourceActor originalActor, Guid? selectedForm,
        Func<Window?> owner, Func<bool> hostIsCurrent, CancellationToken token)
    {
        if (!hostIsCurrent()) throw new InvalidOperationException("The Forms host lifetime ended.");
        // Only private-issued owner services retain the actor/store through awaited service admission.
        var session = await services.GetRequiredService<FormPublicationService>().OpenHostSessionAsync(originalActor, token);
        var host = new FormsNativeWorkspaceHost(async ct =>
        {
            if (!hostIsCurrent()) throw new InvalidOperationException("The Forms host lifetime ended.");
            await session.RequireCurrentAsync(ct);
        }, owner, session.Dispose);
        try
        {
            var workspace = new FormsCuiWorkspace(session.Publications, session.Authoring,
                () => selectedForm, _ => hostIsCurrent(), host.ShowPreviewAsync,
                responseSessions: session.Responses, showResponse: host.ShowResponseAsync);
            await host.MountAsync(workspace, token);
            if (selectedForm is not null) await workspace.DispatchAsync("9to1.Forms.Open", null, token);
            await host.RequireCurrentAsync(token);
            return (host, workspace);
        }
        catch { host.Dispose(); throw; }
    }

}
