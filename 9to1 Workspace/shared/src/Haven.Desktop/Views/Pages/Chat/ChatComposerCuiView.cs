using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Prefabs;
using HavenOS.Apps.Spaces.Chat;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>One authored composer chip (capability/Agent/Skill/resource or persisted attachment).</summary>
public sealed record ChatComposerChip(string Id, string Label, bool Invokable);

/// <summary>Borrowed actual host of the existing Voice workflow. Opening presentation never
/// starts capture; its owning Start command retains device consent and call lifetime.</summary>
internal interface IChatComposerVoiceActions
{
    bool CanEnter(Guid conversationId, ModelDescriptor model);
    ValueTask Enter(Guid conversationId, ModelDescriptor model);
}

internal interface IChatComposerTaskActions
{
    bool IsApplicable { get; }
    bool CanSteer { get; }
    bool CanQueue { get; }
    bool CanStop { get; }
    bool CanPause => false;
    bool IsPassPaused => false;
    bool CanEndPass => false;
    bool CanResume => false;
    bool HasSendModePreference => false;
    TaskFollowUpMode? PreferredSendMode => null;
    bool CanSubmit => PreferredSendMode is { } mode && (mode == TaskFollowUpMode.Steer ? CanSteer : CanQueue);
    ValueTask SetPreferredSendMode(TaskFollowUpMode mode) => ValueTask.FromException(new NotSupportedException("No Space preference owner is bound."));
    ValueTask Submit(bool alternate)
    {
        if (PreferredSendMode is not { } mode)
            return ValueTask.FromException(new InvalidOperationException("Choose Steer or Queue for this Space before using the send shortcut."));
        var steer = alternate ? mode != TaskFollowUpMode.Steer : mode == TaskFollowUpMode.Steer;
        if (steer ? !CanSteer : !CanQueue)
            return ValueTask.FromException(new InvalidOperationException("The selected task submit action is unavailable. The draft was retained."));
        return steer ? Steer() : Queue();
    }
    ValueTask Pause() => ValueTask.FromException(new NotSupportedException("This task source has no original pause command."));
    ValueTask EndPass() => ValueTask.FromException(new NotSupportedException("This task source has no original End Pass command."));
    ValueTask Resume() => ValueTask.FromException(new NotSupportedException("This task source has no original resume command."));
    ValueTask Steer();
    ValueTask Queue();
    ValueTask Stop();
}

/// <summary>
/// The single shared canonical composer's data source. Each real consumer (the Haven Chat native
/// scene and the ChatSpace CUI host) supplies a live implementation; no composer state is cloned.
/// Action methods report the actual admitted operation through typed async callbacks so a caller
/// can join the real lifetime instead of swallowing it.
/// </summary>
public interface IChatComposerSource
{
    object? ActionIdentity => null;
    bool CanEnterVoice => false;
    ValueTask EnterVoice() => ValueTask.FromException(new NotSupportedException("No actual Voice workflow owner is bound."));
    string Draft { get; set; }
    string Placeholder { get; }
    bool IsSending { get; }
    bool CanStop => IsSending;
    string StopAccessibleName => "Stop current response";
    string StopTooltip => "Stop the active response; retain its saved input and partial reply";
    bool HasPendingMessage => !string.IsNullOrWhiteSpace(Draft);
    bool CanQueue => IsSending && CanStop && HasPendingMessage && CanSend && !IsEditing;
    bool IsSafetyLocked { get; }
    bool HasAdd { get; }
    bool HasSettings => true;
    bool HasSendModePreference => false;
    TaskFollowUpMode? PreferredSendMode => null;
    bool CanSubmit => IsSending ? CanQueue : CanSend;
    ValueTask SetPreferredSendMode(TaskFollowUpMode mode) => ValueTask.FromException(new NotSupportedException("No Space preference owner is bound."));
    bool HasFormatting => true;
    bool CanSteer => false;
    bool CanPause => false;
    bool IsPassPaused => false;
    bool CanEndPass => false;
    bool CanResume => false;
    bool CanSend { get; }
    bool IsEditing { get; }
    IReadOnlyList<ChatComposerChip> Chips { get; }
    ValueTask Send();
    ValueTask Queue() => Send();
    ValueTask Submit(bool alternate) => IsSending ? Queue() : Send();
    ValueTask Stop();
    ValueTask Steer() => ValueTask.FromException(new NotSupportedException("This source has no canonical task steering command."));
    ValueTask Pause() => ValueTask.FromException(new NotSupportedException("This source has no canonical task pause command."));
    ValueTask EndPass() => ValueTask.FromException(new NotSupportedException("This source has no canonical End Pass command."));
    ValueTask Resume() => ValueTask.FromException(new NotSupportedException("This source has no canonical task continuation command."));
    ValueTask Add();
    ValueTask OpenSettings();
    ValueTask CloseSettings();
    ValueTask SetActionMode(string mode);
    ValueTask SetResponseMode(string mode);
    ValueTask InvokeChip(string id);
    ValueTask RemoveChip(string id);
    ValueTask FormatSelection(string command);
    ValueTask InsertEmoji(string emoji);
    ValueTask CancelEdit();
}

