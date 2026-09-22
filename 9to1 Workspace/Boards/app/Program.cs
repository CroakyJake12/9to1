// CUI Boards — Avalonia 12.0.1 Win32 desktop entry point.
// Loads Boards.cui and opens a native window with Skia rendering.
// Mirrors apps/Home/src/AvaloniaHome packaging: .cui discovery, fallback UI,
// diagnostics printed to console. No AXAML.

using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;

namespace CakeOS.Apps.Boards.App;

internal static class Program
{
    internal static string? OpenPath;

    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine("[CUI Boards] Starting Avalonia Win32 + Skia...");
        // File associations and drops may arrive as a split path when quoting is lost;
        // rejoin extra arguments onto the first when it does not resolve on its own.
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            OpenPath = args.Length > 1 && !File.Exists(args[0])
                ? string.Join(" ", args)
                : args[0];
            Console.WriteLine($"[CUI Boards] Board argument: {OpenPath}");
        }

        var cuiPath = FindCuiFile();
        Console.WriteLine($"[CUI Boards] .cui file: {cuiPath ?? "NOT FOUND"}");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<BoardsApp>()
            .UseWin32()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont();
    }

    internal static string? FindCuiFile()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Boards.cui"),
            Path.Combine(baseDir, "ui", "Boards.cui"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 12 && dir != null; i++)
        {
            var path = Path.Combine(dir.FullName, "9to1 Workspace", "Boards", "app", "Boards.cui");
            if (File.Exists(path))
                return path;
            var alt = Path.Combine(dir.FullName, "Boards", "app", "Boards.cui");
            if (File.Exists(alt))
                return alt;
            dir = dir.Parent;
        }

        return null;
    }
}

/// <summary>
/// Avalonia Application hosting the Boards CUI surface with a live
/// BoardsViewModel bound to an IRichBoardSession.
/// </summary>
internal sealed class BoardsApp : Application
{
    private JsonFileHavenBoardStore? _store;
    private IRichBoardSession? _session;
    private BoardsViewModel? _viewModel;
    private CuiControlLoader? _loader;
    private Control? _root;
    private Window? _window;
    private StackPanel? _blocksHost;
    private ComboBox? _styleBox;
    private ComboBox? _addKindBox;
    private bool _syncingCombos;

    public override void OnFrameworkInitializationCompleted()
    {
        Styles.Add(new FluentTheme());
        ApplySavedTheme();
        // Real durable session: edits persist to a physical .9to1board file.
        // Optional CLI arg opens/creates that board; otherwise the default board opens.
        var storeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "9to1", "Boards");
        _store = new JsonFileHavenBoardStore(storeRoot);
        _session = ContractSessionAdapter.OpenAsync(_store, Program.OpenPath).GetAwaiter().GetResult();
        _viewModel = new BoardsViewModel(_session);
        WireViewModel(_viewModel);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = BuildWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void WireViewModel(BoardsViewModel viewModel)
    {
        viewModel.PickFileAsync = PickFileAsync;
        viewModel.OpenBoardAsync = SwitchBoardAsync;
        viewModel.RebuildRequested += PostRebuild;
        viewModel.StyleEditorRequested += PostStyleEditor;
    }

    private void UnwireViewModel(BoardsViewModel viewModel)
    {
        viewModel.RebuildRequested -= PostRebuild;
        viewModel.StyleEditorRequested -= PostStyleEditor;
        viewModel.SelectionChanged -= OnSelectionChanged;
        viewModel.NavRebuildRequested -= RebuildNav;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.FileMenuRequested -= PostFileMenu;
        viewModel.OverflowRequested -= PostOverflowMenu;
        viewModel.ThemeToggleRequested -= PostThemeToggle;
        viewModel.PickFileAsync = null;
        viewModel.OpenBoardAsync = null;
        viewModel.Detach();
    }

