using System.Text.Json;
using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

/// <summary>
/// Client navigation metadata for the landing page. It stores only paths to boards;
/// the .9to1board document remains the sole owner of board and page content.
/// </summary>
public sealed class RecentBoardCatalog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly List<RecentBoardReference> _references;
    private readonly List<RecentPageReference> _pages;
    public string? LoadError { get; private set; }
    public string? SaveError { get; private set; }

    public RecentBoardCatalog(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        try
        {
            var document = File.Exists(_path)
                ? JsonSerializer.Deserialize<RecentBoardCatalogDocument>(File.ReadAllText(_path), Json)
                : null;
            if (document is not null && document.SchemaVersion != 1)
                throw new JsonException("Recent Boards metadata uses an unsupported version.");
            _references = document?.Boards?.Take(24).ToList() ?? [];
            _pages = document?.Pages?.Take(32).ToList() ?? [];
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            _references = [];
            _pages = [];
            LoadError = error.Message.Split('\n')[0];
        }
        _references.RemoveAll(reference => !IsBoardPath(reference.Path));
        _pages.RemoveAll(reference => !IsBoardPath(reference.Path) || string.IsNullOrWhiteSpace(reference.PageId) ||
            string.IsNullOrWhiteSpace(reference.SectionId));
    }

    public IReadOnlyList<RecentBoardReference> References => _references.ToArray();

    public void Record(string? path)
    {
        if (!IsBoardPath(path)) return;
        var fullPath = Path.GetFullPath(path!);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _references.RemoveAll(reference => comparer.Equals(reference.Path, fullPath));
        _references.Insert(0, new RecentBoardReference(fullPath, DateTimeOffset.UtcNow));
        if (_references.Count > 24) _references.RemoveRange(24, _references.Count - 24);
        Persist();
    }

    public void RecordPage(string? path, string sectionId, string pageId)
    {
        if (!IsBoardPath(path) || string.IsNullOrWhiteSpace(sectionId) || string.IsNullOrWhiteSpace(pageId)) return;
        var fullPath = Path.GetFullPath(path!);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _pages.RemoveAll(reference => comparer.Equals(reference.Path, fullPath) &&
            reference.SectionId == sectionId && reference.PageId == pageId);
        _pages.Insert(0, new RecentPageReference(fullPath, sectionId, pageId, DateTimeOffset.UtcNow));
        if (_pages.Count > 32) _pages.RemoveRange(32, _pages.Count - 32);
        Persist();
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new RecentBoardCatalogDocument(1, _references, _pages), Json));
            File.Move(temporary, _path, overwrite: true);
            SaveError = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SaveError = error.Message.Split('\n')[0];
        }
    }

    public async Task<IReadOnlyList<RecentBoardCard>> LoadCardsAsync(JsonFileHavenBoardStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var result = new List<RecentBoardCard>(_references.Count);
        foreach (var reference in _references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasBoardOrRecoveryBackup(reference.Path))
            {
                result.Add(new RecentBoardCard(reference.Path, reference.Path, "Board file is unavailable",
                    "Choose Open to locate this board again.", reference.OpenedUtc, false));
                continue;
            }

            try
            {
                var document = await store.LoadDocumentAtPathAsync(reference.Path, cancellationToken).ConfigureAwait(false);
                if (document?.RichNotes is not { } notes)
                    throw new InvalidDataException("The file does not contain a Boards notes document.");
                var page = notes.Sections.SelectMany(section => section.Pages).FirstOrDefault();
                var preview = page?.Blocks.Select(block => block.Text).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
                    ?? (page is null ? "No pages" : $"{notes.Sections.Sum(section => section.Pages.Count)} pages · {notes.Sections.Count} sections");
                result.Add(new RecentBoardCard(reference.Path, reference.Path, notes.Title, preview,
                    document.ModifiedUtc, true));
            }
            catch (Exception error) when (IsExpectedBoardReadFailure(error))
            {
                result.Add(new RecentBoardCard(reference.Path, reference.Path, "Board could not be opened",
                    error.Message.Split('\n')[0], reference.OpenedUtc, false));
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<RecentPageCard>> LoadRecentPagesAsync(JsonFileHavenBoardStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var result = new List<RecentPageCard>();
        foreach (var reference in _pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasBoardOrRecoveryBackup(reference.Path)) continue;
            try
            {
                var document = await store.LoadDocumentAtPathAsync(reference.Path, cancellationToken).ConfigureAwait(false);
                if (document?.RichNotes is not { } notes) continue;
                var section = notes.Sections.FirstOrDefault(section => section.Id == reference.SectionId);
                var page = section?.Pages.FirstOrDefault(page => page.Id == reference.PageId);
                if (section is null || page is null) continue;
                result.Add(new RecentPageCard(reference.Path, section.Id, page.Id, notes.Title, page.Title,
                    page.Blocks.Select(block => block.Text).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "No text preview",
                    reference.OpenedUtc));
            }
            catch (Exception error) when (IsExpectedBoardReadFailure(error))
            {
                // The board card carries its own actionable load error; do not make stale page links look usable.
            }
        }
        return result.OrderByDescending(item => item.OpenedUtc).Take(12).ToArray();
    }

    private static bool IsExpectedBoardReadFailure(Exception error) =>
        error is IOException || error is InvalidDataException || error is UnauthorizedAccessException ||
        error is InvalidOperationException || error is JsonException;

    private static bool IsBoardPath(string? path) => !string.IsNullOrWhiteSpace(path) &&
        path.EndsWith(".9to1board", StringComparison.OrdinalIgnoreCase);

    private static bool HasBoardOrRecoveryBackup(string path) => File.Exists(path) || File.Exists(path + ".bak");
}

public sealed record RecentBoardReference(string Path, DateTimeOffset OpenedUtc);
public sealed record RecentPageReference(string Path, string SectionId, string PageId, DateTimeOffset OpenedUtc);
public sealed record RecentBoardCatalogDocument(int SchemaVersion, List<RecentBoardReference> Boards,
    List<RecentPageReference> Pages);

public sealed record RecentBoardCard(string Id, string Path, string Title, string Preview,
    DateTimeOffset ModifiedUtc, bool IsAvailable)
{
    public RecentBoardCard Target => this;
    public string IconLabel => "▤";
    public string ModifiedLabel => ModifiedUtc.ToLocalTime().ToString("g");
    public string AvailabilityLabel => IsAvailable ? string.Empty : "Unavailable · choose Open to locate or recover this board.";
}

public sealed record RecentPageCard(string Path, string SectionId, string PageId, string BoardTitle,
    string PageTitle, string Preview, DateTimeOffset OpenedUtc)
{
    public string Id => Path + "|" + SectionId + "|" + PageId;
    public RecentPageCard Target => this;
    public string OpenedLabel => OpenedUtc.ToLocalTime().ToString("g");
}
