using CakeOS.Platform;
using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiText = Haven.UI.Components.Text;

namespace HavenOS.Apps.Data.Hui;

public enum DataHuiAction
{
    EditSelected,
    SortAscending,
    SaveAndReopen,
    PreviousRowPage,
    NextRowPage,
    PreviousColumnPage,
    NextColumnPage,
}

/// <summary>Shared registry metadata for the Data HUI surface.</summary>
public static class DataHuiProduct
{
    public const string ProductId = "haven.data";
    public const string Entrypoint = "HavenOS.Apps.Data.Hui.DataHuiProduct";

    public static ProductRegistration CreateRegistration() => new(
        ProductId,
        "Data",
        ProductType.App,
        "/apps/data",
        CreateRoot,
        Entrypoint,
        new ProductCapabilities(true, false, false, false, ["workbook"]),
        new ProductDependencies([], [], []),
        new ProductPersistence(true, true, true, true),
        new ProductPermissions(["files.read", "files.write"], []),
        [],
        [
            AppLifecycleOperation.Create,
            AppLifecycleOperation.Activate,
            AppLifecycleOperation.Open,
            AppLifecycleOperation.Suspend,
            AppLifecycleOperation.Resume,
            AppLifecycleOperation.RequestClose,
            AppLifecycleOperation.Recover,
        ],
        LaunchAvailability.Always);

    public static void Register(IProductRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(CreateRegistration());
    }

    public static IRootElement CreateRoot(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new DataHuiRootElement(new DataHuiScene().Root);
    }
}

/// <summary>HUI framework root exposed through the shared platform root contract.</summary>
public sealed class DataHuiRootElement(Page root) : IHuiRootElement
{
    public Page Root { get; } = root ?? throw new ArgumentNullException(nameof(root));
    public object NativeRoot => Root;
}

/// <summary>
/// Thin CakeOS-native HUI scene for the already-typed DataGridSession boundary.
/// It deliberately owns no LibreOffice, DuckDB, Avalonia or platform-renderer objects.
/// </summary>
public sealed class DataHuiScene
{
    private readonly HuiButton[,] _cells = new HuiButton[DataGridSession.VisibleRows, DataGridSession.VisibleColumns];
    private readonly Queue<DataHuiAction> _actions = new();
    private int _selectedRow = 1;
    private int _selectedColumn;
    private int _rowStart;
    private int _columnStart;
    private string _activeSheetName = "No workbook open";

