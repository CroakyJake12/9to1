using System.Text.Json;
using HavenOS.Home.Core;

namespace HavenOS.Home.PermissionsTrustNotifications;

/// <summary>
/// Persistent Home notification history and preference provider. Clear keeps records in history
/// and dismisses them from the active view because the canonical retention period is not specified.
/// </summary>
public sealed class HomeNotificationFeatureProvider : IHomeNotificationFeatureProvider
{
    private const string StateRecordId = "home.notifications";
    private const string StateRecordType = "home.notifications";
    private const int StateSchemaVersion = 1;
    private readonly IHomeCoreStateStore _stateStore;
    private readonly IHomeFeatureNavigationHost _navigation;
    private readonly Func<string, CancellationToken, Task<bool>> _mayPublish;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HomeNotificationFeatureProvider(
        IHomeCoreStateStore stateStore,
        IHomeFeatureNavigationHost navigation,
        Func<string, CancellationToken, Task<bool>> mayPublish,
        TimeProvider? timeProvider = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _mayPublish = mayPublish ?? throw new ArgumentNullException(nameof(mayPublish));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<HomeCoreOperationResult<HomeNotificationPage>> GetSnapshotAsync(
        HomeNotificationQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query is null) return Failure<HomeNotificationPage>("NotificationQueryInvalid", "A notification query is required.");
        if (query.Offset < 0 || query.Limit is < 1 or > 200 ||
            query.FromUtc is { } from && query.ToUtc is { } to && from > to)
            return Failure<HomeNotificationPage>("NotificationQueryInvalid", "Notification history page or date range is invalid.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var matches = state.Notifications.Where(item => Matches(item, query))
                .OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.NotificationId, StringComparer.Ordinal).ToArray();
            var items = matches.Skip(query.Offset).Take(query.Limit + 1).ToArray();
            return Success(new HomeNotificationPage(items.Take(query.Limit).ToArray(), query.Offset, query.Limit,
                items.Length > query.Limit, state.DomainRevision), "Notification history loaded.", state.DomainRevision);
        }
        catch (HomeFeatureStoreException exception)
        {
            return Failure<HomeNotificationPage>(exception.Code, exception.Message, recoverable: true);
        }
        finally { _gate.Release(); }
    }

    public async Task<HomeCoreOperationResult<HomeNotificationRecord>> PublishAsync(
        HomeNotificationRecord notification,
        CancellationToken cancellationToken = default)
    {
        if (notification is null) return Failure<HomeNotificationRecord>("NotificationInvalid", "A notification is required.");
        if (!TryValidate(notification, out var normalized, out var validationMessage))
            return Failure<HomeNotificationRecord>("NotificationInvalid", validationMessage);
        bool allowed;
        try { allowed = await _mayPublish(normalized.SourceId, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { allowed = false; }
        if (!allowed) return Failure<HomeNotificationRecord>("PermissionDenied", "Home did not authorize this source to publish notifications.", recoverable: false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (state.Notifications.Any(item => item.NotificationId == normalized.NotificationId))
                return Failure<HomeNotificationRecord>("NotificationIdConflict", "A notification with this identity already exists.", recoverable: false);
            var stored = normalized with { CreatedAtUtc = _timeProvider.GetUtcNow(), IsRead = false, IsDismissed = false, Revision = 1 };
            state.Notifications.Add(stored);
            state.DomainRevision = checked(state.DomainRevision + 1);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return Success(stored, "Notification published to Home history.", state.DomainRevision);
        }
        catch (HomeFeatureStoreException exception)
        {
            return Failure<HomeNotificationRecord>(exception.Code, exception.Message, recoverable: true);
        }
        finally { _gate.Release(); }
    }

    public Task<HomeCoreOperationResult<HomeNotificationRecord>> MarkReadAsync(
        string notificationId,
        long expectedRevision,
        CancellationToken cancellationToken = default) =>
        UpdateNotificationAsync(notificationId, expectedRevision, item => item with { IsRead = true }, cancellationToken);

    public Task<HomeCoreOperationResult<HomeNotificationRecord>> DismissAsync(
        string notificationId,
        long expectedRevision,
        CancellationToken cancellationToken = default) =>
        UpdateNotificationAsync(notificationId, expectedRevision, item => item with { IsRead = true, IsDismissed = true }, cancellationToken);

    public async Task<HomeCoreOperationResult<int>> ClearAsync(
        HomeNotificationQuery query,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (query is null) return Failure<int>("NotificationQueryInvalid", "A notification query is required.");
        if (query.Offset < 0 || query.Limit is < 1 or > 200)
            return Failure<int>("NotificationQueryInvalid", "Notification history page is invalid.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (state.DomainRevision != expectedRevision)
                return Failure<int>("HomeStateConflict", "Notification history changed before it could be cleared; refresh and try again.");
            var changed = 0;
            for (var index = 0; index < state.Notifications.Count; index++)
            {
                var item = state.Notifications[index];
                if (!Matches(item, query) || item.IsDismissed) continue;
                state.Notifications[index] = item with { IsRead = true, IsDismissed = true, Revision = checked(item.Revision + 1) };
                changed++;
            }
            if (changed == 0) return Success(0, "No matching notifications needed clearing.", state.DomainRevision);
            state.DomainRevision = checked(state.DomainRevision + 1);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return Success(changed, "Matching notifications were cleared from the active view and retained in history.", state.DomainRevision);
        }
        catch (HomeFeatureStoreException exception) { return Failure<int>(exception.Code, exception.Message, recoverable: true); }
        finally { _gate.Release(); }
    }

    public async Task<HomeCoreOperationResult<HomeNotificationPreferences>> UpdatePreferencesAsync(
        HomeNotificationPreferences preferences,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (preferences is null || string.IsNullOrWhiteSpace(preferences.SourceId) || preferences.EnabledCategories is null ||
            preferences.EnabledCategories.Any(category => string.IsNullOrWhiteSpace(category)))
            return Failure<HomeNotificationPreferences>("NotificationPreferencesInvalid", "Notification preferences require a source and valid category IDs.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (state.DomainRevision != expectedRevision)
                return Failure<HomeNotificationPreferences>("HomeStateConflict", "Notification preferences changed; refresh and try again.");
            var sourceId = preferences.SourceId.Trim();
            var current = state.Preferences.FirstOrDefault(item => item.SourceId == sourceId);
            var updated = preferences with
            {
                SourceId = sourceId,
                EnabledCategories = preferences.EnabledCategories.Select(item => item.Trim()).ToHashSet(StringComparer.Ordinal),
                Revision = checked((current?.Revision ?? 0) + 1),
            };
            if (current is null) state.Preferences.Add(updated);
            else state.Preferences[state.Preferences.IndexOf(current)] = updated;
            state.DomainRevision = checked(state.DomainRevision + 1);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return Success(updated, "Notification preferences saved.", state.DomainRevision);
        }
        catch (HomeFeatureStoreException exception) { return Failure<HomeNotificationPreferences>(exception.Code, exception.Message, recoverable: true); }
        finally { _gate.Release(); }
    }

    public async Task<HomeCoreOperationResult<HomeFeatureNavigationRequest>> ResolveDeepLinkAsync(
        HomeNotificationDeepLinkRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.NotificationId) || string.IsNullOrWhiteSpace(request.ActionId))
            return Failure<HomeFeatureNavigationRequest>("NotificationActionInvalid", "A notification and typed action identity are required.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var notification = state.Notifications.FirstOrDefault(item => item.NotificationId == request.NotificationId);
            var action = notification?.Actions.FirstOrDefault(item => item.ActionId == request.ActionId);
            if (action is null)
                return Failure<HomeFeatureNavigationRequest>("NotificationActionNotFound", "That notification action is no longer available.", recoverable: false);
            if (!string.Equals(action.ActionName, "open", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action.ActionName, "navigate", StringComparison.OrdinalIgnoreCase))
                return Failure<HomeFeatureNavigationRequest>("NotificationActionUnsupported", "Notifications can navigate to an entity but cannot execute arbitrary consequential actions.", recoverable: false);

            var target = action.TargetApp.Trim();
            var routeId = target.StartsWith("home.", StringComparison.Ordinal) || target.StartsWith("app.", StringComparison.Ordinal)
                ? target
                : "app." + target;
            if (!routeId.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
                return Failure<HomeFeatureNavigationRequest>("NotificationDeepLinkInvalid", "The notification target is not a valid Home route.", recoverable: false);
            action.Arguments.TryGetValue("entityType", out var entityType);
            action.Arguments.TryGetValue("entityId", out var entityId);
            var navigationRequest = new HomeFeatureNavigationRequest(routeId, entityType, entityId, "open");
            if (!_navigation.AvailableRoutes.Contains(routeId, StringComparer.Ordinal))
                return Failure<HomeFeatureNavigationRequest>("HomeServiceUnavailable", "The target app destination is not currently registered in Home.");
            return Success(navigationRequest, "The notification deep link resolved to a registered Home route.", state.DomainRevision);
        }
        catch (HomeFeatureStoreException exception) { return Failure<HomeFeatureNavigationRequest>(exception.Code, exception.Message, recoverable: true); }
        finally { _gate.Release(); }
    }

    private async Task<HomeCoreOperationResult<HomeNotificationRecord>> UpdateNotificationAsync(
        string notificationId,
        long expectedRevision,
        Func<HomeNotificationRecord, HomeNotificationRecord> update,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notificationId))
            return Failure<HomeNotificationRecord>("NotificationIdInvalid", "A notification ID is required.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var index = state.Notifications.FindIndex(item => item.NotificationId == notificationId);
            if (index < 0) return Failure<HomeNotificationRecord>("NotificationNotFound", "The notification no longer exists.", recoverable: false);
            var current = state.Notifications[index];
            if (current.Revision != expectedRevision)
                return Failure<HomeNotificationRecord>("HomeStateConflict", "The notification changed before the update; refresh and try again.");
            var candidate = update(current);
            if (candidate == current) return Success(current, "Notification state is already current.", state.DomainRevision);
            var updated = candidate with { Revision = checked(current.Revision + 1) };
            state.Notifications[index] = updated;
            state.DomainRevision = checked(state.DomainRevision + 1);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return Success(updated, "Notification state updated.", state.DomainRevision);
        }
        catch (HomeFeatureStoreException exception) { return Failure<HomeNotificationRecord>(exception.Code, exception.Message, recoverable: true); }
        finally { _gate.Release(); }
    }

    private async Task<State> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await _stateStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess) throw new HomeFeatureStoreException(result.Failure!.Code.ToString(), result.Failure.Message);
        var record = result.State!.Records.SingleOrDefault(item => item.RecordId == StateRecordId);
        if (record is null) return new State();
        if (record.RecordType != StateRecordType || record.SchemaVersion != StateSchemaVersion)
            throw new HomeFeatureStoreException("HOME_STATE_VERSION_UNSUPPORTED", "Saved notification state has an incompatible schema.");
        try
        {
            var state = record.Payload.Deserialize<State>() ?? throw new HomeFeatureStoreException("HOME_STATE_INVALID", "Saved notification state has no payload.");
            state.RecordRevision = record.Revision;
            if (state.Notifications is null || state.Preferences is null || state.DomainRevision < 0)
                throw new HomeFeatureStoreException("HOME_STATE_INVALID", "Saved notification state is incomplete.");
            return state;
        }
        catch (JsonException exception)
        {
            throw new HomeFeatureStoreException("HOME_STATE_CORRUPT", "Saved notification state could not be decoded and was preserved.", exception);
        }
    }

    private async Task SaveAsync(State state, CancellationToken cancellationToken)
    {
        var record = new HomeCoreStateRecord(StateRecordId, StateRecordType, StateSchemaVersion,
            HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, state.RecordRevision,
            JsonSerializer.SerializeToElement(state));
        var result = await _stateStore.WriteAsync(record, state.RecordRevision, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess) throw new HomeFeatureStoreException(result.Failure!.Code.ToString(), result.Failure.Message);
        state.RecordRevision = result.State!.Records.Single(item => item.RecordId == StateRecordId).Revision;
    }

    private static bool Matches(HomeNotificationRecord item, HomeNotificationQuery query) =>
        (query.SourceId is null || item.SourceId == query.SourceId) &&
        (query.Category is null || item.Category == query.Category) &&
        (query.IsRead is null || item.IsRead == query.IsRead) &&
        (query.IsDismissed is null || item.IsDismissed == query.IsDismissed) &&
        (query.FromUtc is null || item.CreatedAtUtc >= query.FromUtc) &&
        (query.ToUtc is null || item.CreatedAtUtc <= query.ToUtc);

    private static bool TryValidate(HomeNotificationRecord value, out HomeNotificationRecord normalized, out string message)
    {
        normalized = value;
        message = "";
        if (string.IsNullOrWhiteSpace(value.SourceId) || string.IsNullOrWhiteSpace(value.Category) ||
            string.IsNullOrWhiteSpace(value.Severity) || string.IsNullOrWhiteSpace(value.Title) ||
            value.Actions is null || value.Actions.Any(action => action is null || string.IsNullOrWhiteSpace(action.ActionId) ||
                string.IsNullOrWhiteSpace(action.TargetApp) || string.IsNullOrWhiteSpace(action.ActionName) || action.Arguments is null))
        {
            message = "A notification requires source, category, severity, title and valid typed actions.";
            return false;
        }
        if (value.Body is null || value.Title.Length > 240 || value.Body.Length > 8_000 ||
            value.Actions.Select(action => action.ActionId).Distinct(StringComparer.Ordinal).Count() != value.Actions.Count)
        {
            message = "Notification text or action identities exceed supported limits.";
            return false;
        }
        var ids = string.IsNullOrWhiteSpace(value.NotificationId) ? Guid.NewGuid().ToString("N") : value.NotificationId.Trim();
        normalized = value with
        {
            NotificationId = ids,
            SourceId = value.SourceId.Trim(),
            Category = value.Category.Trim(),
            Severity = value.Severity.Trim(),
            Title = value.Title.Trim(),
            Body = value.Body ?? string.Empty,
            Actions = value.Actions.Select(action => action with
            {
                ActionId = action.ActionId.Trim(),
                TargetApp = action.TargetApp.Trim(),
                ActionName = action.ActionName.Trim(),
                Arguments = action.Arguments.ToDictionary(pair => pair.Key.Trim(), pair => pair.Value?.Trim() ?? string.Empty, StringComparer.Ordinal),
            }).ToArray(),
        };
        return true;
    }

    private static HomeCoreOperationResult<T> Success<T>(T value, string message, long revision) =>
        new(true, "Succeeded", message, value, true, revision);

    private static HomeCoreOperationResult<T> Failure<T>(string code, string message, bool recoverable = true) =>
        new(false, code, message, default, recoverable);

    private sealed class State
    {
        public List<HomeNotificationRecord> Notifications { get; set; } = [];
        public List<HomeNotificationPreferences> Preferences { get; set; } = [];
        public long DomainRevision { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public long RecordRevision { get; set; }
        public State() { }
    }
}
