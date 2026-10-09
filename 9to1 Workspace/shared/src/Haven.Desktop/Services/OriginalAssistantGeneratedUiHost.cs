#if !ANDROID
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.NativeUI;

namespace Haven.Desktop.Services;

/// <summary>Scoped adapter over the maintained renderer, router and instance store. No generated-app SQL writes.</summary>
internal sealed partial class OriginalAssistantGeneratedUiHost : IAssistantGeneratedUiHost
{
    private readonly AssistantsWorkspaceController _controller;
    private readonly GenerativeUiEventRouter _router;
    private readonly GenUiInstanceStore _instances;
    private readonly CalculatorTemplateRuntime _calculator;
    private readonly ChecklistTemplateRuntime _checklist;
    private readonly DataGridTemplateRuntime _dataGrid;
    private readonly CustomTemplateRuntime _custom;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly List<CloudflareOriginalTaskLedger> _sourceCohorts = [];
    private readonly List<Mount> _mounts = [];
    private readonly List<GenerativeUiSurface> _surfaces = [];
    private readonly List<Task> _childCloses = [];
    private CloudflareOriginalTaskLedger? _cleanupSources;

    internal OriginalAssistantGeneratedUiHost(AssistantsWorkspaceController controller,
        GenerativeUiEventRouter router, GenUiInstanceStore instances, CalculatorTemplateRuntime calculator,
        ChecklistTemplateRuntime checklist, DataGridTemplateRuntime dataGrid, CustomTemplateRuntime custom)
    {
        _controller = controller; _router = router; _instances = instances;
        _calculator = calculator; _checklist = checklist; _dataGrid = dataGrid; _custom = custom;
        _work = new(StopOriginalChildrenAsync, CleanupOriginalAsync);
    }

