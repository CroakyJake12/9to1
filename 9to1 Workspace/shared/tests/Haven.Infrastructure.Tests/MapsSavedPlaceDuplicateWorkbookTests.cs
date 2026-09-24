using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class MapsSavedPlaceDuplicateWorkbookTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "haven-maps-duplicate-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Save_and_remove_clean_duplicate_owned_cells_but_preserve_user_cells_and_unrelated_rows()
    {
        var paths = new TestPaths(_dataDirectory);
        var repository = new DataWorkbookRepository(paths);
        var store = new MapsSavedPlaceStore(paths, repository);
        var original = new SavedMapPlace("place-1", "Earliest", "Original", new GeoPoint(51.5, -0.12), DateTimeOffset.UtcNow);
        await store.SaveAsync(original, CancellationToken.None);

        var workbook = Assert.IsType<DataWorkbook>(await repository.LoadAsync(store.DataWorkbookId, CancellationToken.None));
        var sheet = workbook.Sheets[0];
        SetPlace(sheet, 2, original with { DisplayName = "Duplicate", Note = "Obsolete" });
        sheet.SetCell(2, 6, "duplicate user note");
        sheet.SetCell(8, 0, "unrelated row");
        sheet.SetCell(8, 6, "unrelated user value");
        await repository.SaveAsync(workbook, "Data app duplicate fixture", CancellationToken.None);

        await store.SaveAsync(original with { DisplayName = "Updated earliest", Note = "Kept" }, CancellationToken.None);

        workbook = Assert.IsType<DataWorkbook>(await repository.LoadAsync(store.DataWorkbookId, CancellationToken.None));
        sheet = workbook.Sheets[0];
        Assert.Equal("Updated earliest", sheet.GetCell(1, 1)?.Value);
        Assert.Equal("place-1", sheet.GetCell(1, 0)?.Value);
        Assert.Null(sheet.GetCell(2, 0));
        Assert.Null(sheet.GetCell(2, 1));
        Assert.Null(sheet.GetCell(2, 2));
        Assert.Null(sheet.GetCell(2, 3));
        Assert.Null(sheet.GetCell(2, 4));
        Assert.Null(sheet.GetCell(2, 5));
        Assert.Equal("duplicate user note", sheet.GetCell(2, 6)?.Value);
        Assert.Equal("unrelated row", sheet.GetCell(8, 0)?.Value);
        Assert.Equal("unrelated user value", sheet.GetCell(8, 6)?.Value);
        Assert.Single(await store.GetSavedPlacesAsync(CancellationToken.None));

        SetPlace(sheet, 3, original with { DisplayName = "Second duplicate" });
        sheet.SetCell(3, 6, "another user note");
        await repository.SaveAsync(workbook, "Data app second duplicate fixture", CancellationToken.None);
        await store.RemoveAsync(original.Id, CancellationToken.None);

        workbook = Assert.IsType<DataWorkbook>(await repository.LoadAsync(store.DataWorkbookId, CancellationToken.None));
        sheet = workbook.Sheets[0];
        Assert.All(new[] { 1, 2, 3 }, row =>
        {
            Assert.Null(sheet.GetCell(row, 0));
            Assert.Null(sheet.GetCell(row, 1));
            Assert.Null(sheet.GetCell(row, 2));
            Assert.Null(sheet.GetCell(row, 3));
            Assert.Null(sheet.GetCell(row, 4));
            Assert.Null(sheet.GetCell(row, 5));
        });
        Assert.Equal("duplicate user note", sheet.GetCell(2, 6)?.Value);
        Assert.Equal("another user note", sheet.GetCell(3, 6)?.Value);
        Assert.Equal("unrelated row", sheet.GetCell(8, 0)?.Value);
        Assert.Empty(await store.GetSavedPlacesAsync(CancellationToken.None));
    }

    private static void SetPlace(DataSheet sheet, int row, SavedMapPlace place)
    {
        sheet.SetCell(row, 0, place.Id);
        sheet.SetCell(row, 1, place.DisplayName);
        sheet.SetCell(row, 2, place.Note ?? string.Empty);
        sheet.SetCell(row, 3, place.Location.Latitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        sheet.SetCell(row, 4, place.Location.Longitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        sheet.SetCell(row, 5, place.SavedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class TestPaths(string dataDirectory) : IAppPaths
    {
        public string DataDirectory { get; } = dataDirectory;
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
