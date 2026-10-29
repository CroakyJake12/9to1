// CUI Boards — Avalonia 12.0.1 Win32 desktop entry point.
// Loads the embedded, compiled Boards CUI documents and opens a native window.
// The runtime consumes the canonical compiled transport; no source path discovery or AXAML.

using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Polyline = Avalonia.Controls.Shapes.Polyline;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;

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

        Console.WriteLine("[CUI Boards] Loading embedded compiled CUI documents.");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return CuiNativeHost.ConfigureFonts(AppBuilder.Configure<BoardsApp>()
            .UseWin32()
            .UseSkia()
            .UseHarfBuzz());
    }

    /// <summary>Shared read-only drawing-layer renderer used by standalone and embedded surfaces.</summary>
    internal static async Task RenderReadOnlyInkOverlayAsync(Panel overlay, BoardsViewModel viewModel)
    {
        await BoardInkOverlayRenderer.RenderReadOnlyAsync(overlay, viewModel);
    }
}

/// <summary>
/// Avalonia Application hosting the Boards CUI surface with a live
/// BoardsViewModel bound to an IRichBoardSession.
/// </summary>
internal sealed class BoardsApp : Application
{
    private JsonFileHavenBoardStore? _store;
    private BoardsLocalDocuments? _localDocuments;
    private readonly CancellationTokenSource _databaseLifetime = new();
    private StandaloneDataSource? _databaseSource;

    private sealed class StandaloneDataSource(DataLocalRecordSessionFactory factory, string profile,
        CancellationToken lifetime) : IBoardsDatabaseSource
    {
        public Task<DataRecordSession> CreateDatabaseAsync(string title, CancellationToken token = default) =>
            factory.CreateAsync(profile, title, token, lifetime);
        public Task<DataRecordSession> OpenDatabaseAsync(Guid workbookID, CancellationToken token = default) =>
            factory.OpenAsync(profile, workbookID, token, lifetime);

    }
    private RecentBoardCatalog? _recentBoardCatalog;
    private string? _lastRecentPageKey;
    private IRichBoardSession? _session;
    private BoardsViewModel? _viewModel;
    private CuiControlLoader? _loader;
    private Control? _root;
    private Window? _window;
    private StackPanel? _blocksHost;
    private Panel? _inkOverlay;
    private BoardInkInteractionController? _inkInteraction;
    private BoardsTextAndKeyboardOwner? _textAndKeyboardOwner;
    private ComboBox? _styleBox;
    private ComboBox? _addKindBox;
    private bool _syncingCombos;
    private BoardsCloseSaveOwner? _closeSaveOwner;
    private Task? _closeAttempt;
    private Task? _closeActionDrain;
    private Task? _closeDrain;
    private bool _closeAccepted;
    private IDisposable? _closeInteractionRestriction;
    private readonly List<Task> _menuActionTasks = [];