/// <summary>
/// Typed CUI binding/action adapter over the shared composer. It projects the current
/// <see cref="IChatComposerSource"/> into the composer document and forwards authored commands to
/// the source. Presentation-only state (settings/emoji panels, formatting expansion) lives here,
/// not in domain. Dispatching returns the real admitted source operation instead of a swallowed
/// completed task.
/// </summary>
public sealed class ChatComposerBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    private readonly IChatComposerSource _source;
    private readonly Func<object, bool>? _ownsSubmitSender;
    private bool _settingsOpen;
    private bool _emojiOpen;
    private bool _focused;
    private bool _expanded;
    // Query/cursor/page state is authored in canonical CUI via the generic runtime QueryViewport
    // (which owns the query string + opaque cursor) backed by the typed ICuiPagedSource port exposed
    // below. No application C# page/query/selection cache exists here.

    public ChatComposerBindings(IChatComposerSource source, Func<object, bool>? ownsSubmitSender = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _ownsSubmitSender = ownsSubmitSender;
    }

    private IReadOnlyList<CuiActionShelfItem> EligibleShelfActions()
    {
        var actions = new List<CuiActionShelfItem>(4);
        CuiActionShelfItem Item(string id, string command, string label, string icon, string tooltip) =>
            new(id, command, label, "avares://Haven/Assets/" + (icon == "send" ? "send" : "composer-" + icon) + ".svg", tooltip, ContextIdentity: _source.ActionIdentity);
        if (_source.CanResume || _source.IsPassPaused || _source.CanEndPass)
        {
            if (_source.CanResume)
                actions.Add(Item("resume", "composer.resume", "Resume", "resume", "Continue this same task from its approved continuation boundary"));
            if (_source.CanEndPass)
                actions.Add(Item("end-pass", "composer.end-pass", "End Pass", "stop", "End this acknowledged paused pass; retain draft and queued content"));
            return actions; // Inspection is never an invented continuation.
        }
        if (_source.CanSteer)
            actions.Add(Item("steer", "composer.steer", "Steer", "steer", "Send this instruction to the current task"));
        if (_source.CanPause)
            actions.Add(Item("pause", "composer.pause", "Stop", "stop", "Request a supported resumable pause; wait for the task owner's acknowledgement"));
        else if (_source.CanStop)
            actions.Add(Item("cancel", "composer.stop", "Cancel response", "stop", _source.StopTooltip));
        if (_source.CanQueue)
            actions.Add(Item("queue", "composer.queue", "Queue", "queue", "Queue this instruction without replacing the current pass"));
        if (!_source.IsSending && _source.HasPendingMessage && _source.CanSend)
            actions.Insert(0, Item("send", "composer.send", _source.IsEditing ? "Update message" : "Send", "send",
                _source.IsEditing ? "Update the message being edited" : "Send message"));
        if (_source.CanEnterVoice)
            actions.Add(new("voice", "composer.voice", "Voice", "avares://Haven/Assets/composer-voice.svg",
                "Open Voice in this same conversation; capture starts only with its Start command", ContextIdentity: _source.ActionIdentity));
        return actions;
    }

    /// <summary>Observed focus on the real composer input; drives compact/expanded formatting.</summary>
    internal bool IsFocused
    {
        get => _focused;
        set
        {
            if (_focused == value) return;
            _focused = value;
            if (value) _expanded = true;
            Refresh();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IChatComposerSource Source => _source;

    public bool TryGetValue(string path, out object? value)
    {
        value = null;
        switch (path)
        {
            case "Composer.ActionShelf": value = EligibleShelfActions(); return true;
            case "Composer.Draft":
                value = _source.Draft;
                return true;
            case "Composer.Placeholder":
                value = _source.Placeholder;
                return true;
            case "Composer.HasChips":
                value = _source.Chips.Count > 0;
                return true;
            case "Composer.Chips":
                value = _source.Chips;
                return true;
            case "Composer.CanSend":
                value = _source.CanSend;
                return true;
            case "Composer.ShowSend":
                value = !_source.IsSending && _source.HasPendingMessage && _source.CanSend;
                return true;
            case "Composer.HasAdd": value = _source.HasAdd; return true;
            case "Composer.HasSettings": value = _source.HasSettings; return true;
            case "Composer.HasSendModePreference": value = _source.HasSendModePreference; return true;
            case "Composer.PreferredSendModeLabel": value = _source.PreferredSendMode?.ToString() ?? "Choose send action"; return true;
            case "Composer.CanSteer": value = _source.CanSteer; return true;
            case "Composer.CanPause": value = _source.CanPause; return true;
            case "Composer.CanResume": value = _source.CanResume; return true;
            case "Composer.CanQueue":
                value = _source.CanQueue;
                return true;
            case "Composer.StopAccessibleName": value = _source.StopAccessibleName; return true;
            case "Composer.StopTooltip": value = _source.StopTooltip; return true;
            case "Composer.CanStop":
                value = _source.CanStop;
                return true;
            case "Composer.IsEditing":
                value = _source.IsEditing;
                return true;
            case "Composer.SendLabel":
                value = _source.IsEditing ? "Update" : "Send";
                return true;
            case "Composer.SendAccessibleName":
                value = _source.IsEditing ? "Update message" : "Send message";
                return true;
            case "Composer.SendTooltip":
                value = _source.IsEditing ? "Update the message being edited" : "Send message";
                return true;
            case "Composer.ShowFormatting":
                value = _source.HasFormatting && _expanded && (_focused || _settingsOpen || _emojiOpen);
                return true;
            case "Composer.ShowSettings":
                value = _settingsOpen;
                return true;
            case "Composer.ShowEmoji":
                value = _emojiOpen;
                return true;
            case "Composer.EmojiCatalogueSource":
                // Typed data-only port for the runtime QueryViewport. Query filtering/pagination
                // operate on the FULL catalogue (including flags and trigger-excluded sequences);
                // the neutral allowlist/flag/suggestive filters apply only to the random-trigger
                // icon pool, never to the picker path.
                value = EmojiCatalogueSource.Instance;
                return true;
            case "Composer.EmojiIconCandidates":
                // Read-only catalogue eligibility: the random-trigger pool. CUI owns the
                // choose-once selection; this is only a bounded non-I/O typed query.
                value = ComposerEmojiIconPool.Candidates;
                return true;
            default:
                return false;
        }
    }

    public bool TrySetValue(string path, object? value)
    {
        if (path == "Composer.Draft")
        {
            _source.Draft = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            return true;
        }
        return false;
    }

    /// <summary>
    /// The typed, data-only paged query port consumed by the runtime QueryViewport. It adapts the
    /// pure <see cref="ComposerEmojiCatalogueQuery"/> engine to the generic
    /// <see cref="ICuiPagedSource"/> contract. This is immutable catalogue data projection, never
    /// per-view page/query/cursor state.
    /// </summary>
    private sealed class EmojiCatalogueSource : ICuiPagedSource
    {
        public static readonly EmojiCatalogueSource Instance = new();

        public CuiPagedResult Evaluate(string? query, CuiPageCursor? cursor)
        {
            var result = ComposerEmojiCatalogueQuery.Evaluate(
                query, cursor is null ? null : new ComposerEmojiCatalogueQuery.Cursor(cursor.Offset));
            return new CuiPagedResult(
                result.Items.Cast<object>().ToArray(),
                result.TotalCount,
                result.StartOffset,
                result.Prev is null ? null : new CuiPageCursor(result.Prev.Value.Offset),
                result.Next is null ? null : new CuiPageCursor(result.Next.Value.Offset));
        }
    }

    public bool? IsActionAvailable(string command)
    {
        return command switch
        {
            "composer.submit" => _source.CanSubmit,
            "composer.prefer-steer" or "composer.prefer-queue" => _source.HasSendModePreference,
            "composer.send" => !_source.IsSending && _source.HasPendingMessage && _source.CanSend,
            "composer.queue" => _source.CanQueue,
            "composer.voice" => _source.CanEnterVoice,
            "composer.end-pass" => _source.CanEndPass,
            "composer.stop" => _source.CanStop,
            "composer.steer" => _source.CanSteer,
            "composer.pause" => _source.CanPause,
            "composer.resume" => _source.CanResume,
            "composer.add" => _source.HasAdd,
            "composer.emoji" or "composer.emoji-toggle" or "composer.emoji-search" or "composer.emoji-insert" => _source.HasFormatting,
            "composer.settings" or "composer.set-action-mode" or "composer.set-response-mode" => _source.HasSettings,
            _ => true
        };
    }

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case "composer.submit" when parameter is CuiInputSubmitRequest request && _ownsSubmitSender?.Invoke(request.Sender) == true &&
                request.Key == Avalonia.Input.Key.Enter && (request.Modifiers is CuiInputSubmitModifiers.None or CuiInputSubmitModifiers.Control or CuiInputSubmitModifiers.Alt) &&
                _source.CanSubmit:
                return _source.Submit(request.Alt);
            case "composer.prefer-steer" when _source.HasSendModePreference:
                return _source.SetPreferredSendMode(TaskFollowUpMode.Steer);
            case "composer.prefer-queue" when _source.HasSendModePreference:
                return _source.SetPreferredSendMode(TaskFollowUpMode.Queue);
            case "composer.send" when IsActionAvailable(command) == true:
                return _source.Send();
            case "composer.queue" when IsActionAvailable(command) == true:
                return _source.Queue();
            case "composer.end-pass" when _source.CanEndPass:
                return _source.EndPass();
            case "composer.voice" when _source.CanEnterVoice:
                return _source.EnterVoice();
            case "composer.stop" when _source.CanStop:
                return StopProjectedAsync();
            case "composer.steer" when _source.CanSteer:
                return _source.Steer();
            case "composer.pause" when _source.CanPause:
                return _source.Pause();
            case "composer.resume" when _source.CanResume:
                return _source.Resume();
            case "composer.submit" or "composer.prefer-steer" or "composer.prefer-queue" or "composer.send" or "composer.queue" or "composer.stop" or "composer.steer" or "composer.pause" or "composer.resume":
                return ValueTask.FromException(new InvalidOperationException("This conversation action is no longer available."));
            case "composer.add":
                return _source.Add();
            case "composer.settings":
                _settingsOpen = !_settingsOpen;
                _emojiOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return _settingsOpen ? _source.OpenSettings() : _source.CloseSettings();
            case "composer.set-action-mode" when parameter is string mode:
                _settingsOpen = false;
                _emojiOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return ApplyActionModeAsync(mode);
            case "composer.set-response-mode" when parameter is string mode:
                _settingsOpen = false;
                _emojiOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return ApplyResponseModeAsync(mode);
            case "composer.invoke-chip" when parameter is string id:
                return _source.InvokeChip(id);
            case "composer.remove-chip" when parameter is string id:
                return _source.RemoveChip(id);
            case "composer.format" when parameter is string formatCommand:
                return _source.FormatSelection(formatCommand);
            case "composer.emoji" or "composer.emoji-toggle":
                _emojiOpen = !_emojiOpen;
                if (_emojiOpen) _expanded = true;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return ValueTask.CompletedTask;
            case "composer.emoji-insert" when parameter is string emoji:
                return InsertEmojiProjectedAsync(emoji);
            case "composer.cancel-edit":
                return _source.CancelEdit();
            default:
                return ValueTask.FromException(new InvalidOperationException($"Chat composer CUI command '{command}' is not owned by the shared composer."));
        }
    }

    private async ValueTask StopProjectedAsync()
    {
        Exception? sourceFailure = null;
        try { await _source.Stop().ConfigureAwait(false); }
        catch (Exception error) { sourceFailure = error; }
        try
        {
            // Cancellation admission can settle before the active producer acknowledges it.
            // Re-read the same live source now so retired Stop/Queue controls withdraw even
            // if no further stream event arrives. This does not assert producer completion.
            await Dispatcher.UIThread.InvokeAsync(Refresh).GetTask().ConfigureAwait(false);
        }
        catch (Exception projectionFailure) when (sourceFailure is not null)
        {
            throw new AggregateException("The original stop and its projection both failed.", sourceFailure, projectionFailure);
        }
        if (sourceFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(sourceFailure).Throw();
    }

    private async ValueTask ApplyActionModeAsync(string mode)
    {
        await _source.SetActionMode(mode).ConfigureAwait(false);
        await _source.CloseSettings().ConfigureAwait(false);
    }

    private async ValueTask ApplyResponseModeAsync(string mode)
    {
        await _source.SetResponseMode(mode).ConfigureAwait(false);
        await _source.CloseSettings().ConfigureAwait(false);
    }

    /// <summary>
    /// Owns the full emoji-insert action: the genuine admitted source insertion followed by the
    /// retained editor projection refresh. The original action stays pending until BOTH the source
    /// operation and the UI-owned projection have settled, so the picker-closing refresh can never
    /// publish a stale draft before the insertion changes it. The projection step is marshaled to
    /// the UI thread through the supported awaited dispatcher operation (never a fire-and-forget
    /// Post), so an insertion that resumes off-thread still projects through the real editor from
    /// the UI thread. The source insertion's own fault/lifetime is preserved and settles the action
    /// without the projection being skipped or swallowed.
    /// </summary>
    private async ValueTask InsertEmojiProjectedAsync(string emoji)
    {
        // 1. Genuine source insertion first. Keep its exact admitted fault/lifetime; the original
        //    action remains pending here until the source operation admits/retires.
        await _source.InsertEmoji(emoji).ConfigureAwait(false);

        // 2. Only after the source drafted, publish the picker-closing editor projection so the
        //    retained input reflects the inserted sequence. If the insertion faulted, this is not
        //    reached and the exact fault settles the original action instead.
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _emojiOpen = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }).GetTask().ConfigureAwait(false);
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (item is ChatComposerChip chip)
        {
            value = path switch
            {
                "Id" => chip.Id,
                "Label" => chip.Label,
                "Invokable" => chip.Invokable,
                _ => null
            };
            return value is not null;
        }
        if (item is ComposerEmoji emoji)
        {
            value = path switch
            {
                "Text" => emoji.Text,
                "Name" => emoji.Name,
                _ => null
            };
            return value is not null;
        }
        return false;
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