    private Window BuildWindow()
    {
        var window = new Window
        {
            Title = "Boards",
            MinWidth = 900,
            MinHeight = 600,
            Width = 1440,
            Height = 900,
            Background = BoardsTheme.AppBackgroundBrush,
        };
        _window = window;

        window.KeyDown += (s, e) =>
        {
            if (_viewModel is null)
                return;
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (!ctrl)
                return;
            var handled = e.Key switch
            {
                Key.B => "ToggleBold",
                Key.I => "ToggleItalic",
                Key.U => "ToggleUnderline",
                Key.Z => "Undo",
                Key.Y => "Redo",
                Key.S => "SaveBoard",
                _ => null,
            };
            if (handled is not null)
            {
                e.Handled = true;
                _ = _viewModel.DispatchAsync(handled, null);
            }
            else if (e.Key == Key.Escape)
            {
                _viewModel.Set("StatusText", "Ready");
            }
        };

        // Flush pending edits before the window closes so close never loses notes.
        window.Closing += (_, _) =>
        {
            try { _session?.SaveAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception error) { Console.WriteLine($"[CUI Boards] Save on close failed: {error.Message}"); }
        };
        window.Closed += (_, _) =>
        {
            try { _session?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception error) { Console.WriteLine($"[CUI Boards] Dispose failed: {error.Message}"); }
        };

        var cuiPath = Program.FindCuiFile();
        if (cuiPath != null && _viewModel is not null)
        {
            Console.WriteLine($"[CUI Boards] Loading .cui: {cuiPath}");
            _loader = new CuiControlLoader();
            _loader.SetBindingContext(_viewModel);
            _loader.SetActionDispatcher(_viewModel);

            var (root, diagnostics) = _loader.LoadFile(cuiPath);
            Console.WriteLine($"[CUI Boards] Diagnostics: {diagnostics.Count}");
            foreach (var diagnostic in diagnostics)
                Console.WriteLine($"[CUI Boards] Diagnostic: {diagnostic}");

            if (root != null)
            {
                _root = root;
                _loader.WireBindings(root);
                _viewModel.Attach(root);
                WireDynamicSurface(root);
                window.Content = root;
                Console.WriteLine($"[CUI Boards] Root: {root.GetType().Name} — window rendered");
            }
            else
            {
                window.Content = CreateFallbackUI(diagnostics.Any()
                    ? string.Join("\n", diagnostics.Select(d => d.ToString()))
                    : "Failed to load CUI");
            }
        }
        else
        {
            window.Content = CreateFallbackUI("No .cui file found");
        }

        return window;
    }

    // ----- Dynamic surface: top bar, nav, toolbar, context, blocks -----

    private StackPanel? _navHost;
    private TextBlock? _saveStateText;

    private void WireDynamicSurface(Control root)
    {
        if (_viewModel is null)
            return;
        _blocksHost = FindByAutomationId<StackPanel>(root, "BlocksHost");
        _styleBox = FindByAutomationId<ComboBox>(root, "StyleBox");
        _addKindBox = FindByAutomationId<ComboBox>(root, "AddKindBox");
        var insertButton = FindByAutomationId<Button>(root, "AddBlockKind");
        // NOTE: the CUI loader never applies id="..." to Control.Name
        // (CuiComponent.Name is dropped), so static-surface lookup uses the
        // automationid attributes above; dynamic controls use real Names.
        WireTitleBox(root);
        WireSearchBox(root);
        FillTopBar(root);
        FillNav(root);
        FillToolbar();
        ThemeApplier.ApplyChrome(root);
        TameEditorScroll(root);
        UpdateWindowTitle();
        _viewModel.SelectionChanged += OnSelectionChanged;
        _viewModel.NavRebuildRequested += RebuildNav;
        _viewModel.RebuildRequested += PostRebuild;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.StyleEditorRequested += PostStyleEditor;
        _viewModel.FileMenuRequested += PostFileMenu;
        _viewModel.OverflowRequested += PostOverflowMenu;
        _viewModel.ThemeToggleRequested += PostThemeToggle;
        if (_styleBox is not null)
        {
            ToolTip.SetTip(_styleBox, "Apply a style to the selected block");
            AutomationProperties.SetName(_styleBox, "Block style");
            _styleBox.SelectionChanged += (_, _) =>
            {
                if (_syncingCombos || _viewModel is null)
                    return;
                if (_styleBox.SelectedItem is ComboBoxItem selected && selected.Tag is string styleId)
                    _ = _viewModel.ApplyStyleToSelectedAsync(styleId);
            };
        }
        if (_addKindBox is not null)
        {
            ToolTip.SetTip(_addKindBox, "Block kind or style to insert");
            AutomationProperties.SetName(_addKindBox, "Block kind to insert");
        }
        if (insertButton is not null)
        {
            ToolTip.SetTip(insertButton, "Insert the selected block kind");
            insertButton.Click += (_, _) =>
            {
                if (_viewModel is null || _addKindBox?.SelectedItem is not ComboBoxItem selected)
                    return;
                var tag = selected.Tag as string;
                if (!string.IsNullOrEmpty(tag))
                    _ = _viewModel.InsertKindAsync(tag);
            };
        }
        PostRebuild();
    }

