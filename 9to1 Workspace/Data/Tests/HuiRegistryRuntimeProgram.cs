using CakeOS.Platform;
using HavenOS.Apps.Data;
using Haven.UI;
using Haven.UI.Components;
using HavenOS.Apps.Data.Hui;

var dataRoot = Path.Combine(Path.GetTempPath(), "haven-data-hui-registry", Guid.NewGuid().ToString("N"));

try
{
    var registry = new ProductRegistry();
    DataHuiProduct.Register(registry);

    if (!registry.TryGet(DataHuiProduct.ProductId, out var registration) || registration is null)
        throw new InvalidOperationException("Data product was not registered in the shared registry.");
    if (registration.Route != "/apps/data" || registration.Entrypoint != DataHuiProduct.Entrypoint)
        throw new InvalidOperationException("Data product metadata does not match its registered route.");

    var permissions = new PermissionService(new VersionedSettingsStore(new XdgPlatformStorageLayout(dataRoot)));
    await permissions.GrantAsync("product.haven.data", "launch", "execute", GrantSource.System);
    var resolved = await new RegistryBackedRouter(registry).ResolveAsync(
        new ProductRouteRequest(registry.Identity, DataHuiProduct.ProductId, EmptyServiceProvider.Instance),
        permissions);

    if (resolved.Kind != RouteResolutionKind.Resolved || resolved.Root is not DataHuiRootElement { NativeRoot: Page })
        throw new InvalidOperationException("Data product did not resolve to a mountable HUI Page.");

    var sheet = new DataSheetSummary("Summary", 1);
    var workbook = new DataWorkbookHandle("paging-fixture", "paging-fixture.ods", false);
    var pageValues = Enumerable.Range(0, DataGridSession.VisibleRows)
        .Select(row => (IReadOnlyList<string>)Enumerable.Range(0, DataGridSession.VisibleColumns)
            .Select(column => $"R{row + 11}C{column + 9}")
            .ToArray())
        .ToArray();
    var scene = new DataHuiScene();
    scene.ApplySnapshot(new DataGridSessionSnapshot(
        workbook,
        [sheet],
        sheet,
        new DataRangeSnapshot(sheet.Name, 10, 8, pageValues)));
    if (scene.CellButton(0, 0).Accessibility.AccessibleName != "I11: R11C9")
        throw new InvalidOperationException("Paged HUI cell accessibility did not use the absolute spreadsheet address.");
    scene.SelectCell(1, 2);
    if (!(scene.StatusText.Content ?? string.Empty).Contains("rows 11–20", StringComparison.Ordinal) ||
        !(scene.StatusText.Content ?? string.Empty).Contains("columns I–P", StringComparison.Ordinal) ||
        !(scene.StatusText.Content ?? string.Empty).Contains("selected K12", StringComparison.Ordinal) ||
        scene.StatusText.Accessibility.AccessibleName != scene.StatusText.Content)
        throw new InvalidOperationException("Paged HUI status and accessibility summary diverged from the absolute selected address.");
    if (scene.PreviousRowsButton.State.HasFlag(HavenElementState.Disabled) ||
        scene.PreviousColumnsButton.State.HasFlag(HavenElementState.Disabled))
        throw new InvalidOperationException("HUI paging controls were disabled while the viewport was on a later page.");

    var firstPageValues = Enumerable.Range(0, DataGridSession.VisibleRows)
        .Select(row => (IReadOnlyList<string>)Enumerable.Range(0, DataGridSession.VisibleColumns)
            .Select(column => $"R{row + 1}C{column + 1}")
            .ToArray())
        .ToArray();
    var firstSheet = new DataSheetSummary("Sheet 1", 0);
    scene.ApplySnapshot(new DataGridSessionSnapshot(
        workbook with { Path = "another-workbook.ods" },
        [firstSheet, sheet],
        firstSheet,
        new DataRangeSnapshot(firstSheet.Name, 0, 0, firstPageValues)));
    if (!scene.PreviousRowsButton.State.HasFlag(HavenElementState.Disabled) ||
        !scene.PreviousColumnsButton.State.HasFlag(HavenElementState.Disabled) ||
        scene.CellButton(0, 0).Accessibility.AccessibleName != "A1: R1C1" ||
        !(scene.StatusText.Content ?? string.Empty).Contains("rows 1–10", StringComparison.Ordinal) ||
        !(scene.StatusText.Content ?? string.Empty).Contains("columns A–H", StringComparison.Ordinal) ||
        !(scene.StatusText.Content ?? string.Empty).Contains("selected C2", StringComparison.Ordinal) ||
        scene.StatusText.Accessibility.AccessibleName != scene.StatusText.Content)
        throw new InvalidOperationException("HUI sheet/workbook reset did not restore first-page addresses and accessible status.");

    Console.WriteLine("Data HUI registry, mountable root, paging address, accessibility and reset runtime checks passed.");
}
finally
{
    if (Directory.Exists(dataRoot))
        Directory.Delete(dataRoot, recursive: true);
}

file sealed class EmptyServiceProvider : IServiceProvider
{
    public static EmptyServiceProvider Instance { get; } = new();

    public object? GetService(Type serviceType) => null;
}
