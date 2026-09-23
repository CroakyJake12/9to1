/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/Maps/MapsSavedPlaceStore.cs, in Infrastructure.
 * What: Stores Maps saved places in a stable Data workbook so the existing Data app can
 *       inspect, edit, query and export the same records. Recent searches remain in a small
 *       local JSON document because they are transient navigation history.
 * How: Saved-place rows are keyed by their stable id; workbook columns and user-added cells are
 *      retained, legacy saved-places.json is imported once, and recent-search writes use a
 *      verified temporary file with a previous-file backup.
 * Why: Maps keeps its OpenStreetMap provider and native map UI while sharing the real Data app's
 *      durable workbook workflow rather than maintaining a second disconnected data viewer.
 */

using System.Globalization;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Legacy saved-places.json shape read only during the one-time workbook migration.</summary>
public sealed record MapsSavedPlaceDocument(
    IReadOnlyList<SavedMapPlace> SavedPlaces,
    IReadOnlyList<string> RecentSearches);

/// <summary>Persists Maps saved places in the Data app's workbook repository.</summary>
public sealed class MapsSavedPlaceStore : IMapsSavedPlaceStore
{
    public const string StoreFileName = "saved-places.json";

    private static readonly string[] Headers = ["Saved place ID", "Name", "Note", "Latitude", "Longitude", "Saved At (UTC)"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IDataWorkbookRepository _workbooks;
    private readonly string _legacyStorePath;
    private readonly string _recentSearchesPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates a Maps store whose saved places open in the existing Data app.</summary>
    public MapsSavedPlaceStore(IAppPaths appPaths, IDataWorkbookRepository workbooks)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _workbooks = workbooks ?? throw new ArgumentNullException(nameof(workbooks));
        var mapsDirectory = Path.Combine(appPaths.DataDirectory, "maps");
        _legacyStorePath = Path.Combine(mapsDirectory, StoreFileName);
        _recentSearchesPath = Path.Combine(mapsDirectory, "recent-searches.json");
    }

