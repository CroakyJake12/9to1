using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

internal enum NewTabLayoutPreset { Focus, Balanced, MoreWidgets }
internal enum NewTabBackgroundPreset { Neutral, Horizon, Paper }

internal sealed record NewTabWidgetPlacement(string Id, string SourceId, bool IsVisible = true,
    bool IsWide = false, bool IsPinned = false, bool IsLocked = false);

internal sealed record NewTabShortcutPlacement(string Id, string Label, string Target)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? AppId { get; init; }

    internal const string Apps = "browse://home";
    internal const string Library = "browse://home/library";
    internal const string Dashboard = "browse://home/dashboard";
    internal const string Models = "browse://home/models";
    internal const string Events = "browse://home/events";
    internal const string Settings = "browse://home/settings";
    internal const int MaximumCount = 32;
    internal const int MaximumLabelLength = 80;

    internal static ImmutableArray<NewTabShortcutPlacement> Defaults { get; } =
        [new("apps", "Apps", Apps), new("library", "Library", Library), new("dashboard", "Dashboard", Dashboard)];

    internal static bool TryParseTarget(string? value, out AppContentAddress? target)
    {
        if (!AppContentAddress.TryParse(value, out target)) return false;
        return target!.IsWeb || target.Value is Apps or Library or Dashboard or Models or Events or Settings;
    }

    internal bool IsValid() =>
        Id is { Length: > 0 and <= 64 } && Id.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') &&
        Label is { Length: > 0 and <= MaximumLabelLength } && !Label.Any(char.IsControl) && !string.IsNullOrWhiteSpace(Label) &&
        (AppId is { } appId
            ? appId != Guid.Empty && AppContentAddress.TryParse(Target, out var app) &&
              app is { Namespace: AppContentNamespace.NativeApp, ResourceSegments.Count: 0 } && app.Value == Target
            : TryParseTarget(Target, out var address) && address!.Value == Target);
}

internal sealed record NewTabLayoutPreference(long Revision, NewTabLayoutPreset Preset,
    NewTabBackgroundPreset Background, bool ShowClock, bool ShowShortcuts, ImmutableArray<NewTabWidgetPlacement> Widgets)
{
    internal const string NewTabLayoutId = "haven.new-tab";

    [JsonRequired] public string LayoutId { get; init; } = NewTabLayoutId;
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    public string? BackgroundImageId { get; init; }
    public bool ShowCustomBackground { get; init; }
    public bool FillBackgroundImage { get; init; } = true;
    public ImmutableArray<NewTabShortcutPlacement> Shortcuts { get; init; } = NewTabShortcutPlacement.Defaults;

    internal static NewTabLayoutPreference Default { get; } = new(0, NewTabLayoutPreset.Balanced,
        NewTabBackgroundPreset.Neutral, true, true,
        [new("up-next", "planner:calendar"), new("pinned-note", "boards:notebook")]);

    internal NewTabLayoutPreference Validate()
    {
        if (LayoutId != NewTabLayoutId || SchemaVersion != 1 || Revision < 0 || !Enum.IsDefined(Preset) ||
            !Enum.IsDefined(Background) || Widgets.IsDefault || Widgets.Length > 32 ||
            ShowCustomBackground && BackgroundImageId is null ||
            BackgroundImageId is { } imageId && (imageId.Length != 64 || imageId.Any(character => !char.IsAsciiHexDigit(character))) ||
            Widgets.Any(widget => widget is null || string.IsNullOrWhiteSpace(widget.Id) || widget.Id.Length > 128 ||
                string.IsNullOrWhiteSpace(widget.SourceId) || widget.SourceId.Length > 512) ||
            Widgets.Select(widget => widget.Id).Distinct(StringComparer.Ordinal).Count() != Widgets.Length ||
            Shortcuts.IsDefault || Shortcuts.Length > NewTabShortcutPlacement.MaximumCount ||
            Shortcuts.Any(shortcut => shortcut is null || !shortcut.IsValid()) ||
            Shortcuts.Select(shortcut => shortcut.Id).Distinct(StringComparer.Ordinal).Count() != Shortcuts.Length)
            throw new InvalidDataException("The saved New Tab layout has an unsupported version or invalid placement. Its saved data was preserved.");

        return this;
    }
}

/// <summary>One guarded, profile-scoped setting for New Tab. Dashboard uses its own layout keys.</summary>
internal sealed class NewTabLayoutPreferences
{
    internal sealed record Snapshot(NewTabLayoutPreference Layout, string? Serialized);
    internal sealed record SaveResult(bool Saved, Snapshot Current);

    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly IReadOnlyDictionary<string, string?> NoAdditionalGuards = new Dictionary<string, string?>();
    private readonly IVersionedSettingsStore _settings;
    private readonly Func<CancellationToken, Task> _admit;
    private readonly string _key;

    internal NewTabLayoutPreferences(IVersionedSettingsStore settings, string profileId, Func<CancellationToken, Task> admit,
        NewTabBackgroundAssets? assets = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _admit = admit ?? throw new ArgumentNullException(nameof(admit));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        _key = "haven.new-tab.layout.v1." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileId)));
        Assets = assets;
    }

    internal bool CanEdit => _settings is IVersionedSettingsGuardedCompareExchange;
    internal NewTabBackgroundAssets? Assets { get; }

    internal async Task<Snapshot> ReadAsync(CancellationToken token)
    {
        await _admit(token).ConfigureAwait(false);
        var exported = await _settings.ExportAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return Read(exported.Settings.GetValueOrDefault(_key));
    }

    internal async Task<SaveResult> SaveAsync(Snapshot expected, NewTabLayoutPreference candidate, CancellationToken token)
    {
        var store = _settings as IVersionedSettingsGuardedCompareExchange
            ?? throw new NotSupportedException("New Tab requires a guarded layout settings store.");
        var next = (candidate with { Revision = checked(expected.Layout.Revision + 1) }).Validate();
        var replacement = JsonSerializer.Serialize(next, Json);
        var result = await store.CompareExchangeGuardedAsync(_key, expected.Serialized, replacement,
            NoAdditionalGuards, new CommitAdmission(_admit), token).ConfigureAwait(false);
        if (result.AdmissionRejected)
            throw new UnauthorizedAccessException("New Tab layout authority changed before the save completed.");
        return result.Exchanged ? new(true, new(next, replacement)) : new(false, Read(result.CurrentJson));
    }

    private static Snapshot Read(string? raw)
    {
        if (raw is null) return new(NewTabLayoutPreference.Default, null);
        try
        {
            var layout = JsonSerializer.Deserialize<NewTabLayoutPreference>(raw, Json)
                ?? throw new InvalidDataException("The saved New Tab layout is empty. Its saved data was preserved.");
            return new(layout.Validate(), raw);
        }
        catch (JsonException cause)
        {
            throw new InvalidDataException("The saved New Tab layout could not be loaded. Its saved data was preserved.", cause);
        }
    }

    private sealed class CommitAdmission(Func<CancellationToken, Task> admit) : ISettingsCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken cancellationToken)
        {
            try { await admit(cancellationToken).ConfigureAwait(false); return true; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