    public bool IsOriginalController(AssistantsWorkspaceController sameController) => ReferenceEquals(_controller, sameController);
    internal bool HasOriginalComposition(AssistantsWorkspaceController controller, GenerativeUiEventRouter router, GenUiInstanceStore instances) =>
        ReferenceEquals(_controller, controller) && ReferenceEquals(_router, router) && ReferenceEquals(_instances, instances);
    public Task? OriginalClose => _work.OriginalClose;
    public void DemandExternalOriginalRetirementJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _work.DemandExternalClose();
        foreach (var child in _surfaces.ToArray()) child.DemandOriginalExternalClose();
    }
    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }

    public Task<IAssistantGeneratedUiMount?> CreateOriginalMessageAsync(AssistantConversationBinding sameBinding,
        AssistantMessagePresentation sameMessage, Func<bool> originalPresentationCurrent,
        Action<IAssistantGeneratedUiMount> retainOriginalMount, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sameBinding); ArgumentNullException.ThrowIfNull(sameMessage);
        ArgumentNullException.ThrowIfNull(originalPresentationCurrent); ArgumentNullException.ThrowIfNull(retainOriginalMount);
        token.ThrowIfCancellationRequested();
        var parsed = GenUiChatDirectiveParser.Parse(sameMessage.Content);
        if (!parsed.HasDirective) return Task.FromResult<IAssistantGeneratedUiMount?>(null);
        return _work.RunAsync<IAssistantGeneratedUiMount?>(async original =>
        {
            using var owned = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this);
            _sourceCohorts.Add(sources); // Actual finite source cohort before any caller or bridge callback.
            bool Current() => !_work.IsRetiring && sources.Invoke(originalPresentationCurrent) &&
                ReferenceEquals(_controller.Snapshot.ConversationBinding, sameBinding);
            if (!Current()) return null;
            // The protected canonical bridge owns this exact READ. The persisted
            // payload, rather than a copied presentation ID, is the reopen source.
            OriginalAssistantGeneratedUiOriginOwner.MessageEvidence? originalMessage = null;
            if (_interactionOrigins is { } origins)
            {
                originalMessage = await sources.CaptureOriginalAcquisitionAsync(() => origins.ObserveOriginalMessageForHostAsync(this,
                    sameBinding, sameMessage.Id, body => sources.Invoke(() => { body(); return true; }),
                    raw => sources.Track(raw), CancellationToken.None), actual => { originalMessage = actual; });
                if (originalMessage.Message.Content != sameMessage.Content) return null;
            }
            else
            {
                var rawRead = sources.Invoke(() => _controller.OriginalCanonicalBridge.ReadConversationAsync(sameBinding, CancellationToken.None));
                var data = await sources.AwaitAsync(rawRead);
                var persisted = data.Messages.SingleOrDefault(message => message.Id == sameMessage.Id);
                if (data.Conversation.Id != sameBinding.Conversation.Id || persisted?.ConversationId != sameBinding.Conversation.Id ||
                    persisted is null || persisted.Content != sameMessage.Content) return null;
            }
            Dispatcher.UIThread.VerifyAccess();
            if (!Current()) return null;
            var mount = new Mount(sameMessage.Content, parsed.Error is null ? parsed.DisplayContent : sameMessage.Content);
            _mounts.Add(mount);
            sources.Invoke(() => { retainOriginalMount(mount); return true; }); // Before child/native setters or subscriptions.
            if (!Current()) return mount;
            if (parsed.Error is { } error) { mount.Status = "Generated UI was not opened: " + error; return mount; }
            if (parsed.Requests.Count > 4 || _surfaces.Count + parsed.Requests.Count > 64)
            { mount.Status = "This view's generated surface limit was reached. Reopen the conversation to continue."; mount.DisplayContent = sameMessage.Content; return mount; }
            if (parsed.Requests.Any(request => request.TemplateKey is not ("calculator" or "checklist" or "data-grid" or "custom")))
            { mount.Status = "This generated template is not available in Assistants yet."; mount.DisplayContent = sameMessage.Content; return mount; }
            for (var ordinal = 0; ordinal < parsed.Requests.Count; ordinal++)
            {
                var request = parsed.Requests[ordinal];
                if (!Current()) return mount;
                var document = request.TemplateKey switch
                {
                    "calculator" => _calculator.Create(sameBinding.Conversation.Id, "assistants", request.Expression),
                    "checklist" => _checklist.Create(sameBinding.Conversation.Id, "assistants", request.Inputs),
                    "data-grid" => _dataGrid.Create(sameBinding.Conversation.Id, "assistants", request.Inputs),
                    "custom" => _custom.Create(sameBinding.Conversation.Id, "assistants", request.Inputs),
                    _ => throw new InvalidOperationException("The template is unavailable.")
                };
                mount.Documents.Add((document, request.TemplateKey == "custom")); // Exact registrations before validation/presentation.
                var validation = GenerativeUiContractValidator.Validate(document);
                if (validation.Count != 0)
                { mount.Status = "Generated UI was not opened: " + string.Join(" ", validation); mount.DisplayContent = sameMessage.Content; return mount; }
                if (Components(document.Root).Any(component => component.Actions.Any(action =>
                    action.Route != GenUiRouteKind.Local || action.RequiresPermission || action.RiskClass != CapabilityRiskClass.Low)))
                { mount.Status = "External generated actions require their canonical supplier."; mount.DisplayContent = sameMessage.Content; return mount; }
                if (!Current()) return mount;
                var child = sources.Invoke(() => new GenerativeUiSurface(_router, _instances, Current));
                _surfaces.Add(child); mount.Surfaces.Add(child); // Actual child before Present can publish/raise store callbacks.
                if (_work.IsRetiring) child.RequestRetirement();
                if (!Current()) return mount;
                sources.Invoke(() => { child.Present(document); return true; });
                if (originalMessage is { } messageEvidence && _interactionOrigins is { } actualOrigins)
                    sources.Invoke(() =>
                    {
                        var registration = _instances.ObserveOriginalRegisteredDocument(document);
                        var selection = actualOrigins.CaptureOriginalRenderedSelection(this, messageEvidence, ordinal, request,
                            document, registration, child, request.TemplateKey == "custom" ? _custom : null);
                        mount.OriginalSelections.Add(selection);
                        AttachOriginalInteraction(mount, request, messageEvidence, ordinal, selection, child, Current, sources); return true;
                    });
                if (!Current()) return mount;
                sources.Invoke(() => { mount.Panel.Children.Add(child); return true; });
            }
            mount.Status = _interactionStore is null ? "Generated UI from this saved message. Interaction state lasts for this view; saving requires its protected destination action." :
                "Generated UI from this saved message. Save interaction requests separate Home approval. App artifact conversion is unavailable.";
            return mount;
        });
    }

    private static IEnumerable<GenUiComponent> Components(GenUiComponent root)
    { yield return root; foreach (var child in root.Children) foreach (var component in Components(child)) yield return component; }

    private Task StopOriginalChildrenAsync()
    {
        var failures = new List<Exception>();
        try { RetireOriginalInteractionDeliveries(); } catch (Exception cause) { failures.Add(cause); }
        foreach (var child in _surfaces.ToArray())
        {
            try { _work.RunCloseCallback(child.RequestRetirement); }
            catch (Exception cause) { failures.Add(cause); }
            finally { if (child.OriginalClose is { } close) _childCloses.Add(close); }
        }
        return failures.Count == 0 ? Task.CompletedTask : Task.FromException(new AggregateException("Original generated children could not all retire.", failures));
    }

    private Task CleanupOriginalAsync()
    {
        var sources = _cleanupSources = new(); sources.BindOriginalOwner(this);
        return sources.RunToOriginalSettlementAsync(async () =>
        {
            foreach (var child in _surfaces.ToArray())
            {
                Task? raw = null;
                try { raw = sources.Invoke(child.CloseAndDrainAsync); _childCloses.Add(raw); }
                catch (Exception cause) { sources.Retain(cause); }
            }
            foreach (var raw in _childCloses.Distinct<Task>(ReferenceEqualityComparer.Instance))
                try { await sources.AwaitAsync(raw); } catch (Exception cause) { sources.Capture(raw, cause); }
            await JoinOriginalInteractionDeliveriesAsync(sources);
            // Admitted host bodies and child callbacks are terminal before this
            // snapshot. A successful child close does not waive a swallowed or
            // post-callback fault in the SAME source cohort that acquired it.
            foreach (var cohort in _sourceCohorts.ToArray())
            {
                Task? observation = null;
                try
                {
                    observation = sources.Invoke(cohort.ObserveAllOriginalTasksAsync);
                    await sources.AwaitAsync(observation);
                }
                catch (Exception cause) { sources.Capture(observation, cause); }
                foreach (var cause in cohort.OriginalErrors) sources.Retain(cause);
            }
            if (sources.OriginalErrors.Count != 0)
                throw new AggregateException("The actual generated source and child originals remain unresolved.", sources.OriginalErrors);
            var detach = sources.Invoke(() => Dispatcher.UIThread.InvokeAsync(() => _work.RunCloseCallback(() =>
            {
                foreach (var mount in _mounts)
                    foreach (var (document, custom) in mount.Documents)
                    {
                        if (_interactionOrigins?.OwnsOriginalDocument(this, document) == true) continue;
                        if (custom && !_custom.ReleaseOriginalInstanceActions(document))
                            throw new InvalidOperationException("The generated custom document lost its original action registration.");
                        _instances.Remove(document.Origin.InstanceId); // Only after SAME renderer/action originals joined.
                    }
            })).GetTask());
            await sources.AwaitAsync(detach);
        });
    }

    private sealed class Mount(string source, string display) : IAssistantGeneratedUiMount
    {
        internal readonly StackPanel Panel = new() { Spacing = 8 };
        internal readonly List<(GenUiDocument Document, bool Custom)> Documents = [];
        internal readonly List<GenerativeUiSurface> Surfaces = [];
        internal readonly List<ICanonicalGeneratedUiOriginalSelection> OriginalSelections = [];
        public Control View => Panel;
        public string OriginalSourceContent { get; } = source;
        public string DisplayContent { get; internal set; } = display;
        public string Status { get; internal set; } = "";
    }
}
#endif