    public override void OnFrameworkInitializationCompleted()
    {
        ApplySavedTheme();
        CuiNativeHost.InitialisePrimitiveTheme(this, "Boards",
            BoardsTheme.Mode == BoardsThemeMode.Dark ? CuiAppearance.Dark : CuiAppearance.Bright);
        // Real durable session: edits persist to a physical .9to1board file.
        // An explicit file opens through the local catalogue; otherwise resume a recoverable draft.
        var configuredRoot = Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
        var paths = new AppPaths();
        var profiles = new HomeLocalProfileIdentity(new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "Home", "state.json")),
            new OperatingSystemPrincipalSource());
        var actor = profiles.GetCurrentAsync(_databaseLifetime.Token).AsTask().GetAwaiter().GetResult()
            ?? throw new UnauthorizedAccessException("The actual local Home profile is unavailable.");
        _databaseSource = new StandaloneDataSource(new DataLocalRecordSessionFactory(new DataWorkbookRepository(paths), profiles),
            actor.ProfileId, _databaseLifetime.Token);
        _localDocuments = new BoardsLocalDocuments(actor.ProfileId, Path.Combine(paths.DataDirectory, "Boards"),
            _ => Task.FromResult(BoardsWorkspaceStorage.DefaultBoardsDirectory(paths.DataDirectory)));
        _store = _localDocuments.Store;
        _recentBoardCatalog = _localDocuments.Recent;
        var legacyFailures = string.IsNullOrWhiteSpace(configuredRoot)
            ? _localDocuments.AdoptLegacyRecentsAsync(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "9to1", "Boards", "recent-board-paths.json"),
                ContractSessionAdapter.DefaultBoardPath()).GetAwaiter().GetResult()
            : Array.Empty<string>();
        if (Program.OpenPath is not null)
        {
            var id = _localDocuments.ImportAsync(Program.OpenPath).GetAwaiter().GetResult();
            _session = _localDocuments.OpenAsync(id).GetAwaiter().GetResult();
        }
        else
        {
            // Starting the Boards app without a named board is the splash, not a board. The old
            // path resolved LatestOrCreateAsync, which reopened some other recent board - or
            // silently created an empty one every time none existed - and left that board's name
            // and page list on screen beside the splash. The placeholder session is store-less, so
            // nothing is written and nothing is recorded as recent until a board is chosen.
            _session = ContractSessionAdapter.OpenSplashPlaceholderAsync(
                HavenBoardDocumentCodec.CreateNew("Boards")).GetAwaiter().GetResult();
        }
        _viewModel = new BoardsViewModel(_session, databaseSource: _databaseSource);
        if (Program.OpenPath is null) _viewModel.SetNoBoardOpen();
        RecordCurrentPage();
        _viewModel.SetLanding(Program.OpenPath is null);
        if (legacyFailures.Count > 0)
            _viewModel.Set("StatusText", "Some existing boards need Open to locate or recover them: " + string.Join("; ", legacyFailures));
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
        viewModel.CreateBoardAsync = _ => CreateNewBoardAsync();
        viewModel.SaveAsBoardAsync = async token =>
        {
            if (_window is null || _session is null) return;
            var path = await BoardsWritablePresentation.PickSavePathAsync(_window, viewModel.Get("BoardTitle")?.ToString());
            if (path is null) return;
            await _session.SaveAsAsync(path, token);
        };
        viewModel.OpenBoardAsync = SwitchBoardAsync;
        viewModel.OpenBoardPathAsync = OpenBoardPathAsync;
        viewModel.RebuildRequested += PostRebuild;
        viewModel.StyleEditorRequested += PostStyleEditor;
    }

    private void UnwireViewModel(BoardsViewModel viewModel)
    {
        viewModel.RebuildRequested -= PostRebuild;
        viewModel.StyleEditorRequested -= PostStyleEditor;
        viewModel.NewBoardRequested -= PostNewBoard;
        viewModel.NewMenuRequested -= PostNewMenu;
        viewModel.SelectionChanged -= OnSelectionChanged;
        viewModel.NavRebuildRequested -= RebuildNav;
        viewModel.LandingRequested -= OnLandingRequested;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.FileMenuRequested -= PostFileMenu;
        viewModel.OverflowRequested -= PostOverflowMenu;
        viewModel.BoardPropertiesRequested -= ShowProperties;
        viewModel.CloseWindowRequested -= RequestWindowClose;
        viewModel.ThemeToggleRequested -= PostThemeToggle;
        viewModel.PickFileAsync = null;
        viewModel.OpenBoardAsync = null;
        viewModel.OpenBoardPathAsync = null;
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

        // Native close is canceled until the actual save acknowledges this draft.
        // Keep the UI loop free for storage completion and visible retry feedback.
        window.Closing += (_, args) =>
        {
            if (_closeAccepted) return;
            args.Cancel = true;
            if (_closeAttempt is { IsCompleted: false }) return;
            if (window.Content is Control content)
                _closeInteractionRestriction ??= CuiLifecycleProperties.RestrictInteraction(content);
            _closeAttempt = PrepareWindowCloseAsync(window);
        };

        if (_viewModel is not null)
        {
            try
            {
                _loader = BoardsCuiDocuments.CreateLoader();
                _loader.SetBindingContext(_viewModel);
                _loader.SetActionDispatcher(_viewModel);
                var document = BoardsCuiDocuments.Load(BoardsCuiDocuments.MainSourceName);
                var (root, diagnostics) = _loader.TryLoad(document);
                Console.WriteLine($"[CUI Boards] Compiled document diagnostics: {diagnostics.Count}");
                foreach (var diagnostic in diagnostics)
                    Console.WriteLine($"[CUI Boards] Diagnostic: {diagnostic}");

                if (root is not null)
                {
                    _root = root;
                    _loader.WireBindings(root);
                    _viewModel.Attach(root);
                    root.LayoutUpdated += WireDynamicSurfaceWhenReady;
                    window.Content = root;
                    Console.WriteLine($"[CUI Boards] Root: {root.GetType().Name} — awaiting mounted CUI layout");
                }
                else
                    window.Content = CreateFallbackUI(diagnostics.Count > 0
                        ? string.Join("\n", diagnostics.Select(diagnostic => diagnostic.ToString()))
                        : "Compiled Boards CUI failed to load");
            }
            catch (Exception error)
            {
                window.Content = CreateFallbackUI(error.Message);
            }
        }
        else
            window.Content = CreateFallbackUI("The Boards session is unavailable.");

        return window;
    }

    private void WireDynamicSurfaceWhenReady(object? sender, EventArgs e)
    {
        if (_root is null || FindByAutomationId<StackPanel>(_root, "BlocksHost") is null)
            return;
        _root.LayoutUpdated -= WireDynamicSurfaceWhenReady;
        WireDynamicSurface(_root);
    }

    // ----- Dynamic surface: top bar, nav, toolbar, context, blocks -----

    private StackPanel? _navHost;
    private TextBlock? _saveStateText;

    private async Task PrepareWindowCloseAsync(Window window)
    {
        // Publish the observer before any save/status callback can reenter close.
        await Task.Yield();
        // Complete already accepted CUI actions before selecting the current
        // session to save. Preserve failed action tasks; their failure is not a
        // reason to discard the user's current draft or forbid a physical retry.
        try
        {
            _closeActionDrain = Task.WhenAll(_menuActionTasks.Append(
                _loader?.WhenActionsIdleAsync() ?? Task.CompletedTask));
            await _closeActionDrain;
        }
        catch (Exception error)
        {
            Console.WriteLine($"[CUI Boards] Earlier action failed before close save: {_closeActionDrain?.Exception ?? error}");
        }
        var databasePreparation = _viewModel is null ? null :
            await _viewModel.PrepareDatabaseCloseAsync(_databaseLifetime.Token);
        if (_viewModel is not null && databasePreparation is null)
        {
            _closeInteractionRestriction?.Dispose(); _closeInteractionRestriction = null;
            return;
        }
        _closeSaveOwner ??= new BoardsCloseSaveOwner(() => _session
            ?? throw new InvalidOperationException("The Boards session is unavailable."));
        // A splash with no board open has nothing to persist: its placeholder session is store-less
        // by design, so attempting to save it would report a false failure when the window closes.
        var splashWithoutBoard = _viewModel is not null && !_viewModel.HasOpenBoard;
        try { if (!splashWithoutBoard) await _closeSaveOwner.SaveBeforeCloseAsync(); }
        catch (Exception error)
        {
            if (databasePreparation is not null) await databasePreparation.AbortAsync();
            Console.WriteLine($"[CUI Boards] Save on close failed: {error.Message}");
            _viewModel?.Set("StatusText", "Close canceled; save failed: " + error.Message.Split('\n')[0]);
            _closeInteractionRestriction?.Dispose();
            _closeInteractionRestriction = null;
            return;
        }
        try
        {
            if (databasePreparation is not null) await databasePreparation.CommitAsync();
            _closeDrain = _closeSaveOwner.DrainAfterSaveAsync(
                () =>
                {
                    _textAndKeyboardOwner?.Dispose();
                    _inkInteraction?.Dispose();
                    _loader?.Dispose();
                },
                () => Task.WhenAll(
                    _loader?.WhenActionsIdleAsync() ?? Task.CompletedTask,
                    _textAndKeyboardOwner?.DrainAsync() ?? Task.CompletedTask,
                    _inkInteraction?.DrainAsync() ?? Task.CompletedTask));
            await _closeDrain;
        }
        catch (Exception error)
        {
            // The original drain tasks remain owned and terminal. The draft was
            // physically saved, but cleanup failure is an unsuccessful exit.
            Console.WriteLine($"[CUI Boards] Close cleanup failed: {error}");
            Environment.ExitCode = 1;
        }
        _databaseLifetime.Cancel();
        _databaseLifetime.Dispose();
        _closeAccepted = true;
        window.Close();
    }

    private void WireDynamicSurface(Control root)
    {
        if (_viewModel is null)
            return;
        var activeViewModel = _viewModel;
        _blocksHost = FindByAutomationId<StackPanel>(root, "BlocksHost");
        _inkOverlay = FindByAutomationId<Panel>(root, "InkOverlayLayer");
        _styleBox = FindByAutomationId<ComboBox>(root, "StyleBox");
        _addKindBox = FindByAutomationId<ComboBox>(root, "AddKindBox");
        var insertButton = FindByAutomationId<Button>(root, "AddBlockKind");
        // Canonical CUI keeps authored identity on its outer control. Layout
        // panels are internal adapters, resolved through the helper below.
        _textAndKeyboardOwner?.Dispose();
        if (_window is not null)
        {
            _textAndKeyboardOwner = new BoardsTextAndKeyboardOwner(root, activeViewModel,
                command => ObserveMenuActionAsync(() => activeViewModel.DispatchAsync(command, null).AsTask()));
            _textAndKeyboardOwner.AttachWindow(_window);
        }
        ThemeApplier.ApplyChrome(root);
        FillTopBar(root);
        FillNav(root);
        FillToolbar();
        TameEditorScroll(root);
        _inkInteraction?.Dispose();
        _inkInteraction = _inkOverlay is null ? null : new BoardInkInteractionController(_inkOverlay, activeViewModel);
        _ = RenderInkOverlayAsync();
        UpdateWindowTitle();
        _viewModel.SelectionChanged += OnSelectionChanged;
        _viewModel.LandingRequested += OnLandingRequested;
        _viewModel.NavRebuildRequested += RebuildNav;
        _viewModel.RebuildRequested += PostRebuild;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.StyleEditorRequested += PostStyleEditor;
        _viewModel.FileMenuRequested += PostFileMenu;
        _viewModel.NewBoardRequested += PostNewBoard;
        _viewModel.NewMenuRequested += PostNewMenu;
        _viewModel.OverflowRequested += PostOverflowMenu;
        _viewModel.BoardPropertiesRequested += ShowProperties;
        _viewModel.CloseWindowRequested += RequestWindowClose;
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
        _ = RefreshLandingDataAsync();
    }

    private async Task RefreshLandingDataAsync()
    {
        var store = _store;
        var catalog = _recentBoardCatalog;
        var viewModel = _viewModel;
        if (store is null || catalog is null || viewModel is null) return;
        try
        {
            var boards = await catalog.LoadCardsAsync(store);
            var pages = await catalog.LoadRecentPagesAsync(store);
            if (!ReferenceEquals(viewModel, _viewModel)) return;
            viewModel.SetLandingData(boards, pages);
            viewModel.Set("RecentBoardStatus", catalog.LoadError is not null
                ? "Recent board list could not be read: " + catalog.LoadError
                : catalog.SaveError is not null ? "Recent locations could not be saved: " + catalog.SaveError : string.Empty);
        }
        catch (Exception error)
        {
            if (ReferenceEquals(viewModel, _viewModel))
            {
                viewModel.SetLandingData([], []);
                viewModel.Set("StatusText", "Recent boards could not be loaded: " + error.Message.Split('\n')[0]);
                viewModel.Set("RecentBoardStatus", "Recent boards could not be loaded. Existing board files are unchanged.");
            }
        }
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
        RecordCurrentPage();
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

    private void RecordCurrentPage()
    {
        if (_session?.FilePath is not { } boardPath || _viewModel is null || _recentBoardCatalog is null) return;
        var sectionId = _viewModel.SelectedSectionId;
        var pageId = _viewModel.SelectedPageId;
        var key = boardPath + "\0" + sectionId + "\0" + pageId;
        if (key == _lastRecentPageKey) return;
        _recentBoardCatalog.RecordPage(_localDocuments?.CatalogPathFor(boardPath) ?? boardPath, sectionId, pageId);
        _lastRecentPageKey = key;
    }

    private void OnLandingRequested() => _ = RefreshLandingDataAsync();

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
        _viewModel.RefreshCuiContext();
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
        var scroller = FindByAutomationId<CuiContainerControl>(root, "EditorScroll");
        if (scroller is not null && !CuiLayoutProperties.SetLiteral(scroller,"HorizontalScrolling","False",source:"Boards editor viewport"))
            throw new InvalidOperationException("The canonical Boards scrolling property is unavailable.");
    }

    private void FillTopBar(Control root)
    {
        if (_viewModel is null)
            return;
        // Top-bar topology and action wiring are authored once in Boards.cui.
        _saveStateText = FindByAutomationId<TextBlock>(root, "SaveState");
        SyncSaveState();
    }

    private void SyncSaveState()
    {
        if (_saveStateText is null || _viewModel is null)
            return;
        var state = _viewModel.Get("SaveStateText")?.ToString() ?? "Saved";
        CuiPropertyPresentation.Set(_saveStateText, "Text", state, CuiPropertyLevel.Instance, "Boards save owner");
        var role = state.StartsWith("Save failed", StringComparison.OrdinalIgnoreCase) ? "CuiValidationBrush" : "CuiTextSoftBrush";
        if (_saveStateText.TryFindResource(role, out var resource) && resource is IBrush brush)
            CuiPropertyPresentation.Set(_saveStateText, "Foreground", brush, CuiPropertyLevel.Instance, "Boards save owner");
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

    private void PostFileMenu() => OpenAuthoredMenu("FileMenuButton");

    private void PostNewMenu() => OpenAuthoredMenu("AddNewButton");

    private void OpenAuthoredMenu(string id) => Post(() =>
    {
        if (_root is null || _closeAttempt is { IsCompleted: false }) return;
        if (FindByAutomationId<MenuItem>(_root, id) is { IsEffectivelyEnabled: true } menu)
            menu.Open();
    });

    private void PostNewBoard() => Post(() => _ = ObserveMenuActionAsync(CreateNewBoardAsync));

    private void RequestWindowClose() => Post(() => _window?.Close());

    private async Task CreateNewBoardAsync()
    {
        if (_window is null || _store is null || _viewModel is null)
            return;
        try
        {
            var documents = _localDocuments ?? throw new InvalidOperationException("Local Boards storage is unavailable.");
            var id = await documents.CreateAsync();
            var adapter = await documents.OpenAsync(id);
            await ActivateBoardAsync(adapter);
            _recentBoardCatalog?.Record(_localDocuments is null ? adapter.FilePath : _localDocuments.PathFor((await adapter.GetDetachedContractDocumentAsync()).DocumentId));
            await RefreshLandingDataAsync();
        }
        catch (Exception error)
        {
            _viewModel?.Set("StatusText", "New board failed: " + error.Message.Split('\n')[0]);
            throw; // The menu owner retains the same failed task for close/drain diagnostics.
        }
    }

    private async Task ObserveMenuActionAsync(Func<Task> tapped)
    {
        if (_closeAttempt is { IsCompleted: false }) return;
        _menuActionTasks.RemoveAll(task => task.IsCompletedSuccessfully);
        if (_menuActionTasks.Count >= 128)
        {
            _viewModel?.Set("StatusText", "Close and reopen the saved board before retrying more menu actions.");
            return;
        }
        Task? original = null;
        try
        {
            original = tapped();
            _menuActionTasks.Add(original);
            await original;
        }
        catch (Exception error)
        {
            // Failed Save/Save As must remain a visible, retained task failure,
            // rather than escaping an async-void native menu handler.
            Console.WriteLine($"[CUI Boards] Menu action failed: {original?.Exception ?? error}");
            _viewModel?.Set("StatusText", "Action failed: " + error.Message.Split('\n')[0]);
        }
    }

    private void PostOverflowMenu() => OpenAuthoredMenu("OverflowButton");

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
        Post(() => _ = ObserveMenuActionAsync(async () =>
        {
            var (title, fileName, path, size) = viewModel.BoardInfo();
            var dialog = new Window
            {
                Title = "Board properties",
                Width = 440,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var properties = new CuiViewModel();
            properties.Set("Title", string.IsNullOrEmpty(title) ? "—" : title);
            properties.Set("File", string.IsNullOrEmpty(fileName) ? "—" : fileName);
            properties.Set("Location", string.IsNullOrEmpty(path) ? "—" : path);
            properties.Set("Size", BlockRenderer.FormatBytes(size));
            properties.On("CloseProperties", _ => dialog.Close());
            using var loader = BoardsCuiDocuments.CreateLoader();
            loader.SetBindingContext(properties); loader.SetActionDispatcher(properties);
            var (content, diagnostics) = loader.TryLoad(
                BoardsCuiDocuments.Load(BoardsCuiDocuments.BoardPropertiesSourceName));
            if (content is null)
                throw new InvalidOperationException("The canonical board properties view could not load: " + string.Join("; ", diagnostics));
            loader.WireBindings(content);
            dialog.Content = content;
            await dialog.ShowDialog(owner);
            await loader.WhenActionsIdleAsync();
        }));
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
            await RenderInkOverlayAsync();
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
            if (!File.Exists(path) && !File.Exists(path + ".bak"))
                throw new FileNotFoundException("The recent board and its recovery backup are both unavailable. Use Open to locate another file.");
            var documents = _localDocuments ?? throw new InvalidOperationException("Local Boards storage is unavailable.");
            var id = documents.OwnsPath(path)
                ? documents.IdFor(path) : await documents.ImportAsync(path);
            var adapter = await documents.OpenAsync(id);
            await ActivateBoardAsync(adapter, oldVm, oldSession);
            _recentBoardCatalog?.Record(_localDocuments is null ? adapter.FilePath : _localDocuments.PathFor((await adapter.GetDetachedContractDocumentAsync()).DocumentId));
            await RefreshLandingDataAsync();
        }
        catch (Exception error)
        {
            Console.WriteLine($"[CUI Boards] Open failed: {error.Message}");
            _viewModel?.Set("StatusText", "Open failed: " + error.Message.Split('\n')[0]);
        }
    }

    private async Task OpenBoardPathAsync(string path, string? pageId)
    {
        if (_store is null) return;
        var oldVm = _viewModel;
        var oldSession = _session;
        try
        {
            var documents = _localDocuments ?? throw new InvalidOperationException("Local Boards storage is unavailable.");
            var id = documents.OwnsPath(path)
                ? documents.IdFor(path) : await documents.ImportAsync(path);
            var adapter = await documents.OpenAsync(id);
            await ActivateBoardAsync(adapter, oldVm, oldSession, pageId);
            _recentBoardCatalog?.Record(_localDocuments is null ? adapter.FilePath : _localDocuments.PathFor((await adapter.GetDetachedContractDocumentAsync()).DocumentId));
            await RefreshLandingDataAsync();
        }
        catch (Exception error)
        {
            _viewModel?.Set("StatusText", "Recent board could not be opened: " + error.Message.Split('\n')[0]);
        }
    }

    private async Task ActivateBoardAsync(
        ContractSessionAdapter adapter, BoardsViewModel? oldVm = null, IRichBoardSession? oldSession = null,
        string? selectedPageId = null)
    {
        if (_loader is null || _root is null)
        {
            await adapter.DisposeAsync();
            throw new InvalidOperationException("The Boards surface is not ready to switch documents.");
        }

        oldVm ??= _viewModel;
        oldSession ??= _session;
        if (oldVm is not null)
        {
            var databasePreparation = await oldVm.PrepareDatabaseCloseAsync(_databaseLifetime.Token);
            if (databasePreparation is null)
            {
                await adapter.DisposeAsync();
                throw new InvalidOperationException("Save or discard database field changes before switching boards.");
            }
            await databasePreparation.CommitAsync();
        }
        _textAndKeyboardOwner?.Dispose();
        _inkInteraction?.Dispose();
        await (_inkInteraction?.DrainAsync() ?? Task.CompletedTask);
        var next = new BoardsViewModel(adapter, databaseSource: _databaseSource);
        next.SetLanding(false);
        if (selectedPageId is not null && !next.SelectPageById(selectedPageId))
            next.Set("StatusText", "The recent page no longer exists in this board; opened its current page instead.");
        if (oldVm is not null)
            UnwireViewModel(oldVm);
        _session = adapter;
        _viewModel = next;
        _lastRecentPageKey = null;
        RecordCurrentPage();
        WireViewModel(next);
        _loader.SetBindingContext(next);
        _loader.SetActionDispatcher(next);
        _loader.WireBindings(_root);
        next.Attach(_root);
        WireDynamicSurface(_root);
        if (oldSession is not null)
            await oldSession.DisposeAsync();
    }

    // ----- Shared editable page surface owners. -----

    private Task RenderInkOverlayAsync() => _inkInteraction?.RenderAsync() ?? Task.CompletedTask;

    // ----- Shared CUI theme + Light/Dark appearance -----

    private static string SettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "9to1", "Boards", "settings.json");

    private void ApplySavedTheme()
    {
        BoardsTheme.LoadGlobalThemePreference();
        // The Home appearance is the truth for a fresh profile: without this, a
        // board that has never been saved keeps the compiled "Light" default and
        // every open force-enabled light theme over the owner's dark Home.
        BoardsTheme.LoadGlobalAppearancePreference();
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
        => BoardsCuiControlFinder.FindByAutomationId<T>(root, automationId);

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

    private Control CreateFallbackUI(string message)
    {
        // An explicit CUI failure surface never substitutes for the board or
        // reports successful application readiness after a rejected document.
        _loader?.Dispose(); _loader = BoardsCuiDocuments.CreateLoader();
        var diagnostic = new CuiViewModel(); diagnostic.Set("Failure",message);
        _loader.SetBindingContext(diagnostic);
        var (root, diagnostics) = _loader.TryLoad(
            BoardsCuiDocuments.Load(BoardsCuiDocuments.StartupFailureSourceName));
        if (root is null) throw new InvalidOperationException("Boards and its CUI diagnostic surface could not load: "+string.Join("; ",diagnostics));
        _loader.WireBindings(root); return root;
    }
}