    /// <inheritdoc />
    public Guid DataWorkbookId => DataWorkbookAppLinks.MapsSavedPlaces;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SavedMapPlace>> GetSavedPlacesAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var workbook = await LoadOrCreateWorkbookAsync(cancellationToken).ConfigureAwait(false);
            var sheet = GetSavedPlacesSheet(workbook);
            var columns = ReadColumnIndexes(sheet);
            return MapsStoreLogic.NormaliseSavedPlaces(ReadPlaceRows(sheet, columns).Select(row => row.Place));
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task SaveAsync(SavedMapPlace place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(place);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var workbook = await LoadOrCreateWorkbookAsync(cancellationToken).ConfigureAwait(false);
            var sheet = GetSavedPlacesSheet(workbook);
            var columns = ReadColumnIndexes(sheet);
            var existing = ReadPlaceRows(sheet, columns);
            var updatedPlaces = MapsStoreLogic.NormaliseSavedPlaces(existing.Select(row => row.Place)
                .Where(existingPlace => !existingPlace.Id.Equals(place.Id.Trim(), StringComparison.Ordinal))
                .Append(place));
            WritePlaces(sheet, columns, existing, updatedPlaces);
            EnsureSavedPlacesTable(workbook, sheet, columns, ReadPlaceRows(sheet, columns));
            SetWorkbookPurpose(workbook);
            await _workbooks.SaveAsync(workbook, "Maps saved place updated", cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var workbook = await LoadOrCreateWorkbookAsync(cancellationToken).ConfigureAwait(false);
            var sheet = GetSavedPlacesSheet(workbook);
            var columns = ReadColumnIndexes(sheet);
            var existing = ReadPlaceRows(sheet, columns);
            var updatedPlaces = MapsStoreLogic.NormaliseSavedPlaces(existing
                .Where(row => !row.Place.Id.Equals(id.Trim(), StringComparison.Ordinal))
                .Select(row => row.Place));
            WritePlaces(sheet, columns, existing, updatedPlaces);
            EnsureSavedPlacesTable(workbook, sheet, columns, ReadPlaceRows(sheet, columns));
            SetWorkbookPurpose(workbook);
            await _workbooks.SaveAsync(workbook, "Maps saved place removed", cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRecentSearchesAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ = await LoadOrCreateWorkbookAsync(cancellationToken).ConfigureAwait(false);
            return await ReadRecentSearchesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task RecordSearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ = await LoadOrCreateWorkbookAsync(cancellationToken).ConfigureAwait(false);
            var current = await ReadRecentSearchesAsync(cancellationToken).ConfigureAwait(false);
            await WriteRecentSearchesAsync(MapsStoreLogic.NormaliseRecentSearches(current.Append(query)), cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<DataWorkbook> LoadOrCreateWorkbookAsync(CancellationToken cancellationToken)
    {
        var workbook = await _workbooks.LoadAsync(DataWorkbookId, cancellationToken).ConfigureAwait(false);
        var changed = false;
        if (workbook is null)
        {
            workbook = DataWorkbook.Create("Maps saved places");
            workbook.Id = DataWorkbookId;
            workbook.Sheets[0].Name = "Saved places";
            workbook.Queries[0].Visual.Source = "Saved places";
            workbook.Queries[0].Sql = "SELECT * FROM \"Saved places\";";
            changed = true;
        }

        var originalSheetCount = workbook.Sheets.Count;
        var sheet = GetSavedPlacesSheet(workbook);
        changed |= workbook.Sheets.Count != originalSheetCount;
        var priorColumnCount = sheet.Cells.Count(cell => cell.Row == 0);
        var columns = EnsureHeaders(sheet);
        changed |= priorColumnCount != sheet.Cells.Count(cell => cell.Row == 0);
        changed |= EnsureSavedPlacesTable(workbook, sheet, columns, ReadPlaceRows(sheet, columns));

        var legacy = await TryReadLegacyDocumentAsync(cancellationToken).ConfigureAwait(false);
        var imported = workbook.Metadata.TryGetValue("haven.maps.legacyImported", out var importedText)
            && bool.TryParse(importedText, out var importedValue) && importedValue;
        if (legacy is not null && !imported)
        {
            var currentRows = ReadPlaceRows(sheet, columns);
            var existingIds = currentRows.Select(row => row.Place.Id).ToHashSet(StringComparer.Ordinal);
            var places = currentRows.Select(row => row.Place)
                .Concat(legacy.SavedPlaces.Where(place => place is not null && !existingIds.Contains(place.Id.Trim())));
            WritePlaces(sheet, columns, currentRows, MapsStoreLogic.NormaliseSavedPlaces(places));
            workbook.Metadata["haven.maps.legacyImported"] = "true";
            changed = true;

            var recent = await ReadRecentSearchesAsync(cancellationToken).ConfigureAwait(false);
            var mergedSearches = MapsStoreLogic.NormaliseRecentSearches(legacy.RecentSearches.Reverse().Concat(recent.Reverse()));
            await WriteRecentSearchesAsync(mergedSearches, cancellationToken).ConfigureAwait(false);
        }

        changed |= EnsureSavedPlacesTable(workbook, sheet, columns, ReadPlaceRows(sheet, columns));
        SetWorkbookPurpose(workbook);
        if (changed)
            await _workbooks.SaveAsync(workbook, "Maps saved places workbook prepared", cancellationToken).ConfigureAwait(false);

        // Retire the legacy source only after both destinations have been written successfully.
        if (legacy is not null && !File.Exists(_legacyStorePath + ".migrated"))
            File.Move(_legacyStorePath, _legacyStorePath + ".migrated", overwrite: false);
        return workbook;
    }

    private static DataSheet GetSavedPlacesSheet(DataWorkbook workbook)
    {
        var sheet = workbook.Sheets.FirstOrDefault(candidate => candidate.Name.Equals("Saved places", StringComparison.OrdinalIgnoreCase));
        if (sheet is not null) return sheet;
        if (workbook.Sheets.Count == 0) workbook.Sheets.Add(DataSheet.Create(0, "Saved places"));
        else workbook.Sheets.Add(DataSheet.Create(workbook.Sheets.Count, "Saved places"));
        return workbook.Sheets[^1];
    }

    private static Dictionary<string, int> EnsureHeaders(DataSheet sheet)
    {
        var columns = ReadHeaderMap(sheet);
        var nextColumn = sheet.Cells.Where(cell => cell.Row == 0).Select(cell => cell.Column).DefaultIfEmpty(-1).Max() + 1;
        foreach (var header in Headers)
        {
            if (columns.ContainsKey(header)) continue;
            while (sheet.Cells.Any(cell => cell.Row == 0 && cell.Column == nextColumn)) nextColumn++;
            sheet.SetCell(0, nextColumn, header, null, DataCellKind.Text);
            columns[header] = nextColumn++;
        }
        return columns;
    }

    private static Dictionary<string, int> ReadColumnIndexes(DataSheet sheet)
    {
        var columns = ReadHeaderMap(sheet);
        var missing = Headers.Where(header => !columns.ContainsKey(header)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException("The Maps saved places workbook is missing columns: " + string.Join(", ", missing));
        return columns;
    }

    private static Dictionary<string, int> ReadHeaderMap(DataSheet sheet) => sheet.Cells
        .Where(cell => cell.Row == 0 && !string.IsNullOrWhiteSpace(cell.Value))
        .GroupBy(cell => cell.Value.Trim(), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Column, StringComparer.OrdinalIgnoreCase);

    private static List<StoredPlace> ReadPlaceRows(DataSheet sheet, IReadOnlyDictionary<string, int> columns)
    {
        var result = new List<StoredPlace>();
        foreach (var row in sheet.Cells.Where(cell => cell.Column == columns[Headers[0]] && cell.Row > 0)
                     .Select(cell => cell.Row).Distinct().OrderBy(row => row))
        {
            var id = ReadCell(sheet, row, columns[Headers[0]]).Trim();
            var savedAt = ReadCell(sheet, row, columns[Headers[5]]);
            var latitude = ReadCell(sheet, row, columns[Headers[3]]);
            var longitude = ReadCell(sheet, row, columns[Headers[4]]);
            if (id.Length == 0
                || !DateTimeOffset.TryParse(savedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedAt)
                || !double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLatitude)
                || !double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLongitude))
                continue;
            var place = new SavedMapPlace(id, ReadCell(sheet, row, columns[Headers[1]]),
                EmptyToNull(ReadCell(sheet, row, columns[Headers[2]])), new GeoPoint(parsedLatitude, parsedLongitude), parsedAt);
            var knownColumns = columns.Values.ToHashSet();
            var extra = sheet.Cells.Where(cell => cell.Row == row && !knownColumns.Contains(cell.Column))
                .ToDictionary(cell => cell.Column, cell => cell.Value);
            result.Add(new StoredPlace(row, place, extra));
        }
        return result;
    }

    private static void WritePlaces(DataSheet sheet, IReadOnlyDictionary<string, int> columns,
        IReadOnlyList<StoredPlace> existing, IReadOnlyList<SavedMapPlace> places)
    {
        var oldById = existing.GroupBy(row => row.Place.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.Row).First(), StringComparer.Ordinal);
        var retainedIds = places.Select(place => place.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in existing.Where(row => !retainedIds.Contains(row.Place.Id)))
            foreach (var header in Headers) sheet.SetCell(stale.Row, columns[header], string.Empty);

        var usedRows = sheet.Cells.Where(cell => cell.Row > 0).Select(cell => cell.Row).ToHashSet();
        var nextRow = usedRows.Count == 0 ? 1 : usedRows.Max() + 1;
        foreach (var place in places)
        {
            int row;
            IReadOnlyDictionary<int, string>? extras = null;
            if (oldById.TryGetValue(place.Id, out var prior))
            {
                row = prior.Row;
                extras = prior.ExtraCells;
            }
            else
            {
                while (usedRows.Contains(nextRow)) nextRow++;
                row = nextRow++;
                usedRows.Add(row);
            }

            WriteCell(sheet, row, columns[Headers[0]], place.Id);
            WriteCell(sheet, row, columns[Headers[1]], place.DisplayName);
            WriteCell(sheet, row, columns[Headers[2]], place.Note ?? string.Empty);
            WriteCell(sheet, row, columns[Headers[3]], place.Location.Latitude.ToString("R", CultureInfo.InvariantCulture));
            WriteCell(sheet, row, columns[Headers[4]], place.Location.Longitude.ToString("R", CultureInfo.InvariantCulture));
            WriteCell(sheet, row, columns[Headers[5]], place.SavedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            if (extras is null) continue;
            foreach (var (column, value) in extras) WriteCell(sheet, row, column, value);
        }
    }

    private static bool EnsureSavedPlacesTable(DataWorkbook workbook, DataSheet sheet,
        IReadOnlyDictionary<string, int> columns, IReadOnlyList<StoredPlace> places)
    {
        var oldCount = workbook.Tables.Count;
        var oldTable = workbook.Tables.FirstOrDefault(item => item.Name.Equals("MapsSavedPlaces", StringComparison.OrdinalIgnoreCase));
        (Guid SheetId, int StartRow, int StartColumn, int EndRow, int EndColumn, bool HasHeaders)? before = oldTable is null ? null : (oldTable.SheetId, oldTable.Range.StartRow, oldTable.Range.StartColumn,
            oldTable.Range.EndRow, oldTable.Range.EndColumn, oldTable.HasHeaders);
        var table = oldTable ?? new DataTableDefinition { Name = "MapsSavedPlaces", SheetId = sheet.Id };
        if (oldTable is null) workbook.Tables.Add(table);
        table.SheetId = sheet.Id;
        table.HasHeaders = true;
        var lastRow = places.Count == 0 ? 0 : places.Max(item => item.Row);
        table.Range = new DataCellRange { StartRow = 0, StartColumn = columns.Values.Min(), EndRow = lastRow, EndColumn = columns.Values.Max() };
        return oldTable is null || oldCount != workbook.Tables.Count || before != (table.SheetId, table.Range.StartRow,
            table.Range.StartColumn, table.Range.EndRow, table.Range.EndColumn, table.HasHeaders);
    }

    private async Task<MapsSavedPlaceDocument?> TryReadLegacyDocumentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_legacyStorePath)) return null;
        try
        {
            await using var stream = File.OpenRead(_legacyStorePath);
            return await JsonSerializer.DeserializeAsync<MapsSavedPlaceDocument>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> ReadRecentSearchesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_recentSearchesPath) && !File.Exists(_recentSearchesPath + ".bak")) return [];
        foreach (var path in new[] { _recentSearchesPath, _recentSearchesPath + ".bak" })
        {
            try
            {
                await using var stream = File.OpenRead(path);
                var document = await JsonSerializer.DeserializeAsync<RecentSearchDocument>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (document is not null) return MapsStoreLogic.NormaliseRecentSearches(document.Searches.Reverse());
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }
        return [];
    }