/// <summary>
/// The bounded, read-only eligibility query behind the composer's random actual-emoji trigger. It
/// draws from the real canonical Unicode catalogue and restricts the pool to an explicit neutral
/// curated allowlist (friendly expressions, animals, plants, weather, art/science), intersected
/// with the actual catalogue. Defence-in-depth also excludes every Flags-group sequence (generic,
/// country and subdivision) and an explicit owner-variation-selector-normalized suggestive
/// blacklist. The picker/search/insertion data remain the full catalogue; this pool governs only
/// the random-trigger icon. This is immutable catalogue data, never per-instance selection state.
/// </summary>
public static class ComposerEmojiIconPool
{
    /// <summary>Variation Selector-16 (FE0F), stripped for robust sequence matching.</summary>
    private const char VariationSelector = '\uFE0F';

    /// <summary>Explicit owner-specified and adjacent clearly-suggestive trigger sequences.
    /// Matching is variation-selector-normalized so a candidate with/without FE0F is still caught.</summary>
    private static readonly string[] Suggestive = new[]
    {
        "\U0001F92C", // 🤬 face with symbols over mouth
        "\U0001F4A6", // 💦 sweat droplets
        "\U0001F51E", // 🔞 no one under eighteen
        "\U0001F351", // 🍑 peach
        "\U0001F346", // 🍆 eggplant
        "\U0001F34C", // 🍌 banana
        "\U0001FAE6", // 🫦 biting lip
        "\U0001F37E", // 🍾 bottle with popping cork
        "\U0001F445", // 👅 tongue
        "\U0001F48B", // 💋 kiss mark
        "\U0001F352", // 🍒 cherries
        "\U0001F36D", // 🍭 lollipop
    };

