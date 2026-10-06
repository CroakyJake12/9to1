using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Services;
using Haven.UI;
using Haven.UI.Components;
using Container = Haven.UI.Components.Container;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Catalog;

/// <summary>
/// Haven-native Agents management surface. Agent definitions and all persistence remain
/// owned by CatalogPageViewModel/ICatalogRepository; this scene projects that real state
/// through Haven.UI and uses DynamicUI for the runtime-owned repeated card list.
/// </summary>
internal sealed class AgentsHavenScene : IDisposable, IAsyncDisposable,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private const int MaxActivityJsonCharacters = 256 * 1024;
    private const int MaxActivityEventCount = 512;
    private const int VisibleActivityEventCount = 8;
    private static readonly JsonSerializerOptions ActivityJsonOptions = new() { MaxDepth = 16 };

    private sealed record ActivityLogEvent(string? Title, bool? Succeeded, TimeSpan? Duration, DateTimeOffset? Timestamp);

    private readonly CatalogPageViewModel _viewModel;
    private readonly DynamicUI _dynamicUi;
    private readonly AgentTaskRuntimeService? _runtime;
    private AgentRun? _latestRun;
    private IReadOnlyList<AgentRun> _recentRuns = [];
    private Guid? _pendingDeleteId;
    private bool _disposed;
    private bool _constructing = true;
    private readonly DesktopOriginalWorkLifetime _originalWork;
    private readonly Dictionary<DynamicUIItem, List<Action>> _cardSubscriptions = [];
    private readonly object _observationGate = new();
    private readonly List<AgentsOriginalObservationCapture> _observations = [];
    internal Task? OriginalClose => _originalWork.OriginalClose;

    public AgentsHavenScene(CatalogPageViewModel viewModel, AgentTaskRuntimeService? runtime = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _runtime = runtime;
        if (viewModel.Kind != CatalogPageKind.Agents)
            throw new ArgumentException("AgentsHavenScene requires the Agents catalogue view-model.", nameof(viewModel));
        _originalWork = new DesktopOriginalWorkLifetime(StopOriginalAsync, CleanupOriginalAsync);

        Root = new Page
        {
            Name = "Agents.Root",
            Layout = HavenLayout.Grid,
            Columns = "1fr",
            Rows = "auto auto auto auto 1fr"
        };
        Set(Root, HavenProperties.Padding, HavenThickness.Parse("26px 30px"));
        Set(Root, HavenProperties.Gap, HavenLength.Px(14));
        Set(Root, HavenProperties.Background, "Transparent");

        var header = new Container { Name = "Agents.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto Auto", Rows = "auto" };
        Set(header, HavenProperties.Gap, HavenLength.Px(8));
        var titles = new Container { Layout = HavenLayout.Vertical };
        Set(titles, HavenProperties.Gap, HavenLength.Px(4));
        titles.Add(new HavenText("Agents") { Name = "Agents.Title", Level = TextLevel.H1 });
        var subtitle = new HavenText("Create and manage saved agent definitions used by Chat and Go.") { Name = "Agents.Subtitle", Level = TextLevel.Paragraph };
        Set(subtitle, HavenProperties.Foreground, "TextSecondary");
        titles.Add(subtitle);
        header.Add(titles);

        RefreshButton = new HavenButton { Name = "Agents.Refresh", Content = "Refresh", Variant = ButtonVariant.Secondary };
        RefreshButton.Accessibility.AccessibleName = "Refresh agents";
        Set(RefreshButton, HavenProperties.Column, 1);
        header.Add(RefreshButton);
        CreateToggleButton = new HavenButton { Name = "Agents.Create.Toggle", Content = "Create agent", Variant = ButtonVariant.Primary };
        CreateToggleButton.Accessibility.AccessibleName = "Create agent";
        Set(CreateToggleButton, HavenProperties.Column, 2);
        header.Add(CreateToggleButton);
        Root.Add(header);

        var runtimeNotice = new Container { Name = "Agents.RuntimeNotice", Layout = HavenLayout.Vertical };
        Set(runtimeNotice, HavenProperties.Row, 1);
        Set(runtimeNotice, HavenProperties.Width, HavenLength.Percent(100));
        Set(runtimeNotice, HavenProperties.Background, "SurfaceRaised");
        Set(runtimeNotice, HavenProperties.BorderColor, "Border");
        Set(runtimeNotice, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(runtimeNotice, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        Set(runtimeNotice, HavenProperties.Padding, HavenThickness.Parse("12px 14px"));
        Set(runtimeNotice, HavenProperties.Gap, HavenLength.Px(3));
        var runtimeTitle = new HavenText("Agent runtime") { Name = "Agents.RuntimeNotice.Title", Level = TextLevel.H4 };
        runtimeNotice.Add(runtimeTitle);
        ExecutionStatusText = new HavenText(runtime is null
            ? "Agent runtime is unavailable in this host."
            : "Ready. Enter a task, then run it with any enabled Agent.") { Name = "Agents.Execution.Status", Level = TextLevel.Paragraph };
        ExecutionStatusText.Accessibility.AccessibleName = "Agent execution status";
        Set(ExecutionStatusText, HavenProperties.Foreground, "TextSecondary");
        runtimeNotice.Add(ExecutionStatusText);
        RunTaskInput = InputField("Agents.Execution.Task", "Task for this agent");
        RunTaskInput.Multiline = true;
        RunTaskInput.SubmitOnEnter = false;
        Set(RunTaskInput, HavenProperties.MinHeight, HavenLength.Px(72));
        runtimeNotice.Add(RunTaskInput);
        RunResourceInput = InputField("Agents.Execution.Resource", "Optional resource, file, URL, or Haven item reference");
        runtimeNotice.Add(RunResourceInput);
        var runActions = new Container { Layout = HavenLayout.Horizontal };
        Set(runActions, HavenProperties.Gap, HavenLength.Px(8));
        CancelLatestButton = new HavenButton { Name = "Agents.Execution.Cancel", Content = "Cancel active run", Variant = ButtonVariant.Secondary };
        RetryLatestButton = new HavenButton { Name = "Agents.Execution.Retry", Content = "Retry latest", Variant = ButtonVariant.Secondary };
        CancelLatestButton.Accessibility.AccessibleName = "Cancel active agent run";
        RetryLatestButton.Accessibility.AccessibleName = "Retry latest agent run";
        runActions.Add(CancelLatestButton);
        runActions.Add(RetryLatestButton);
        runtimeNotice.Add(runActions);
        var recentTitle = new HavenText("Recent runs") { Level = TextLevel.H4 };
        runtimeNotice.Add(recentTitle);
        RecentRunsText = new HavenText("No Agent runs yet.") { Name = "Agents.Execution.RecentRuns", Level = TextLevel.Caption };
        RecentRunsText.Accessibility.AccessibleName = "Recent Agent runs";
        Set(RecentRunsText, HavenProperties.Foreground, "TextSecondary");
        runtimeNotice.Add(RecentRunsText);
        var latestActivityTitle = new HavenText("Latest run activity") { Level = TextLevel.H4 };
        runtimeNotice.Add(latestActivityTitle);
        LatestActivityText = new HavenText("No Agent run selected.") { Name = "Agents.Execution.LatestActivity", Level = TextLevel.Caption };
        LatestActivityText.Accessibility.AccessibleName = "Latest Agent run activity log";
        Set(LatestActivityText, HavenProperties.Foreground, "TextSecondary");
        runtimeNotice.Add(LatestActivityText);
        Root.Add(runtimeNotice);

        Creator = BuildCreator();
        Set(Creator, HavenProperties.Row, 2);
        Root.Add(Creator);

        StatusText = new HavenText { Name = "Agents.Status", Level = TextLevel.Caption };
        Set(StatusText, HavenProperties.Row, 3);
        Set(StatusText, HavenProperties.Foreground, "TextSecondary");
        Root.Add(StatusText);

        AgentCards = new DynamicUIRuntime { Name = "AgentCards", Layout = HavenLayout.Vertical };
        Set(AgentCards, HavenProperties.Row, 4);
        Set(AgentCards, HavenProperties.Width, HavenLength.Percent(100));
        Set(AgentCards, HavenProperties.Height, HavenLength.Percent(100));
        Set(AgentCards, HavenProperties.Gap, HavenLength.Px(10));
        Set(AgentCards, HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Add(AgentCards);

        var templates = HavenDynamicUITemplateCatalog.FromAssembly(typeof(AgentsHavenScene).Assembly);
        _dynamicUi = new DynamicUI(Root, templates);

        RefreshButton.Invoked += OnRefreshInvoked;
        CreateToggleButton.Invoked += OnCreateToggleInvoked;
        BuildWithAiButton.Invoked += OnBuildWithAiInvoked;
        SaveButton.Invoked += OnSaveInvoked;
        BuilderPromptInput.Invalidated += OnDraftInvalidated;
        NameInput.Invalidated += OnDraftInvalidated;
        DescriptionInput.Invalidated += OnDraftInvalidated;
        InstructionsInput.Invalidated += OnDraftInvalidated;
        ModelInput.Invalidated += OnDraftInvalidated;
        CapabilitiesInput.Invalidated += OnDraftInvalidated;
        PermissionProfileInput.Invalidated += OnDraftInvalidated;
        SandboxProfileInput.Invalidated += OnDraftInvalidated;
        KnowledgeResourcesInput.Invalidated += OnDraftInvalidated;
        MemoryModeInput.Invalidated += OnDraftInvalidated;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Items.CollectionChanged += OnItemsCollectionChanged;
        CancelLatestButton.Invoked += OnCancelLatestInvoked;
        RetryLatestButton.Invoked += OnRetryLatestInvoked;
        if (_runtime is not null) _runtime.RunChanged += OnRunChanged;

        SyncDraftFromViewModel();
        RefreshChrome();
        RefreshCards();
        _constructing = false;
        _ = RefreshRunsAsync();
    }

    public Page Root { get; }
    public HavenButton RefreshButton { get; }
    public HavenButton CreateToggleButton { get; }
    public Container Creator { get; }
    public Input BuilderPromptInput { get; private set; } = null!;
    public HavenButton BuildWithAiButton { get; private set; } = null!;
    public Input NameInput { get; private set; } = null!;
    public Input DescriptionInput { get; private set; } = null!;
    public Input InstructionsInput { get; private set; } = null!;
    public Input ModelInput { get; private set; } = null!;
    public Input CapabilitiesInput { get; private set; } = null!;
    public Input PermissionProfileInput { get; private set; } = null!;
    public Input SandboxProfileInput { get; private set; } = null!;
    public Input KnowledgeResourcesInput { get; private set; } = null!;
    public Input MemoryModeInput { get; private set; } = null!;
    public HavenButton SaveButton { get; private set; } = null!;
    public HavenText StatusText { get; }
    public HavenText ExecutionStatusText { get; }
    public Input RunTaskInput { get; }
    public Input RunResourceInput { get; }
    public HavenButton CancelLatestButton { get; }
    public HavenButton RetryLatestButton { get; }
    public HavenText RecentRunsText { get; }
    public HavenText LatestActivityText { get; }
    public DynamicUIRuntime AgentCards { get; }

    private Container BuildCreator()
    {
        var panel = new Container { Name = "Agents.Creator", Layout = HavenLayout.Vertical };
        Set(panel, HavenProperties.Width, HavenLength.Percent(100));
        Set(panel, HavenProperties.Background, "SurfaceRaised");
        Set(panel, HavenProperties.BorderColor, "Border");
        Set(panel, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(panel, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        Set(panel, HavenProperties.Padding, HavenThickness.Parse("16px"));
        Set(panel, HavenProperties.Gap, HavenLength.Px(10));
        Set(panel, HavenProperties.Shadow, "Card");

        var builderRow = new Container { Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "auto" };
        Set(builderRow, HavenProperties.Gap, HavenLength.Px(8));
        BuilderPromptInput = InputField("Agents.Creator.BuilderPrompt", "Describe the assistant you want Haven to create");
        builderRow.Add(BuilderPromptInput);
        BuildWithAiButton = new HavenButton { Name = "Agents.Creator.BuildWithAi", Content = "Build with AI", Variant = ButtonVariant.Secondary };
        BuildWithAiButton.Accessibility.AccessibleName = "Draft agent instructions with AI";
        Set(BuildWithAiButton, HavenProperties.Column, 1);
        builderRow.Add(BuildWithAiButton);
        panel.Add(builderRow);

        var identityRow = new Container { Layout = HavenLayout.Grid, Columns = "1fr 1.5fr", Rows = "auto" };
        Set(identityRow, HavenProperties.Gap, HavenLength.Px(8));
        NameInput = InputField("Agents.Creator.Name", "Name");
        identityRow.Add(NameInput);
        DescriptionInput = InputField("Agents.Creator.Description", "Short description");
        Set(DescriptionInput, HavenProperties.Column, 1);
        identityRow.Add(DescriptionInput);
        panel.Add(identityRow);

        InstructionsInput = InputField("Agents.Creator.Instructions", "System instructions");
        InstructionsInput.Multiline = true;
        InstructionsInput.SubmitOnEnter = false;
        Set(InstructionsInput, HavenProperties.MinHeight, HavenLength.Px(112));
        panel.Add(InstructionsInput);

        CapabilitiesInput = InputField("Agents.Creator.Capabilities", "Capability keys, comma-separated (for example web-search)");
        panel.Add(CapabilitiesInput);
        var permissionNote = new HavenText("Capability access is an allowlist. Haven's global sandbox and permission settings always remain authoritative.") { Level = TextLevel.Caption };
        Set(permissionNote, HavenProperties.Foreground, "TextSecondary");
        panel.Add(permissionNote);

        PermissionProfileInput = InputField("Agents.Creator.PermissionProfile", "Permission profile reference (optional)");
        SandboxProfileInput = InputField("Agents.Creator.SandboxProfile", "Sandbox profile reference (optional)");
        KnowledgeResourcesInput = InputField("Agents.Creator.KnowledgeResources", "Knowledge/resource references, comma-separated");
        MemoryModeInput = InputField("Agents.Creator.MemoryMode", "Memory default: session, persistent, or none");
        panel.Add(PermissionProfileInput); panel.Add(SandboxProfileInput); panel.Add(KnowledgeResourcesInput); panel.Add(MemoryModeInput);

        var footer = new Container { Layout = HavenLayout.Grid, Columns = "Auto 1fr Auto", Rows = "auto" };
        Set(footer, HavenProperties.Gap, HavenLength.Px(8));
        var modelLabel = new HavenText("Preferred model") { Level = TextLevel.Caption };
        Set(modelLabel, HavenProperties.Foreground, "TextSecondary");
        Set(modelLabel, HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        footer.Add(modelLabel);
        ModelInput = InputField("Agents.Creator.Model", "Use current/default model");
        Set(ModelInput, HavenProperties.Column, 1);
        footer.Add(ModelInput);
        SaveButton = new HavenButton { Name = "Agents.Creator.Save", Content = "Create agent", Variant = ButtonVariant.Primary };
        SaveButton.Accessibility.AccessibleName = "Create agent";
        Set(SaveButton, HavenProperties.Column, 2);
        footer.Add(SaveButton);
        panel.Add(footer);
        return panel;
    }

    internal Task RefreshAsync() => AdmitOriginal(_ => RefreshOriginalAsync());

    private async Task RefreshOriginalAsync()
    {
        await AwaitOriginalAsync(_viewModel.RefreshCommand.ExecuteAsync());
        RefreshChrome();
        RefreshCards();
    }

    internal Task DraftAgentAsync() => AdmitOriginal(_ => DraftAgentOriginalAsync());

    private async Task DraftAgentOriginalAsync()
    {
        SyncDraftToViewModel();
        await AwaitOriginalAsync(_viewModel.BuildWithAiCommand.ExecuteAsync());
        SyncDraftFromViewModel();
        RefreshChrome();
    }

    internal Task CreateAgentAsync() => AdmitOriginal(_ => CreateAgentOriginalAsync());

    private async Task CreateAgentOriginalAsync()
    {
        SyncDraftToViewModel();
        if (_viewModel.IsEditingAgent)
            await AwaitOriginalAsync(_viewModel.SaveAgentEditsAsync());
        else
            await AwaitOriginalAsync(_viewModel.CreateCommand.ExecuteAsync());
        SyncDraftFromViewModel();
        RefreshChrome();
        RefreshCards();
    }

    internal Task<bool> EditAgentAsync(CatalogCardViewModel card) => AdmitOriginal(_ => EditAgentOriginalAsync(card));

    private async Task<bool> EditAgentOriginalAsync(CatalogCardViewModel card)
    {
        _pendingDeleteId = null;
        UpdateDeleteLabels();
        var opened = await AwaitOriginalAsync(_viewModel.BeginAgentEditAsync(card));
        if (!opened) return false;
        SyncDraftFromViewModel();
        RefreshChrome();
        return true;
    }

    internal Task DuplicateAgentAsync(CatalogCardViewModel card) => AdmitOriginal(_ => DuplicateAgentOriginalAsync(card));

    private async Task DuplicateAgentOriginalAsync(CatalogCardViewModel card)
    {
        _pendingDeleteId = null;
        UpdateDeleteLabels();
        await AwaitOriginalAsync(_viewModel.DuplicateCommand.ExecuteAsync(card));
        RefreshCards();
    }

    internal Task<bool> DeleteAgentAsync(CatalogCardViewModel card) => AdmitOriginal(_ => DeleteAgentOriginalAsync(card));

    private async Task<bool> DeleteAgentOriginalAsync(CatalogCardViewModel card)
    {
        if (card.IsBuiltIn) return false;
        if (_pendingDeleteId != card.Id)
        {
            _pendingDeleteId = card.Id;
            UpdateDeleteLabels();
            return false;
        }

        _pendingDeleteId = null;
        await AwaitOriginalAsync(_viewModel.DeleteCommand.ExecuteAsync(card));
        RefreshCards();
        return true;
    }

    private void RefreshCards()
    {
        var expected = _viewModel.Items.Select(card => card.Id.ToString("N")).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in AgentCards.Items.Where(item => !expected.Contains(item.InstanceID)).Select(item => item.InstanceID).ToArray())
        {
            if (_dynamicUi.TryGetItem("AgentCards", stale, out var removed)) UnwireOriginalCard(removed);
            PublishOriginal(() => _dynamicUi.DeleteItem("AgentCards", stale));
        }

        for (var index = 0; index < _viewModel.Items.Count; index++)
        {
            var card = _viewModel.Items[index];
            var id = card.Id.ToString("N");
            if (!_dynamicUi.TryGetItem("AgentCards", id, out var item))
            {
                item = ReadOriginal(() => _dynamicUi.CreateItem("AgentCatalogCard", "AgentCards", id, ValuesFor(card), index));
                WireCard(item, card);
            }
            else
            {
                PublishOriginal(() => item.SetVariables(ValuesFor(card)));
                var currentIndex = AgentCards.Items.ToList().IndexOf(item);
                if (currentIndex != index) PublishOriginal(() => _dynamicUi.MoveItem("AgentCards", id, index));
            }

            UpdateCardAccessibility(item, card);
            Visible(item.GetComponent<HavenButton>("Edit"), !card.IsBuiltIn);
            Visible(item.GetComponent<HavenButton>("Delete"), !card.IsBuiltIn);
        }
        RefreshChrome();
    }

    private void WireCard(DynamicUIItem item, CatalogCardViewModel card)
    {
        var edit = item.GetComponent<HavenButton>("Edit");
        WireOriginalCardCallback(item, edit, () => EditAgentAsync(card));

        var duplicate = item.GetComponent<HavenButton>("Duplicate");
        WireOriginalCardCallback(item, duplicate, () => DuplicateAgentAsync(card));

        var run = item.GetComponent<HavenButton>("Run");
        WireOriginalCardCallback(item, run, () => RunAgentAsync(card));
        var toggle = item.GetComponent<HavenButton>("Toggle");
        WireOriginalCardCallback(item, toggle, () => ToggleAgentAsync(card));
        var delete = item.GetComponent<HavenButton>("Delete");
        WireOriginalCardCallback(item, delete, () => DeleteAgentAsync(card));
    }

    private void UpdateCardAccessibility(DynamicUIItem item, CatalogCardViewModel card)
    {
        PublishOriginal(() =>         item.GetComponent<HavenButton>("Run").Accessibility.AccessibleName = "Run " + card.Name);
        PublishOriginal(() =>         item.GetComponent<HavenButton>("Toggle").Accessibility.AccessibleName = (card.IsEnabled ? "Disable " : "Enable ") + card.Name);
        PublishOriginal(() =>         item.GetComponent<HavenButton>("Edit").Accessibility.AccessibleName = "Edit " + card.Name);
        PublishOriginal(() =>         item.GetComponent<HavenButton>("Duplicate").Accessibility.AccessibleName = "Duplicate " + card.Name);
        PublishOriginal(() =>         item.GetComponent<HavenButton>("Delete").Accessibility.AccessibleName = "Delete " + card.Name);
    }

    private Dictionary<string, object?> ValuesFor(CatalogCardViewModel card) => new(StringComparer.Ordinal)
    {
        ["NAME"] = card.Name,
        ["DESCRIPTION"] = card.Description,
        ["MODEL"] = string.IsNullOrWhiteSpace(card.Meta) ? "default" : card.Meta,
        ["BADGE"] = card.IsBuiltIn ? (card.IsEnabled ? "BUILT-IN" : "BUILT-IN · OFF") : (card.IsEnabled ? "CUSTOM" : "CUSTOM · OFF"),
        ["ENABLE_LABEL"] = card.IsEnabled ? "Disable" : "Enable",
        ["DELETE_LABEL"] = _pendingDeleteId == card.Id ? "Confirm delete" : "Delete"
    };

    internal Task<AgentRun?> RunAgentAsync(CatalogCardViewModel card) => AdmitOriginal(_ => RunAgentOriginalAsync(card));

    private async Task<AgentRun?> RunAgentOriginalAsync(CatalogCardViewModel card)
    {
        if (_runtime is null)
        {
            SetContent(ExecutionStatusText, "Agent runtime is unavailable in this host.");
            return null;
        }
        if (!card.IsEnabled)
        {
            SetContent(ExecutionStatusText, $"{card.Name} is disabled. Enable it before running.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(RunTaskInput.Text))
        {
            SetContent(ExecutionStatusText, "Enter a task before starting the Agent.");
            return null;
        }

        var original = _originalWork.Executing ?? throw new InvalidOperationException("No original Agent scene invocation owns this run.");
        var originalAgentId = card.Id;
        var originalTask = RunTaskInput.Text.Trim();
        var originalResource = RunResourceInput.Text;
        SetContent(ExecutionStatusText, $"Starting {card.Name}…");
        original.DemandPublication();
        try
        {
            AgentRun? run;
            if ((object)_runtime is IAgentRunOriginalObservationSource observationSource)
            {
                // The same service privately owns the business producer. This view
                // retains only its actually issued wait/retirement observation.
                run = await AwaitIssuedOriginalObservationAsync(original, observationSource, () =>
                    observationSource.StartObservedOriginalRun(originalAgentId, originalTask,
                        CancellationToken.None, resourceReference: originalResource));
            }
            else
            {
                // Compatibility for a host without the actual optional issuer:
                // retain/join the unchanged business Task; close may remain pending.
                var actual = _runtime.RunAsync(originalAgentId, originalTask, CancellationToken.None, resourceReference: originalResource);
                run = await AwaitOriginalAsync(actual);
            }
            if (run is not null) ApplyRunUpdate(run);
            return run;
        }
        catch (Exception error)
        {
            original.Retain(error);
            if (original.IsPublicationCurrent)
                SetContent(ExecutionStatusText, "Agent run unavailable or failed: " + error.Message);
            original.ThrowRetained();
            throw;
        }
    }

    internal Task ToggleAgentAsync(CatalogCardViewModel card) => AdmitOriginal(_ => ToggleAgentOriginalAsync(card));

    private async Task ToggleAgentOriginalAsync(CatalogCardViewModel card)
    {
        await AwaitOriginalAsync(_viewModel.SetAgentEnabledAsync(card, !card.IsEnabled));
        RefreshCards();
    }

    internal Task RefreshRunsAsync() => AdmitOriginal(_ => RefreshRunsOriginalAsync());

    private async Task RefreshRunsOriginalAsync()
    {
        if (_runtime is null)
        {
            RefreshExecutionStatus();
            return;
        }
        SetContent(ExecutionStatusText, "Loading Agent run history…");
        var original = _originalWork.Executing ?? throw new InvalidOperationException("No original Agent scene invocation owns this history read.");
        try
        {
            var actual = _runtime.GetRecentAsync(8, CancellationToken.None);
            var recent = await AwaitOriginalAsync(actual);
            _recentRuns = recent;
            _latestRun = recent.FirstOrDefault();
            RefreshExecutionStatus();
        }
        catch (Exception error)
        {
            original.Retain(error);
            if (original.IsPublicationCurrent)
                SetContent(ExecutionStatusText, "Agent run history unavailable: " + error.Message);
            original.ThrowRetained();
            throw;
        }
    }

    private void RefreshExecutionStatus()
    {
        SetContent(LatestActivityText, FormatActivityLog(_latestRun));
        if (_runtime is null)
        {
            SetContent(ExecutionStatusText, "Agent runtime is unavailable in this host.");
            Enabled(CancelLatestButton, false);
            Enabled(RetryLatestButton, false);
            RefreshRecentRunsText();
            return;
        }

        if (_latestRun is null)
        {
            SetContent(ExecutionStatusText, "Ready. Enter a task, then run it with any enabled Agent.");
            Enabled(CancelLatestButton, false);
            Enabled(RetryLatestButton, false);
            RefreshRecentRunsText();
            return;
        }

        var detail = _latestRun.Status switch
        {
            AgentRunStatus.Completed => string.IsNullOrWhiteSpace(_latestRun.Result) ? "Completed." : _latestRun.Result,
            AgentRunStatus.Failed => "Failed · " + _latestRun.Error,
            AgentRunStatus.Cancelled => "Cancelled.",
            AgentRunStatus.Suspended => "Suspended · " + (string.IsNullOrWhiteSpace(_latestRun.Error)
                ? "A recorded review response does not resume this Task/Run." : _latestRun.Error),
            AgentRunStatus.Running => "Running…",
            AgentRunStatus.Queued => "Queued…",
            _ => "Unknown recorded status."
        };
        var canonical = _latestRun.CanonicalTask is { } binding
            ? $" · Task {binding.TaskId:N} · Run {binding.ExecutionId:N} · saved revision {binding.PersistenceRevision}"
            : string.Empty;
        var resource = string.IsNullOrWhiteSpace(_latestRun.ResourceReference) ? string.Empty : $" · {_latestRun.ResourceReference}";
        SetContent(ExecutionStatusText, $"{_latestRun.AgentName} · {_latestRun.Status} · {_latestRun.ProgressPercent}%{resource}{canonical} · {detail}");
        Enabled(CancelLatestButton, _latestRun.Status is AgentRunStatus.Queued or AgentRunStatus.Running);
        Enabled(RetryLatestButton, _latestRun.Status == AgentRunStatus.Suspended
            && _runtime.HasOriginalUnstartedRetrySource(_latestRun.Id));
        RefreshRecentRunsText();
    }

    private void RefreshRecentRunsText()
    {
        SetContent(RecentRunsText, _recentRuns.Count == 0
            ? "No Agent runs yet."
            : string.Join(Environment.NewLine, _recentRuns.Take(6).Select(run =>
                $"{run.AgentName} · {run.Status} · {run.ProgressPercent}% · {Short(run.Task, 72)}")));
    }

    internal static string FormatActivityLog(AgentRun? run)
    {
        if (run is null)
            return "No Agent run selected.";
        if (string.IsNullOrWhiteSpace(run.ActivityJson))
            return "No tool events were recorded for this run.";
        if (run.ActivityJson.Length > MaxActivityJsonCharacters)
            return "Saved activity log is too large to display.";

        ActivityLogEvent?[] activities;
        try
        {
            using var document = JsonDocument.Parse(run.ActivityJson, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind is JsonValueKind.Array or JsonValueKind.Null)
            {
                // Keep the original legacy array/null projection and all its bounds.
                activities = root.Deserialize<ActivityLogEvent?[]>(ActivityJsonOptions) ?? [];
            }
            else
            {
                if (!TryReadKnownActivityArray(root, run.Id, out var originalActivities))
                    return "Saved activity log could not be read.";
                activities = originalActivities.Deserialize<ActivityLogEvent?[]>(ActivityJsonOptions) ?? [];
            }
        }
        catch (JsonException)
        {
            return "Saved activity log could not be read.";
        }

        if (activities.Length == 0)
            return "No tool events were recorded for this run.";
        if (activities.Length > MaxActivityEventCount || activities.Any(activity =>
                activity is null || !activity.Succeeded.HasValue || activity.Duration is null || activity.Duration < TimeSpan.Zero || activity.Timestamp is null))
            return "Saved activity log contains invalid events.";

        var visible = activities.TakeLast(VisibleActivityEventCount).ToArray();
        var lines = visible.Select(activity =>
        {
            var entry = activity!;
            var title = SafeActivityTitle(entry.Title);
            var timestamp = FormatActivityTimestamp(entry.Timestamp!.Value);
            var outcome = entry.Succeeded!.Value ? "Succeeded" : "Needs attention";
            return $"{timestamp} · {outcome} · {title} · {entry.Duration!.Value.TotalMilliseconds:0} ms";
        }).ToList();

        var earlierCount = activities.Length - visible.Length;
        if (earlierCount > 0)
            lines.Insert(0, $"{earlierCount} earlier events omitted.");

        return string.Join(Environment.NewLine, lines);
    }

    private static bool TryReadKnownActivityArray(JsonElement root, Guid actualRunId, out JsonElement activities)
    {
        activities = default;
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() != 1)
            || !root.TryGetProperty("Activities", out var originalActivities)
            || originalActivities.ValueKind != JsonValueKind.Array) return false;

        if (root.TryGetProperty("SchemaVersion", out var schema))
        {
            if (schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version)
                || version != AgentActivityObservation.CurrentSchemaVersion
                || root.TryGetProperty("LegacyActivitySchema", out _)
                || !root.TryGetProperty("Producer", out var producer) || producer.ValueKind != JsonValueKind.String
                || !string.Equals(producer.GetString(), AgentActivityObservation.OwningProducer, StringComparison.Ordinal)
                || !root.TryGetProperty("AgentRunId", out var runId) || runId.ValueKind != JsonValueKind.String
                || !runId.TryGetGuid(out var id) || id == Guid.Empty || id != actualRunId
                || !root.TryGetProperty("ObservationComplete", out var complete)
                || complete.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("HasDeferredInvocations", out var deferred)
                || deferred.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("Invocations", out var invocations)
                || invocations.ValueKind != JsonValueKind.Array) return false;
        }
        else
        {
            // Exact repository legacy-array wrapper. Binding/stamps are ignored;
            // they are detached display metadata, never a receipt or Retry grant.
            if (!root.TryGetProperty("LegacyActivitySchema", out var legacy)
                || legacy.ValueKind != JsonValueKind.String
                || !string.Equals(legacy.GetString(), "array", StringComparison.Ordinal)
                || root.EnumerateObject().Any(property => property.Name is not
                    ("LegacyActivitySchema" or "Activities" or "CanonicalBindingVersion" or "CanonicalTask"))) return false;
        }

        // Project only the original display fields. Private Details/evidence and
        // completion/identity stamps do not become action or runtime authority.
        activities = originalActivities;
        return true;
    }

    private static string Short(string value, int limit) => value.Length <= limit ? value : value[..(limit - 1)] + "…";

    private static string SafeActivityTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Agent activity";

        var normalized = new string(title.Select(character =>
        {
            var category = char.GetUnicodeCategory(character);
            return char.IsControl(character) || char.IsWhiteSpace(character) || category == UnicodeCategory.Format
                ? ' '
                : character;
        }).ToArray());
        var compact = string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return compact.Length == 0 ? "Agent activity" : Short(compact, 96);
    }

    private static string FormatActivityTimestamp(DateTimeOffset timestamp)
    {
        try
        {
            timestamp = timestamp.ToLocalTime();
        }
        catch (ArgumentException)
        {
            // A boundary timestamp in a damaged log should still produce a readable entry.
        }

        return timestamp.ToString("HH:mm:ss zzz", CultureInfo.InvariantCulture);
    }

    private void OnRunChanged(AgentRun run)
    {
        if (_disposed || _originalWork.IsRetiring || _constructing) return;
        _ = _originalWork.RunAsync(async original =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                original.DemandPublication();
                ApplyRunUpdate(run);
                return;
            }
            var actualDispatcher = Dispatcher.UIThread.InvokeAsync(() =>
            {
                original.DemandPublication();
                ApplyRunUpdate(run);
            }).GetTask();
            await original.AwaitAsync(actualDispatcher);
        });
    }

    internal void ApplyRunUpdate(AgentRun run) => ObserveOriginal(() =>
    {

        var recent = _recentRuns.ToList();
        var existingIndex = recent.FindIndex(item => item.Id == run.Id);
        if (existingIndex >= 0)
            recent[existingIndex] = run;
        else
            recent.Insert(0, run);
        _recentRuns = recent.Take(8).ToArray();

        if (_latestRun is null || _latestRun.Id == run.Id || run.CreatedAt >= _latestRun.CreatedAt)
            _latestRun = run;

        RefreshExecutionStatus();
    });

    private void OnCancelLatestInvoked(object? sender, EventArgs e) => ObserveOriginal(() =>
    {
        if (_runtime is not null && _latestRun is not null) _runtime.Cancel(_latestRun.Id);
    });

    private void OnRetryLatestInvoked(object? sender, EventArgs e) => ObserveOriginalTask(RetryLatestAsync);

    internal Task<AgentRun?> RetryLatestAsync() => AdmitOriginal<AgentRun?>(async original =>
    {
        var expected = _latestRun;
        if (_runtime is null || expected is null) return null;
        // Deny-only observation. The actual private service rechecks all original
        // identity/decision/admission conditions; no detached binding is a grant.
        if (!_runtime.HasOriginalUnstartedRetrySource(expected.Id))
        {
            SetContent(ExecutionStatusText, "Retry unavailable: the SAME original unstarted Agent Task/Run is not available. Start explicit new work if needed.");
            return null;
        }
        original.DemandPublication();
        try
        {
            AgentRun? run;
            if ((object)_runtime is IAgentRunOriginalObservationSource observationSource)
            {
                run = await AwaitIssuedOriginalObservationAsync(original, observationSource, () =>
                    observationSource.StartObservedOriginalRetry(expected.Id, CancellationToken.None));
            }
            else
            {
                // Missing issuer preserves the old borrowed await and None lifetime.
                // Only the separate explicit Cancel action requests task cancellation.
                var actual = _runtime.RetryAsync(expected.Id, CancellationToken.None);
                run = await AwaitOriginalAsync(actual);
            }
            if (run is not null) ApplyRunUpdate(run);
            return run;
        }
        catch (Exception error)
        {
            original.Retain(error);
            if (original.IsPublicationCurrent)
                SetContent(ExecutionStatusText, "Retry unavailable or failed: " + error.Message);
            original.ThrowRetained();
            throw;
        }
    });

    private void UpdateDeleteLabels()
    {
        foreach (var card in _viewModel.Items)
            if (_dynamicUi.TryGetItem("AgentCards", card.Id.ToString("N"), out var item))
                PublishOriginal(() => item.SetVariable("DELETE_LABEL", _pendingDeleteId == card.Id ? "Confirm delete" : "Delete"));
    }

    private void RefreshChrome()
    {
        SetContent(StatusText, _viewModel.Status);
        Visible(Creator, _viewModel.IsCreating);
        SetContent(CreateToggleButton, _viewModel.IsCreating
            ? _viewModel.IsEditingAgent ? "Cancel edit" : "Close creator"
            : "Create agent");
        SetContent(SaveButton, _viewModel.IsEditingAgent ? "Save changes" : "Create agent");
        PublishOriginal(() => SaveButton.Accessibility.AccessibleName = SaveButton.Content);
        Enabled(BuildWithAiButton, _viewModel.BuildWithAiCommand.CanExecute(null));
        Enabled(SaveButton, _viewModel.CreateCommand.CanExecute(null));
        Set(StatusText, HavenProperties.Visibility, string.IsNullOrWhiteSpace(_viewModel.Status) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private void SyncDraftToViewModel()
    {
        PublishOriginal(() =>         _viewModel.BuilderPrompt = BuilderPromptInput.Text);
        PublishOriginal(() =>         _viewModel.NewName = NameInput.Text);
        PublishOriginal(() =>         _viewModel.NewDescription = DescriptionInput.Text);
        PublishOriginal(() =>         _viewModel.NewInstructions = InstructionsInput.Text);
        PublishOriginal(() =>         _viewModel.NewModel = ModelInput.Text);
        PublishOriginal(() =>         _viewModel.NewCapabilities = CapabilitiesInput.Text);
        PublishOriginal(() =>         _viewModel.NewPermissionProfile = PermissionProfileInput.Text);
        PublishOriginal(() =>         _viewModel.NewSandboxProfile = SandboxProfileInput.Text);
        PublishOriginal(() =>         _viewModel.NewKnowledgeResources = KnowledgeResourcesInput.Text);
        PublishOriginal(() =>         _viewModel.NewMemoryMode = MemoryModeInput.Text);
    }

    private void SyncDraftFromViewModel()
    {
        BuilderPromptInput.Invalidated -= OnDraftInvalidated;
        NameInput.Invalidated -= OnDraftInvalidated;
        DescriptionInput.Invalidated -= OnDraftInvalidated;
        InstructionsInput.Invalidated -= OnDraftInvalidated;
        ModelInput.Invalidated -= OnDraftInvalidated;
        CapabilitiesInput.Invalidated -= OnDraftInvalidated;
        PermissionProfileInput.Invalidated -= OnDraftInvalidated; SandboxProfileInput.Invalidated -= OnDraftInvalidated; KnowledgeResourcesInput.Invalidated -= OnDraftInvalidated; MemoryModeInput.Invalidated -= OnDraftInvalidated;
        try
        {
            SyncText(BuilderPromptInput, _viewModel.BuilderPrompt);
            SyncText(NameInput, _viewModel.NewName);
            SyncText(DescriptionInput, _viewModel.NewDescription);
            SyncText(InstructionsInput, _viewModel.NewInstructions);
            SyncText(ModelInput, _viewModel.NewModel);
            SyncText(CapabilitiesInput, _viewModel.NewCapabilities);
            SyncText(PermissionProfileInput, _viewModel.NewPermissionProfile); SyncText(SandboxProfileInput, _viewModel.NewSandboxProfile); SyncText(KnowledgeResourcesInput, _viewModel.NewKnowledgeResources); SyncText(MemoryModeInput, _viewModel.NewMemoryMode);
        }
        finally
        {
            if (!_originalWork.IsRetiring)
            {
            BuilderPromptInput.Invalidated += OnDraftInvalidated;
            NameInput.Invalidated += OnDraftInvalidated;
            DescriptionInput.Invalidated += OnDraftInvalidated;
            InstructionsInput.Invalidated += OnDraftInvalidated;
            ModelInput.Invalidated += OnDraftInvalidated;
            CapabilitiesInput.Invalidated += OnDraftInvalidated;
            PermissionProfileInput.Invalidated += OnDraftInvalidated; SandboxProfileInput.Invalidated += OnDraftInvalidated; KnowledgeResourcesInput.Invalidated += OnDraftInvalidated; MemoryModeInput.Invalidated += OnDraftInvalidated;
            }
        }
    }

    private void SyncText(Input input, string value)
    {
        if (!string.Equals(input.Text, value, StringComparison.Ordinal)) PublishOriginal(() => input.Text = value);
    }

    private void OnDraftInvalidated(object? sender, EventArgs e) => ObserveOriginal(() =>
    {
        SyncDraftToViewModel();
        RefreshChrome();
    });

    private void OnRefreshInvoked(object? sender, EventArgs e) => ObserveOriginalTask(RefreshAsync);
    private void OnCreateToggleInvoked(object? sender, EventArgs e) => ObserveOriginal(() =>
    {
        if (_viewModel.IsEditingAgent)
        {
            _viewModel.CancelAgentEdit();
            SyncDraftFromViewModel();
            RefreshChrome();
            return;
        }

        _viewModel.ToggleCreateCommand.Execute(null);
    });
    private void OnBuildWithAiInvoked(object? sender, EventArgs e) => ObserveOriginalTask(DraftAgentAsync);
    private void OnSaveInvoked(object? sender, EventArgs e) => ObserveOriginalTask(CreateAgentAsync);
    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ObserveOriginal(RefreshCards);
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => ObserveOriginal(RefreshChrome);

    private Input InputField(string name, string placeholder)
    {
        var input = new Input { Name = name, Placeholder = placeholder, SubmitOnEnter = false };
        input.Accessibility.AccessibleName = placeholder;
        Set(input, HavenProperties.Width, HavenLength.Percent(100));
        return input;
    }

    private void Visible(HavenElement element, bool visible) =>
        Set(element, HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);

    private void Enabled(HavenElement element, bool enabled) => Set(element, HavenProperties.Enabled, enabled);
    private void Set<T>(HavenElement element, HavenProperty<T> property, T value) =>
        PublishOriginal(() => element.SetValue(property, value));

    private Task AdmitOriginal(Func<DesktopOriginalWorkLifetime.Original, Task> body)
    {
        try { return _originalWork.RunAsync(body); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private Task<T> AdmitOriginal<T>(Func<DesktopOriginalWorkLifetime.Original, Task<T>> body)
    {
        try { return _originalWork.RunAsync(body); }
        catch (Exception error) { return Task.FromException<T>(error); }
    }
    private async Task AwaitOriginalAsync(Task actual)
    {
        var original = _originalWork.Executing ?? throw new InvalidOperationException("No original Agent scene callback owns this Task.");
        try { await original.AwaitAsync(actual); original.DemandPublication(); }
        catch (Exception error) { original.Retain(error); original.ThrowRetained(); throw; }
    }
    private async Task<T> AwaitOriginalAsync<T>(Task<T> actual)
    {
        var original = _originalWork.Executing ?? throw new InvalidOperationException("No original Agent scene callback owns this Task.");
        try { var value = await original.AwaitAsync(actual); original.DemandPublication(); return value; }
        catch (Exception error) { original.Retain(error); original.ThrowRetained(); throw; }
    }
    private void ObserveOriginal(Action callback)
    {
        if (_disposed || _originalWork.IsRetiring || _constructing) return;
        _originalWork.RunSynchronous(original => { original.DemandPublication(); callback(); });
    }
    private void ObserveOriginalTask(Func<Task> callback)
    {
        if (_disposed || _originalWork.IsRetiring || _constructing) return;
        // This actual outer event Task exists in the owner before the callback
        // and independently joins the SAME public invocation/admission result.
        _ = _originalWork.RunAsync(async original =>
        {
            original.DemandPublication();
            var actual = callback();
            await original.AwaitAsync(actual);
        });
    }
    private void DemandScenePublication()
    {
        if (_constructing) return; // Constructor-before-return custody remains a host prerequisite.
        (_originalWork.Executing ?? throw new InvalidOperationException("No original Agent scene publication is admitted.")).DemandPublication();
    }
    private void PublishOriginal(Action write)
    { DemandScenePublication(); write(); DemandScenePublication(); }
    private T ReadOriginal<T>(Func<T> create)
    { DemandScenePublication(); var value = create(); DemandScenePublication(); return value; }
    private void SetContent(HavenText element, string content) => PublishOriginal(() => element.Content = content);
    private void SetContent(HavenButton element, string content) => PublishOriginal(() => element.Content = content);
    private void WireOriginalCardCallback(DynamicUIItem item, HavenButton button, Func<Task> callback)
    {
        EventHandler actual = (_, _) => ObserveOriginalTask(callback);
        if (!_cardSubscriptions.TryGetValue(item, out var subscriptions))
            _cardSubscriptions.Add(item, subscriptions = []);
        subscriptions.Add(() => button.Invoked -= actual);
        PublishOriginal(() => button.Invoked += actual);
    }
    private void UnwireOriginalCard(DynamicUIItem item)
    {
        if (!_cardSubscriptions.Remove(item, out var subscriptions)) return;
        var errors = new List<Exception>();
        foreach (var unsubscribe in subscriptions) TryOriginalStop(unsubscribe, errors);
        if (errors.Count != 0) throw new AggregateException("Original Agent card unsubscription failed.", errors);
    }
    private static void TryOriginalStop(Action actual, List<Exception> errors)
    {
        try { actual(); }
        catch (Exception error)
        { if (!errors.Any(known => ReferenceEquals(known, error))) errors.Add(error); }
    }
    private async Task<AgentRun?> AwaitIssuedOriginalObservationAsync(
        DesktopOriginalWorkLifetime.Original original, IAgentRunOriginalObservationSource source,
        Func<AgentRunOriginalObservationLease> acquire)
    {
        if (!ReferenceEquals(source, _runtime))
            throw new InvalidOperationException("Only the SAME actual Agent runtime may issue this scene observation.");
        var capture = new AgentsOriginalObservationCapture(source, original, acquire, () => _originalWork.IsRetiring);
        // No source callback has run. Publish the actual pending capture before
        // releasing its acquisition gate, including a late capture after sealing.
        lock (_observationGate) _observations.Add(capture);
        AgentRunObservationResult? result = null;
        var failed = false;
        try
        {
            var actualObservation = original.AwaitAsync(capture.ActualObservation);
            capture.BeginOriginalAcquisition();
            result = await actualObservation;
        }
        catch (Exception error) { original.Retain(error); failed = true; }
        finally
        {
            try { capture.RequestRetirement(); }
            catch (Exception error) { original.Retain(error); failed = true; }
            Task? actualClose = null;
            try { actualClose = capture.CloseAndDrainAsync(); }
            catch (Exception error) { original.Retain(error); failed = true; }
            if (actualClose is not null)
                try { await original.AwaitAsync(actualClose); }
                catch (Exception error) { original.Retain(error); failed = true; }
        }
        if (failed) original.ThrowRetained();
        if (capture.CanPruneHealthy)
            lock (_observationGate) _observations.Remove(capture);
        // A genuine detached observation or sealed presentation is not a
        // canonical terminal result. Never invent completion/cancellation.
        if (result is null || result.Disposition == AgentRunObservationDisposition.ObservationDetached ||
            !original.IsPublicationCurrent) return null;
        original.DemandPublication();
        return result.TerminalRun;
    }

    private AgentsOriginalObservationCapture[] CaptureOriginalObservations()
    { lock (_observationGate) return _observations.ToArray(); }

    private Task StopOriginalAsync()
    {
        _disposed = true;
        var errors = new List<Exception>();
        var actualChildCloses = new List<Task>();
        // Start every known observation stop before joining scene originals.
        // A late admitted capture sees the sealed scene and independently joins
        // its own source close in the original body above.
        foreach (var capture in CaptureOriginalObservations())
        {
            TryOriginalStop(capture.RequestRetirement, errors);
            if (capture.OriginalClose is { } actualClose) actualChildCloses.Add(actualClose);
            else errors.Add(new InvalidOperationException("No actual Agent observation retirement was published."));
        }
        TryOriginalStop(() => _viewModel.PropertyChanged -= OnViewModelPropertyChanged, errors);
        TryOriginalStop(() => _viewModel.Items.CollectionChanged -= OnItemsCollectionChanged, errors);
        if (_runtime is not null) TryOriginalStop(() => _runtime.RunChanged -= OnRunChanged, errors);
        TryOriginalStop(() => RefreshButton.Invoked -= OnRefreshInvoked, errors);
        TryOriginalStop(() => CreateToggleButton.Invoked -= OnCreateToggleInvoked, errors);
        TryOriginalStop(() => BuildWithAiButton.Invoked -= OnBuildWithAiInvoked, errors);
        TryOriginalStop(() => SaveButton.Invoked -= OnSaveInvoked, errors);
        TryOriginalStop(() => CancelLatestButton.Invoked -= OnCancelLatestInvoked, errors);
        TryOriginalStop(() => RetryLatestButton.Invoked -= OnRetryLatestInvoked, errors);
        TryOriginalStop(() => BuilderPromptInput.Invalidated -= OnDraftInvalidated, errors);
        TryOriginalStop(() => NameInput.Invalidated -= OnDraftInvalidated, errors);
        TryOriginalStop(() => DescriptionInput.Invalidated -= OnDraftInvalidated, errors);
        TryOriginalStop(() => InstructionsInput.Invalidated -= OnDraftInvalidated, errors);
        TryOriginalStop(() => ModelInput.Invalidated -= OnDraftInvalidated, errors);
        TryOriginalStop(() => CapabilitiesInput.Invalidated -= OnDraftInvalidated, errors);
        TryOriginalStop(() => PermissionProfileInput.Invalidated -= OnDraftInvalidated, errors);
        TryOriginalStop(() => SandboxProfileInput.Invalidated -= OnDraftInvalidated, errors);
        TryOriginalStop(() => KnowledgeResourcesInput.Invalidated -= OnDraftInvalidated, errors);
        TryOriginalStop(() => MemoryModeInput.Invalidated -= OnDraftInvalidated, errors);
        foreach (var item in _cardSubscriptions.Keys.ToArray())
            TryOriginalStop(() => UnwireOriginalCard(item), errors);
        // The service keeps the SAME producer, and None remains None. Source
        // detach acknowledges observation work only; scene dispatchers are joined
        // by this owner's original ledger before the wrapper removes its Root.
        if (errors.Count != 0)
            actualChildCloses.Add(Task.FromException(new AggregateException("Original Agent scene stop failures remain retained.", errors)));
        return actualChildCloses.Count == 0 ? Task.CompletedTask : Task.WhenAll(actualChildCloses);
    }
    private Task CleanupOriginalAsync()
    {
        _dynamicUi.Clear("AgentCards");
        return Task.CompletedTask;
    }
    public void RequestRetirement() => _originalWork.RequestRetirement();
    public void DemandExternalOriginalRetirementJoin()
    {
        _originalWork.DemandExternalClose();
        foreach (var capture in CaptureOriginalObservations()) capture.DemandExternalClose();
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin(); // Also before returning any existing parent close.
        return _originalWork.CloseAndDrainAsync();
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    public void Dispose() => RequestRetirement();
}
