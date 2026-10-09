#if !ANDROID
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;

namespace Haven.Desktop.Services;

// The native picker returns only a path observation. Actual registered Files
// selection and independent Home READ/import remain the configured source's work.
internal sealed class OriginalAssistantAttachmentPicker : IAssistantOriginalAttachmentPicker
{
    private readonly Window _window;
    private readonly CancellationToken _windowLifetime;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly object _gate = new();
    private readonly List<CloudflareOriginalTaskLedger> _sources = [];
    private readonly List<IStorageFile> _items = [];
    private readonly HashSet<IStorageFile> _disposed = new(ReferenceEqualityComparer.Instance);
    private CloudflareOriginalTaskLedger? _closeSources;
    internal OriginalAssistantAttachmentPicker(Window actualWindow, CancellationToken actualWindowLifetime)
    {
        if (!actualWindowLifetime.CanBeCanceled) throw new ArgumentException("Retain the actual native window lifetime.");
        _window = actualWindow; _windowLifetime = actualWindowLifetime;
        _work = new(() => Task.CompletedTask, Cleanup);
    }
    internal Task? OriginalClose => _work.OriginalClose;
    internal void DemandExternalOriginalJoin() { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _work.DemandExternalClose(); }
    internal void RequestOriginalRetirement() => _work.RequestRetirement();
    internal Task CloseAndDrainOriginalAsync() { DemandExternalOriginalJoin(); return _work.CloseAndDrainAsync(); }
    public Task<string?> PickOriginalAttachmentWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        _work.RunAsync(async original =>
        {
            Dispatcher.UIThread.VerifyAccess();
            var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(this);
            lock (_gate) _sources.Add(source);
            Task<IReadOnlyList<IStorageFile>>? raw = null; IReadOnlyList<IStorageFile>? returned = null;
            var errors = new List<Exception>();
            void Demand()
            { token.ThrowIfCancellationRequested(); _windowLifetime.ThrowIfCancellationRequested(); original.DemandPublication(); if (!_window.IsVisible) throw new UnauthorizedAccessException("The actual native window retired."); }
            try
            {
                Demand();
                Invoke(source, scope, () =>
                {
                    Demand();
                    var provider = _window.StorageProvider; // Actual platform getter under the finite source guard.
                    if (!provider.CanOpen) throw new NotSupportedException("The actual native file picker is unavailable.");
                    raw = provider.OpenFilePickerAsync(new()
                    {
                        Title = "Attach a document", AllowMultiple = false,
                        FileTypeFilter =
                        [
                            new FilePickerFileType("Text and source files") { Patterns = ["*.txt", "*.csv", "*.tsv", "*.log", "*.ini", "*.cfg", "*.conf", "*.rtf", "*.cs", "*.fs", "*.vb", "*.java", "*.kt", "*.kts", "*.cpp", "*.c", "*.h", "*.hpp", "*.rs", "*.go", "*.py", "*.js", "*.jsx", "*.ts", "*.tsx", "*.html", "*.htm", "*.css", "*.scss", "*.sass", "*.less", "*.xml", "*.xaml", "*.axaml", "*.json", "*.jsonc", "*.yaml", "*.yml", "*.toml", "*.sql", "*.ps1", "*.sh", "*.bash", "*.cmd", "*.bat", "*.md", "*.razor", "*.vue", "*.svelte", "*.gradle", "*.csproj", "*.fsproj", "*.vbproj", "*.sln"] },
                            new FilePickerFileType("Word, PowerPoint and Excel documents") { Patterns = ["*.docx", "*.pptx", "*.xlsx"] }
                        ]
                    });
                    source.Track(raw); // SAME actual task precedes the foreign retainer/postchecks.
                    retain(raw);
                });
            }
            catch (Exception cause) { source.Retain(cause); }
            if (raw is not null)
                try
                {
                    returned = await source.AwaitAsync(raw);
                    // Returned original items are retained even when the view or
                    // source callback already failed. No stream/bookmark is opened.
                    lock (_gate) foreach (var item in returned) if (!_items.Any(own => ReferenceEquals(own, item))) _items.Add(item);
                }
                catch (Exception cause) { source.Capture(raw, cause); }
            string? selected = null;
            try
            {
                await source.ObserveAllOriginalTasksAsync(); ThrowOriginalPickerErrors(source);
                if (raw is null || returned is null) throw new InvalidOperationException("The actual picker returned no retained original task/items.");
                Invoke(source, scope, () =>
                {
                    Demand();
                    if (returned.Count > 1) throw new InvalidDataException("The original single-file picker returned multiple items.");
                    selected = returned.Count == 0 ? null : returned[0].TryGetLocalPath();
                    if (returned.Count != 0 && string.IsNullOrWhiteSpace(selected)) throw new NotSupportedException("Choose an actual local Files materialization.");
                });
            }
            catch (Exception cause) { source.Retain(cause); }
            // Each original platform item is disposed once before this finite
            // selection completes. Dispose faults retain the actual item owner.
            if (returned is not null)
                foreach (var item in returned)
                    try { DisposeOriginal(source, item); } catch (Exception cause) { source.Retain(cause); }
            try { await source.ObserveAllOriginalTasksAsync(); ThrowOriginalPickerErrors(source); } catch (Exception cause) { errors.Add(cause); }
            if (errors.Count != 0) throw new AggregateException("The actual picker selection did not settle.", errors);
            return selected;
        });
    private void Invoke(CloudflareOriginalTaskLedger ledger, Action<Action> scope, Action body)
    {
        var thread = Environment.CurrentManagedThreadId; var calls = 0; var active = true;
        Exception? bodyFailure = null; Exception? protocolFailure = null;
        void Callback()
        {
            if (!active || Environment.CurrentManagedThreadId != thread || Interlocked.Increment(ref calls) != 1)
            { var cause = new InvalidOperationException("The original picker callback must run synchronously once on its current thread."); protocolFailure = cause; ledger.Retain(cause); throw cause; }
            try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { body(); return true; }); }
            catch (Exception cause) { bodyFailure = cause; ledger.Retain(cause); throw; }
        }
        try { scope(Callback); }
        catch (Exception cause) { ledger.Retain(cause); throw; }
        finally { active = false; }
        if (calls != 1) { var cause = new InvalidOperationException("The original picker scope did not invoke its callback."); ledger.Retain(cause); throw cause; }
        if (protocolFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(protocolFailure).Throw();
        if (bodyFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(bodyFailure).Throw();
    }
    private void DisposeOriginal(CloudflareOriginalTaskLedger source, IStorageFile same)
    {
        lock (_gate) { if (_disposed.Contains(same)) return; _disposed.Add(same); }
        source.Invoke(() => { same.Dispose(); return true; });
    }
    private static void ThrowOriginalPickerErrors(CloudflareOriginalTaskLedger source)
    {
        if (source.OriginalErrors.Count != 0)
            throw new AggregateException("The actual original picker sources did not settle.", source.OriginalErrors);
    }
    private async Task Cleanup()
    {
        var source = _closeSources = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(this);
        CloudflareOriginalTaskLedger[] originalSources; IStorageFile[] items;
        lock (_gate) { originalSources = _sources.ToArray(); items = _items.ToArray(); }
        foreach (var actual in originalSources)
        {
            try { await actual.ObserveAllOriginalTasksAsync(); ThrowOriginalPickerErrors(actual); }
            catch (Exception cause) { source.Retain(cause); }
        }
        foreach (var item in items) try { DisposeOriginal(source, item); } catch (Exception cause) { source.Retain(cause); }
        await source.ObserveAllOriginalTasksAsync(); ThrowOriginalPickerErrors(source);
    }
}
#endif
