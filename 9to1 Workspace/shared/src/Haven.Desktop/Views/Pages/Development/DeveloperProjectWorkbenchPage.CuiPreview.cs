using Avalonia.Controls;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private const int MaximumPreviewScenes = 128;
    private ContentControl? _cuiPreviewSlot;
    private CuiSceneHost? _cuiPreviewHost;
    private readonly List<CuiSceneHost> _cuiPreviewScenes = [];
    private DeveloperCuiDraftPreview? _cuiPreviewLastValid;
    private DeveloperCuiDraftPreview? _cuiPreviewAttempt;
    private Guid _cuiPreviewTab;
    private long _cuiPreviewDraftRevision;
    private string? _cuiPreviewText;
    private string _cuiPreviewRuntimeStatus = "Preview this draft to see it here.";

    // A trusted native boundary, registered only by this workbench. The authored draft
    // can never request Object renderers; only the compiled workbench owns this slot.
    private Control CreateCuiPreviewSlot()
    {
        lock (_gate)
        {
            var slot = new ContentControl { IsHitTestVisible = false, Focusable = false };
            _cuiPreviewSlot = slot;
            return slot;
        }
    }

    private bool TryGetCuiPreviewValue(string path, out object? value)
    {
        value = path switch
        {
            "CanPreviewCuiDraft" => IsActionAvailable("dev.preview-cui-draft"),
            "CuiDraftPreviewSubset" => DeveloperCuiDraftPreviewCompiler.SupportedSubset,
            "CuiDraftPreviewStatus" => CuiPreviewStatus(),
            "CuiDraftPreviewDiagnostics" => _cuiPreviewAttempt is null ? "" :
                string.Join(Environment.NewLine, _cuiPreviewAttempt.Diagnostics.Take(100)),
            _ => null
        };
        return path is "CanPreviewCuiDraft" or "CuiDraftPreviewSubset" or "CuiDraftPreviewStatus" or "CuiDraftPreviewDiagnostics";
    }

    private string CuiPreviewStatus()
    {
        if (_cuiPreviewLastValid is not { } valid) return _cuiPreviewRuntimeStatus;
        if (_cuiPreviewSlot is null || !ReferenceEquals(_cuiPreviewSlot.Content, _cuiPreviewHost))
            return "Preview is out of date. Preview this draft again.";
        var current = CurrentSourceTab()?.Id == _cuiPreviewTab && CurrentDraftRevision() == _cuiPreviewDraftRevision &&
            StringComparer.Ordinal.Equals(CurrentEditorText(), _cuiPreviewText);
        return _cuiPreviewRuntimeStatus + $" Showing {valid.RelativePath}." + (current ? "" : " Preview is out of date.");
    }

    private bool IsCuiPreviewCaptureCurrent(Guid tab, long revision, string path, string text, ContentControl slot) =>
        CurrentSourceTab() is { } current && current.Id == tab && CurrentDraftRevision() == revision &&
        StringComparer.Ordinal.Equals(current.Path, path) && StringComparer.Ordinal.Equals(CurrentEditorText(), text) &&
        ReferenceEquals(_cuiPreviewSlot, slot);

    private ValueTask DispatchCuiPreviewAsync(CancellationToken token)
    {
        long generation, revision; Guid tabId; string path, text; ContentControl slot;
        lock (_gate)
        {
            if (IsActionAvailable("dev.preview-cui-draft") != true || CurrentSourceTab() is not { } tab || _cuiPreviewSlot is null)
                throw new InvalidOperationException("Open a CUI file before previewing it.");
            generation = _generation; revision = CurrentDraftRevision(); tabId = tab.Id; path = tab.Path; text = CurrentEditorText();
            slot = _cuiPreviewSlot; _pending = true;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                CuiSceneHost? candidate = null, replaced = null; var mounted = false;
                try
                {
                    // The same already-authorised in-memory draft is compiled on a retained
                    // finite CPU task. Only installed compiler identity is read; no project IO.
                    var compilation = await original.AwaitAsync(Task.Run(() =>
                        DeveloperCuiDraftPreviewCompiler.Compile(path, text, token), token)).ConfigureAwait(false);
                    Task<CuiSceneAvailability>? show = null;
                    await PublishAsync(original, () =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (!IsCuiPreviewCaptureCurrent(tabId, revision, path, text, slot))
                        { _status = "The draft changed. Preview it again."; return; }
                        _cuiPreviewAttempt = compilation;
                        if (!compilation.Succeeded || compilation.Document is null)
                        { _cuiPreviewRuntimeStatus = "This draft could not be previewed. Check the problems below."; return; }
                        if (_cuiPreviewScenes.Count >= MaximumPreviewScenes)
                            throw new InvalidOperationException("Some previews are still closing. Wait before previewing again.");
                        var acquired = new CuiSceneHost { IsHitTestVisible = false, Focusable = false };
                        candidate = acquired;
                        _cuiPreviewScenes.Add(acquired); // Retain before readiness/native callbacks.
                        show = InvokeOriginalSource(() => acquired.ShowAsync(new("dev.project-workbench", "Static draft preview", "Dev draft preview",
                            compilation.Document, EmptyPreviewPorts.Instance, EmptyPreviewPorts.Instance, _readiness)
                        { IsPublicationCurrent = () => IsCurrent(generation) }, token));
                    }).ConfigureAwait(false);
                    if (show is not null && candidate is not null)
                    {
                        var admitted = candidate;
                        var availability = await original.AwaitAsync(show).ConfigureAwait(false);
                        await PublishAsync(original, () =>
                        {
                            token.ThrowIfCancellationRequested();
                            if (!IsCuiPreviewCaptureCurrent(tabId, revision, path, text, slot))
                            { _status = "The draft changed. The previous preview is unchanged."; return; }
                            if (availability.State != CuiSceneAvailabilityState.Ready || admitted.Diagnostics.Any(value => value.Severity == CuiDiagnosticSeverity.Error))
                            {
                                _cuiPreviewRuntimeStatus = "Preview is unavailable. The previous preview is unchanged.";
                                var diagnostics = compilation.Diagnostics.Concat(admitted.Diagnostics).ToList();
                                if (availability.State != CuiSceneAvailabilityState.Ready)
                                    diagnostics.Add(new("DEVCP006", CuiDiagnosticSeverity.Error, availability.Code + ": " + availability.Message, CuiSourceSpan.At(path, 1, 1)));
                                _cuiPreviewAttempt = compilation with { Document = null, Diagnostics = diagnostics.ToArray() };
                                return;
                            }
                            // The candidate is genuinely compiled/admitted and has finished its
                            // actual readiness/native load before replacing the retained scene.
                            var previousContent = slot.Content;
                            try { slot.Content = admitted; }
                            catch (Exception publication)
                            {
                                try { slot.Content = previousContent; }
                                catch (Exception restoration) { throw new AggregateException("Preview publication and prior-scene restoration failed.", publication, restoration); }
                                throw;
                            }
                            if (!IsCuiPreviewCaptureCurrent(tabId, revision, path, text, slot))
                            {
                                slot.Content = previousContent;
                                _status = "The draft changed. The previous preview is unchanged."; return;
                            }
                            replaced = _cuiPreviewHost;
                            _cuiPreviewHost = admitted; _cuiPreviewLastValid = compilation;
                            _cuiPreviewTab = tabId; _cuiPreviewDraftRevision = revision; _cuiPreviewText = text;
                            _cuiPreviewRuntimeStatus = "Preview updated.";
                            mounted = true; _status = "Preview updated.";
                        }).ConfigureAwait(false);
                    }
                }
                catch (Exception error) { original.Retain(error); }
                finally
                {
                    foreach (var host in new[] { mounted ? null : candidate, replaced }.OfType<CuiSceneHost>())
                    {
                        try
                        {
                            var close = InvokeOriginalSource(host.CloseOriginalAsync);
                            await original.AwaitAsync(close).ConfigureAwait(false);
                            lock (_gate) _cuiPreviewScenes.Remove(host);
                        }
                        catch (Exception error) { original.Retain(error); }
                    }
                    lock (_gate) _pending = false;
                    if (IsCurrent(generation))
                        try { await PublishAsync(original, () => { }).ConfigureAwait(false); }
                        catch (Exception error) { original.Retain(error); }
                    original.ThrowRetained();
                }
            }));
        }
        catch { lock (_gate) _pending = false; throw; }
    }

    private async Task CloseCuiPreviewScenesAsync()
    {
        CuiSceneHost[] scenes; lock (_gate) scenes = _cuiPreviewScenes.ToArray();
        var errors = new List<Exception>();
        foreach (var host in scenes)
        {
            Task? close = null;
            try { close = InvokeOriginalSource(host.CloseOriginalAsync); }
            catch (Exception error) { errors.Add(error); }
            if (close is null) continue;
            try { await close.ConfigureAwait(false); lock (_gate) _cuiPreviewScenes.Remove(host); }
            catch (Exception error) { errors.Add(close.Exception ?? error); }
        }
        if (errors.Count != 0) throw new AggregateException("Compiled preview scenes did not drain cleanly.", errors);
    }

    private sealed class EmptyPreviewPorts : ICuiBindingContext, ICuiActionDispatcher
    {
        public static EmptyPreviewPorts Instance { get; } = new();
        public bool TryGetValue(string path, out object? value) { value = null; return false; }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new NotSupportedException("Static draft preview never dispatches application actions."));
    }
}
