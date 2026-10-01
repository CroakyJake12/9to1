using System.Text.Json.Serialization;
using Haven.Application;

namespace NineToOne.Os.Shell;

public enum TaskbarItemKind { Go, Application, Window, SystemControl, QuickAction, Settings, File, Folder, Shelf, Route, Agent, AgentRun, Automation, Widget, Status, Media, Spacer, Extension }
public enum TaskbarAlignment { Start, Centre, End }
public enum TaskbarEdge { Bottom, Top, Left, Right }
public sealed record ShellEntityReference(string Owner, string Kind, string Id);
public sealed record TaskbarItem(Guid Id, TaskbarItemKind Kind, string Label, ShellEntityReference? Target);
public sealed record TaskbarPresentation(int Thickness, int IconSize, int Spacing, int Padding, int CornerRadius,
    double Opacity, TaskbarAlignment Alignment, bool Labels, bool Floating);
public sealed record TaskbarLayer(Guid Id, string Name, TaskbarPresentation Presentation, IReadOnlyList<TaskbarItem> Items);
public sealed record TaskbarConfiguration(Guid Id, TaskbarEdge Edge, Guid ActiveLayerId, IReadOnlyList<TaskbarLayer> Layers);
public sealed record DesktopSpace(Guid Id, string Name, TaskbarConfiguration Taskbar)
{ public DesktopSurfaceConfiguration? DesktopSurface { get; init; } }
public sealed record ShellConfiguration(int SchemaVersion, Guid ActiveSpaceId, IReadOnlyList<DesktopSpace> Spaces)
{
    public const int CurrentSchema = 2;
    public GoHomeConfiguration? GoHome { get; init; }
    [JsonIgnore] public GoHomeConfiguration EffectiveGoHome => GoHome ?? GoHomeConfiguration.Default();
    public DesktopSurfaceConfiguration? GlobalDesktopSurface { get; init; }
    [JsonIgnore] public DesktopSpace ActiveSpace => Spaces.Single(s => s.Id == ActiveSpaceId);
    public static ShellConfiguration Default()
    {
        var layer = new TaskbarLayer(Guid.NewGuid(), "Main", new(56, 32, 8, 8, 12, 1, TaskbarAlignment.Start, true, false),
            [new(Guid.NewGuid(), TaskbarItemKind.Go, "Go", null)]);
        var space = new DesktopSpace(Guid.NewGuid(), "Standard", new(Guid.NewGuid(), TaskbarEdge.Bottom, layer.Id, [layer]));
        return new(CurrentSchema, space.Id, [space]) { GlobalDesktopSurface = DesktopSurfaceConfiguration.Default(), GoHome = GoHomeConfiguration.Default() };
    }

