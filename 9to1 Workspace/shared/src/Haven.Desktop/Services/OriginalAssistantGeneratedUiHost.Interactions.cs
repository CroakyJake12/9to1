#if !ANDROID
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Infrastructure;

namespace Haven.Desktop.Services;

internal sealed partial class OriginalAssistantGeneratedUiHost
{
    private CanonicalGeneratedUiInteractionOriginalOwner? _interactionStore;
    private readonly List<Interaction> _interactions = [];
    private readonly List<ICanonicalGeneratedUiOriginalSaveObservation> _interactionDeliveries = [];
    private readonly object _interactionGate = new();
    private readonly List<Task> _interactionCommands = [];
    internal IReadOnlyList<Task> OriginalInteractionCommands { get { lock (_interactionGate) return _interactionCommands.ToArray(); } }
    internal IReadOnlyList<ICanonicalGeneratedUiOriginalObservation> OriginalInteractionObservations => _interactions.Where(item => item.Read is not null).Select(item => item.Read!).ToArray();
    private sealed class Interaction(Mount mount, GenUiTemplateRequest request,
        OriginalAssistantGeneratedUiOriginOwner.MessageEvidence message, int ordinal,
        ICanonicalGeneratedUiOriginalSelection selection, GenerativeUiSurface surface, Func<bool> current)
    {
        internal readonly Mount Mount = mount;
        internal readonly GenUiTemplateRequest Request = request;
        internal readonly OriginalAssistantGeneratedUiOriginOwner.MessageEvidence Message = message;
        internal readonly int Ordinal = ordinal;
        internal ICanonicalGeneratedUiOriginalSelection Selection = selection;
        internal GenerativeUiSurface Surface = surface;
        internal readonly Func<bool> Current = current;
        internal readonly TextBlock Status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        internal readonly Button Save = new() { Content = "Save interaction" };
        internal readonly Button Restore = new() { Content = "Restore saved interaction" };
        internal readonly Button Refresh = new() { Content = "Review saved interaction" };
        internal readonly StackPanel Controls = new() { Spacing = 6 };
        internal bool Busy;
        internal ICanonicalGeneratedUiOriginalObservation? Read;
        internal ICanonicalGeneratedUiOriginalSaveIntent? Intent;
        internal Guid OperationId = Guid.NewGuid();
    }
    internal void BindOriginalInteractionStore(CanonicalGeneratedUiInteractionOriginalOwner actual)
    {
        _work.RunSynchronous(original =>
        {
            if (_interactionStore is not null || _interactionOrigins is null || _mounts.Count != 0 ||
                !ReferenceEquals(actual.OriginalMessageAuthority, _interactionOrigins))
                throw new UnauthorizedAccessException("Bind the SAME protected writer and process origin before controls are published.");
            _interactionStore = actual;
        });
    }
    private void AttachOriginalInteraction(Mount mount, GenUiTemplateRequest request,
        OriginalAssistantGeneratedUiOriginOwner.MessageEvidence message, int ordinal,
        ICanonicalGeneratedUiOriginalSelection selection, GenerativeUiSurface surface, Func<bool> current,
        CloudflareOriginalTaskLedger sources)
    {
        if (_interactionStore is null) return;
        var item = new Interaction(mount, request, message, ordinal, selection, surface, current);
        _interactions.Add(item); // Actual controls/callback owner before any native setters/subscriptions.
        sources.Invoke(() =>
        {
            Avalonia.Automation.AutomationProperties.SetName(item.Save, "Save generated interaction " + (ordinal + 1));
            Avalonia.Automation.AutomationProperties.SetName(item.Restore, "Restore generated interaction " + (ordinal + 1));
            Avalonia.Automation.AutomationProperties.SetName(item.Refresh, "Review generated interaction " + (ordinal + 1));
            item.Status.Text = "Review or save this interaction. Saving asks for separate approval in Home.";
            item.Restore.IsEnabled = false;
            item.Controls.Children.Add(item.Status); item.Controls.Children.Add(item.Save);
            item.Controls.Children.Add(item.Refresh); item.Controls.Children.Add(item.Restore);
            item.Save.Click += (_, _) => StartOriginalInteractionCommand(item, save: true, restore: false);
            item.Refresh.Click += (_, _) => StartOriginalInteractionCommand(item, save: false, restore: false);
            item.Restore.Click += (_, _) => StartOriginalInteractionCommand(item, save: false, restore: true);
            mount.Panel.Children.Add(item.Controls); return true;
        });
    }
    private void StartOriginalInteractionCommand(Interaction item, bool save, bool restore)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_work.IsRetiring || item.Busy || !_interactions.Any(actual => ReferenceEquals(actual, item)) || !item.Current()) return;
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this); _sourceCohorts.Add(sources);
        var raw = _work.RunAsync(async original =>
        {
            using var physical = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            item.Busy = true;
            try
            {
                PublishOriginalInteraction(item, sources, "Reviewing the current saved interaction…");
                var store = _interactionStore ?? throw new InvalidOperationException("The original interaction source is unavailable.");
                var observed = await sources.AwaitAsync(sources.Invoke(() => store.ReadOriginalInteractionWithinSourceAsync(item.Selection,
                    body => sources.Invoke(() => { body(); return true; }), raw => { sources.Track(raw); }, CancellationToken.None)));
                Dispatcher.UIThread.VerifyAccess();
                if (_work.IsRetiring || !item.Current()) return;
                if (!sources.Invoke(() => store.IsIssuedOriginalObservation(observed))) throw new UnauthorizedAccessException("The actual interaction source did not issue this observation.");
                item.Read = observed;
                if (restore)
                {
                    if (observed.State != CanonicalGeneratedUiOriginalReadState.Restorable || item.Request.TemplateKey == "custom")
                    { PublishOriginalInteraction(item, sources, observed.Detail); return; }
                    await RestoreOriginalInteraction(item, observed, sources);
                    PublishOriginalInteraction(item, sources, "Restored the audited saved interaction. Local changes need a new save approval.");
                }
                else if (save)
                {
                    var intent = await sources.AwaitAsync(sources.Invoke(() => store.PrepareOriginalSaveWithinSourceAsync(observed, item.OperationId,
                        body => sources.Invoke(() => { body(); return true; }), raw => { sources.Track(raw); }, CancellationToken.None)));
                    item.Intent = intent;
                    if (_work.IsRetiring || !item.Current()) return;
                    PublishOriginalInteraction(item, sources, "Review this save in Home. Closing this view detaches its observation.");
                    var delivery = await sources.CaptureOriginalAcquisitionAsync(() => store.StartOriginalSaveProcessWithinSourceAsync(intent,
                        body => sources.Invoke(() => { body(); return true; }), raw => { sources.Track(raw); }, CancellationToken.None),
                        actual =>
                        {
                            lock (_interactionGate) _interactionDeliveries.Add(actual); // Also capture late success after a source post-callback fault.
                            if (_work.IsRetiring) actual.RequestOriginalRetirement();
                        });
                    if (!sources.Invoke(() => store.IsIssuedOriginalSaveObservation(delivery))) throw new UnauthorizedAccessException("The same process did not issue the actual save delivery.");
                    if (_work.IsRetiring) delivery.RequestOriginalRetirement();
                    var completion = await sources.AwaitAsync(sources.Invoke(() => delivery.WaitOriginalCompletionAsync(CancellationToken.None)));
                    if (!sources.Invoke(() => delivery.IsIssuedOriginalCompletion(completion))) throw new UnauthorizedAccessException("The returned save outcome belongs to another delivery.");
                    // Delivery close is independently settled before publication.
                    var close = sources.Invoke(delivery.CloseAndDrainOriginalAsync); await sources.AwaitAsync(close);
                    if (completion.Kind == CanonicalGeneratedUiOriginalCompletionKind.Saved)
                    {
                        if (completion.Acknowledgment is null || !ReferenceEquals(completion.Acknowledgment.OriginalIntent, intent))
                            throw new UnauthorizedAccessException("No SAME acknowledged save was returned.");
                        item.OperationId = Guid.NewGuid(); item.Intent = null; item.Read = null;
                        PublishOriginalInteraction(item, sources, "Interaction saved with Home approval. Review it to restore later.");
                    }
                    else PublishOriginalInteraction(item, sources, completion.Kind == CanonicalGeneratedUiOriginalCompletionKind.DeclinedBeforeEffect ?
                        "Save declined in Home. This interaction was kept and no stored change was made." : "Save observation detached. Its actual process remains owned by the app.");
                }
                else PublishOriginalInteraction(item, sources, observed.Detail);
            }
            catch (Exception cause)
            {
                if (!_work.IsRetiring && item.Current()) PublishOriginalInteraction(item, sources, "Interaction could not settle: " + cause.Message);
                throw;
            }
            finally
            {
                item.Busy = false;
                if (!_work.IsRetiring && item.Current()) PublishOriginalInteraction(item, sources, null);
            }
        });
        lock (_interactionGate) _interactionCommands.Add(raw);
        sources.Track(raw); // The SAME admitted host task is already prepublished in _work.
    }
    private void PublishOriginalInteraction(Interaction item, CloudflareOriginalTaskLedger source, string? status)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_work.IsRetiring || !item.Current()) return;
        source.Invoke(() =>
        {
            if (_work.IsRetiring || !item.Current()) return false;
            if (status is not null) item.Status.Text = status;
            item.Save.IsEnabled = !item.Busy; item.Refresh.IsEnabled = !item.Busy;
            item.Restore.IsEnabled = !item.Busy && item.Request.TemplateKey != "custom" && item.Read?.State == CanonicalGeneratedUiOriginalReadState.Restorable;
            return true;
        });
    }
    private async Task RestoreOriginalInteraction(Interaction item, ICanonicalGeneratedUiOriginalObservation observed,
        CloudflareOriginalTaskLedger sources)
    {
        var origins = _interactionOrigins ?? throw new InvalidOperationException("The process origin is unavailable.");
        if (_surfaces.Count >= 64) throw new InvalidOperationException("The current view's original surface custody is full.");
        var document = sources.Invoke(() => item.Request.TemplateKey switch
        {
            "calculator" => _calculator.Create(item.Message.Message.ConversationId, "assistants", item.Request.Expression),
            "checklist" => _checklist.Create(item.Message.Message.ConversationId, "assistants", item.Request.Inputs),
            "data-grid" => _dataGrid.Create(item.Message.Message.ConversationId, "assistants", item.Request.Inputs),
            _ => throw new NotSupportedException("This canonical template restoration supplier is unavailable.")
        });
        // Standard template creation has no instance registration; custody starts with the actual presented restored document.
        var restored = await sources.AwaitAsync(sources.Invoke(() => origins.ComposeOriginalAuditedRestorationAsync(this, item.Selection,
            observed, document, body => sources.Invoke(() => { body(); return true; }), raw => { sources.Track(raw); }, CancellationToken.None)));
        Dispatcher.UIThread.VerifyAccess();
        if (_work.IsRetiring || !item.Current()) return;
        var child = sources.Invoke(() => new GenerativeUiSurface(_router, _instances, item.Current));
        _surfaces.Add(child); item.Mount.Surfaces.Add(child); item.Mount.Documents.Add((restored, false));
        sources.Invoke(() => { child.Present(restored); return true; });
        var registration = sources.Invoke(() => _instances.ObserveOriginalRegisteredDocument(restored));
        var selection = sources.Invoke(() => origins.CaptureOriginalRenderedSelection(this, item.Message, item.Ordinal, item.Request,
            restored, registration, child, null));
        item.Mount.OriginalSelections.Add(selection);
        var old = item.Surface; sources.Invoke(() => { old.RequestRetirement(); return true; });
        var close = sources.Invoke(old.CloseAndDrainAsync); await sources.AwaitAsync(close);
        Dispatcher.UIThread.VerifyAccess();
        if (_work.IsRetiring || !item.Current()) return;
        sources.Invoke(() =>
        {
            var position = item.Mount.Panel.Children.IndexOf(old);
            if (position < 0) throw new InvalidOperationException("The SAME rendered occurrence is no longer mounted.");
            item.Mount.Panel.Children.RemoveAt(position); item.Mount.Panel.Children.Insert(position, child); return true;
        });
        item.Surface = child; item.Selection = selection; item.Read = null; item.Intent = null; item.OperationId = Guid.NewGuid();
    }
    private async Task JoinOriginalInteractionDeliveriesAsync(CloudflareOriginalTaskLedger sources)
    {
        ICanonicalGeneratedUiOriginalSaveObservation[] deliveries;
        lock (_interactionGate) deliveries = _interactionDeliveries.ToArray();
        foreach (var actual in deliveries)
        {
            Task? close = null;
            try { close = sources.Invoke(actual.CloseAndDrainOriginalAsync); }
            catch (Exception cause) { sources.Retain(cause); }
            if (close is not null)
                try { await sources.AwaitAsync(close); } catch (Exception cause) { sources.Capture(close, cause); }
        }
    }
    private void RetireOriginalInteractionDeliveries()
    {
        ICanonicalGeneratedUiOriginalSaveObservation[] deliveries;
        lock (_interactionGate) deliveries = _interactionDeliveries.ToArray();
        var errors = new List<Exception>();
        foreach (var actual in deliveries) try { _work.RunCloseCallback(actual.RequestOriginalRetirement); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Actual generated interaction deliveries could not all retire.", errors);
    }
}
#endif
