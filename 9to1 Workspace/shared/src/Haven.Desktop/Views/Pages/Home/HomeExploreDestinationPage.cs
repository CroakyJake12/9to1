using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>A per-tab conversation. Browsing and inference never create an artifact.
/// Only a separately admitted Browse navigation can open the chosen app.</summary>
internal sealed class HomeExploreDestinationPage : UserControl, ICuiActionDispatcher, ICuiActionAvailability
{
    internal sealed record ChoiceTarget(long Revision, HomeExploreChoice Choice, HomeAppItem App);
    // Repeated rows expose a narrow, read-only CUI contract. The loader does not
    // reflect anonymous objects; the exact admitted target remains the action value.
    private sealed record ConversationRow(int Key, string Role, string Text) : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch { "key" => Key, "role" => Role, "text" => Text, _ => null };
            return value is not null;
        }
    }

    private sealed record ChoiceRow(string Key, ChoiceTarget Target, bool Available) : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "key" => Key,
                "target" => Target,
                "title" => Target.Choice.Deliverable,
                "app" => Target.App.Name,
                "reason" => Target.Choice.Reason,
                "available" => Available,
                "availability" => Target.App.IsAvailable ? "Continue in " + Target.App.Name : Target.App.Detail,
                "label" => "Continue in " + Target.App.Name + " for " + Target.Choice.Deliverable,
                _ => null
            };
            return value is not null;
        }
    }
    private readonly IModeRegistry _modes;
    private readonly IHomeExplorePlanner? _planner;
    private readonly Func<CancellationToken, Task> _admit;
    private readonly Func<ModeDefinition, bool> _available;
    private readonly Func<AppContentAddress, string, Guid, Task, bool> _queue;
    private readonly CuiControlLoader _loader;
    private readonly CuiViewModel _bindings = new();
    private readonly TextBox _composer = new() { MaxLength = 3000 };
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly List<HomeExploreTurn> _conversation = [];
    private IReadOnlyList<HomeAppItem> _catalogue = [];
    private ChoiceTarget[] _choices = [];
    private string _originalBrief = string.Empty;
    private Guid _conversationId = Guid.NewGuid();
    private string _status = string.Empty;
    private bool _active, _busy, _stopped;
    private long _generation, _revision;
    private Task? _initialization;
    private Task<bool>? _preparation;
    private Task _keyboardAction = Task.CompletedTask;

    internal HomeExploreDestinationPage(IModeRegistry modes, IHomeExplorePlanner? planner,
        Func<CancellationToken, Task> admit, Func<ModeDefinition, bool> available,
        Func<AppContentAddress, string, Guid, Task, bool> queue, CuiThemePreferenceContext? theme = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _modes = modes ?? throw new ArgumentNullException(nameof(modes)); _planner = planner;
        _admit = admit ?? throw new ArgumentNullException(nameof(admit));
        _available = available ?? throw new ArgumentNullException(nameof(available));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _work = new(StopAsync, CleanupAsync);
        _loader = new(HomeAppsCuiDocument.CreateControlRegistry());
        if (theme is not null) _loader.SetThemePreferenceContext(theme);
        _loader.SetActionDispatcher(this); _loader.SetBindingContext(_bindings);
        _loader.BindAuthoredControl("home-explore-composer", _ => _composer);
        using var stream = typeof(HomeExploreDestinationPage).Assembly.GetManifestResourceStream("Haven.Desktop.Home.Explore.cui")
            ?? throw new InvalidDataException("The Home Explore document is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "HomeExplore.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Home Explore document is invalid.");
        var loaded = _loader.TryLoad(document);
        if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Home Explore document could not be rendered: " + string.Join("; ",
                loaded.Diagnostics.Where(item => item.Severity == CuiDiagnosticSeverity.Error)
                    .Select(item => item.Code + " " + item.Message)));
        _loader.WireBindings(loaded.Root); Content = loaded.Root;
        _composer.KeyDown += OnComposerKey; _composer.TextChanged += OnComposerText;
        SizeChanged += OnViewportChanged;
        Refresh();
    }

    internal Task? OriginalInitialization => _initialization;
    internal Task? OriginalClose => _work.OriginalClose;
    internal bool IsAlive => !_work.IsRetiring;
    internal IReadOnlyList<HomeExploreTurn> Conversation => _conversation;
    internal IReadOnlyList<ChoiceTarget> Choices => _choices;
    internal string OriginalBrief => _originalBrief;
    internal bool OwnsBrief(Guid id) => IsAlive && _conversationId == id && _originalBrief.Length > 0;
    internal Task WhenActionsIdleAsync() => Task.WhenAll(_loader.WhenActionsIdleAsync(), _keyboardAction);

    internal Task ActivateAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); _active = true;
        return _work.RunAsync(async original =>
        {
            var generation = ++_generation;
            original.BindPublicationGuard(() => IsAlive && _active && generation == _generation);
            await ReadCatalogueAsync(original, token);
            if (original.IsAcceptedPublicationCurrent) original.RunAcceptedPublicationCallback(Refresh);
        }, task => _initialization ??= task);
    }
    internal void Deactivate() { Dispatcher.UIThread.VerifyAccess(); _active = false; ++_generation; Refresh(); }

    public bool? IsActionAvailable(string command) => IsAlive && _active && !_busy && (command switch
    {
        "ExploreFind" => _planner is not null && !string.IsNullOrWhiteSpace(_composer.Text) &&
            _composer.Text.Length <= 3000 && _conversation.Count < HomeExplorePlanner.MaximumTurns,
        "ExploreContinue" => _choices.Any(choice => choice.App.IsAvailable && choice.App.IsInstalled),
        "ExploreNewBrief" => _conversation.Count > 0 || !string.IsNullOrWhiteSpace(_composer.Text),
        "ExploreGameSuggestion" or "ExploreWritingSuggestion" or "ExplorePresentationSuggestion" or "ExploreCanvasSuggestion" => true,
        _ => false
    });

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Explore action is unavailable.");
        var admittedDraft = _composer.Text ?? string.Empty;
        return new(_work.RunAsync(async original =>
        {
            var generation = _generation;
            original.BindPublicationGuard(() => IsAlive && _active && generation == _generation);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            // Claim before the first asynchronous admission read. The clicked draft and target
            // cannot be replaced by a second click or a later programmatic composer update.
            _busy = true; Refresh();
            try
            {
                await original.AwaitAsync(_admit(linked.Token));
                if (!original.IsAcceptedPublicationCurrent) return;
                if (command.EndsWith("Suggestion", StringComparison.Ordinal))
                {
                    original.RunAcceptedPublicationCallback(() =>
                    {
                        _composer.Text = command switch
                        {
                            "ExploreGameSuggestion" => "I want to make a game about…",
                            "ExploreWritingSuggestion" => "I want to write a document about…",
                            "ExplorePresentationSuggestion" => "I want to make a presentation about…",
                            _ => "I want to make a canvas for…"
                        };
                        _composer.Focus(); _composer.CaretIndex = _composer.Text.Length;
                    });
                    return;
                }
                if (command == "ExploreNewBrief")
                {
                    original.RunAcceptedPublicationCallback(() =>
                    {
                        _conversation.Clear(); _choices = []; _originalBrief = _status = string.Empty;
                        _conversationId = Guid.NewGuid();
                        ++_revision;
                        if (StringComparer.Ordinal.Equals(_composer.Text, admittedDraft)) _composer.Text = string.Empty;
                        Refresh(); _composer.Focus();
                    });
                    return;
                }
                if (command == "ExploreContinue") { await ContinueAsync(original, parameter, linked.Token); return; }
                await FindAsync(original, admittedDraft, linked.Token);
            }
            finally { _busy = false; if (IsAlive) Refresh(); }
        }));
    }

    private async Task FindAsync(DesktopOriginalWorkLifetime.Original original, string admittedDraft, CancellationToken token)
    {
        var text = admittedDraft.Trim();
        if (text.Length is 0 or > 3000) return;
        _busy = true; _status = "Finding the app for your brief…"; Refresh();
        try
        {
            await ReadCatalogueAsync(original, token);
            if (!original.IsAcceptedPublicationCurrent) return;
            var pending = _conversation.Append(new HomeExploreTurn("user", text)).ToArray();
            var result = await original.AwaitAsync(_planner!.PlanAsync(pending, _catalogue, token));
            await original.AwaitAsync(_admit(token));
            if (!original.IsAcceptedPublicationCurrent) return;
            original.RunAcceptedPublicationCallback(() =>
            {
                _status = result.Message;
                if (!result.Succeeded || result.Plan is null) return;
                if (_originalBrief.Length == 0) _originalBrief = text;
                _conversation.Add(new("user", text)); _conversation.Add(new("assistant", result.Plan.Message));
                var revision = ++_revision;
                _choices = result.Plan.Choices.Select(choice => new ChoiceTarget(revision, choice,
                    _catalogue.Single(app => app.StableIdentity == choice.AppIdentity))).ToArray();
                if (StringComparer.Ordinal.Equals(_composer.Text, admittedDraft)) _composer.Text = string.Empty;
                _status = string.Empty;
            });
        }
        finally
        {
            if (IsAlive && !original.IsAcceptedPublicationCurrent)
                _status = "Your brief is kept. Find an app again when you return to Explore.";
        }
    }

    private async Task ContinueAsync(DesktopOriginalWorkLifetime.Original original, object? parameter, CancellationToken token)
    {
        if (parameter is not ChoiceTarget target || !_choices.Any(choice => ReferenceEquals(choice, target)) ||
            target.Revision != _revision || !target.App.IsAvailable || !target.App.IsInstalled || target.App.CanonicalId is null)
            throw new UnauthorizedAccessException("Choose an available app from the current recommendation.");
        await ReadCatalogueAsync(original, token);
        if (!original.IsAcceptedPublicationCurrent) return;
        var current = _catalogue.SingleOrDefault(app => app.StableIdentity == target.App.StableIdentity);
        if (current is null || !current.IsAvailable || !current.IsInstalled || current.CanonicalId != target.App.CanonicalId ||
            current.Source != target.App.Source || !StringComparer.Ordinal.Equals(current.Key, target.App.Key))
        {
            original.RunAcceptedPublicationCallback(() => { _status = "This app changed or became unavailable. Find an app again; your brief is kept."; _choices = []; ++_revision; });
            return;
        }
        // Root routes only. Neither model output nor the brief can name an artifact or command.
        var address = AppContentAddress.Parse("9to1://" + current.Key);
        var accepted = false;
        var settlement = Task.WhenAll(original.Task, _loader.WhenActionsIdleAsync());
        original.RunAcceptedPublicationCallback(() => accepted = _queue(address, _originalBrief, _conversationId, settlement));
        if (original.IsAcceptedPublicationCurrent)
            original.RunAcceptedPublicationCallback(() => _status = accepted ? "Opening " + current.Name + ". This conversation stays in Explore." :
                "The browser is busy. Your choices and brief are kept; try Continue again.");
    }

    private async Task ReadCatalogueAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        await original.AwaitAsync(_admit(token));
        var modes = await original.AwaitAsync(_modes.GetModesAsync(token));
        await original.AwaitAsync(_admit(token));
        if (!original.IsAcceptedPublicationCurrent) return;
        original.RunAcceptedPublicationCallback(() =>
        {
            var duplicateKeys = modes.GroupBy(mode => mode.Key, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() != 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _catalogue = HomeAppHubCatalogue.Catalogue(modes.Where(HomeAppHubCatalogue.IsAppRecord).Select(mode =>
            {
                var available = mode.IsEnabled && !duplicateKeys.Contains(mode.Key) && _available(mode);
                return new HomeAppItem(mode.Key, mode.Name, mode.Description, mode.IconKey,
                    CanonicalId: mode.Id, Source: mode.Source, IsAvailable: available,
                    AvailabilityNote: !mode.IsEnabled ? "Disabled in this profile." : !available ? "This app has no available native workspace." : string.Empty,
                    InstallState: mode.InstallState);
            }).ToArray());
        });
    }

    private void Refresh()
    {
        _bindings.Set("explore.welcome", _conversation.Count == 0);
        _bindings.Set("explore.hasConversation", _conversation.Count > 0);
        _bindings.Set("explore.heading", _conversation.Count == 0 ? "What would you like to make?" : "Explore");
        _bindings.Set("explore.conversation", _conversation.Select((turn, index) =>
            new ConversationRow(index, turn.Role == "user" ? "You" : "9to1", turn.Text)).ToArray());
        _bindings.Set("explore.choices", _choices.Select((target, index) => new ChoiceRow(
            target.Revision + ":" + index, target,
            IsAlive && _active && target.App.IsAvailable && target.App.IsInstalled && !_busy)).ToArray());
        _bindings.Set("explore.status", _status);
        _bindings.Set("explore.hasStatus", _status.Length > 0);
        _bindings.Set("explore.canFind", IsActionAvailable("ExploreFind") == true);
        _bindings.Set("explore.canNew", IsActionAvailable("ExploreNewBrief") == true);
        _bindings.Set("explore.canEdit", IsAlive && _active && !_busy);
        _bindings.Set("explore.findLabel", _busy ? "Finding an app…" : "Find an app");
        _bindings.Set("explore.modelUnavailable", _planner is null);
        _bindings.Set("explore.draftCount", (_composer.Text?.Length ?? 0) + " / 3,000");
        _bindings.Set("explore.padding", Bounds.Width > 900 ? "30 28 30 16" : Bounds.Width > 550 ? "22 24 22 14" : "16 20 16 12");
        _bindings.Set("explore.titleSize", _conversation.Count > 0 ? 29d : Bounds.Width > 900 ? 34d : Bounds.Width > 550 ? 29d : 26d);
    }
    private void OnViewportChanged(object? sender, Avalonia.Controls.SizeChangedEventArgs args) => Refresh();
    private void OnComposerText(object? sender, TextChangedEventArgs args) => Refresh();
    private async void OnComposerKey(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || (args.KeyModifiers & KeyModifiers.Shift) != 0 || IsActionAvailable("ExploreFind") != true) return;
        args.Handled = true;
        var actual = _keyboardAction = DispatchAsync("ExploreFind", null).AsTask();
        try { await actual; }
        catch (Exception failure) { System.Diagnostics.Trace.TraceError("Explore retained its original keyboard action failure: {0}", failure); }
    }

    internal Task<bool> TryPrepareToCloseAsync(CancellationToken token)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _work.DemandExternalClose(); token.ThrowIfCancellationRequested();
        if (_preparation is null || _preparation.IsCompletedSuccessfully) _preparation = PrepareAsync();
        return _preparation.WaitAsync(token);
    }
    private async Task<bool> PrepareAsync()
    { await WhenActionsIdleAsync(); await _work.JoinAdmittedOriginalsAsync(); return true; }
    internal Task CloseAndDrainAsync() { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); return _work.CloseAndDrainAsync(); }
    private async Task StopAsync()
    {
        _active = false; ++_generation;
        _composer.KeyDown -= OnComposerKey; _composer.TextChanged -= OnComposerText; SizeChanged -= OnViewportChanged;
        await WhenActionsIdleAsync();
        if (_preparation is { } preparation) await preparation;
        _loader.Dispose(); await _loader.WhenActionsIdleAsync(); _stopped = true;
    }
    private Task CleanupAsync()
    {
        if (!_stopped) throw new InvalidOperationException("Join the original Explore loader before detaching it.");
        Content = null; _conversation.Clear(); _choices = []; _catalogue = []; return Task.CompletedTask;
    }
}
