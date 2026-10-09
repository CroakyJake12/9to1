using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using Haven.Application;
using Haven.Desktop.Views.Shell;
using HavenOS.Files;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Services;

// This adapter borrows the actual original shell/Files service. It neither starts
// an executable nor manufactures another provider, Home graph or Files workspace.
internal sealed class OriginalBrowserDownloadFilesNavigator(MainView originalShell,
    FilesNativeBrowserService originalBrowser) : IFilesOriginalBrowserDownloadNavigator
{
    public async Task RevealOriginalRegisteredDownloadWithinSourceAsync(FilesNativeBrowserService sameBrowser,
        FilesNativeBrowserPage originalPage, HostedItemMetadata originalRow,
        AuthenticatedResourceActor originalActor, Action<Action> scope, Action<Task> retain,
        CancellationToken token)
    {
        if (!ReferenceEquals(sameBrowser, originalBrowser))
            throw new UnauthorizedAccessException("The native Files navigator belongs to another original Files service.");
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(retain);
        List<Exception> failures = [];
        Task? body = null;
        if (Dispatcher.UIThread.CheckAccess())
        {
            try
            {
                scope(() => body = originalShell.RevealOriginalRegisteredBrowserDownloadAsync(sameBrowser,
                    originalPage, originalRow, originalActor, scope, retain, token));
            }
            catch (Exception error) { CallbackFailure(error); }
        }
        else
        {
            Task<Task?>? dispatch = null;
            try { scope(() => dispatch = Dispatcher.UIThread.InvokeAsync<Task?>(() =>
            {
                Task? actual = null;
                try
                {
                    scope(() => actual = originalShell.RevealOriginalRegisteredBrowserDownloadAsync(sameBrowser,
                        originalPage, originalRow, originalActor, scope, retain, token));
                }
                catch (Exception error) { CallbackFailure(error); }
                // Preserve an already-issued body even if its callback protocol
                // subsequently refused. The outer driver must still join it.
                return actual;
            }).GetTask()); }
            catch (Exception error) { CallbackFailure(error); }
            if (dispatch is not null)
            {
                try { retain(dispatch); } catch (Exception error) { CallbackFailure(error); }
                try { body = await dispatch.ConfigureAwait(false); }
                catch (Exception error) { Capture(dispatch, error); }
            }
        }
        if (body is not null)
        {
            try { retain(body); } catch (Exception error) { CallbackFailure(error); }
            try { await body.ConfigureAwait(false); }
            catch (Exception error) { Capture(body, error); }
        }
        else if (failures.Count == 0) Add(new InvalidOperationException("The original Files navigation callback did not execute."));
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Original Files UI navigation and callback custody failed.", failures);

        void Add(Exception error) { if (!failures.Any(prior => ReferenceEquals(prior, error))) failures.Add(error); }
        void CallbackFailure(Exception error) => Add(error is OperationCanceledException
            ? new AggregateException("The synchronous native Files callback supplied no canceled original Task.", error) : error);
        void Capture(Task actual, Exception caught)
        {
            if (actual.Exception is { } group) foreach (var error in group.InnerExceptions) Add(error);
            else Add(caught);
        }
    }
}