    public DataHuiScene()
    {
        Root = new Page
        {
            Name = "Data.Hui.Root",
            Layout = HavenLayout.Grid,
            Columns = "1fr",
            Rows = "Auto Auto 1fr Auto",
        };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("22px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        Root.SetValue(HavenProperties.Background, "Surface");

        var header = new Container { Name = "Data.Hui.Header", Layout = HavenLayout.Vertical };
        header.SetValue(HavenProperties.Row, 0);
        header.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        header.Add(new HuiText("Data") { Name = "Data.Hui.Title", Level = TextLevel.H2 });
        header.Add(new HuiText("Calc-backed local workbook · HUI-owned scene") { Name = "Data.Hui.Subtitle", Level = TextLevel.Caption });
        Root.Add(header);

        var toolbar = new Container { Name = "Data.Hui.Toolbar", Layout = HavenLayout.Horizontal };
        toolbar.SetValue(HavenProperties.Row, 1);
        toolbar.SetValue(HavenProperties.Gap, HavenLength.Px(8));

        EditButton = NewActionButton("Data.Hui.Edit", "Edit selected", DataHuiAction.EditSelected);
        SortAscendingButton = NewActionButton("Data.Hui.SortAscending", "Sort ascending", DataHuiAction.SortAscending);
        SaveReopenButton = NewActionButton("Data.Hui.SaveReopen", "Save + reopen", DataHuiAction.SaveAndReopen);
        PreviousRowsButton = NewActionButton("Data.Hui.PreviousRows", "Rows -10", DataHuiAction.PreviousRowPage);
        NextRowsButton = NewActionButton("Data.Hui.NextRows", "Rows +10", DataHuiAction.NextRowPage);
        PreviousColumnsButton = NewActionButton("Data.Hui.PreviousColumns", "Cols -8", DataHuiAction.PreviousColumnPage);
        NextColumnsButton = NewActionButton("Data.Hui.NextColumns", "Cols +8", DataHuiAction.NextColumnPage);
        PreviousRowsButton.Accessibility.AccessibleName = "Previous 10 rows";
        NextRowsButton.Accessibility.AccessibleName = "Next 10 rows";
        PreviousColumnsButton.Accessibility.AccessibleName = "Previous 8 columns";
        NextColumnsButton.Accessibility.AccessibleName = "Next 8 columns";
        toolbar.Add(EditButton);
        toolbar.Add(SortAscendingButton);
        toolbar.Add(SaveReopenButton);
        toolbar.Add(PreviousRowsButton);
        toolbar.Add(NextRowsButton);
        toolbar.Add(PreviousColumnsButton);
        toolbar.Add(NextColumnsButton);
        Root.Add(toolbar);

        Grid = new Container
        {
            Name = "Data.Hui.Grid",
            Layout = HavenLayout.Grid,
            Columns = string.Join(' ', Enumerable.Repeat("1fr", DataGridSession.VisibleColumns)),
            Rows = string.Join(' ', Enumerable.Repeat("1fr", DataGridSession.VisibleRows)),
        };
        Grid.SetValue(HavenProperties.Row, 2);
        Grid.SetValue(HavenProperties.Gap, HavenLength.Px(3));
        Grid.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        for (var row = 0; row < DataGridSession.VisibleRows; row++)
        {
            for (var column = 0; column < DataGridSession.VisibleColumns; column++)
            {
                var capturedRow = row;
                var capturedColumn = column;
                var cell = new HuiButton
                {
                    Name = $"Data.Hui.Cell.{row}.{column}",
                    Content = string.Empty,
                    Variant = ButtonVariant.Ghost,
                };
                cell.SetValue(HavenProperties.Row, row);
                cell.SetValue(HavenProperties.Column, column);
                cell.SetValue(HavenProperties.MinHeight, HavenLength.Px(38));
                cell.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
                cell.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
                cell.Invoked += (_, _) => SelectCell(capturedRow, capturedColumn);
                _cells[row, column] = cell;
                Grid.Add(cell);
            }
        }
        Root.Add(Grid);

        StatusText = new HuiText("No workbook open")
        {
            Name = "Data.Hui.Status",
            Level = TextLevel.Caption,
        };
        StatusText.SetValue(HavenProperties.Row, 3);
        StatusText.SetValue(HavenProperties.Foreground, "TextSecondary");
        Root.Add(StatusText);

        Root.ValidateUniqueNames();
        ApplySelectionState();
    }

    public Page Root { get; }
    public Container Grid { get; }
    public HuiButton EditButton { get; }
    public HuiButton SortAscendingButton { get; }
    public HuiButton SaveReopenButton { get; }
    public HuiButton PreviousRowsButton { get; }
    public HuiButton NextRowsButton { get; }
    public HuiButton PreviousColumnsButton { get; }
    public HuiButton NextColumnsButton { get; }
    public HuiText StatusText { get; }
    public int SelectedRow => _selectedRow;
    public int SelectedColumn => _selectedColumn;

    public HuiButton CellButton(int row, int column)
    {
        ValidateCell(row, column);
        return _cells[row, column];
    }

    public bool TryDequeueAction(out DataHuiAction action) => _actions.TryDequeue(out action);

    public void ApplySnapshot(DataGridSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Grid.Values.Count != DataGridSession.VisibleRows ||
            snapshot.Grid.Values.Any(row => row.Count != DataGridSession.VisibleColumns))
            throw new InvalidDataException("Data HUI requires the fixed 10 x 8 DataGridSession viewport.");
        if (snapshot.Grid.RowVisibility is not null && snapshot.Grid.RowVisibility.Count != DataGridSession.VisibleRows)
            throw new InvalidDataException("Data HUI received invalid row-visibility metadata.");

        _rowStart = snapshot.Grid.StartRow;
        _columnStart = snapshot.Grid.StartColumn;
        _activeSheetName = snapshot.ActiveSheet.Name;
        PreviousRowsButton.SetState(HavenElementState.Disabled, _rowStart == 0);
        PreviousColumnsButton.SetState(HavenElementState.Disabled, _columnStart == 0);

        for (var row = 0; row < DataGridSession.VisibleRows; row++)
        {
            var visible = snapshot.Grid.RowVisibility?[row] ?? true;
            for (var column = 0; column < DataGridSession.VisibleColumns; column++)
            {
                var cell = _cells[row, column];
                cell.Content = snapshot.Grid.Values[row][column];
                cell.SetValue(HavenProperties.Opacity, visible ? 1d : .32d);
                cell.SetState(HavenElementState.Disabled, !visible);
                var absoluteRow = _rowStart + row + 1;
                var absoluteColumn = _columnStart + column;
                cell.Accessibility.AccessibleName = visible
                    ? $"{ColumnName(absoluteColumn)}{absoluteRow}: {DisplayValue(cell.Content)}"
                    : $"Hidden row {absoluteRow}, {ColumnName(absoluteColumn)}: {DisplayValue(cell.Content)}";
            }
        }

        UpdateStatus();
        ApplySelectionState();
    }

    public void SelectCell(int row, int column)
    {
        ValidateCell(row, column);
        if (_cells[row, column].State.HasFlag(HavenElementState.Disabled))
            return;
        _selectedRow = row;
        _selectedColumn = column;
        ApplySelectionState();
        UpdateStatus();
    }