    /// <summary>Neutral curated trigger sequences (friendly expressions, animals, plants, weather,
    /// art/science). Each is a real Unicode sequence; matching is normalize against the catalogue.</summary>
    private static readonly string[] Curated = new[]
    {
        // Friendly expressions
        "\U0001F600", "\U0001F603", "\U0001F604", "\U0001F642", "\U0001F60A",
        "\U0001F607", "\U0001F60C", "\U0001F63A", "\U0001F638", "\U0001F63D",
        "\U0001F44B", "\U0001F44D", "\U0001F44F", "\U0001F64C", "\U0001F917", "\U0001F91D",
        // Animals
        "\U0001F436", "\U0001F431", "\U0001F42D", "\U0001F439", "\U0001F430",
        "\U0001F98A", "\U0001F43B", "\U0001F43C", "\U0001F428", "\U0001F42F",
        "\U0001F981", "\U0001F42E", "\U0001F437", "\U0001F438", "\U0001F435",
        "\U0001F648", "\U0001F649", "\U0001F64A", "\U0001F984", "\U0001F427",
        "\U0001F426", "\U0001F424", "\U0001F98B", "\U0001F422", "\U0001F419",
        "\U0001F41A", "\U0001F420", "\U0001F42C", "\U0001F433", "\U0001F41D",
        "\U0001F994", "\U0001F415", "\U0001F408",
        // Plants
        "\U0001F331", "\U0001F33F", "\u2618", "\U0001F340", "\U0001F335",
        "\U0001F334", "\U0001F333", "\U0001F332", "\U0001F338", "\U0001F339",
        "\U0001F33B", "\U0001F33C", "\U0001F337", "\U0001F308", "\U0001F33E", "\U0001F342",
        // Weather
        "\u2600", "\u2601", "\u26C5", "\U0001F325", "\U0001F326",
        "\U0001F327", "\U0001F328", "\u2744", "\U0001F30A", "\U0001F32B",
        "\U0001F324", "\U0001F32C",
        // Art / science
        "\U0001F52C", "\U0001F52D", "\U0001F4DA", "\U0001F4D6", "\u270F",
        "\U0001F3A8", "\U0001F3B5", "\U0001F3B6", "\U0001F3B9", "\U0001F3BA",
        "\U0001F3BB", "\u2B50", "\u2728", "\U0001F31F", "\U0001F30D",
        "\U0001F9EA", "\U0001F9EB", "\U0001F9EC", "\U0001F4A1", "\U0001F48E",
    };

