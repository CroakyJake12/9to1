using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace CakeOS.Apps.Boards.App;

/// <summary>One floating insertion surface. CUI owns destination actions; this owns native hosting.</summary>
internal sealed class InsertMenuContext(BoardsViewModel vm, Popup popup, TextBox search) : IDisposable
{
    private readonly string _pageId = vm.SelectedPageId;
    private readonly RichBoardDocument _document = vm.Session.Document;
    private readonly List<Task> _actions = [];
    private CuiControlLoader? _destinationLoader;
    private CuiControlLoader? _surfaceLoader;
    private bool _disposed;
    private long _opening;
    internal long ContentVersion { get; private set; }
    internal BlockInsertionPreviewOwner? Owner { get; private set; }
    public Popup Popup { get; } = popup;
    public TextBox Search { get; } = search;
    public string? AnchorBlockId { get; init; }
    public string? RowBlockId { get; init; }
    public string? Placement { get; set; }
    public Action? Refill { get; set; }
    internal long Opening => _opening;

    internal Control CreateSurface(Control body)
    {
        var loader = BoardsCuiDocuments.CreateLoader();
        var (root, diagnostics) = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.InsertMenuSurfaceSourceName));
        var host = root is null ? null : BoardsCuiControlFinder.FindByAutomationId<StackPanel>(root, "InsertMenuContents");
        if (root is null || host is null)
        {
            loader.Dispose();
            throw new InvalidOperationException("Insertion surface CUI: " + string.Join("; ", diagnostics.Select(d => d.Message)));
        }
        host.Children.Add(body);
        _surfaceLoader = loader;
        return root;
    }

    internal bool Open()
    {
        if (_disposed || !vm.IsEditMode || vm.SelectedPageId != _pageId ||
            !ReferenceEquals(_document, vm.Session.Document)) return false;
        Close();
        _opening++;
        if (AnchorBlockId is not null)
        {
            Owner = vm.BeginInsertionPreview(AnchorBlockId, RowBlockId ?? AnchorBlockId);
            if (Owner is null) return false;
            vm.InsertionPreviewChanged += CheckOwner;
        }
        Search.Text = string.Empty;
        Refill?.Invoke();
        Popup.Open();
        if (!Popup.IsOpen) { Close(); return false; }
        Search.Focus();
        return true;
    }

    private void CheckOwner()
    {
        if (Owner is { } owner && !vm.IsInsertionPreviewCurrent(owner)) Close();
    }

    internal void Close()
    {
        vm.InsertionPreviewChanged -= CheckOwner;
        var owner = Owner;
        Owner = null;
        Placement = null;
        if (owner is not null) vm.EndInsertionPreview(owner);
        RetireDestination();
        Popup.Close();
    }

    internal void RetireDestination()
    {
        ContentVersion++;
        if (_destinationLoader is not { } loader) return;
        _destinationLoader = null;
        Retain(loader.WhenActionsIdleAsync());
        loader.Dispose();
    }

    internal Control CreateDestination()
    {
        if (Owner is not { } owner) throw new InvalidOperationException("The insert destination has no active owner.");
        var stage = vm.BeginInsertionPreviewStage(owner);
        var loader = BoardsCuiDocuments.CreateLoader();
        var context = new DestinationContext(this, vm, owner, stage);
        loader.SetBindingContext(context);
        loader.SetActionDispatcher(context);
        var (root, diagnostics) = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.InsertDestinationSourceName));
        if (root is null)
        {
            loader.Dispose();
            throw new InvalidOperationException("Insertion destination CUI: " + string.Join("; ", diagnostics.Select(d => d.Message)));
        }
        loader.WireBindings(root);
        AttachDestinationRowCues(root);
        _destinationLoader = loader;
        return root;
    }

    /// <summary>
    /// Gives each authored destination row a visible chosen state for both the pointer and the keyboard.
    ///
    /// Measured on the running build (2 November 2026, run58/run59, `home33`/`profile34` sandboxes, receipts
    /// BC02-BC04 and BD01-BD02, frames 307-309 and 311): pressing Down in this stage moved the keyboard and
    /// changed the page-side insertion preview (the accent bar beside the plus and along the card), yet no
    /// row in the menu changed appearance at all, so a keyboard user cannot see which destination Enter will
    /// apply - and this is the one choice that decides where a block lands. Reading the projection back in a
    /// mounted surface showed why: CUI's own interaction state does write BorderColor="CuiFocusBrush" (the
    /// accent purple) while the row has keyboard focus, but the menu-row theme sets BorderWidth to 0, so that
    /// colour has nothing to paint (measured inventory: BorderColor #ff8f56e1@Overlay, BorderWidth 0,0,0,0@Theme).
    ///
    /// The cue is therefore completed here rather than in the framework, at instance priority, in the same
    /// precedence pattern Boards already uses for the toolbar rows (ToolbarBuilder.SetQuietToolbarControl and
    /// the type-row reconciliation). Only the width is owned: the colour stays CUI's, so focus and selection
    /// keep the palette the theme chose.
    /// </summary>
    private const double FallbackMenuRowHeight = 34;

    /// <summary>The row height the author declared, so clearing the cue restores it rather than assuming a
    /// number. Measured on the running build: the destination rows author Height/MinHeight 34 at Instance
    /// priority, which is what the cue grows from.</summary>
    private static double AuthoredRowHeight(Control row) =>
        CuiPropertyPresentation.TryGetExisting(row, out var store) && store is not null &&
        store.Resolve("Height").Value is double height && height > 0 ? height : FallbackMenuRowHeight;

    private static void AttachDestinationRowCues(Control root)
    {
        foreach (var row in root.GetVisualDescendants().OfType<MenuItem>())
        {
            void Reconcile()
            {
                // Width is owned here for both routes because the menu-row theme's zero width suppresses
                // whatever colour is current. The colour stays the framework's while the row has keyboard
                // focus (CUI writes CuiFocusBrush at overlay priority, which outranks this instance value);
                // the pointer route gets no colour from CUI at all, so the accent comes from the active
                // theme's own CuiAccentBrush resource rather than a Boards-private brush - the same
                // resource-resolving approach BlockRenderer.AccentColorOf uses, so an embedded dark surface
                // gets the dark palette's accent.
                var focusHeld = row.IsKeyboardFocusWithin;
                var chosen = focusHeld || row.IsPointerOver;
                CuiPropertyPresentation.Set(row, "BorderWidth",
                    chosen ? new Avalonia.Thickness(1.5) : new Avalonia.Thickness(0),
                    CuiPropertyLevel.Instance, "Boards insert destination chosen cue");
                // A focused row has to grow, not just gain a border. Measured on the running build
                // (2 November, run61 receipts BF02-BF05, frames 316-319, cue build App a9dc4a3f / Embedding
                // 10bb6491): holding the authored 34-unit height while adding a 1.5-unit border and the same
                // padding removed the row's label from the screen entirely - every label band below the
                // highlighted row went blank, one more per Down press. Compensating the padding alone made it
                // worse (frame 317: the highlighted row was an empty rounded box with its label pushed into
                // the next row's space). The cue is therefore completed by giving the focused row 3 extra
                // units of height - 1.5 per edge - which the menu's fixed 384-unit panel absorbs.
                CuiPropertyPresentation.Set(row, "Height",
                    chosen ? 37d : AuthoredRowHeight(row),
                    CuiPropertyLevel.Instance, "Boards insert destination chosen cue size");
                CuiPropertyPresentation.Set(row, "MinHeight",
                    chosen ? 37d : AuthoredRowHeight(row),
                    CuiPropertyLevel.Instance, "Boards insert destination chosen cue size");
                if (focusHeld)
                    CuiPropertyPresentation.Remove(row, "BorderColor", CuiPropertyLevel.Instance,
                        "Boards insert destination chosen cue");
                else if (row.IsPointerOver)
                    CuiPropertyPresentation.Set(row, "BorderColor",
                        row.TryFindResource("CuiAccentBrush", out var accent) ? accent : Avalonia.Media.Brushes.Transparent,
                        CuiPropertyLevel.Instance, "Boards insert destination chosen cue");
                else
                    CuiPropertyPresentation.Set(row, "BorderColor", Avalonia.Media.Brushes.Transparent,
                        CuiPropertyLevel.Instance, "Boards insert destination chosen cue");
            }
            row.GotFocus += (_, _) => Reconcile();
            row.LostFocus += (_, _) => Reconcile();
            row.PointerEntered += (_, _) => Reconcile();
            row.PointerExited += (_, _) => Reconcile();
        }
    }

    internal void Commit(string kind, long opening, long contentVersion, BlockInsertionPreviewOwner? owner, string? placement)
    {
        if (_disposed || !Popup.IsOpen || opening != _opening || contentVersion != ContentVersion ||
            placement != Placement || vm.SelectedPageId != _pageId ||
            !ReferenceEquals(_document, vm.Session.Document) || !vm.IsEditMode) return;
        if (AnchorBlockId is not null && (owner is null || !vm.IsInsertionPreviewCurrent(owner) || placement is null)) return;
        // Snapshot the destination before Closed revokes transient ownership. The actual typed edit
        // then captures its own revision/origin and never reads mutable popup state after an await.
        Close();
        Retain(AnchorBlockId is not null
            ? vm.InsertKindRelativeToAsync(kind, AnchorBlockId, placement!)
            : vm.InsertKindAsync(kind));
    }

    private void Retain(Task task)
    {
        _actions.RemoveAll(value => value.IsCompletedSuccessfully);
        _actions.Add(task);
        _ = Observe(task);
    }

    private async Task Observe(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
        catch (Exception error) { vm.Set("StatusText", "Insert failed: " + error.Message); }
    }

    internal Task WhenActionsIdleAsync() => Task.WhenAll(_actions.Append(
        _destinationLoader?.WhenActionsIdleAsync() ?? Task.CompletedTask));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        _surfaceLoader?.Dispose();
        _surfaceLoader = null;
    }

    private sealed record DestinationRequest(BlockInsertionPreviewOwner Owner, object Stage, BlockInsertionPlacement Placement);

    private sealed class DestinationContext : ICuiBindingContext, ICuiActionDispatcher
    {
        private readonly InsertMenuContext _menu;
        private readonly BoardsViewModel _vm;
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
        internal DestinationContext(InsertMenuContext menu, BoardsViewModel vm, BlockInsertionPreviewOwner owner, object stage)
        {
            _menu = menu; _vm = vm;
            foreach (var placement in Enum.GetValues<BlockInsertionPlacement>())
            {
                var unavailable = vm.InsertionDestinationUnavailableReason(owner.AnchorBlockId, placement);
                _values["Can" + placement] = unavailable is null;
                _values[placement + "Description"] = unavailable ?? (placement switch
                {
                    BlockInsertionPlacement.Above => "Put a new block in front of this one",
                    BlockInsertionPlacement.Below => "Put a new block after this one",
                    _ => "Replace this block with the type you choose next",
                });
                _values[placement + "Choose"] = new DestinationRequest(owner, stage, placement);
                foreach (var input in Enum.GetValues<BlockPreviewInput>())
                {
                    _values[placement + input.ToString() + "Enter"] = new BlockPreviewRequest(owner, stage, placement, input, true);
                    _values[placement + input.ToString() + "Exit"] = new BlockPreviewRequest(owner, stage, placement, input, false);
                }
            }
        }
        public bool TryGetValue(string path, out object? value) => _values.TryGetValue(path, out value);
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_menu.Popup.IsOpen) return ValueTask.CompletedTask;
            if (command == "Preview" && parameter is BlockPreviewRequest preview) _vm.UpdateInsertionPreview(preview);
            else if (command == "ChooseDestination" && parameter is DestinationRequest choice &&
                _vm.PinInsertionPreview(choice.Owner, choice.Stage, choice.Placement))
            {
                _menu.Placement = choice.Placement.ToString().ToLowerInvariant();
                _menu.Refill?.Invoke();
                _menu.Search.Focus();
            }
            return ValueTask.CompletedTask;
        }
    }
}
