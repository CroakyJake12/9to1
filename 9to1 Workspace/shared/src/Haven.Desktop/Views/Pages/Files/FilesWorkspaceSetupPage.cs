using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;

namespace Haven.Desktop.Views.Pages.Files;

/// <summary>A stable Files presentation while explicit first-use setup completes.
/// The configured child is the real Files surface, never another file repository.</summary>
internal sealed class FilesWorkspaceSetupPage : UserControl, ICuiActionDispatcher, ICuiActionAvailability
{
    private readonly Func<CancellationToken, Task> _choose;
    private readonly CuiControlLoader _loader;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _button = new();
    private readonly List<Task> _originalActions = [];
    private Task? _close;
    private bool _busy, _retired, _configured;

    internal FilesWorkspaceSetupPage(Func<CancellationToken, Task> choose)
    {
        Dispatcher.UIThread.VerifyAccess(); _choose = choose; _loader = new();
    }

    // The session retains this owner before resource parsing, binding or theme
    // callbacks can fail, so external close can drain even an unpublished page.
    internal void Initialize()
    {
        Dispatcher.UIThread.VerifyAccess();
        using var stream = typeof(FilesWorkspaceSetupPage).Assembly.GetManifestResourceStream("Haven.Desktop.Files.WorkspaceSetup.cui")
            ?? throw new InvalidDataException("The Files setup document is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), "FilesWorkspaceSetup.cui");
        if (parser.Diagnostics.Diagnostics.Any(row => row.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Files setup document is invalid.");
        _loader.SetActionDispatcher(this);
        _loader.BindAuthoredControl("files-choose-folder", _ => _button);
        _loader.BindAuthoredControl("files-setup-status", _ => _status);
        var loaded = _loader.TryLoad(document);
        if (loaded.Root is null || loaded.Diagnostics.Any(row => row.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Files setup document could not be rendered.");
        _loader.WireBindings(loaded.Root); Content = loaded.Root;
    }

    internal string Status => _status.Text ?? string.Empty;
    internal Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();
    internal bool IsAlive => !_retired;
    internal Task? OriginalClose => _close;
    public bool? IsActionAvailable(string command) => command == "files.setup.choose" && !_busy && !_retired && !_configured;
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Files setup action is unavailable.");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = DispatchOriginalAsync(start.Task, cancellationToken);
        _originalActions.RemoveAll(row => row.IsCompletedSuccessfully); _originalActions.Add(actual);
        start.SetResult(); return new(actual);
    }
    private async Task DispatchOriginalAsync(Task start, CancellationToken token)
    {
        await start;
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        if (_retired) throw new ObjectDisposedException(nameof(FilesWorkspaceSetupPage));
        await _choose(token);
    }
    internal void SetBusy(bool busy) { _busy = busy; _button.IsEnabled = !busy && !_retired && !_configured; }
    internal void SetStatus(string message) => _status.Text = message;
    internal void ShowConfigured(Control originalFilesView)
    { Dispatcher.UIThread.VerifyAccess(); _configured = true; _button.IsEnabled = false; Content = originalFilesView; }
    internal Task CloseAndDrainAsync()
    {
        Dispatcher.UIThread.VerifyAccess(); CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        if (_close is not null) return _close;
        _retired = true; _button.IsEnabled = false;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<Exception>();
        _close = CompleteCloseAsync(start.Task, failures);
        try { _loader.Dispose(); }
        catch (Exception error) { failures.Add(error); }
        finally { start.SetResult(); }
        return _close;
    }
    private async Task CompleteCloseAsync(Task start, List<Exception> failures)
    {
        await start;
        // Loader disposal stops future clicks and retains the complete native
        // action pipeline (including its observers), not only the inner command.
        foreach (var original in _originalActions.Append(_loader.WhenActionsIdleAsync()))
            try { await original; }
            catch (Exception error)
            {
                if (original.Exception is { } group) failures.AddRange(group.InnerExceptions);
                else failures.Add(error);
            }
        if (failures.Count > 0) throw new AggregateException("Files setup UI did not retire cleanly.", failures);
    }
    // The session calls this only after its actual Files route has also settled.
    // Loader retirement alone cannot release the borrowed real Files child.
    internal void ReleaseRetiredContent()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_retired || _close is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("The original setup actions have not retired cleanly.");
        Content = null;
    }

    internal static async Task<string?> ChooseFolderAsync(Control owner)
    {
        Dispatcher.UIThread.VerifyAccess();
        var storage = TopLevel.GetTopLevel(owner)?.StorageProvider
            ?? throw new InvalidOperationException("Open Files in a native window to choose its workspace folder.");
        if (!storage.CanPickFolder) throw new NotSupportedException("The native folder picker is unavailable.");
        var selected = await storage.OpenFolderPickerAsync(new()
        { Title = "Choose an empty Files workspace folder", AllowMultiple = false });
        try
        {
            if (selected.Count == 0) return null;
            if (selected.Count != 1) throw new InvalidOperationException("Choose one empty Files workspace folder.");
            return selected[0].TryGetLocalPath()
                ?? throw new NotSupportedException("Files setup needs a local folder selected by the native picker.");
        }
        finally { foreach (var folder in selected) folder.Dispose(); }
    }
}