    private static readonly IReadOnlyList<string> Cached = Build();

    /// <summary>Eligible trigger sequences from the real catalogue (neutral curated allowlist ∩
    /// catalogue, all Flags excluded, variation-selector-normalized suggestive blacklist excluded).</summary>
    public static IReadOnlyList<string> Candidates => Cached;

    private static string Normalize(string value) => string.Concat(value.Where(ch => ch != VariationSelector));

    private static IReadOnlyList<string> Build()
    {
        var curated = Curated.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var blacklist = Suggestive.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var pool = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in ComposerEmojiCatalogue.Entries)
        {
            if (entry.Group.Equals("Flags", StringComparison.Ordinal)) continue;
            var normalized = Normalize(entry.Text);
            if (!curated.Contains(normalized)) continue;
            if (blacklist.Contains(normalized)) continue;
            pool.Add(entry.Text);
        }
        return pool.Count == 0 ? pool.ToArray() : ComposerEmojiCatalogue.Entries
            .Select(entry => entry.Text).Where(pool.Contains).Distinct(StringComparer.Ordinal).ToArray();
    }
}

/// <summary>Immediate readiness for the shared composer scene (the host page owns deeper readiness).</summary>
internal sealed class ChatComposerReadiness : ICuiSceneReadiness
{
    public static readonly ChatComposerReadiness Instance = new();
    private ChatComposerReadiness() { }
    public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
        new(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Chat composer is ready."));
}

/// <summary>
/// The single shared canonical Chat composer view. It owns the composer loader/scene host and the
/// typed presentation adapter, rendering <c>ChatComposer.cui</c>. Both real consumers mount this
/// same view: the newer ChatSpace page (through an authored CUI mount slot) and the Haven Chat
/// native scene (through a narrow native-control transport). It also exposes the typed composer
/// input port for the shared engine.
/// </summary>
public sealed class ChatComposerCuiView : ContentControl, IDisposable, IAsyncDisposable
{
    private readonly CuiSceneHost _host;
    private readonly ChatComposerBindings _bindings;
    private readonly CuiComposerInputPort _input;
    private readonly object _initGate = new();
    private Task<CuiSceneAvailability>? _initializeTask;
    /// <summary>The EXACT raw host Show task retained for loader settlement capture. This is a
    /// distinct identity from the public guarded initialization wrapper; only this raw task can
    /// be handed to <see cref="CuiSceneHost.CaptureOriginalActionSettlement"/>.</summary>
    private Task<CuiSceneAvailability>? _rawShowTask;
    private readonly object _closeGate = new();
    private Task? _closeTask;
    private bool _closing;
    private bool _disposed;

    public ChatComposerCuiView(IChatComposerSource source, string? owningAppId = null,
        CuiThemePreferenceContext? themePreference = null)
        : this(source, ChatComposerReadiness.Instance, owningAppId, themePreference)
    {
    }

