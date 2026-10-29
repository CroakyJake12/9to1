using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Views.Pages.Files;

/// <summary>The owner's recovery screen for a 9to1 workspace folder that used to be there and is gone:
/// "9to1 directory missing - Re-locate or create new", offering either the folder the app found by
/// itself, a folder the owner points at, or a new folder. It appears only for a root that was previously
/// registered; a new install never sees it, and nothing here may touch the owner's existing files.
///
/// Authored as CUI (FilesWorkspaceRecovery.cui); this class only supplies the real state and routes the
/// three commands to the owning Files service.</summary>
internal sealed class FilesWorkspaceRecoveryPage : UserControl, ICuiActionDispatcher, ICuiActionAvailability
{
    private readonly FilesWorkspaceRootObservation _observation;
    private readonly Func<string, CancellationToken, Task> _relocate;
    private readonly Func<CancellationToken, Task> _chooseOther;
    private readonly Func<CancellationToken, Task> _createNew;
    private readonly CuiControlLoader _loader = new();
    private readonly List<Task> _originalActions = [];
    private readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _useFound = new();
    private readonly Button _chooseOtherButton = new();
    private readonly Button _createNewButton = new();
    private bool _busy, _retired, _settled, _configured;

    internal FilesWorkspaceRecoveryPage(FilesWorkspaceRootObservation observation,
        Func<string, CancellationToken, Task> relocate, Func<CancellationToken, Task> chooseOther,
        Func<CancellationToken, Task> createNew)
    {
        Dispatcher.UIThread.VerifyAccess();
        _observation = observation; _relocate = relocate; _chooseOther = chooseOther; _createNew = createNew;
    }

    internal void Initialize()
    {
        Dispatcher.UIThread.VerifyAccess();
        using var stream = typeof(FilesWorkspaceRecoveryPage).Assembly.GetManifestResourceStream("Haven.Desktop.Files.WorkspaceRecovery.cui")
            ?? throw new InvalidDataException("The Files workspace recovery document is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "FilesWorkspaceRecovery.cui");
        if (parser.Diagnostics.Diagnostics.Any(row => row.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Files workspace recovery document is invalid.");
        _loader.SetActionDispatcher(this);
        _loader.BindAuthoredControl("files-recovery-detail", _ => _detail);
        _loader.BindAuthoredControl("files-recovery-status", _ => _status);
        _loader.BindAuthoredControl("files-recovery-use-found", _ => _useFound);
        _loader.BindAuthoredControl("files-recovery-choose-other", _ => _chooseOtherButton);
        _loader.BindAuthoredControl("files-recovery-create-new", _ => _createNewButton);
        var loaded = _loader.TryLoad(document);
        if (loaded.Root is null || loaded.Diagnostics.Any(row => row.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Files workspace recovery document could not be rendered.");
        _loader.WireBindings(loaded.Root);
        Content = loaded.Root;

        // The exact location that disappeared, stated rather than implied, and the folder the app found
        // by its own identity when it did find one.
        _detail.Text = "Files was using the 9to1 folder at " + (_observation.RegisteredRoot ?? "an unknown location") +
            ", and that folder is no longer there. Your boards and documents were not moved or deleted." +
            (_observation.DiscoveredRoots.Count == 1
                ? "\nThe 9to1 folder was found again at " + _observation.DiscoveredRoots[0] + "."
                : _observation.DiscoveredRoots.Count > 1
                    ? "\nSeveral 9to1 folders were found: " + string.Join(", ", _observation.DiscoveredRoots) +
                      " - choose the one that is yours."
                    : "\nNo 9to1 folder was found automatically.");
        _useFound.IsVisible = _observation.DiscoveredRoots.Count >= 1;
        if (_observation.DiscoveredRoots.Count > 1)
            _useFound.Content = "Use a found 9to1 folder (" + _observation.DiscoveredRoots.Count + " found)";
        SetStatus("Re-locate the folder to keep your existing files, or create a new one.");
    }

    internal string Status => _status.Text ?? string.Empty;
    internal string Detail => _detail.Text ?? string.Empty;
    internal Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();
    internal bool IsAlive => !_retired;

    public bool? IsActionAvailable(string command) => command switch
    {
        "files.recovery.use-found" => !_busy && !_retired && !_settled && !_configured && _observation.DiscoveredRoots.Count >= 1,
        "files.recovery.choose-other" => !_busy && !_retired && !_settled && !_configured,
        "files.recovery.create-new" => !_busy && !_retired && !_settled && !_configured && _observation.DiscoveredRoots.Count == 0,
        _ => null,
    };

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Files recovery action is unavailable.");
        _busy = true; SetButtonsEnabled();
        var actual = command switch
        {
            "files.recovery.use-found" => RunAsync(() => _relocate(_observation.DiscoveredRoots[0], cancellationToken)),
            "files.recovery.choose-other" => RunAsync(() => _chooseOther(cancellationToken)),
            _ => RunAsync(() => _createNew(cancellationToken)),
        };
        _originalActions.RemoveAll(row => row.IsCompletedSuccessfully); _originalActions.Add(actual);
        return new(actual);
    }

    private async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The previous setup stays exactly as it was; the reason is shown, not swallowed.
            SetStatus("Nothing was changed. " + error.Message.Split('\n', 2)[0]);
        }
        finally { _busy = false; SetButtonsEnabled(); }
    }

    internal void SetStatus(string message) { _status.Text = message; }
    internal void MarkSettled() { _settled = true; SetButtonsEnabled(); }
    private void SetButtonsEnabled()
    {
        _useFound.IsEnabled = IsActionAvailable("files.recovery.use-found") == true;
        _chooseOtherButton.IsEnabled = IsActionAvailable("files.recovery.choose-other") == true;
        _createNewButton.IsEnabled = IsActionAvailable("files.recovery.create-new") == true;
    }

    /// <summary>Recovery finished and the real Files surface takes this page's content. There is no
    /// second Files browser here: this is the same configured view the session opened everywhere
    /// else, mounted inside the stable presentation root.</summary>
    internal void ShowConfigured(Control view)
    {
        Dispatcher.UIThread.VerifyAccess();
        _configured = true;
        if (!ReferenceEquals(Content, view)) Content = view;
        SetButtonsEnabled();
    }

    /// <summary>Retires the page and joins every recovery action it started, so closing a Files tab can
    /// never leave a re-location or new-folder write running on its own.</summary>
    internal async Task CloseAndDrainAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        Retire();
        await WhenActionsIdleAsync();
        Task[] pending;
        lock (_originalActions) pending = _originalActions.ToArray();
        List<Exception>? failures = null;
        foreach (var original in pending)
            try { await original.ConfigureAwait(false); }
            catch (Exception error) { (failures ??= []).Add(error); }
        if (failures is not null) throw new AggregateException("Files workspace recovery actions did not settle cleanly.", failures);
    }

    internal void ReleaseRetiredContent()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_retired) throw new InvalidOperationException("The Files recovery page has not retired.");
        Content = null;
    }

    internal void Retire() { _retired = true; SetButtonsEnabled(); }
}