    public void Validate()
    {
        GoHome?.Validate();
        if (SchemaVersion != CurrentSchema) throw new InvalidDataException("Unsupported shell configuration. Preserve it for recovery.");
        if (Spaces is null || Spaces.Count is < 1 or > 64 || Spaces.Any(s => s is null)) throw new InvalidDataException("One to 64 Desktop Spaces are required.");
        var identities = new HashSet<Guid>();
        void Identity(Guid id) { if (id == Guid.Empty || !identities.Add(id)) throw new InvalidDataException("Shell identities must be stable and unique."); }
        void Surface(DesktopSurfaceConfiguration surface)
        {
            Identity(surface.Id);
            if (surface.Columns is < 1 or > 32 || surface.Rows is < 1 or > 32 || surface.Pages is null || surface.Pages.Count is < 1 or > 64 || surface.Pages.Any(p => p is null) || !surface.Pages.Any(p => p.Id == surface.ActivePageId))
                throw new InvalidDataException("Desktop Pages require a bounded grid and valid active page.");
            foreach (var page in surface.Pages)
            {
                Identity(page.Id); Name(page.Name);
                if (page.Items is null || page.Items.Count > 1024 || page.Items.Any(i => i is null)) throw new InvalidDataException("Desktop page items exceed supported bounds.");
                var occupied = new HashSet<(int, int)>();
                foreach (var item in page.Items)
                {
                    Identity(item.Id); Name(item.Label);
                    if (!Enum.IsDefined(item.Kind) || item.Target is null) throw new InvalidDataException("Desktop items require a typed canonical owner reference.");
                    Name(item.Target.Owner); Name(item.Target.Kind);
                    if (string.IsNullOrWhiteSpace(item.Target.Id) || item.Target.Id.Length > 4096 || item.Target.Id.Any(char.IsControl) ||
                        item.Column < 0 || item.Row < 0 || item.ColumnSpan < 1 || item.RowSpan < 1 || item.ColumnSpan > surface.Columns || item.RowSpan > surface.Rows ||
                        item.Column > surface.Columns - item.ColumnSpan || item.Row > surface.Rows - item.RowSpan) throw new InvalidDataException("Desktop item identity or placement is invalid.");
                    for (var row = item.Row; row < item.Row + item.RowSpan; row++) for (var column = item.Column; column < item.Column + item.ColumnSpan; column++)
                        if (!occupied.Add((column, row))) throw new InvalidDataException("Desktop items cannot overlap.");
                }
            }
        }
        if (GlobalDesktopSurface is null) throw new InvalidDataException("Global desktop surface requires recovery.");
        Surface(GlobalDesktopSurface);
        foreach (var space in Spaces)
        {
            Identity(space.Id); Name(space.Name); if (space.DesktopSurface is { } localSurface) Surface(localSurface); var bar = space.Taskbar ?? throw new InvalidDataException("Desktop Space has no taskbar.");
            Identity(bar.Id);
            if (!Enum.IsDefined(bar.Edge) || bar.Layers is null || bar.Layers.Count is < 1 or > 5 || bar.Layers.Any(l => l is null) || !bar.Layers.Any(l => l.Id == bar.ActiveLayerId))
                throw new InvalidDataException("A taskbar requires one to five complete layers and a valid active layer.");
            foreach (var layer in bar.Layers)
            {
                Identity(layer.Id); Name(layer.Name); var p = layer.Presentation ?? throw new InvalidDataException("Layer presentation is missing.");
                if (p.Thickness is < 24 or > 256 || p.IconSize is < 16 or > 128 || p.Spacing is < 0 or > 64 || p.Padding is < 0 or > 64 || p.CornerRadius is < 0 or > 128 || !double.IsFinite(p.Opacity) || p.Opacity is < .2 or > 1 || !Enum.IsDefined(p.Alignment))
                    throw new InvalidDataException("Taskbar presentation is outside supported limits.");
                if (layer.Items is null || layer.Items.Count > 256 || layer.Items.Any(i => i is null)) throw new InvalidDataException("Taskbar items exceed supported limits.");
                foreach (var item in layer.Items)
                {
                    Identity(item.Id); Name(item.Label);
                    if (!Enum.IsDefined(item.Kind)) throw new InvalidDataException("Unknown taskbar item type.");
                    if (item.Kind is not (TaskbarItemKind.Go or TaskbarItemKind.Spacer) && item.Target is null) throw new InvalidDataException("Taskbar items must reference their canonical owner.");
                    if (item.Target is { } target) { Name(target.Owner); Name(target.Kind); if (string.IsNullOrWhiteSpace(target.Id) || target.Id.Length > 4096) throw new InvalidDataException("Invalid canonical entity reference."); }
                }
            }
        }
        if (!Spaces.Any(s => s.Id == ActiveSpaceId)) throw new InvalidDataException("Active Desktop Space is missing.");
    }
    public ShellConfiguration UpgradeSupportedLegacy()
    {
        if (SchemaVersion == CurrentSchema) { Validate(); return this; }
        if (SchemaVersion != 1 || GlobalDesktopSurface is not null || Spaces is null || Spaces.Count == 0 || Spaces.Any(s => s is null || s.DesktopSurface is not null))
            throw new InvalidDataException("Unsupported shell configuration. Preserve it for recovery.");
        var migrated = this with { SchemaVersion = CurrentSchema, GlobalDesktopSurface = DesktopSurfaceConfiguration.FromLegacy(Spaces[0].Id) };
        migrated.Validate(); return migrated;
    }
    private static void Name(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl)) throw new InvalidDataException("Invalid shell name."); }
}