    /// <summary>Readiness injection seam used by tests to hold/observe the real host show
    /// pipeline; defaults to production readiness. The SAME production host/loader/document/input
    /// are exercised, never an alternate renderer.</summary>
    internal ChatComposerCuiView(IChatComposerSource source, ICuiSceneReadiness readiness,
        string? owningAppId = null, CuiThemePreferenceContext? themePreference = null)
    {
        _bindings = new ChatComposerBindings(source ?? throw new ArgumentNullException(nameof(source)),
            sender => ReferenceEquals(sender, FindComposerTextBox()));
        _readiness = readiness ?? ChatComposerReadiness.Instance;
        var registry = new CuiControlRegistry();
        // Shared typed packaged-SVG image infrastructure for authored icon assets (e.g. the enlarged
        // rounded plus). This is the one registered image-source resolver for the shared composer.
        registry.RegisterImageSourceResolver(HavenPackagedSvgResolver.Resolve);
        _host = new CuiSceneHost(registry);
        AutomationProperties.SetAutomationId(this, "ChatComposerCuiView");
        AutomationProperties.SetName(this, "Chat composer");
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        Content = _host;
        _input = new CuiComposerInputPort(() => FindComposerTextBox(), focused => _bindings.IsFocused = focused);
        _owningAppId = owningAppId;
        _themePreference = themePreference;
        _ = InitializeAsync();
    }

    private readonly string? _owningAppId;
    private readonly CuiThemePreferenceContext? _themePreference;
    private readonly ICuiSceneReadiness _readiness;

    public IChatComposerInputPort Input => _input;
    public ChatComposerBindings Bindings => _bindings;
    public IChatComposerSource Source => _bindings.Source;

    /// <summary>Idempotent guarded initialization: the real scene show plus real input wiring.
    /// Faults/cancellation reach this same task; readiness is never claimed before the input is
    /// actually attached. Parents join it before publication/restoration.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_initGate)
        {
            if (_initializeTask is null)
            {
                var scene = new CuiNativeScene(
                    _owningAppId ?? "spaces.chat.composer", "Chat Composer", "Chat",
                    ChatComposerCuiDocument.Load(), _bindings, _bindings, _readiness)
                {
                    ThemePreference = _themePreference,
                    IsPublicationCurrent = () => !_closing
                };
                // Retain the EXACT raw host Show task for loader settlement capture; the public
                // guarded initialization wrapper is a distinct task identity.
                var rawShow = _host.ShowAsync(scene, cancellationToken);
                _rawShowTask = rawShow;
                _initializeTask = ShowGuardedAsync(rawShow, cancellationToken);
            }
            return _initializeTask;
        }
    }

    private async Task<CuiSceneAvailability> ShowGuardedAsync(Task<CuiSceneAvailability> rawShow, CancellationToken cancellationToken)
    {
        var availability = await rawShow.ConfigureAwait(false);
        if (_closing) return availability;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_closing) AttachInputOnce();
        }).GetTask().ConfigureAwait(false);
        return availability;
    }

    /// <summary>
    /// Captures the ACTUAL loader action-settlement pipeline for actions admitted on this
    /// composer, using the exact original raw Show task. External callers join this snapshot
    /// outside any included action (an action must never await a snapshot containing itself).
    /// Requires UI-thread access, a successful guarded initialization/input attachment AND an
    /// exact retained raw host Show; raw Show ready alone does not mean the real input is wired,
    /// so capture is refused before that guarded attachment succeeds, after withdrawal/close, or
    /// when no retained raw Show exists.
    /// </summary>
    public Task CaptureOriginalActionSettlement()
    {
        Dispatcher.UIThread.VerifyAccess();
        Task<CuiSceneAvailability>? rawShow;
        Task<CuiSceneAvailability>? initTask;
        lock (_initGate)
        {
            rawShow = _rawShowTask;
            initTask = _initializeTask;
        }
        if (rawShow is null || _closing || initTask is null || !initTask.IsCompletedSuccessfully)
            throw new InvalidOperationException("The composer has no completed live initialization to capture from.");
        return _host.CaptureOriginalActionSettlement(rawShow);
    }

    /// <summary>Publishes one child close task BEFORE any close callback can reenter, mirroring the
    /// host start-gate pattern. The parent joins this exact task during cleanup; repeated close
    /// requests share the SAME task, and no duplicate work is admitted after it.</summary>
    public Task CloseAsync()
    {
        lock (_closeGate)
        {
            if (_closeTask is not null) return _closeTask;
            _closing = true;
            _input.Withdraw(); // producer retirement begins before any close callback reentry
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _closeTask = CloseOriginalCoreAsync(start.Task);
            start.SetResult(); // original is published before the close body/callbacks run
            return _closeTask;
        }
    }

    private async Task CloseOriginalCoreAsync(Task start)
    {
        await start.ConfigureAwait(false);
        // Withdraw producers/action admission first: start the host close independently of the
        // initialization, so a cancellation or fault in init can never skip withdrawal.
        var hostClose = _host.CloseOriginalAsync();
        var faults = new List<Exception>();

        // Join the complete actual initialization independently, retaining its fault.
        Task<CuiSceneAvailability>? initTask;
        lock (_initGate) initTask = _initializeTask;

        try { await hostClose.ConfigureAwait(false); }
        catch (Exception error) { faults.Add(error); }

        if (initTask is not null)
        {
            try { await initTask.ConfigureAwait(false); }
            catch (Exception error) { faults.Add(error); }
        }

        // Physical input watcher detach happens ONLY after both the host close and initialization
        // acknowledged (positive ACK). A faulted close is not approval: references stay owned for
        // the caller and no terminal Detach hides the failure. The release itself is marshaled to
        // the UI thread because these are real TextBox subscriptions/visual-tree operations.
        if (faults.Count == 0)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _input.Detach()).GetTask().ConfigureAwait(false);
        }

        if (faults.Count == 1) throw faults[0];
        if (faults.Count > 1) throw new AggregateException(faults);
    }

    private void AttachInputOnce()
    {
        var box = FindComposerTextBox();
        if (box is null) return;
        _input.Attach(box);
    }

    private TextBox? FindComposerTextBox()
    {
        if (!ReferenceEquals(Content, _host) || _host.Content is not Visual root) return null;
        foreach (var visual in root.GetVisualDescendants())
        {
            if (visual is TextBox box &&
                AutomationProperties.GetAutomationId(box) == "composer-input")
                return box;
        }
        return null;
    }

    /// <summary>Republishes host state into the composer scene (Send/Safety/chips/placeholder).</summary>
    public void RefreshSurface() => _bindings.Refresh();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { CloseAsync(); } catch { /* Best-effort synchronous teardown; joins observe faults. */ }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return new ValueTask(CloseAsync());
        _disposed = true;
        return new ValueTask(CloseAsync());
    }
}