    private void PostRebuild()
    {
        if (_viewModel is null)
            return;
        if (Dispatcher.UIThread.CheckAccess())
            _ = RebuildAllAsync();
        else
            Dispatcher.UIThread.Post(() => _ = RebuildAllAsync());
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "SaveStateText" or "StatusText")
            Post(SyncSaveState);
    }

    private void OnSelectionChanged()
    {
        if (_viewModel is null)
            return;
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshToolbarAndContext();
            if (!NavBuilder.RenameActive)
                RebuildNav();
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                RefreshToolbarAndContext();
                if (!NavBuilder.RenameActive)
                    RebuildNav();
            });
        }
    }

    private void RebuildNav()
    {
        if (_viewModel is null)
            return;
        if (Dispatcher.UIThread.CheckAccess())
            RebuildNavCore();
        else
            Dispatcher.UIThread.Post(RebuildNavCore);
    }

    private void RebuildNavCore()
    {
        if (_root is null || _viewModel is null)
            return;
        if (NavBuilder.RenameActive)
            return;
        var host = FindByAutomationId<StackPanel>(_root, "NavHost");
        if (host is not null)
            NavBuilder.Rebuild(host, _viewModel);
    }

    private async Task RebuildAllAsync()
    {
        await RebuildBlocksHostAsync();
        RefreshToolbarAndContext();
        RebuildNavCore();
        UpdateWindowTitle();
    }

    private void RefreshToolbarAndContext()
    {
        if (_root is null || _viewModel is null)
            return;
        var toolbar = FindByAutomationId<StackPanel>(_root, "ToolbarHost");
        if (toolbar is not null)
            ToolbarBuilder.Rebuild(toolbar, _viewModel);
        var context = FindByAutomationId<StackPanel>(_root, "ContextHost");
        if (context is not null)
            ContextPanels.Rebuild(context, _viewModel);
    }

    private void UpdateWindowTitle()
    {
        if (_window is null || _viewModel is null)
            return;
        var title = _viewModel.Session.Document.Title;
        _window.Title = string.IsNullOrWhiteSpace(title) ? "Boards" : title + " — Boards";
    }

    private void TameEditorScroll(Control root)
    {
        var scroller = FindByAutomationId<ScrollViewer>(root, "EditorScroll");
        if (scroller is not null)
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    private void WireSearchBox(Control root)
    {
        if (_viewModel is null)
            return;
        var search = FindByAutomationId<TextBox>(root, "NavSearchBox");
        if (search is null)
            return;
        var viewModel = _viewModel;
        search.TextChanged += (_, _) => viewModel.SearchText = search.Text ?? string.Empty;
    }

    private void FillTopBar(Control root)
    {
        if (_viewModel is null)
            return;
        var left = FindByAutomationId<StackPanel>(root, "TopBarLeft");
        var right = FindByAutomationId<StackPanel>(root, "TopBarRight");
        if (left is null || right is null)
            return;
        var vm = _viewModel;
        ShellChrome.BuildTopBar(left, right, vm,
            onFileMenu: PostFileMenu,
            onOverflow: PostOverflowMenu,
            onTheme: PostThemeToggle,
            onSaveState: text => { _saveStateText = text; SyncSaveState(); });
    }

    private void SyncSaveState()
    {
        if (_saveStateText is null || _viewModel is null)
            return;
        var state = _viewModel.Get("SaveStateText")?.ToString() ?? "Saved";
        _saveStateText.Text = state;
        _saveStateText.Foreground = state.StartsWith("Save failed", StringComparison.OrdinalIgnoreCase)
            ? BoardsTheme.ErrorBrush
            : BoardsTheme.SecondaryTextBrush;
    }

    private void FillNav(Control root)
    {
        if (_viewModel is null)
            return;
        _navHost = FindByAutomationId<StackPanel>(root, "NavHost");
        RebuildNavCore();
    }

    private void FillToolbar()
    {
        RefreshToolbarAndContext();
    }

    private void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private void PostFileMenu()
    {
        if (_window is null || _viewModel is null)
            return;
        Post(() =>
        {
            if (_window is null || _viewModel is null)
                return;
            var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
            var vm = _viewModel;
            var win = _window;
            flyout.Items.Add(MenuAction("New board", async () => await vm.DispatchAsync("NewBoard", null)));
            flyout.Items.Add(MenuAction("Open…", async () => await vm.DispatchAsync("OpenBoard", null)));
            flyout.Items.Add(MenuAction("Save", async () => await vm.DispatchAsync("SaveBoard", null), "Ctrl+S"));
            flyout.Items.Add(MenuAction("Save As…", async () => await vm.DispatchAsync("SaveAsBoard", null)));
            flyout.Items.Add(new Separator());
            flyout.Items.Add(MenuAction("Board properties…", () => ShowProperties()));
            flyout.Items.Add(new Separator());
            flyout.Items.Add(MenuAction("Exit", () => win.Close()));
            flyout.ShowAt(win, true);
        });
    }

    private static MenuItem MenuAction(string header, Func<Task> tapped, string? gesture = null)
    {
        var item = new MenuItem { Header = gesture is null ? header : $"{header}    {gesture}" };
        AutomationProperties.SetName(item, header);
        item.Click += async (_, _) => await tapped();
        return item;
    }

    private static MenuItem MenuAction(string header, Action tapped)
    {
        var item = new MenuItem { Header = header };
        AutomationProperties.SetName(item, header);
        item.Click += (_, _) => tapped();
        return item;
    }

    private void PostOverflowMenu()
    {
        if (_window is null || _viewModel is null)
            return;
        Post(() =>
        {
            if (_window is null || _viewModel is null)
                return;
            var vm = _viewModel;
            var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
            flyout.Items.Add(MenuAction("Styles…", async () => await vm.DispatchAsync("ManageStyles", null)));
            flyout.Items.Add(MenuAction(
                BoardsTheme.Mode == BoardsThemeMode.Dark ? "Light theme" : "Dark theme",
                () => PostThemeToggle()));
            flyout.Items.Add(MenuAction("Board properties…", () => ShowProperties()));
            flyout.ShowAt(_window, true);
        });
    }

    private void PostThemeToggle()
    {
        BoardsTheme.Toggle();
        SaveThemeChoice();
        ApplyThemeVariant();
        if (_root is null || _viewModel is null)
            return;
        Post(() =>
        {
            if (_root is null || _viewModel is null)
                return;
            ThemeApplier.ApplyChrome(_root);
            RefreshToolbarAndContext();
            RebuildNavCore();
            _ = RebuildBlocksHostAsync();
        });
    }

    private void ShowProperties()
    {
        if (_window is null || _viewModel is null)
            return;
        var owner = _window;
        var viewModel = _viewModel;
        Post(() =>
        {
            var (title, fileName, path, size) = viewModel.BoardInfo();
            var dialog = new Window
            {
                Title = "Board properties",
                Width = 440,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = BoardsTheme.CardBrush,
            };
            var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = "Board properties",
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = BoardsTheme.TextBrush,
                Margin = new Thickness(0, 0, 0, 12),
            });
            void Row(string label, string value)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = BoardsTheme.SecondaryTextBrush,
                });
                panel.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrEmpty(value) ? "—" : value,
                    FontSize = 13,
                    Foreground = BoardsTheme.TextBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8),
                });
            }
            Row("Title", title);
            Row("File", fileName);
            Row("Location", path);
            Row("Size", BlockRenderer.FormatBytes(size));
            var close = new Button { Content = "Close", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            AutomationProperties.SetName(close, "Close properties");
            close.Click += (_, _) => dialog.Close();
            panel.Children.Add(close);
            dialog.Content = panel;
            dialog.ShowDialog(owner);
        });
    }

    private void PostStyleEditor()
    {
        if (_window is null || _viewModel is null)
            return;
        if (Dispatcher.UIThread.CheckAccess())
            _ = StyleEditor.ShowAsync(_window, _viewModel);
        else
            Dispatcher.UIThread.Post(() => _ = StyleEditor.ShowAsync(_window!, _viewModel!));
    }

    private async Task RebuildBlocksHostAsync()
    {
        if (_blocksHost is null || _viewModel is null)
            return;
        try
        {
            await BlockRenderer.RebuildAsync(_blocksHost, _viewModel);
            WireInkCanvases(_blocksHost);
            FillStyleBox();
            FillKindBox();
        }
        catch (Exception error)
        {
            Console.WriteLine($"[CUI Boards] Rebuild failed: {error.Message}");
        }
    }

    private void FillStyleBox()
    {
        if (_styleBox is null || _viewModel is null)
            return;
        _syncingCombos = true;
        try
        {
            var selectedStyle = _viewModel.FindBlock(_viewModel.SelectedBlockId)?.StyleId;
            _styleBox.ItemsSource = _viewModel.StyleList
                .Select(s => new ComboBoxItem { Content = s.Name, Tag = s.Id })
                .ToList();
            _styleBox.SelectedItem = _styleBox.ItemsSource.Cast<ComboBoxItem>()
                .FirstOrDefault(i => (string?)i.Tag == selectedStyle);
        }
        finally
        {
            _syncingCombos = false;
        }
    }

    private void FillKindBox()
    {
        if (_addKindBox is null || _viewModel is null)
            return;
        _syncingCombos = true;
        try
        {
            var prior = (_addKindBox.SelectedItem as ComboBoxItem)?.Tag as string;
            var items = new List<ComboBoxItem>
            {
                new() { Content = "Paragraph", Tag = "paragraph" },
                new() { Content = "Heading", Tag = "heading" },
                new() { Content = "Checklist", Tag = "checklist" },
                new() { Content = "Table", Tag = "table" },
                new() { Content = "Image…", Tag = "image" },
                new() { Content = "Divider", Tag = "divider" },
                new() { Content = "Graph", Tag = "graph" },
            };
            items.AddRange(_viewModel.StyleList.Select(s =>
                new ComboBoxItem { Content = "Style: " + s.Name, Tag = "style:" + s.Id }));
            _addKindBox.ItemsSource = items;
            _addKindBox.SelectedItem = items.FirstOrDefault(i => (string?)i.Tag == prior) ?? items[0];
        }
        finally
        {
            _syncingCombos = false;
        }
    }

    // ----- File pickers -----

    private async Task<string?> PickFileAsync(string kind)
    {
        if (_window is null)
            return null;
        var options = kind switch
        {
            "image" => new FilePickerOpenOptions
            {
                Title = "Choose an image",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Images")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"],
                        MimeTypes = ["image/png", "image/jpeg", "image/gif", "image/bmp", "image/webp"],
                    },
                ],
            },
            "board" => new FilePickerOpenOptions
            {
                Title = "Open board",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Boards") { Patterns = ["*.9to1board"] }],
            },
            _ => new FilePickerOpenOptions
            {
                Title = "Choose a file",
                AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.All],
            },
        };
        var files = await _window.StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task SwitchBoardAsync()
    {
        if (_store is null || _loader is null || _root is null)
            return;
        var path = await PickFileAsync("board");
        if (string.IsNullOrWhiteSpace(path))
            return;
        var oldVm = _viewModel;
        var oldSession = _session;
        try
        {
            var adapter = await ContractSessionAdapter.OpenAsync(_store, path);
            var next = new BoardsViewModel(adapter);
            if (oldVm is not null)
                UnwireViewModel(oldVm);
            _session = adapter;
            _viewModel = next;
            WireViewModel(next);
            _loader.SetBindingContext(next);
            _loader.SetActionDispatcher(next);
            _loader.WireBindings(_root);
            next.Attach(_root);
            WireDynamicSurface(_root);
            if (oldSession is not null)
                await oldSession.DisposeAsync();
        }
        catch (Exception error)
        {
            Console.WriteLine($"[CUI Boards] Open failed: {error.Message}");
            _viewModel?.Set("StatusText", "Open failed: " + error.Message.Split('\n')[0]);
        }
    }

    // ----- Ink: pressure capture, tool routing, eraser path -----

    private void WireInkCanvases(Control root)
    {
        if (_viewModel is null)
            return;
        foreach (var canvas in FindAll<Canvas>(root))
        {
            if (canvas.Name is not { } name || !name.StartsWith("ink_", StringComparison.Ordinal))
                continue;
            if (canvas.Tag as string == "ink-wired")
                continue;
            canvas.Tag = "ink-wired";
            var drawing = false;
            var points = new System.Collections.Generic.List<(double X, double Y, double Pressure)>();
            var viewModel = _viewModel;
            canvas.PointerPressed += (_, e) =>
            {
                // Eraser hit-test path: erase strokes near the tap point.
                if (string.Equals(viewModel.InkTool, "Eraser", StringComparison.OrdinalIgnoreCase))
                {
                    var tap = e.GetPosition(canvas);
                    _ = EraseAtAsync(viewModel, tap.X, tap.Y);
                    e.Handled = true;
                    return;
                }
                drawing = true;
                points.Clear();
                var position = e.GetPosition(canvas);
                points.Add((position.X, position.Y, ReadPressure(e, canvas)));
                DrawDot(canvas, position, viewModel.InkWidth);
            };
            canvas.PointerMoved += (_, e) =>
            {
                if (!drawing)
                    return;
                var position = e.GetPosition(canvas);
                points.Add((position.X, position.Y, ReadPressure(e, canvas)));
                DrawDot(canvas, position, viewModel.InkWidth);
            };
            canvas.PointerReleased += (_, _) =>
            {
                if (!drawing)
                    return;
                drawing = false;
                if (points.Count > 0)
                    _ = viewModel.CommitInkStrokeAsync(
                        points.ToArray(), viewModel.InkWidth, viewModel.InkColor, viewModel.InkTool);
            };
        }
    }

    private static async Task EraseAtAsync(BoardsViewModel viewModel, double x, double y)
    {
        if (viewModel.Session is ContractSessionAdapter adapter)
            await adapter.EraseInkAtCurrentPageAsync(x, y);
        else
            await viewModel.ClearInkAsync();
        viewModel.RefreshAfterEdit();
    }

    private static async Task RemoveLastStrokeAsync(BoardsViewModel viewModel)
    {
        if (viewModel.Session is ContractSessionAdapter adapter)
            await adapter.RemoveLastInkStrokeAsync();
        else
            await viewModel.ClearInkAsync();
        viewModel.RefreshAfterEdit();
    }

    private static double ReadPressure(PointerEventArgs e, Visual relativeTo)
    {
        try
        {
            var pressure = e.GetCurrentPoint(relativeTo).Properties.Pressure;
            return pressure > 0 ? Math.Clamp(pressure, 0.05, 1) : 0.5;
        }
        catch
        {
            return 0.5;
        }
    }

    private static void DrawDot(Canvas canvas, Point position, double width)
    {
        // Visual feedback only; the stroke is committed once per gesture on release.
        var size = Math.Clamp(width, 2, 24);
        canvas.Children.Add(new Avalonia.Controls.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            Fill = Brushes.Black,
        });
        var dot = canvas.Children[^1];
        Canvas.SetLeft(dot, position.X - size / 2);
        Canvas.SetTop(dot, position.Y - size / 2);
    }

    /// <summary>
    /// The loader drops id="..." (never sets Control.Name), so the static
    /// title box is wired here by automation id. Without this, title edits
    /// would never reach the session.
    /// </summary>
    private void WireTitleBox(Control root)
    {
        if (_viewModel is null)
            return;
        var titleBox = FindByAutomationId<TextBox>(root, "BoardTitleBox");
        if (titleBox is null)
            return;
        var viewModel = _viewModel;
        titleBox.TextChanged += (_, _) => viewModel.EditText("BoardTitleBox", titleBox.Text ?? string.Empty);
    }

    // ----- Theme (Fluent control templates + Boards palette + Light/Dark) -----

    private static string SettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "9to1", "Boards", "settings.json");

    private void ApplySavedTheme()
    {
        try
        {
            var path = SettingsPath();
            if (File.Exists(path) && File.ReadAllText(path).Contains("\"dark\"", StringComparison.OrdinalIgnoreCase))
                BoardsTheme.SetMode(BoardsThemeMode.Dark);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        ApplyThemeVariant();
    }

    internal static void ApplyThemeVariant()
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant =
                BoardsTheme.Mode == BoardsThemeMode.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    internal static void SaveThemeChoice()
    {
        try
        {
            var path = SettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"theme\":\"" + BoardsTheme.Mode.ToString().ToLowerInvariant() + "\"}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ----- Tree helpers -----

    private static T? FindByAutomationId<T>(Control root, string automationId) where T : Control
    {
        if (root is T match
            && (string.Equals(root.Name, automationId, StringComparison.Ordinal)
                || string.Equals(AutomationProperties.GetAutomationId(root), automationId, StringComparison.Ordinal)))
            return match;
        foreach (var child in LogicalChildren(root))
        {
            var found = FindByAutomationId<T>(child, automationId);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static T? FindByName<T>(Control root, string name) where T : Control
    {
        if (root is T match && string.Equals(root.Name, name, StringComparison.Ordinal))
            return match;
        foreach (var child in LogicalChildren(root))
        {
            var found = FindByName<T>(child, name);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static System.Collections.Generic.IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T match)
            yield return match;
        foreach (var child in LogicalChildren(root))
            foreach (var nested in FindAll<T>(child))
                yield return nested;
    }

    private static System.Collections.Generic.IEnumerable<Control> LogicalChildren(Control control)
    {
        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is Control c)
                    yield return c;
        }
        else if (control is Decorator decorator && decorator.Child is Control decChild)
        {
            yield return decChild;
        }
        else if (control is ContentControl cc && cc.Content is Control ccChild)
        {
            yield return ccChild;
        }
        else if (control is ItemsControl ic)
        {
            foreach (var item in ic.Items)
                if (item is Control icChild)
                    yield return icChild;
        }
        else if (control is ScrollViewer scroller && scroller.Content is Control scChild)
        {
            yield return scChild;
        }
    }

    private static StackPanel CreateFallbackUI(string message)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Boards",
            FontSize = 32,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        });

        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 600,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        });

        return panel;
    }
}
