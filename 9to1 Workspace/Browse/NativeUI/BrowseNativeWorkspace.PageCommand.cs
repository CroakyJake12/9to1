using Avalonia.Threading;

namespace HavenOS.Apps.Browse;

/// <summary>An app may contribute one current action from its original native view.</summary>
public interface IBrowseNativePageCommandSource : IBrowseNativeAppContentSession
{
    BrowsePageCommandSlot PageCommandSlot { get; }
}

public sealed record BrowsePageCommand(string Label, Func<CancellationToken, Task> Invoke);

public sealed class BrowsePageCommandSlot
{
    internal BrowsePageCommand? Command { get; private set; }
    internal bool Enabled { get; private set; }
    public bool IsHosted { get; private set; }
    public bool IsSelected { get; private set; }
    public event EventHandler? HostChanged;
    internal event EventHandler? Changed;

    public void Update(BrowsePageCommand? command, bool enabled)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (ReferenceEquals(Command, command) && Enabled == enabled) return;
        Command = command;
        Enabled = enabled;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void SetHosted(bool hosted, bool selected = false)
    {
        if (hosted == IsHosted && selected == IsSelected) return;
        IsHosted = hosted;
        IsSelected = selected;
        HostChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed partial class BrowseNativeWorkspace
{
    private sealed record PageCommandTarget(Guid Tab, long Visit, IBrowseNativePageCommandSource Source, BrowsePageCommand Command);
    private IBrowseNativePageCommandSource? _selectedPageCommands;
    private PageCommandTarget? _pageCommand;
    private Guid? _observedPageTab;
    private long _pageVisit;
    private bool _toolbarMounted, _pageCommandFits;

    // Chrome notifications may coalesce before the renderer repaints. Every actual selection
    // change still expires an already-issued target, including a rapid return to the same tab.
    private void ObservePageVisit(Guid tab)
    {
        lock (_sourceGate)
        {
            if (_observedPageTab == tab) return;
            _observedPageTab = tab;
            Interlocked.Increment(ref _pageVisit);
        }
    }

    private void ObservePageCommand()
    {
        var tab = _snapshot.SelectedTab;
        var source = _toolbarMounted && IsOriginalPublicationCurrent && tab.EngineState == BrowseEngineState.NativeApp
            ? _chrome.ObserveOriginalAppContent(tab.Id) as IBrowseNativePageCommandSource : null;
        if (!ReferenceEquals(_selectedPageCommands, source))
        {
            if (_selectedPageCommands is not null) _selectedPageCommands.PageCommandSlot.Changed -= OnPageCommandChanged;
            _selectedPageCommands = source;
            if (source is not null) source.PageCommandSlot.Changed += OnPageCommandChanged;
        }
        var command = source?.IsPublicationCurrent == true ? source.PageCommandSlot.Command : null;
        var visit = Interlocked.Read(ref _pageVisit);
        if (command is null) _pageCommand = null;
        else if (_pageCommand?.Tab != tab.Id || _pageCommand.Visit != visit || !ReferenceEquals(_pageCommand.Command, command)
            || !ReferenceEquals(_pageCommand.Source, source))
            _pageCommand = new(tab.Id, visit, source!, command);
    }

    private void OnPageCommandChanged(object? sender, EventArgs args)
    {
        if (!IsOriginalPublicationCurrent || !ReferenceEquals(sender, _selectedPageCommands?.PageCommandSlot)) return;
        ObservePageCommand();
        Changed();
    }

    private bool CanRunPageCommand(object? target) => _toolbarMounted && IsOriginalPublicationCurrent &&
        target is PageCommandTarget issued && ReferenceEquals(issued, _pageCommand) &&
        issued.Visit == Interlocked.Read(ref _pageVisit) && _chrome.State.SelectedTabId == issued.Tab &&
        ReferenceEquals(_chrome.ObserveOriginalAppContent(issued.Tab), issued.Source) && issued.Source.IsPublicationCurrent &&
        issued.Source.PageCommandSlot.Enabled && ReferenceEquals(issued.Source.PageCommandSlot.Command, issued.Command);

    private async Task RunPageCommandAsync(object? target, CancellationToken token)
    {
        if (!CanRunPageCommand(target)) return;
        var issued = (PageCommandTarget)target!;
        var original = issued.Command.Invoke(token) ?? throw new InvalidOperationException("The page action returned no operation.");
        lock (_sourceGate) Retain(_originalOperations, original);
        await original;
    }
}