    private async Task WriteRecentSearchesAsync(IReadOnlyList<string> searches, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_recentSearchesPath);
        if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("The Maps data directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporary = _recentSearchesPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, new RecentSearchDocument(searches), JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            await using (var verify = File.OpenRead(temporary))
                _ = await JsonSerializer.DeserializeAsync<RecentSearchDocument>(verify, JsonOptions, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The recent-search history did not pass its persistence verification read.");
            if (File.Exists(_recentSearchesPath)) File.Copy(_recentSearchesPath, _recentSearchesPath + ".bak", overwrite: true);
            File.Move(temporary, _recentSearchesPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static void SetWorkbookPurpose(DataWorkbook workbook)
    {
        workbook.Metadata["haven.app"] = "maps";
        workbook.Metadata["haven.purpose"] = "Locally stored Maps saved places";
    }

    private static string ReadCell(DataSheet sheet, int row, int column) => sheet.GetCell(row, column)?.Value ?? string.Empty;
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static void WriteCell(DataSheet sheet, int row, int column, string value) => sheet.SetCell(row, column, value, null, DataCell.InferKind(value));

    private sealed record StoredPlace(int Row, SavedMapPlace Place, IReadOnlyDictionary<int, string> ExtraCells);
    private sealed record RecentSearchDocument(IReadOnlyList<string> Searches);
}
