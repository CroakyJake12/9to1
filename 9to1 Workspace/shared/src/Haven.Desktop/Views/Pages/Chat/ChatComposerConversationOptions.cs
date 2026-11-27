using Avalonia.Threading;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>A read-only observation of the current chat's existing lifecycle owners.</summary>
public sealed record ChatComposerConversationOptions(
    bool IsTemporary,
    bool CanChangeTemporary,
    string TemporaryStatus,
    bool CanCompactContext,
    string CompactionStatus,
    string StorageStatus);

/// <summary>Optional operations supplied by the actual conversation. A shared composer without
/// this owner does not manufacture Temporary Chat or compaction controls.</summary>
public interface IChatComposerConversationActions
{
    ChatComposerConversationOptions? Current { get; }
    /// <summary>Observe the existing context owner before a new popup session; never starts inference.</summary>
    ValueTask RefreshAsync() => ValueTask.CompletedTask;
    ValueTask SetTemporary(bool temporary);
    ValueTask CompactContext();
}

public sealed partial class ChatComposerBindings
{
    private IChatComposerConversationActions? _optionsConversationOwner;
    private long _conversationOptionGeneration;
    private long _activeConversationOption;
    private bool _permissionsExpanded;
    internal bool OptionsOpenedFromPermissions { get; private set; }

    private bool HasCurrentConversationOptions => HasCurrentOptions && !_source.IsSafetyLocked &&
        Volatile.Read(ref _activeConversationOption) == 0 && _optionsConversationOwner is not null &&
        ReferenceEquals(_optionsConversationOwner, _source.ConversationActions);

    private bool TryGetConversationOptionValue(string path, out object? value)
    {
        value = null;
        switch (path)
        {
            case "Composer.PermissionsExpanded": value = _permissionsExpanded; return true;
            case "Composer.PermissionsHeading": value = _permissionsExpanded ? "Permissions  ▾" : "Permissions  ▸"; return true;
            case "Composer.PermissionLabel":
                value = _source.ActionMode switch
                {
                    Haven.Core.ChatActionMode.JustChat => "Just chat",
                    Haven.Core.ChatActionMode.AllowAllActions => "All supported actions",
                    _ => "Basic actions"
                };
                return true;
            case "Composer.HasConversationOptions": value = _source.ConversationActions?.Current is not null; return true;
            case "Composer.TemporaryLabel": value = _source.ConversationActions?.Current?.IsTemporary == true ? "Temporary · On" : "Temporary · Off"; return true;
            case "Composer.IsTemporary": value = _source.ConversationActions?.Current?.IsTemporary == true; return true;
            case "Composer.CanChangeTemporary": value = HasCurrentConversationOptions && _optionsConversationOwner?.Current?.CanChangeTemporary == true; return true;
            case "Composer.TemporaryStatus": value = _source.ConversationActions?.Current?.TemporaryStatus ?? string.Empty; return true;
            case "Composer.CanCompactContext": value = HasCurrentConversationOptions && _optionsConversationOwner?.Current?.CanCompactContext == true; return true;
            case "Composer.CompactionStatus": value = _source.ConversationActions?.Current?.CompactionStatus ?? string.Empty; return true;
            case "Composer.StorageStatus": value = _source.ConversationActions?.Current?.StorageStatus ?? string.Empty; return true;
            default: return false;
        }
    }

    private ValueTask OpenPermissionsAsync()
    {
        if (!_settingsOpen) return OpenOptionsAsync(permissions: true);
        if (!HasCurrentOptions)
            return ValueTask.FromException(new InvalidOperationException("The original conversation options are no longer current."));
        _permissionsExpanded = true;
        Refresh();
        return ValueTask.CompletedTask;
    }

    private async ValueTask RunConversationOptionAsync(bool compact)
    {
        var owner = _optionsConversationOwner;
        if (!HasCurrentConversationOptions || owner?.Current is not { } state ||
            (compact ? !state.CanCompactContext : !state.CanChangeTemporary))
            throw new InvalidOperationException("This conversation action is no longer available. The draft was retained.");

        var generation = _optionsGeneration;
        var operation = Interlocked.Increment(ref _conversationOptionGeneration);
        if (Interlocked.CompareExchange(ref _activeConversationOption, operation, 0) != 0)
            throw new InvalidOperationException("A conversation action is still running. The draft was retained.");
        var failures = new List<Exception>();
        Task? actual = null;
        try
        {
            // Retain the admitted task before popup dismissal can invoke focus/publication
            // callbacks. A dismissal fault must not abandon the already-running source.
            try { actual = (compact ? owner.CompactContext() : owner.SetTemporary(!state.IsTemporary)).AsTask(); }
            catch (Exception error) { failures.Add(error); }
            if (actual is not null)
            {
                try
                {
                    if (_settingsOpen && generation == _optionsGeneration && ReferenceEquals(owner, _optionsConversationOwner))
                        DismissOptions();
                }
                catch (Exception error) { failures.Add(error); }
                try { await actual.ConfigureAwait(false); }
                catch (Exception error) { failures.Add(error); }
            }
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Refresh may synchronously open and admit another action. Only this
                    // operation can release its marker, including in the finally below.
                    if (Interlocked.CompareExchange(ref _activeConversationOption, 0, operation) == operation)
                        Refresh();
                }).GetTask().ConfigureAwait(false);
            }
            catch (Exception error) { failures.Add(error); }
        }
        finally { Interlocked.CompareExchange(ref _activeConversationOption, 0, operation); }
        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException("The original conversation action or its presentation could not settle cleanly.", failures);
    }
}
