using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Haven.Application;
using Haven.Desktop.Views.Shell;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Activates the existing native Home review in the original local host. No permission decision is made here.</summary>
public sealed class NativeHomeApprovalPromptPresenter(IServiceProvider services) : IHomeApprovalPromptPresenter
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<bool> ShowPendingRequestAsync(string requestId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var actors = services.GetRequiredService<IAuthenticatedResourceActorSource>();
            var original = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (original is null || !ReferenceEquals(App.Services, services)) return false;
            var review = await Dispatcher.UIThread.InvokeAsync<Task?>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(App.Services, services)) return null;
                MainView? shell = null;
                switch (Avalonia.Application.Current?.ApplicationLifetime)
                {
                    case IClassicDesktopStyleApplicationLifetime desktop when desktop.MainWindow is { } window:
                        shell = window.DataContext as MainView;
                        if (shell is null || shell.IsDisposed) return null;
                        if (!window.IsVisible) window.Show();
                        window.Activate();
                        break;
                    case ISingleViewApplicationLifetime single when single.MainView is MainView view:
                        shell = view;
                        break;
                }
                if (shell is null || shell.IsDisposed || TopLevel.GetTopLevel(shell) is not { IsVisible: true }) return null;
                return shell.ReviewHomeRequestForOriginalHostAsync(requestId, services, original, cancellationToken);
            });
            if (review is null) return false;
            await review.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(App.Services, services) || await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != original)
                throw new UnauthorizedAccessException("The original Home prompt host or local actor changed.");
            return true;
        }
        finally { _gate.Release(); }
    }
}