    private HuiButton NewActionButton(string name, string content, DataHuiAction action)
    {
        var button = new HuiButton
        {
            Name = name,
            Content = content,
            Variant = ButtonVariant.Secondary,
        };
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(42));
        button.Invoked += (_, _) => _actions.Enqueue(action);
        return button;
    }

    private void ApplySelectionState()
    {
        for (var row = 0; row < DataGridSession.VisibleRows; row++)
        for (var column = 0; column < DataGridSession.VisibleColumns; column++)
        {
            var selected = row == _selectedRow && column == _selectedColumn;
            _cells[row, column].SetState(HavenElementState.Selected, selected);
            _cells[row, column].SetValue(HavenProperties.BorderColor, selected ? "Accent" : "SurfaceBorder");
            _cells[row, column].SetValue(HavenProperties.BorderWidth, HavenLength.Px(selected ? 2 : 1));
        }
    }

    private static void ValidateCell(int row, int column)
    {
        if (row < 0 || row >= DataGridSession.VisibleRows)
            throw new ArgumentOutOfRangeException(nameof(row));
        if (column < 0 || column >= DataGridSession.VisibleColumns)
            throw new ArgumentOutOfRangeException(nameof(column));
    }

    private static string ColumnName(int column)
    {
        var value = column + 1;
        var name = string.Empty;
        while (value > 0)
        {
            value--;
            name = (char)('A' + (value % 26)) + name;
            value /= 26;
        }
        return name;
    }

    private void UpdateStatus()
    {
        var firstColumn = ColumnName(_columnStart);
        var lastColumn = ColumnName(_columnStart + DataGridSession.VisibleColumns - 1);
        var selectedCell = $"{ColumnName(_columnStart + _selectedColumn)}{_rowStart + _selectedRow + 1}";
        StatusText.Content = $"{_activeSheetName} · {DataGridSession.VisibleRows} rows × {DataGridSession.VisibleColumns} columns · rows {_rowStart + 1}–{_rowStart + DataGridSession.VisibleRows} · columns {firstColumn}–{lastColumn} · selected {selectedCell}";
    }

    private static string DisplayValue(string value) => string.IsNullOrEmpty(value) ? "blank" : value;
}

/// <summary>
/// Typed bridge between HUI intent and DataGridSession. All engine mutations stay
/// behind DataGridSession; the scene never receives UNO or DuckDB authority.
/// </summary>
public sealed class DataHuiController(DataGridSession session, DataHuiScene? scene = null)
{
    private readonly DataGridSession _session = session ?? throw new ArgumentNullException(nameof(session));

    public DataHuiScene Scene { get; } = scene ?? new DataHuiScene();

    public async Task<DataGridSessionSnapshot> OpenAsync(
        string path,
        bool readOnly = false,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _session.OpenAsync(path, readOnly, cancellationToken).ConfigureAwait(false);
        Scene.ApplySnapshot(snapshot);
        return snapshot;
    }

    public async Task<DataGridSessionSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _session.RefreshAsync(cancellationToken).ConfigureAwait(false);
        Scene.ApplySnapshot(snapshot);
        return snapshot;
    }

    public async Task<DataGridSessionSnapshot> ExecuteAsync(
        DataHuiAction action,
        string? editValue = null,
        string? destinationPath = null,
        CancellationToken cancellationToken = default)
    {
        DataGridSessionSnapshot snapshot;
        switch (action)
        {
            case DataHuiAction.EditSelected:
                if (editValue is null)
                    throw new ArgumentNullException(nameof(editValue), "EditSelected requires a typed value supplied by the application host.");
                snapshot = await _session.EditCellAsync(
                    Scene.SelectedRow,
                    Scene.SelectedColumn,
                    editValue,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                break;

            case DataHuiAction.SortAscending:
                snapshot = await _session.SortRangeAsync(
                    0,
                    0,
                    DataGridSession.VisibleRows,
                    DataGridSession.VisibleColumns,
                    Scene.SelectedColumn,
                    ascending: true,
                    containsHeader: true,
                    cancellationToken).ConfigureAwait(false);
                break;

            case DataHuiAction.SaveAndReopen:
                ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
                await _session.SaveAsAsync(destinationPath, cancellationToken).ConfigureAwait(false);
                await _session.CloseAsync(cancellationToken).ConfigureAwait(false);
                snapshot = await _session.OpenAsync(destinationPath, readOnly: false, cancellationToken).ConfigureAwait(false);
                break;

            case DataHuiAction.PreviousRowPage:
                snapshot = await _session.MovePageAsync(-1, 0, cancellationToken).ConfigureAwait(false);
                break;

            case DataHuiAction.NextRowPage:
                snapshot = await _session.MovePageAsync(1, 0, cancellationToken).ConfigureAwait(false);
                break;

            case DataHuiAction.PreviousColumnPage:
                snapshot = await _session.MovePageAsync(0, -1, cancellationToken).ConfigureAwait(false);
                break;

            case DataHuiAction.NextColumnPage:
                snapshot = await _session.MovePageAsync(0, 1, cancellationToken).ConfigureAwait(false);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }

        Scene.ApplySnapshot(snapshot);
        return snapshot;
    }
}