/// <summary>
/// Adapter from the real CUI composer input (the rendered <see cref="TextBox"/>) to the typed
/// composer input port. Reads and writes target the actual canonical input; the input is resolved
/// lazily because the composer scene loads asynchronously. Named watchers are detached on
/// retirement and retained textbox references are cleared so no delayed attachment can publish
/// after close.
/// </summary>
internal sealed class CuiComposerInputPort : IChatComposerInputPort
{
    private readonly Func<TextBox?> _resolve;
    private readonly Action<bool>? _onFocusChanged;
    private TextBox? _attached;
    private string? _pendingText;
    private bool _released;
    private volatile bool _withdrawn;
    private EventHandler<TextChangingEventArgs>? _textChangingHandler;
    private CuiCompoundFocusScope? _focusScope;
    private EventHandler<bool>? _focusScopeHandler;
    private int _attachVersion;

    public CuiComposerInputPort(Func<TextBox?> resolve, Action<bool>? onFocusChanged = null)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _onFocusChanged = onFocusChanged;
    }

    /// <summary>Attaches the real input. The first attach keeps the input live; a later resolved
    /// input replaces watchers without releasing the port. After terminal <see cref="Detach"/>
    /// retirement or retirement onset (<see cref="Withdraw"/>) any late attach is refused.</summary>
    internal void Attach(TextBox box)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_released || _withdrawn) return;
        if (ReferenceEquals(box, _attached)) return;
        ReplaceWatchers(box);
    }

    /// <summary>
    /// Generic producer-retirement gate, separate from terminal physical <see cref="Detach"/>.
    /// Called when close begins: it stops any late attach, text/selection/caret/focus mutation and
    /// watcher/focus producer callbacks from reaching the real TextBox, while RETAINING the retained
    /// input/watcher/mapping custody so failed/pending close can be recovered by the owning caller.
    /// Terminal unsubscribe and reference clearing only ever happen later on a successful ACK via
    /// <see cref="Detach"/>.
    /// </summary>
    internal void Withdraw()
    {
        // This gate may be set by an external close owner on another thread. Keep all physical
        // watcher/input custody until the acknowledged UI-thread Detach below. Removing a handler
        // here would neither stop its already-running invocation nor preserve UI-thread ownership.
        _withdrawn = true;
    }

    private void ReplaceWatchers(TextBox box)
    {
        if (_attached is { } previous && !ReferenceEquals(previous, box))
        {
            if (_textChangingHandler is not null) previous.TextChanging -= _textChangingHandler;
        }
        _attached = box;
        _attachVersion++;
        if (_pendingText is not null)
        {
            box.Text = _pendingText;
            _pendingText = null;
        }
        // The fork's TextBox raises TextChanging SYNCHRONOUSLY after the Text property is set
        // (rendering not required), while TextChanged is posted to the dispatcher and only runs
        // later. An edit followed immediately by the queue/send clear must be observed now, so
        // the real value is captured before the clear: this is the actual edit signal.
        _textChangingHandler = (_, _) =>
        {
            NotifyLiveInputSubscribers(TextChanged);
            NotifyLiveInputSubscribers(Invalidated);
        };
        box.TextChanging += _textChangingHandler;
        ObserveCompoundFocus(box);
    }

    /// <summary>
    /// Focus membership is observed through the generic per-container compound-focus scope rather
    /// than a deferred/retired input LostFocus callback. The scope reports the ACTUAL owning
    /// top-level focus including descendant menu popups, Escape, outside click/focus, unrelated
    /// menus, child removal and close, so the expanded composer surface never collapses during a
    /// genuine toolbar interaction and never stays stuck focused after focus truly leaves.
    /// </summary>
    private void ObserveCompoundFocus(TextBox box)
    {
        UnobserveCompoundFocus();
        if (_released || _withdrawn) return;
        var scope = FindCompoundFocusScope(box)?.CompoundFocusScope;
        if (scope is null)
        {
            _onFocusChanged?.Invoke(false);
            return;
        }
        _focusScope = scope;
        var capturedVersion = _attachVersion;
        var capturedBox = box;
        _focusScopeHandler = (_, focused) =>
        {
            // Reentrant port custody: a preceding scope observer may synchronously reattach the
            // port to another scope/box during the same multicast event. Removing this scope's
            // subscription does not remove its already-captured callback, so validate the captured
            // scope, the actual attached box AND the attachment generation before publishing.
            // B's current fact must survive a late A callback.
            if (_released || _withdrawn) return;
            if (!ReferenceEquals(_focusScope, scope)) return;
            if (!ReferenceEquals(_attached, capturedBox)) return;
            if (_attachVersion != capturedVersion) return;
            _onFocusChanged?.Invoke(focused);
        };
        scope.FocusChanged += _focusScopeHandler;
        _onFocusChanged?.Invoke(scope.IsFocused);
    }

    private void UnobserveCompoundFocus()
    {
        if (_focusScope is not null && _focusScopeHandler is not null)
            _focusScope.FocusChanged -= _focusScopeHandler;
        _focusScope = null;
        _focusScopeHandler = null;
    }

    private void NotifyLiveInputSubscribers(EventHandler? subscribers)
    {
        if (subscribers is null) return;
        foreach (EventHandler subscriber in subscribers.GetInvocationList())
        {
            // A subscriber may synchronously close the owner. Recheck before EACH subsequent
            // callback, including the transition from TextChanged to Invalidated.
            if (_released || _withdrawn) return;
            subscriber(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Walks up from the real input to the generic compound-focus scope container (the authored
    /// surface that owns the outer focus ring). Returns null when no compound scope is present; a
    /// degraded scene then conservatively reports lost focus.
    /// </summary>
    private static CuiContainerControl? FindCompoundFocusScope(TextBox box)
    {
        Visual? current = box;
        while (current is not null)
        {
            if (current is CuiContainerControl container && container.CompoundFocusEnabled) return container;
            current = current.GetVisualParent();
        }
        return null;
    }

    /// <summary>Terminal release: detaches every named watcher, clears retained textbox
    /// references and refuses any later attach. Used only on retirement/close.</summary>
    internal void Detach()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_attached is { } box)
        {
            if (_textChangingHandler is not null) box.TextChanging -= _textChangingHandler;
        }
        UnobserveCompoundFocus();
        _textChangingHandler = null;
        _attached = null;
        _pendingText = null;
        _released = true;
    }

    private TextBox? Current
    {
        get
        {
            if (_released) return null;
            if (_attached is not null) return _attached; // retained for reads/recovery after withdrawal
            if (_withdrawn) return null;                  // no late attach after retirement onset
            var box = _resolve();
            if (box is not null) Attach(box);
            return box;
        }
    }

    public string Text
    {
        get
        {
            if (_released) return string.Empty;
            return Current?.Text ?? _pendingText ?? string.Empty;
        }
        set
        {
            // After retirement onset or terminal release no delayed write may reach the real box.
            if (_released || _withdrawn) return;
            if (Current is { } box) box.Text = value ?? string.Empty;
            else _pendingText = value ?? string.Empty;
        }
    }

    public int CaretIndex => Current?.CaretIndex ?? 0;
    public int SelectionStart => Current?.SelectionStart ?? 0;
    public int SelectionLength
    {
        get
        {
            if (Current is not { } box) return 0;
            return Math.Max(0, box.SelectionEnd - box.SelectionStart);
        }
    }
    public string SelectedText => Current?.SelectedText ?? string.Empty;
    public bool IsFocused => Current?.IsKeyboardFocusWithin ?? false;

    public event EventHandler? TextChanged;
    public event EventHandler? Invalidated;

    public void PlaceCaretAtEnd(bool extendSelection = false)
    {
        if (_released || _withdrawn) return;
        if (Current is not { } box) return;
        var length = box.Text?.Length ?? 0;
        if (extendSelection)
        {
            box.SelectionEnd = length;
        }
        else
        {
            box.CaretIndex = length;
            box.SelectionStart = length;
            box.SelectionEnd = length;
        }
    }

    public void SetSelection(int anchor, int caret)
    {
        if (_released || _withdrawn) return;
        if (Current is not { } box) return;
        var length = box.Text?.Length ?? 0;
        var start = Math.Clamp(anchor, 0, length);
        var end = Math.Clamp(caret, 0, length);
        // Normalize to the bounded selection region so SelectionLength stays non-negative and
        // deterministic regardless of the control's own clamping/clamping ordering.
        box.SelectionStart = Math.Min(start, end);
        box.SelectionEnd = Math.Max(start, end);
    }

    public void ClearSelection()
    {
        if (_released || _withdrawn) return;
        if (Current is not { } box) return;
        var caret = Math.Clamp(box.CaretIndex, 0, box.Text?.Length ?? 0);
        box.SelectionStart = caret;
        box.SelectionEnd = caret;
    }

    public void Focus()
    {
        if (_released || _withdrawn) return;
        Current?.Focus();
    }
}