public sealed record ShellStoredConfiguration(long Revision, ShellConfiguration Current, ShellConfiguration? Previous, string AuthorityId)
{
    // Runtime read binding only: not serialized, never a grant or persisted profile policy.
    internal AuthenticatedResourceActor? SessionActor { get; init; }
}
public interface IShellConfigurationStore
{
    Task<ShellStoredConfiguration> ReadAsync(CancellationToken cancellationToken);
    Task<bool> TryWriteAsync(long expectedRevision, ShellStoredConfiguration next, CancellationToken cancellationToken);
    Task<bool> IsCurrentSessionAsync(string authorityId, AuthenticatedResourceActor actor, CancellationToken cancellationToken) => Task.FromResult(false);
}
public sealed record ShellPreview(Guid Id, long BaseRevision, ShellConfiguration Candidate, DateTimeOffset ExpiresAt, string AuthorityId)
{
    internal AuthenticatedResourceActor? SessionActor { get; init; }
}
public sealed record ShellConfigurationSnapshot(ShellStoredConfiguration Stored, ShellPreview? Preview)
{
    public ShellConfiguration Effective => Preview?.Candidate ?? Stored.Current;
    internal long IntentGeneration { get; init; }
}

/// <summary>Shared typed mutation engine. Preview is volatile; only Keep writes canonical Home state.</summary>
public sealed class ShellConfigurationService(IShellConfigurationStore store, TimeProvider? time = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private ShellPreview? _preview;
    private long _intentGeneration;
    internal Task<bool> IsCurrentSessionAsync(ShellStoredConfiguration expected, CancellationToken ct) => expected.SessionActor is { } actor
        ? store.IsCurrentSessionAsync(expected.AuthorityId, actor, ct) : Task.FromResult(false);
    private async Task RequireSessionAsync(string? authority, AuthenticatedResourceActor? actor, CancellationToken ct)
    { if (authority is null || actor is null || !await store.IsCurrentSessionAsync(authority, actor, ct)) throw new UnauthorizedAccessException("The original Home session changed; reopen the shell before editing."); }
    public async Task<ShellConfigurationSnapshot> GetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return Snapshot(await ReadAsync(ct)); } finally { _gate.Release(); }
    }
    public Task<ShellConfigurationSnapshot> PreviewAsync(ShellStoredConfiguration expected, ShellConfiguration candidate, TimeSpan duration, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return PreviewCoreAsync(expected.Revision, expected.AuthorityId, expected.SessionActor, candidate, duration, ct, null);
    }
    internal Task<ShellConfigurationSnapshot> PreviewIfUnchangedAsync(ShellConfigurationSnapshot expected, ShellConfiguration candidate, TimeSpan duration, CancellationToken ct)
        => PreviewCoreAsync(expected.Stored.Revision, expected.Stored.AuthorityId, expected.Stored.SessionActor, candidate, duration, ct, expected.IntentGeneration);
    private async Task<ShellConfigurationSnapshot> PreviewCoreAsync(long expectedRevision, string? expectedAuthority,
        AuthenticatedResourceActor? expectedActor, ShellConfiguration candidate, TimeSpan duration, CancellationToken ct, long? expectedIntentGeneration)
    {
        candidate.Validate(); candidate = Clone(candidate);
        if (duration < TimeSpan.FromSeconds(5) || duration > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(duration));
        await _gate.WaitAsync(ct);
        try
        {
            await RequireSessionAsync(expectedAuthority, expectedActor, ct);
            var stored = await ReadAsync(ct);
            if (expectedAuthority != stored.AuthorityId || expectedActor != stored.SessionActor) throw new UnauthorizedAccessException("Read the current Home profile before editing its shell configuration.");
            if (stored.Revision != expectedRevision) throw new ShellConfigurationConflictException();
            Snapshot(stored);
            if (expectedIntentGeneration is { } generation && (generation != _intentGeneration || _preview is not null))
                throw new InvalidOperationException("The shell preview changed. Prepare a new intent before continuing.");
            _intentGeneration = checked(_intentGeneration + 1);
            _preview = new(Guid.NewGuid(), expectedRevision, candidate, _time.GetUtcNow() + duration, stored.AuthorityId) { SessionActor = expectedActor };
            return Snapshot(stored);
        }
        finally { _gate.Release(); }
    }
    public async Task<ShellConfigurationSnapshot> KeepAsync(Guid previewId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_preview is { } original) await RequireSessionAsync(original.AuthorityId, original.SessionActor, ct);
            var stored = await ReadAsync(ct);
            if (_preview is { } pending && (pending.AuthorityId != stored.AuthorityId || pending.SessionActor != stored.SessionActor)) { ClearPreview(); throw new UnauthorizedAccessException("The Home profile changed; its previous preview cannot be kept."); }
            if (_preview is { } stale && stale.BaseRevision != stored.Revision) { ClearPreview(); throw new ShellConfigurationConflictException(); }
            Snapshot(stored);
            var preview = _preview;
            if (preview is null || preview.Id != previewId) throw new InvalidOperationException("Preview expired or was replaced; existing configuration was preserved.");
            if (stored.Revision != preview.BaseRevision) { ClearPreview(); throw new ShellConfigurationConflictException(); }
            var next = new ShellStoredConfiguration(checked(stored.Revision + 1), Clone(preview.Candidate), Clone(stored.Current), stored.AuthorityId) { SessionActor = preview.SessionActor };
            if (!await store.TryWriteAsync(stored.Revision, next, ct)) { ClearPreview(); throw new ShellConfigurationConflictException(); }
            ClearPreview();
            return Snapshot(next);
        }
        finally { _gate.Release(); }
    }
    public async Task<ShellConfigurationSnapshot> RevertAsync(Guid previewId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { var stored = await ReadAsync(ct); if (_preview?.Id == previewId) ClearPreview(); return Snapshot(stored); }
        finally { _gate.Release(); }
    }
    private async Task<ShellStoredConfiguration> ReadAsync(CancellationToken ct)
    {
        var stored = await store.ReadAsync(ct); stored.Current.Validate(); stored.Previous?.Validate();
        if (stored.Revision < 0 || stored.Revision == long.MaxValue || string.IsNullOrWhiteSpace(stored.AuthorityId)) throw new InvalidDataException("Shell revision or authority requires recovery.");
        return stored;
    }
    private ShellConfigurationSnapshot Snapshot(ShellStoredConfiguration stored)
    {
        if (_preview is { } p && (p.ExpiresAt <= _time.GetUtcNow() || p.BaseRevision != stored.Revision || p.AuthorityId != stored.AuthorityId || p.SessionActor != stored.SessionActor)) ClearPreview();
        return new(new(stored.Revision, Clone(stored.Current), stored.Previous is null ? null : Clone(stored.Previous), stored.AuthorityId) { SessionActor = stored.SessionActor },
            _preview is null ? null : _preview with { Candidate = Clone(_preview.Candidate) }) { IntentGeneration = _intentGeneration };
    }
    private void ClearPreview()
    { if (_preview is not null) { _preview = null; _intentGeneration = checked(_intentGeneration + 1); } }
    private static ShellConfiguration Clone(ShellConfiguration source) => source with { GoHome = source.GoHome?.Detached(), GlobalDesktopSurface = source.GlobalDesktopSurface is null ? null : DesktopPageEdits.Clone(source.GlobalDesktopSurface), Spaces = source.Spaces.Select(s => s with { DesktopSurface = s.DesktopSurface is null ? null : DesktopPageEdits.Clone(s.DesktopSurface), Taskbar = s.Taskbar with { Layers = s.Taskbar.Layers.Select(l => l with { Items = l.Items.ToArray() }).ToArray() } }).ToArray() };
}
public sealed class ShellConfigurationConflictException() : IOException("Shell configuration changed concurrently. Reload before editing; existing configuration was preserved.");
