using System.Reflection;
using System.Text;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Files.NativeUI;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Actual persistent Files/Home/broker/fence and issued browser selections.
/// These controls do not certify installed bootstrap, production rendering or cloud connectivity.</summary>
public sealed partial class FilesNativeBrowserMutationTests
{
    [AvaloniaFact]
    public async Task Atlas_Files_list_grid_retains_issued_items_selection_sort_review_and_history()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var first = await CreateReviewedFolder(h, h.Parent.Value, "First folder", token);
        var second = await CreateReviewedFolder(h, h.Parent.Value, "Second folder", token);
        await using var view = new FilesNativeBrowserSurface(h.Browser, null, h.Workspace.Actor,
            new OriginalPreviewFixtureReadiness(h), CancellationToken.None);
        var window = new Window { Content = view, Width = 1440, Height = 900 }; window.Show();
        try
        {
            await view.InitializeAsync(token); window.UpdateLayout();
            var list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>());
            list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(row => row.Id == h.Parent);
            await view.DispatchAsync("9to1.Files.Open", null, token);
            var selected = list.Items.Cast<HostedItemMetadata>().Single(row => row.Id == second);
            list.SelectedItem = selected;
            var sort = Assert.Single(view.GetVisualDescendants().OfType<ComboBox>(), control =>
                AutomationProperties.GetName(control) == "Sort names on this displayed page");
            sort.SelectedIndex = 1;
            var originalSource = list.ItemsSource;
            var originalOrder = list.Items.Cast<HostedItemMetadata>().ToArray();
            var name = await ShowNativeNameEditorAsync(view, window, token);
            name.Text = "Retained new folder";
            await view.DispatchAsync("9to1.Files.CreateFolder", null, token);
            var review = Assert.Single((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            var description = ReadNativeValue(view, "PendingChange");
            var before = await h.Workspace.Provider.GetStoreEvidenceAsync(h.Workspace.Configuration.StoreId, token);
            foreach (var command in new[] { "View.Grid", "Details.Toggle", "View.List", "Details.Toggle", "View.Grid" })
                await view.DispatchAsync("9to1.Files." + command, null, token);
            window.UpdateLayout();
            Assert.Same(originalSource, list.ItemsSource);
            Assert.Equal(originalOrder, list.Items.Cast<HostedItemMetadata>());
            Assert.Same(selected, list.SelectedItem);
            Assert.Equal(1, sort.SelectedIndex); Assert.Equal("Retained new folder", name.Text);
            Assert.Equal(description, ReadNativeValue(view, "PendingChange"));
            Assert.True(view.IsActionAvailable("9to1.Files.Apply"));
            Assert.Equal(review.RequestId, Assert.Single((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests).RequestId);
            Assert.Equal(before, await h.Workspace.Provider.GetStoreEvidenceAsync(h.Workspace.Configuration.StoreId, token));
            await view.DispatchAsync("9to1.Files.CancelReview", null, token);
            await view.DispatchAsync("9to1.Files.Name.Cancel", null, token);
            await view.DispatchAsync("9to1.Files.ChooseMove", null, token);
            var cut = ReadNativeValue(view, "MoveSelection");
            await view.DispatchAsync("9to1.Files.View.List", null, token);
            Assert.Equal(cut, ReadNativeValue(view, "MoveSelection"));
            await view.DispatchAsync("9to1.Files.Back", null, token);
            Assert.True(view.TryGetOriginalLocation(out var store, out var root, out _)); Assert.Null(root);
            await view.DispatchAsync("9to1.Files.Forward", null, token);
            Assert.True(view.TryGetOriginalLocation(out var returnedStore, out var returnedFolder, out _));
            Assert.Equal(store, returnedStore); Assert.Equal(h.Parent.Value, returnedFolder);
            Assert.Equal(new[] { second, first }, list.Items.Cast<HostedItemMetadata>().Select(row => row.Id));
            Assert.True(view.IsActionAvailable("9to1.Files.CancelMove"));
            await view.DispatchAsync("9to1.Files.CancelMove", null, token);
            Assert.True(view.CanPrepareOriginalClose());
            var close = view.CloseAndDrainAsync(); await close; Assert.Same(close, view.CloseAndDrainAsync());
            Assert.False(view.IsActionAvailable("9to1.Files.View.Grid"));
        }
        finally { await view.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Glow")]
    [InlineData("Professional")]
    public async Task Atlas_Files_actual_list_grid_and_details_render_at_720_and_1440(string themeName)
    {
        // Original Files/Home component owners, real persisted disposable fixtures and real CUI.
        // These frames do not claim an installed app, production storage or connected cloud account.
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        await CreateReviewedFolder(h, h.Parent.Value, "Assets", token);
        await CreateReviewedFolder(h, h.Parent.Value, "Research", token);
        var file = await CreatePresentationFileAsync(h, "Reference data.bin", token);
        using var theme = new CuiThemePreferenceContext(new FilesPresentationTheme(themeName));
        await using var view = new FilesNativeBrowserSurface(h.Browser, null, h.Workspace.Actor,
            new OriginalPreviewFixtureReadiness(h), CancellationToken.None, interfaceTheme: theme);
        var window = new Window { Content = view, Width = 1440, Height = 900 }; window.Show();
        try
        {
            await view.InitializeAsync(token); window.UpdateLayout();
            var list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>());
            list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(row => row.Id == h.Parent);
            await view.DispatchAsync("9to1.Files.Open", null, token);
            var selected = list.Items.Cast<HostedItemMetadata>().Single(row => row.Id == file);
            list.SelectedItem = selected;
            var before = await h.Workspace.Provider.GetStoreEvidenceAsync(h.Workspace.Configuration.StoreId, token);
            foreach (var size in new[] { new Size(1440, 900), new Size(720, 650) })
            {
                window.Width = size.Width; window.Height = size.Height;
                foreach (var mode in new[] { "List", "Grid" })
                {
                    var button = FilesPresentationControl<Button>(view, "files-view-" + mode.ToLowerInvariant());
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await AwaitNativeRoleStateAsync(window, () => Equals(ReadNativeValue(view, mode + "View"), true), token);
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    Assert.Same(selected, list.SelectedItem);
                    Assert.True(Assert.IsType<bool>(ReadNativeValue(view, "DeviceSelected")));
                    Assert.False(Assert.IsType<bool>(ReadNativeValue(view, "TrashSelected")));
                    Assert.False(Assert.IsType<bool>(ReadNativeValue(view, "DriveSelected")));
                    Assert.Equal("Reference data.bin", FilesPresentationControl<TextBlock>(view, "files-selected-name").Text);
                    Assert.True(FilesPresentationControl<Control>(view, "files-open-unavailable").IsEffectivelyVisible);
                    Assert.False(FilesPresentationControl<Button>(view, "files-details-open").IsEnabled);
                    var items = Assert.IsType<WrapPanel>(list.ItemsPanelRoot);
                    Assert.Equal(mode == "Grid" ? 190 : double.NaN, items.ItemHeight);
                    var container = Assert.IsType<ListBoxItem>(list.ContainerFromItem(selected));
                    Assert.True(container.IsSelected);
                    Assert.Equal(new Thickness(0), container.Padding);
                    var row = FilesPresentationControl<Control>(container, "files-details-row");
                    var point = row.TranslatePoint(default, container); Assert.NotNull(point);
                    Assert.InRange(point.Value.X, -1, 2);
                    Assert.True(row.Bounds.Width >= container.Bounds.Width - 3);
                    var folderImage = FilesPresentationControl<Image>(list.ContainerFromItem(list.Items.Cast<HostedItemMetadata>().First(item => item.Kind == HostedItemKind.Folder))!, "files-row-folder-artwork");
                    var folderDrawing = Assert.IsType<GeometryDrawing>(Assert.IsType<DrawingImage>(folderImage.Source).Drawing);
                    Assert.Null(folderDrawing.Brush); Assert.Equal(1.75, Assert.IsType<Pen>(folderDrawing.Pen).Thickness);
                    var details = FilesPresentationControl<CuiContainerControl>(view, "files-details-pane");
                    var detailsPosition = details.TranslatePoint(default, view); Assert.NotNull(detailsPosition);
                    Assert.True(detailsPosition.Value.X >= -1 && detailsPosition.Value.X + details.Bounds.Width <= view.Bounds.Width + 1);
                    var expectedInset = size.Width >= 1180 ? 22d : 12d;
                    Assert.Equal(new Thickness(expectedInset), details.Padding);
                    Assert.Equal(size.Width >= 1180 ? 12d : 6d, Assert.IsType<Grid>(details.Layout).RowSpacing);
                    var title = FilesPresentationControl<TextBlock>(view, "files-details-title");
                    var titlePoint = title.TranslatePoint(default, details); Assert.NotNull(titlePoint);
                    Assert.InRange(titlePoint.Value.X, expectedInset, expectedInset + 2);
                    Assert.InRange(titlePoint.Value.Y, expectedInset, expectedInset + 2);
                    var summary = FilesPresentationControl<Control>(view, "files-selected-summary");
                    var summaryPoint = summary.TranslatePoint(default, details); Assert.NotNull(summaryPoint);
                    Assert.True(summaryPoint.Value.X >= expectedInset &&
                        summaryPoint.Value.X + summary.Bounds.Width <= details.Bounds.Width - expectedInset + 1,
                        FilesPresentationGeometry(view, summary));
                    var closeArtwork = FilesPresentationControl<Image>(view, "files-details-close-artwork");
                    var closeDrawing = Assert.IsType<GeometryDrawing>(Assert.IsType<DrawingImage>(closeArtwork.Source).Drawing);
                    Assert.Null(closeDrawing.Brush); Assert.NotNull(closeDrawing.Pen);
                    Assert.InRange(closeArtwork.Bounds.Width, 15, 17); Assert.InRange(closeArtwork.Bounds.Height, 15, 17);
                    var closePoint = closeArtwork.TranslatePoint(default, details); Assert.NotNull(closePoint);
                    Assert.True(closePoint.Value.X >= expectedInset &&
                        closePoint.Value.X + closeArtwork.Bounds.Width <= details.Bounds.Width - expectedInset + 1);
                    var selectedOpen = FilesPresentationControl<Button>(view, "files-details-open");
                    var openPoint = selectedOpen.TranslatePoint(default, view); Assert.NotNull(openPoint);
                    Assert.True(openPoint.Value.Y + selectedOpen.Bounds.Height <= view.Bounds.Height + 1);
                    if (size.Width == 720)
                        await CaptureComponentSceneAsync(window, $"files-geometry-{mode.ToLowerInvariant()}-{themeName}-{size.Width:0}x{size.Height:0}");
                    Assert.True(list.Bounds.Height > 150, FilesPresentationGeometry(view, list));
                    foreach (var key in new[] { "back", "forward", "up", "refresh" })
                    {
                        var navigation = FilesPresentationControl<Button>(view, "files-" + key);
                        var artwork = FilesPresentationControl<Image>(navigation, "files-" + key + "-artwork");
                        var drawing = Assert.IsType<GeometryDrawing>(Assert.IsType<DrawingImage>(artwork.Source).Drawing);
                        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(navigation.Foreground).Color,
                            Assert.IsAssignableFrom<ISolidColorBrush>(drawing.Brush).Color);
                        Assert.InRange(artwork.Bounds.Width, 17, 19); Assert.InRange(artwork.Bounds.Height, 17, 19);
                        Assert.Equal(new Thickness(0), navigation.Padding);
                    }
                    var sort = FilesPresentationControl<ComboBox>(view, "files-displayed-sort");
                    Assert.Equal("Name A–Z", sort.SelectedItem);
                    Assert.Equal("Sort names on this displayed page", AutomationProperties.GetName(sort));
                    Assert.Equal("Sort names on this displayed page", ToolTip.GetTip(sort));
                    if (size.Width == 720)
                    {
                        Assert.Equal("PC", FilesPresentationControl<Button>(view, "files-home").Content);
                        Assert.Equal("Bin", FilesPresentationControl<Button>(view, "files-trash").Content);
                        Assert.Equal("Web", FilesPresentationControl<Button>(view, "files-drive").Content);
                        var providers = FilesPresentationControl<CuiMenuItem>(view, "files-providers-menu");
                        Assert.True(providers.IsTopLevel); Assert.Equal("More", providers.Header);
                        // Only the detail body scrolls; the selected item and Open remain reachable.
                        var technical = FilesPresentationControl<Button>(view, "files-technical-details");
                        RequireOriginalDetailsScrollPresenter(view, technical);
                        var beforeScroll = FilesPresentationGeometry(view, technical);
                        technical.BringIntoView(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                        var technicalPoint = technical.TranslatePoint(default, view); Assert.NotNull(technicalPoint);
                        Assert.True(technicalPoint.Value.Y >= 0 && technicalPoint.Value.Y + technical.Bounds.Height <= view.Bounds.Height + 1,
                            "Before BringIntoView: " + beforeScroll + "\nAfter BringIntoView: " + FilesPresentationGeometry(view, technical));
                        FilesPresentationControl<TextBlock>(view, "files-open-unavailable").BringIntoView();
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    }
                    await CaptureComponentSceneAsync(window, $"files-{mode.ToLowerInvariant()}-{themeName}-{size.Width:0}x{size.Height:0}");
                }
            }
            Assert.Equal(before, await h.Workspace.Provider.GetStoreEvidenceAsync(h.Workspace.Configuration.StoreId, token));
            Assert.True(view.CanPrepareOriginalClose());
        }
        finally { await view.CloseAndDrainAsync(); window.Close(); }
    }

    private static async Task<TextBox> ShowNativeNameEditorAsync(FilesNativeBrowserSurface view, Window window, CancellationToken token)
    {
        FilesPresentationControl<Button>(view, "files-new").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await AwaitNativeRoleStateAsync(window, () => Equals(ReadNativeValue(view, "NameEditorVisible"), true), token);
        window.UpdateLayout();
        var name = FilesPresentationControl<TextBox>(view, "files-name-input");
        Assert.True(name.IsEffectivelyVisible);
        return name;
    }

    private static T FilesPresentationControl<T>(Control root, string id) where T : Control => Assert.Single(
        root.GetVisualDescendants().OfType<T>(), control => control.Name == id || AutomationProperties.GetAutomationId(control) == id);

    private static string FilesPresentationGeometry(Control root, Control target)
    {
        var controls = root.GetVisualDescendants().OfType<Control>().ToArray();
        var names = new[] { "files-content-layout", "files-main", "files-command-bar", "files-details-pane", "files-details-scroll" };
        var regions = names.Select(name => controls.FirstOrDefault(control => control.Name == name))
            .OfType<Control>()
            .Select(control => $"{control.Name}: bounds={control.Bounds}, position={control.TranslatePoint(default, root)}, desired={control.DesiredSize}");
        var scrolls = controls.OfType<ScrollViewer>().Take(8).Select(scroll =>
            $"scroll: presenter={scroll.Presenter?.GetType().Name ?? "none"}, bounds={scroll.Bounds}, viewport={scroll.Viewport}, extent={scroll.Extent}, offset={scroll.Offset}, vertical={scroll.VerticalScrollBarVisibility}");
        return $"root={root.Bounds}; target={target.Name}, bounds={target.Bounds}, position={target.TranslatePoint(default, root)}, measure={target.IsMeasureValid}, arrange={target.IsArrangeValid}; " +
            string.Join("; ", regions.Concat(scrolls));
    }

    private static void RequireOriginalDetailsScrollPresenter(Control root, Control target)
    {
        var scroll = Assert.IsType<ScrollViewer>(target.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault());
        Assert.Contains(scroll.GetVisualAncestors().OfType<Control>(), control => control.Name == "files-details-scroll");
        var presenter = Assert.IsType<ScrollContentPresenter>(scroll.Presenter);
        Assert.True(presenter.CanVerticallyScroll);
        Assert.True(scroll.Viewport.Width > 0 && scroll.Viewport.Height > 0, FilesPresentationGeometry(root, target));
    }

    private sealed class FilesPresentationTheme(string name) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new(name, 1, "files-renderer-component", true,
            CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed { add { } remove { } }
    }

    private static async Task<HostedItemId> CreatePresentationFileAsync(Harness h, string name, CancellationToken token)
    {
        var actor = h.Workspace.Actor;
        var folder = (await h.Workspace.Directories.ResolveProfileAsync(Guid.Parse(actor.ProfileId), "write", token)).Value!;
        var bytes = Encoding.UTF8.GetBytes("Disposable Files rendering fixture. No executable content.\n");
        await File.WriteAllBytesAsync(Path.Combine(folder.DirectoryPath, name), bytes, token);
        var parent = (await h.Workspace.Provider.GetAsync(h.Parent, token)).Value!;
        var upload = new FilesUploadedContent(HostedItemId.New(), h.Parent, name, "application/octet-stream", new(Guid.NewGuid()),
            null, actor.ActorId, DateTimeOffset.UtcNow, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), name);
        var authority = h.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
        var commit = await authority.CaptureCommitAuthorityAsync(actor, h.Workspace.Provider, () => true, token);
        Assert.True((await h.Workspace.Provider.CommitUploadedContentAsync(upload,
            [new(h.Parent, parent.CurrentRevisionId)], commit, token)).IsSuccess);
        return upload.FileId;
    }

    [Fact]
    public async Task Create_and_rename_require_original_Home_review_and_commit_each_operation_once()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var page = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        var prepared = await h.Browser.PrepareNativeMutationAsync(page, null, h.Workspace.Actor,
            "CreateFolder", "Friday work", () => true, "files-control", token);
        Assert.Equal(HomePermissionRequestState.PendingApproval, prepared.Approval.State);
        var before = await h.Workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token);
        var waiting = await h.Browser.ApplyNativeMutationAsync(prepared, token);
        Assert.True(waiting.AwaitingHomeReview);
        Assert.Null(waiting.Operation);
        Assert.Equal(before, await h.Workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token));
        Assert.True((await h.Permissions.DecideAsync(prepared.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        var actual = h.Browser.ApplyNativeMutationAsync(prepared, token);
        Assert.Same(actual, h.Browser.ApplyNativeMutationAsync(prepared, token));
        var result = await actual;
        Assert.True(result.Operation!.IsSuccess);
        Assert.True(result.AuditRecorded);
        var created = result.Operation.Value!.ItemId;
        Assert.Equal(FilesOperationState.Committed, result.Operation.Value.State);
        Assert.Equal(HomePermissionRequestState.Succeeded,
            (await h.Permissions.GetAuthorizationAsync(prepared.Approval.RequestId, token)).State);
        Assert.Same(actual, h.Browser.ApplyNativeMutationAsync(prepared, token));
        var refreshed = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        var row = Assert.Single(refreshed.Items);
        Assert.Equal(created, row.Id);
        Assert.Equal("Friday work", row.Name);
        var rename = await h.Browser.PrepareNativeMutationAsync(refreshed, row, h.Workspace.Actor,
            "Rename", "Footage", () => true, "files-control", token);
        Assert.True((await h.Permissions.DecideAsync(rename.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        var renamed = await h.Browser.ApplyNativeMutationAsync(rename, token);
        Assert.True(renamed.Operation!.IsSuccess);
        Assert.True(renamed.AuditRecorded);
        var final = Assert.Single((await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token)).Items);
        Assert.Equal(created, final.Id);
        Assert.Equal("Footage", final.Name);
        Assert.NotEqual(row.CurrentRevisionId, final.CurrentRevisionId);
        var changes = await h.Workspace.Provider.GetChangesAsync(null, 100, token);
        Assert.Equal(2, changes.Items.Count(change => change.ItemId == created));
        var after = await h.Workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token);
        Assert.True((await h.Browser.RetryNativeMutationAuditAsync(rename, token)).Succeeded);
        Assert.Equal(after, await h.Workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token));
    }

    [Fact]
    public async Task Trash_and_restore_require_Home_review_and_preserve_the_same_subtree()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        async Task<HostedItemId> Create(Guid parent, string name)
        {
            var page = await h.Browser.ListAsync(h.Workspace.Actor, parent, token: token);
            var request = await h.Browser.PrepareNativeMutationAsync(page, null, h.Workspace.Actor,
                "CreateFolder", name, () => true, "files-trash-control", token);
            Assert.True((await h.Permissions.DecideAsync(request.Approval.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            return (await h.Browser.ApplyNativeMutationAsync(request, token)).Operation!.Value!.ItemId;
        }
        var folder = await Create(h.Parent.Value, "Recoverable folder");
        var child = await Create(folder.Value, "Retained child");
        var originalChild = (await h.Workspace.Provider.GetAsync(child, token)).Value!;
        var current = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        var row = Assert.Single(current.Items);
        Assert.True(h.Browser.CanDeleteNativeSelection(current, row));
        var deletion = await h.Browser.PrepareNativeMutationAsync(current, row, h.Workspace.Actor,
            "Delete", row.Name, () => true, "files-trash-control", token);
        Assert.Equal(HomePermissionRequestState.PendingApproval, deletion.Approval.State);
        Assert.True((await h.Browser.ApplyNativeMutationAsync(deletion, token)).AwaitingHomeReview);
        Assert.True((await h.Workspace.Provider.GetAsync(child, token)).IsSuccess);
        Assert.True((await h.Permissions.DecideAsync(deletion.Approval.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
        var deleted = await h.Browser.ApplyNativeMutationAsync(deletion, token);
        Assert.True(deleted.Operation!.IsSuccess); Assert.True(deleted.AuditRecorded);
        Assert.False((await h.Workspace.Provider.GetAsync(child, token)).IsSuccess);
        var trash = await h.Browser.ListAsync(h.Workspace.Actor, token: token, isTrash: true);
        var trashed = Assert.Single(trash.Items);
        Assert.Equal(folder, trashed.Id); Assert.Equal(h.Parent, trashed.ParentId);
        Assert.True(h.Browser.CanRestoreNativeSelection(trash, trashed));
        Assert.False(h.Browser.CanRenameNativeSelection(trash, trashed));
        Assert.False(h.Browser.CanDeleteNativeSelection(trash, trashed));
        var restoration = await h.Browser.PrepareNativeMutationAsync(trash, trashed, h.Workspace.Actor,
            "Restore", trashed.Name, () => true, "files-trash-control", token);
        Assert.True((await h.Browser.ApplyNativeMutationAsync(restoration, token)).AwaitingHomeReview);
        Assert.True((await h.Permissions.DecideAsync(restoration.Approval.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
        var restoreTask = h.Browser.ApplyNativeMutationAsync(restoration, token);
        Assert.True((await restoreTask).Operation!.IsSuccess);
        Assert.Same(restoreTask, h.Browser.ApplyNativeMutationAsync(restoration, token));
        Assert.Empty((await h.Browser.ListAsync(h.Workspace.Actor, token: token, isTrash: true)).Items);
        Assert.Equal(originalChild, (await h.Workspace.Provider.GetAsync(child, token)).Value);
        Assert.Equal(folder, Assert.Single((await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token)).Items).Id);
        Assert.Equal(4, (await h.Workspace.Provider.GetChangesAsync(null, 100, token)).Items.Count(change => change.ItemId == folder || change.ItemId == child));
    }

    [Fact]
    public async Task Move_requires_original_pages_and_Home_review_then_preserves_identity_and_subtree_once()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var folder = await CreateReviewedFolder(h, h.Parent.Value, "Moving folder", token);
        var child = await CreateReviewedFolder(h, folder.Value, "Same child", token);
        var destination = await CreateReviewedFolder(h, h.Parent.Value, "Destination", token);
        var sourcePage = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        var row = sourcePage.Items.Single(item => item.Id == folder);
        var destinationPage = await h.Browser.ListAsync(h.Workspace.Actor, destination.Value, token: token);
        var childBefore = (await h.Workspace.Provider.GetAsync(child, token)).Value!;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => h.Browser.PrepareNativeMoveAsync(sourcePage, row,
            destinationPage with { }, h.Workspace.Actor, () => true, "files-move-control", token));
        var move = await h.Browser.PrepareNativeMoveAsync(sourcePage, row, destinationPage,
            h.Workspace.Actor, () => true, "files-move-control", token);
        Assert.Equal(HomePermissionRequestState.PendingApproval, move.Approval.State);
        Assert.True((await h.Browser.ApplyNativeMutationAsync(move, token)).AwaitingHomeReview);
        Assert.Equal(h.Parent, (await h.Workspace.Provider.GetAsync(folder, token)).Value!.ParentId);
        Assert.True((await h.Permissions.DecideAsync(move.Approval.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
        var actual = h.Browser.ApplyNativeMutationAsync(move, token);
        var result = await actual;
        Assert.True(result.Operation!.IsSuccess); Assert.True(result.AuditRecorded);
        Assert.Same(actual, h.Browser.ApplyNativeMutationAsync(move, token));
        var moved = Assert.Single((await h.Browser.ListAsync(h.Workspace.Actor, destination.Value, token: token)).Items);
        Assert.Equal(folder, moved.Id); Assert.Equal(destination, moved.ParentId); Assert.Equal(row.Name, moved.Name);
        Assert.NotEqual(row.CurrentRevisionId, moved.CurrentRevisionId);
        Assert.Equal(childBefore, (await h.Workspace.Provider.GetAsync(child, token)).Value);
        Assert.DoesNotContain((await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token)).Items, item => item.Id == folder);
        var change = Assert.Single((await h.Workspace.Provider.GetChangesAsync(null, 100, token)).Items, item => item.Kind == "Move");
        Assert.Equal(folder, change.ItemId); Assert.Equal(result.Operation.Value!.Id, change.OperationId);
        Assert.Equal(h.Parent, result.Operation.Value.SourceParentId); Assert.Equal(destination, result.Operation.Value.DestinationParentId);
    }

    [Fact]
    public async Task Move_rejects_cycles_and_conflicts_without_losing_items_and_refuses_stale_destinations()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var folder = await CreateReviewedFolder(h, h.Parent.Value, "Same name", token);
        var child = await CreateReviewedFolder(h, folder.Value, "Child", token);
        var destination = await CreateReviewedFolder(h, h.Parent.Value, "Destination", token);
        var occupied = await CreateReviewedFolder(h, destination.Value, "Same name", token);
        var source = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        var row = source.Items.Single(item => item.Id == folder);
        var self = await h.Browser.ListAsync(h.Workspace.Actor, folder.Value, token: token);
        var descendant = await h.Browser.ListAsync(h.Workspace.Actor, child.Value, token: token);
        var target = await h.Browser.ListAsync(h.Workspace.Actor, destination.Value, token: token);
        var changesBeforeRefusal = (await h.Workspace.Provider.GetChangesAsync(null, 100, token)).Items.Count;
        Assert.False(h.Browser.CanPrepareNativeMove(source, row, self));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Browser.PrepareNativeMoveAsync(source, row,
            descendant, h.Workspace.Actor, () => true, "files-move-control", token));
        Assert.Empty((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
        var move = await h.Browser.PrepareNativeMoveAsync(source, row, target, h.Workspace.Actor,
            () => true, "files-move-control", token);
        Assert.True((await h.Permissions.DecideAsync(move.Approval.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
        var refused = await h.Browser.ApplyNativeMutationAsync(move, token);
        Assert.Equal(FilesErrorCode.NameConflict, refused.Operation!.Error!.Code); Assert.True(refused.AuditRecorded);
        Assert.Equal(h.Parent, (await h.Workspace.Provider.GetAsync(folder, token)).Value!.ParentId);
        Assert.Equal(occupied, Assert.Single((await h.Browser.ListAsync(h.Workspace.Actor, destination.Value, token: token)).Items).Id);
        Assert.Equal(changesBeforeRefusal, (await h.Workspace.Provider.GetChangesAsync(null, 100, token)).Items.Count);
        var stale = await h.Browser.PrepareNativeMoveAsync(source, row, target, h.Workspace.Actor,
            () => true, "files-move-control", token);
        Assert.True((await h.Permissions.DecideAsync(stale.Approval.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
        await CreateReviewedFolder(h, destination.Value, "Concurrent destination change", token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Browser.ApplyNativeMutationAsync(stale, token));
        Assert.Equal(h.Parent, (await h.Workspace.Provider.GetAsync(folder, token)).Value!.ParentId);
    }

    private static async Task<HostedItemId> CreateReviewedFolder(Harness h, Guid parent, string name, CancellationToken token)
    {
        var page = await h.Browser.ListAsync(h.Workspace.Actor, parent, token: token);
        var request = await h.Browser.PrepareNativeMutationAsync(page, null, h.Workspace.Actor,
            "CreateFolder", name, () => true, "files-move-control", token);
        Assert.True((await h.Permissions.DecideAsync(request.Approval.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
        var result = await h.Browser.ApplyNativeMutationAsync(request, token);
        Assert.True(result.Operation!.IsSuccess);
        return result.Operation.Value!.ItemId;
    }

    [Fact]
    public async Task Copied_page_configured_App_root_and_retired_review_cannot_admit_an_effect()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var root = await h.Browser.ListAsync(h.Workspace.Actor, token: token);
        var appRoot = root.Items.Single(item => item.Id == h.Parent);
        Assert.False(h.Browser.CanRenameNativeSelection(root, appRoot));
        Assert.False(h.Browser.CanDeleteNativeSelection(root, appRoot));
        Assert.False(h.Browser.CanMoveNativeSelection(root, appRoot));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Browser.PrepareNativeMutationAsync(root,
            appRoot, h.Workspace.Actor, "Delete", appRoot.Name, () => true, "files-control", token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Browser.PrepareNativeMutationAsync(root,
            appRoot, h.Workspace.Actor, "Rename", "Wrong root", () => true, "files-control", token));
        var page = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => h.Browser.PrepareNativeMutationAsync(page with { },
            null, h.Workspace.Actor, "CreateFolder", "Copied page", () => true, "files-control", token));
        var prepared = await h.Browser.PrepareNativeMutationAsync(page, null, h.Workspace.Actor,
            "CreateFolder", "Cancelled review", () => true, "files-control", token);
        var copied = (FilesNativeBrowserService.PreparedMutation)typeof(object).GetMethod("MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(prepared, null)!;
        Assert.Throws<UnauthorizedAccessException>(() => { _ = h.Browser.ApplyNativeMutationAsync(copied, token); });
        Assert.True((await h.Permissions.DecideAsync(prepared.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        h.Browser.RetireNativeMutationReview(prepared);
        Assert.Throws<InvalidOperationException>(() => { _ = h.Browser.ApplyNativeMutationAsync(prepared, token); });
        Assert.Empty((await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token)).Items);
    }

    [Fact]
    public async Task Changed_store_or_retired_original_view_refuses_after_approval_without_committing_prepared_item()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var page = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        var live = true;
        var retired = await h.Browser.PrepareNativeMutationAsync(page, null, h.Workspace.Actor,
            "CreateFolder", "Late original", () => live, "files-control", token);
        Assert.True((await h.Permissions.DecideAsync(retired.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        live = false;
        await Assert.ThrowsAsync<ObjectDisposedException>(() => h.Browser.ApplyNativeMutationAsync(retired, token));
        var stale = await h.Browser.PrepareNativeMutationAsync(page, null, h.Workspace.Actor,
            "CreateFolder", "Stale original", () => true, "files-control", token);
        Assert.True((await h.Permissions.DecideAsync(stale.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        var concurrent = await h.Browser.PrepareNativeMutationAsync(page, null, h.Workspace.Actor,
            "CreateFolder", "Actual concurrent folder", () => true, "files-control", token);
        Assert.True((await h.Permissions.DecideAsync(concurrent.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        Assert.True((await h.Browser.ApplyNativeMutationAsync(concurrent, token)).Operation!.IsSuccess);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Browser.ApplyNativeMutationAsync(stale, token));
        Assert.Equal("Actual concurrent folder", Assert.Single(
            (await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token)).Items).Name);
    }

    [Fact]
    public async Task Saved_Files_ACK_survives_terminal_Home_audit_fault_and_retry_never_repeats_the_effect()
    {
        var token = TestContext.Current.CancellationToken;
        var observer = new PersistedObserver();
        await using var h = await Harness.CreateAsync(token, observer);
        var page = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        var prepared = await h.Browser.PrepareNativeMutationAsync(page, null, h.Workspace.Actor,
            "CreateFolder", "Saved once", () => true, "files-audit-fault-control", token);
        Assert.True((await h.Permissions.DecideAsync(prepared.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        var first = new IOException("Actual terminal Home publication ACK lost.");
        var sibling = new InvalidOperationException("Original terminal observer companion fault.");
        var faultCount = 0;
        observer.BeforeAcknowledgement = async (id, revision, ct) =>
        {
            if (id != "home.permissions-trust") return;
            // Observe only actual already-persisted Home bytes after its locks release;
            // do not reenter permission methods whose original operation may still own its gate.
            var read = await h.Home.ReadAsync(ct);
            Assert.True(read.IsSuccess);
            var record = read.State!.Records.Single(row => row.RecordId == id);
            Assert.Equal(revision, record.Revision);
            var request = record.Payload.GetProperty("Requests").EnumerateArray()
                .Single(row => row.GetProperty("RequestId").GetString() == prepared.Approval.RequestId);
            if (request.GetProperty("State").GetInt32() == (int)HomePermissionRequestState.Succeeded
                && Interlocked.Increment(ref faultCount) == 1)
            {
                Assert.Equal(FilesOperationState.Committed, prepared.ObservedOperation!.Value!.State);
                throw new AggregateException(first, sibling);
            }
        };
        var actual = h.Browser.ApplyNativeMutationAsync(prepared, token);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Contains(failure.Flatten().InnerExceptions, error => ReferenceEquals(error, first));
        Assert.Contains(failure.Flatten().InnerExceptions, error => ReferenceEquals(error, sibling));
        var ack = prepared.ObservedOperation!;
        Assert.True(ack.IsSuccess); Assert.Equal(FilesOperationState.Committed, ack.Value!.State);
        Assert.True(prepared.HasObservedOutcome);
        Assert.Same(actual, h.Browser.ApplyNativeMutationAsync(prepared, token));
        Assert.Same(failure, await Assert.ThrowsAsync<AggregateException>(() => actual));
        var evidence = await h.Workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token);
        var change = Assert.Single((await h.Workspace.Provider.GetChangesAsync(null, 100, token)).Items,
            row => row.ItemId == ack.Value.ItemId);
        Assert.Equal(ack.Value.Id, change.OperationId);
        observer.BeforeAcknowledgement = null;
        Assert.True((await h.Browser.RetryNativeMutationAuditAsync(prepared, token)).Succeeded);
        Assert.Same(ack, prepared.ObservedOperation);
        Assert.Equal(evidence, await h.Workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token));
        Assert.Single((await h.Workspace.Provider.GetChangesAsync(null, 100, token)).Items, row => row.ItemId == ack.Value.ItemId);
        Assert.Same(actual, h.Browser.ApplyNativeMutationAsync(prepared, token));
        Assert.Equal(1, faultCount);
    }

    [AvaloniaFact]
    public async Task Native_controls_stage_Home_review_then_publish_observed_folder_and_preserve_protected_roots()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        await using var view = new FilesNativeBrowserSurface(h.Browser, null, h.Workspace.Actor,
            new Readiness(), CancellationToken.None);
        var window = new Window { Content = view, Width = 720, Height = 640 };
        window.Show();
        try
        {
            await view.InitializeAsync(token); window.UpdateLayout();
            var list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>());
            list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == h.Parent);
            var name = await ShowNativeNameEditorAsync(view, window, token);
            name.Text = "Protected root";
            Assert.False(view.IsActionAvailable("9to1.Files.Rename"));
            await view.DispatchAsync("9to1.Files.Open", null, token);
            name.Text = "Native folder";
            Assert.True(view.IsActionAvailable("9to1.Files.CreateFolder"));
            await view.DispatchAsync("9to1.Files.CreateFolder", null, token);
            Assert.Empty(list.Items);
            Assert.True(view.IsActionAvailable("9to1.Files.Apply"));
            Assert.False(view.IsActionAvailable("9to1.Files.Refresh"));
            var pending = Assert.Single((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            Assert.Equal("files", pending.Scope.TargetAppId);
            Assert.Equal("9to1.Files.CreateFolder", pending.Scope.ActionName);
            Assert.True((await h.Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: token)).Succeeded);
            await view.DispatchAsync("9to1.Files.Apply", null, token);
            Assert.Equal("Native folder", Assert.Single(list.Items.Cast<HostedItemMetadata>()).Name);
            Assert.True(view.IsActionAvailable("9to1.Files.Refresh"));
            Assert.False(view.IsActionAvailable("9to1.Files.Apply"));
            list.SelectedItem = Assert.Single(list.Items.Cast<HostedItemMetadata>());
            var originalId = ((HostedItemMetadata)list.SelectedItem).Id;
            Assert.True(view.TryGetValue("SelectedDetails", out var humanDetails));
            Assert.DoesNotContain(originalId.ToString(), Assert.IsType<string>(humanDetails));
            await view.DispatchAsync("9to1.Files.TechnicalDetails", null, token);
            Assert.True(view.TryGetValue("SelectedDetails", out var technicalDetails));
            Assert.Contains(originalId.ToString(), Assert.IsType<string>(technicalDetails));
            await view.DispatchAsync("9to1.Files.TechnicalDetails", null, token);
            await CaptureComponentSceneAsync(window, "files-current-folder");
            await view.DispatchAsync("9to1.Files.Delete", null, token);
            var deleteReview = Assert.Single((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            Assert.Equal("9to1.Files.Delete", deleteReview.Scope.ActionName);
            Assert.True((await h.Permissions.DecideAsync(deleteReview.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            await view.DispatchAsync("9to1.Files.Apply", null, token); Assert.Empty(list.Items);
            await view.DispatchAsync("9to1.Files.Trash", null, token);
            list.SelectedItem = Assert.Single(list.Items.Cast<HostedItemMetadata>());
            Assert.True(view.IsActionAvailable("9to1.Files.Restore"));
            Assert.False(view.IsActionAvailable("9to1.Files.Open"));
            await CaptureComponentSceneAsync(window, "files-recoverable-trash");
            await view.DispatchAsync("9to1.Files.Restore", null, token);
            var restoreReview = Assert.Single((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            Assert.Equal("9to1.Files.Restore", restoreReview.Scope.ActionName);
            Assert.True((await h.Permissions.DecideAsync(restoreReview.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            await view.DispatchAsync("9to1.Files.Apply", null, token); Assert.Empty(list.Items);
            Assert.Equal(originalId, Assert.Single((await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token)).Items).Id);
            await view.DispatchAsync("9to1.Files.Home", null, token);
            list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == h.Parent);
            await view.DispatchAsync("9to1.Files.Open", null, token);
            list.SelectedItem = Assert.Single(list.Items.Cast<HostedItemMetadata>());
            await view.DispatchAsync("9to1.Files.ChooseMove", null, token);
            Assert.False(view.IsActionAvailable("9to1.Files.Move"));
            await view.DispatchAsync("9to1.Files.Home", null, token);
            var destination = h.Workspace.Configuration.AppFolders["canvas"];
            list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == destination);
            await view.DispatchAsync("9to1.Files.Open", null, token);
            Assert.True(view.IsActionAvailable("9to1.Files.Move"));
            await view.DispatchAsync("9to1.Files.Move", null, token);
            var moveReview = Assert.Single((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            Assert.Equal("9to1.Files.Move", moveReview.Scope.ActionName);
            Assert.True((await h.Permissions.DecideAsync(moveReview.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            await view.DispatchAsync("9to1.Files.Apply", null, token);
            list.SelectedItem = Assert.Single(list.Items.Cast<HostedItemMetadata>());
            Assert.Equal(originalId, ((HostedItemMetadata)list.SelectedItem).Id);
            Assert.Empty((await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token)).Items);
            Assert.False(view.IsActionAvailable("9to1.Files.CancelMove"));
            await CaptureComponentSceneAsync(window, "files-move-destination");
        }
        finally { await view.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Row_menu_targets_the_invoking_row_and_refuses_a_retargeted_selection()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var first = await CreateReviewedFolder(h, h.Parent.Value, "First", token);
        var second = await CreateReviewedFolder(h, h.Parent.Value, "Second", token);
        await using var view = new FilesNativeBrowserSurface(h.Browser, null, h.Workspace.Actor,
            new Readiness(), CancellationToken.None);
        var window = new Window { Width = 900, Height = 700 };
        window.Template = new Avalonia.Controls.Templates.FuncControlTemplate<Window>((_, names) =>
        {
            var layers = new Avalonia.Controls.Primitives.VisualLayerManager { Child = view };
            names.Register("PART_VisualLayerManager", layers);
            return layers;
        });
        window.Show();
        try
        {
            await view.InitializeAsync(token);
            await view.OpenOriginalLocationAsync(h.Workspace.Configuration.StoreId, h.Parent.Value, token);
            window.UpdateLayout();
            var list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>());
            var firstRow = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == first);
            var secondRow = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == second);
            var secondContainer = Assert.IsType<ListBoxItem>(list.ContainerFromItem(secondRow));
            var menu = Assert.IsType<ContextMenu>(secondContainer.ContextMenu);
            var cut = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Cut"));

            list.SelectedItem = firstRow;
            var firstContainer = Assert.IsType<ListBoxItem>(list.ContainerFromItem(firstRow));
            Assert.True(firstContainer.Focus());
            secondContainer.RaiseEvent(new ContextRequestedEventArgs());
            Assert.True(menu.IsOpen); Assert.Same(secondRow, list.SelectedItem);
            menu.Close(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(secondContainer.IsKeyboardFocusWithin);
            Assert.False(firstContainer.IsKeyboardFocusWithin);
            Assert.False(view.IsActionAvailable("9to1.Files.CancelMove"));
            Assert.Empty((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);

            secondContainer.RaiseEvent(new ContextRequestedEventArgs());
            Assert.True(menu.IsOpen);
            Assert.Same(secondRow, list.SelectedItem);
            list.SelectedItem = firstRow;
            list.SelectedItem = secondRow; // Returning to the original row does not revive a stale menu.
            cut.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(view.IsActionAvailable("9to1.Files.CancelMove"));
            menu.Close();

            secondContainer.RaiseEvent(new ContextRequestedEventArgs());
            Assert.True(menu.IsOpen);
            Assert.True(cut.IsEnabled);
            cut.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await AwaitNativeRoleStateAsync(window, () => view.IsActionAvailable("9to1.Files.CancelMove") == true, token);
            Assert.True(view.TryGetValue("MoveSelection", out var clipboard));
            Assert.Contains("Second", Assert.IsType<string>(clipboard));
            Assert.Equal(h.Parent, (await h.Workspace.Provider.GetAsync(second, token)).Value!.ParentId);
        }
        finally { await view.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Row_Rename_edits_only_its_original_item_and_requires_Home_review()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var first = await CreateReviewedFolder(h, h.Parent.Value, "First", token);
        var second = await CreateReviewedFolder(h, h.Parent.Value, "Second", token);
        await using var view = new FilesNativeBrowserSurface(h.Browser, null, h.Workspace.Actor,
            new Readiness(), CancellationToken.None);
        var window = new Window { Width = 900, Height = 700 };
        window.Template = new Avalonia.Controls.Templates.FuncControlTemplate<Window>((_, names) =>
        {
            var layers = new Avalonia.Controls.Primitives.VisualLayerManager { Child = view };
            names.Register("PART_VisualLayerManager", layers);
            return layers;
        });
        window.Show();
        try
        {
            await view.InitializeAsync(token);
            await view.OpenOriginalLocationAsync(h.Workspace.Configuration.StoreId, h.Parent.Value, token);
            window.UpdateLayout();
            var list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>());
            var firstRow = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == first);
            var secondRow = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == second);
            list.SelectedItem = firstRow;
            var container = Assert.IsType<ListBoxItem>(list.ContainerFromItem(secondRow));
            var menu = Assert.IsType<ContextMenu>(container.ContextMenu);
            var rename = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Rename"));
            container.RaiseEvent(new ContextRequestedEventArgs());
            Assert.True(menu.IsOpen);
            Assert.True(rename.IsEnabled);
            rename.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Close();
            await AwaitNativeRoleStateAsync(window, () => Equals(ReadNativeValue(view, "NameEditorVisible"), true), token);
            window.UpdateLayout();
            var name = FilesPresentationControl<TextBox>(view, "files-name-input");
            Assert.True(name.IsEffectivelyVisible);
            Assert.Equal("Second", name.Text);
            Assert.Equal("Second", name.SelectedText);
            Dispatcher.UIThread.RunJobs();
            Assert.True(name.IsKeyboardFocusWithin);
            Assert.False(view.IsActionAvailable("9to1.Files.Rename")); // unchanged
            Assert.Empty((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);

            name.Text = "Renamed Second";
            list.SelectedItem = firstRow;
            Assert.False(view.IsActionAvailable("9to1.Files.Rename"));
            Assert.False(view.IsActionAvailable("9to1.Files.CreateFolder"));
            await view.DispatchAsync("9to1.Files.Rename", null, token);
            Assert.Empty((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            Assert.Equal("Renamed Second", name.Text); // retain the user's unsaved input

            list.SelectedItem = secondRow;
            Assert.True(view.IsActionAvailable("9to1.Files.Rename"));
            await view.DispatchAsync("9to1.Files.Rename", null, token);
            var review = Assert.Single((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            Assert.Equal("9to1.Files.Rename", review.Scope.ActionName);
            Assert.Equal("Second", (await h.Workspace.Provider.GetAsync(second, token)).Value!.Name);
            Assert.True((await h.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            await view.DispatchAsync("9to1.Files.Apply", null, token);
            Assert.Equal("Renamed Second", (await h.Workspace.Provider.GetAsync(second, token)).Value!.Name);
            Assert.Equal("First", (await h.Workspace.Provider.GetAsync(first, token)).Value!.Name);
            Assert.True(string.IsNullOrEmpty(name.Text));

            // A real keyboard event opens the same editor. Escape discards only the draft.
            list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == first);
            window.UpdateLayout();
            var firstContainer = Assert.IsType<ListBoxItem>(list.ContainerFromItem(list.SelectedItem));
            Assert.True(firstContainer.Focus());
            window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);
            await AwaitNativeRoleStateAsync(window, () => name.IsKeyboardFocusWithin && name.Text == "First", token);
            name.Text = "Do not rename";
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await AwaitNativeRoleStateAsync(window, () => list.IsKeyboardFocusWithin && string.IsNullOrEmpty(name.Text), token);
            Assert.Empty((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            Assert.Equal("First", (await h.Workspace.Provider.GetAsync(first, token)).Value!.Name);
        }
        finally { await view.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Failed_Paste_keeps_the_original_Cut_until_its_reviewed_move_commits()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var source = await CreateReviewedFolder(h, h.Parent.Value, "Same name", token);
        var occupiedFolder = await CreateReviewedFolder(h, h.Parent.Value, "Occupied destination", token);
        var alternate = await CreateReviewedFolder(h, h.Parent.Value, "Free destination", token);
        var existing = await CreateReviewedFolder(h, occupiedFolder.Value, "Same name", token);
        await using var view = new FilesNativeBrowserSurface(h.Browser, null, h.Workspace.Actor,
            new Readiness(), CancellationToken.None);
        var window = new Window { Content = view, Width = 720, Height = 640 };
        window.Show();
        try
        {
            await view.InitializeAsync(token);
            await view.OpenOriginalLocationAsync(h.Workspace.Configuration.StoreId, h.Parent.Value, token);
            window.UpdateLayout();
            var list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>());
            void Select(HostedItemId id) => list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == id);
            async Task ApplyReviewedPasteAsync()
            {
                Assert.True(view.IsActionAvailable("9to1.Files.Move"));
                await view.DispatchAsync("9to1.Files.Move", null, token);
                var review = Assert.Single((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
                Assert.Equal("9to1.Files.Move", review.Scope.ActionName);
                Assert.True((await h.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
                await view.DispatchAsync("9to1.Files.Apply", null, token);
            }

            Select(source);
            await view.DispatchAsync("9to1.Files.ChooseMove", null, token);
            Select(occupiedFolder);
            await view.DispatchAsync("9to1.Files.Open", null, token);
            await ApplyReviewedPasteAsync();

            Assert.True(view.TryGetValue("MoveSelection", out var clipboard));
            Assert.Contains("Same name", Assert.IsType<string>(clipboard));
            Assert.True(view.IsActionAvailable("9to1.Files.CancelMove"), "the failed move must leave the original cut intact");
            Assert.Equal(h.Parent, (await h.Workspace.Provider.GetAsync(source, token)).Value!.ParentId);
            Assert.Equal(existing, Assert.Single(list.Items.Cast<HostedItemMetadata>()).Id);

            await view.DispatchAsync("9to1.Files.Back", null, token);
            Select(alternate);
            await view.DispatchAsync("9to1.Files.Open", null, token);
            await ApplyReviewedPasteAsync();
            Assert.Equal(source, Assert.Single(list.Items.Cast<HostedItemMetadata>()).Id);
            Assert.Equal(alternate, (await h.Workspace.Provider.GetAsync(source, token)).Value!.ParentId);
            Assert.False(view.IsActionAvailable("9to1.Files.CancelMove"), "only the matching committed move consumes the cut");
        }
        finally { await view.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Distinct_native_presentations_reopen_the_same_canonical_store_and_folder()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var folder = await CreateReviewedFolder(h, h.Parent.Value, "Shared folder", token);
        var child = await CreateReviewedFolder(h, folder.Value, "Same resource", token);
        await using var first = new FilesNativeBrowserSurface(h.Browser, null, h.Workspace.Actor, new Readiness(), CancellationToken.None);
        await using var second = new FilesNativeBrowserSurface(h.Browser, null, h.Workspace.Actor, new Readiness(), CancellationToken.None);
        var one = new Window { Content = first }; var two = new Window { Content = second };
        one.Show(); two.Show();
        try
        {
            await first.InitializeAsync(token); await second.InitializeAsync(token);
            await first.OpenOriginalLocationAsync(h.Workspace.Configuration.StoreId, folder.Value, token);
            await second.OpenOriginalLocationAsync(h.Workspace.Configuration.StoreId, folder.Value, token);
            one.UpdateLayout(); two.UpdateLayout();
            Assert.NotSame(first, second); Assert.NotSame(first.Content, second.Content);
            Assert.True(first.TryGetOriginalLocation(out var store, out var location, out var title));
            Assert.Equal(h.Workspace.Configuration.StoreId, store); Assert.Equal(folder.Value, location); Assert.Equal("Shared folder", title);
            Assert.True(second.TryGetOriginalLocation(out var otherStore, out var otherLocation, out _));
            Assert.Equal(store, otherStore); Assert.Equal(location, otherLocation);
            foreach (var view in new[] { first, second })
            {
                var list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>());
                Assert.Equal(child, Assert.Single(list.Items.Cast<HostedItemMetadata>()).Id);
                Assert.True(view.CanPrepareOriginalClose());
            }
            await first.CloseAndDrainAsync();
            Assert.False(first.OriginalPublicationIsAlive); Assert.True(second.OriginalPublicationIsAlive);
            await second.RefreshAsync(token);
            Assert.Equal(child, Assert.Single(Assert.Single(second.GetVisualDescendants().OfType<ListBox>()).Items.Cast<HostedItemMetadata>()).Id);
        }
        finally { await first.CloseAndDrainAsync(); await second.CloseAndDrainAsync(); one.Close(); two.Close(); }
    }

    [AvaloniaFact]
    public async Task Close_preflight_preserves_typed_drafts_and_pending_Home_review_until_user_resolves_them()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        await using var view = new FilesNativeBrowserSurface(h.Browser, null, h.Workspace.Actor, new Readiness(), CancellationToken.None);
        var window = new Window { Content = view }; window.Show();
        try
        {
            await view.InitializeAsync(token);
            await view.OpenOriginalLocationAsync(h.Workspace.Configuration.StoreId, h.Parent.Value, token);
            window.UpdateLayout();
            var name = await ShowNativeNameEditorAsync(view, window, token);
            var originalContent = view.Content;
            name.Text = "Unsubmitted folder";
            Assert.False(view.CanPrepareOriginalClose()); Assert.Equal("Unsubmitted folder", name.Text);
            Assert.Same(originalContent, view.Content); Assert.True(view.OriginalPublicationIsAlive);
            await view.DispatchAsync("9to1.Files.CreateFolder", null, token);
            name.Text = ""; // The outstanding review itself must still veto close.
            Assert.False(view.CanPrepareOriginalClose()); Assert.True(view.IsActionAvailable("9to1.Files.CancelReview"));
            Assert.Empty((await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token)).Items);
            await view.DispatchAsync("9to1.Files.CancelReview", null, token);
            Assert.True(view.CanPrepareOriginalClose()); Assert.Same(originalContent, view.Content);
            Assert.True(view.OriginalPublicationIsAlive);
        }
        finally { await view.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Foreign_store_deep_link_refuses_without_rebinding_or_clean_retirement_receipt()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var view = new FilesNativeBrowserSurface(h.Browser, null, h.Workspace.Actor, new Readiness(), CancellationToken.None);
        var window = new Window { Content = view }; window.Show();
        try
        {
            await view.InitializeAsync(token);
            var before = await h.Workspace.Provider.GetStoreEvidenceAsync(h.Workspace.Configuration.StoreId, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => view.OpenOriginalLocationAsync(Guid.NewGuid(), h.Parent.Value, token));
            Assert.False(view.OriginalPublicationIsAlive); Assert.False(view.TryGetOriginalLocation(out _, out _, out _));
            Assert.Equal(before, await h.Workspace.Provider.GetStoreEvidenceAsync(h.Workspace.Configuration.StoreId, token));
            var close = view.CloseAndDrainAsync(); Assert.Same(close, view.CloseAndDrainAsync());
            Assert.NotNull(await Record.ExceptionAsync(() => close)); Assert.True(close.IsFaulted);
        }
        finally { _ = await Record.ExceptionAsync(() => view.CloseAndDrainAsync()); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Canonical_file_identity_cannot_be_reinterpreted_as_a_folder_deep_link()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var file = HostedItemId.New();
        Assert.True((await h.Workspace.Provider.RegisterArtifactAsync(new("write", Guid.NewGuid().ToString("D"), file,
            h.Parent, nameof(FilesArtifactType.WriteDocument), "A document"), h.Workspace.Actor.ActorId, token)).IsSuccess);
        var view = new FilesNativeBrowserSurface(h.Browser, null, h.Workspace.Actor, new Readiness(), CancellationToken.None);
        var window = new Window { Content = view }; window.Show();
        try
        {
            await view.InitializeAsync(token);
            var before = (await h.Workspace.Provider.GetAsync(file, token)).Value;
            await Assert.ThrowsAsync<InvalidOperationException>(() => view.OpenOriginalLocationAsync(h.Workspace.Configuration.StoreId, file.Value, token));
            Assert.False(view.OriginalPublicationIsAlive);
            Assert.Equal(before, (await h.Workspace.Provider.GetAsync(file, token)).Value);
        }
        finally { Assert.NotNull(await Record.ExceptionAsync(() => view.CloseAndDrainAsync())); window.Close(); }
    }

    [Theory]
    [InlineData(FilesAccessRole.Viewer, FilesAccessRole.Editor)]
    [InlineData(FilesAccessRole.Editor, FilesAccessRole.Viewer)]
    public async Task Existing_recipient_role_changes_require_Home_and_keep_original_identity_expiry_and_one_shot_ACK(
        FilesAccessRole beforeRole, FilesAccessRole afterRole)
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var seed = await SeedOriginalNativeShareAsync(h, beforeRole, token);
        var prepared = await h.Browser.PrepareNativeShareRoleChangeAsync(seed.Shares, seed.Grant, afterRole,
            h.Workspace.Actor, () => true, "files-role-control", token);
        Assert.Equal(HomePermissionRequestState.PendingApproval, prepared.Approval.State);
        Assert.True(h.Browser.IsNativeShareMutation(prepared)); Assert.False(h.Browser.IsNativeShareRevocation(prepared));
        Assert.Contains(afterRole.ToString(), h.Browser.DescribeNativeShareMutation(prepared));
        Assert.Equal(h.Browser.DescribeNativeShareMutation(prepared), prepared.Description);
        Assert.Throws<UnauthorizedAccessException>(() => h.Browser.DescribeNativeShareRevocation(prepared));
        Assert.Throws<UnauthorizedAccessException>(() => h.Browser.ReadObservedNativeShareRevocation(prepared));
        var evidence = await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token);
        Assert.True((await h.Browser.ApplyNativeShareMutationAsync(prepared, token)).AwaitingHomeReview);
        Assert.Equal(evidence, await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token));
        Assert.Null(h.Browser.ReadObservedNativeShareMutation(prepared));
        Assert.False(h.Browser.WasNativeShareMutationCommitted(prepared));
        Assert.True((await h.Permissions.DecideAsync(prepared.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        var actual = h.Browser.ApplyNativeShareMutationAsync(prepared, token);
        Assert.Same(actual, h.Browser.ApplyNativeShareMutationAsync(prepared, token));
        var outcome = await actual;
        Assert.True(outcome.AuditRecorded); Assert.False(outcome.AwaitingHomeReview);
        var ack = h.Browser.ReadObservedNativeShareMutation(prepared)!;
        Assert.True(ack.IsSuccess); Assert.True(h.Browser.WasNativeShareMutationCommitted(prepared));
        Assert.Equal(afterRole, ack.Value!.Share.Role); Assert.False(ack.Value.Share.IsRevoked);
        Assert.NotEqual(seed.Grant.Share.Id, ack.Value.Share.Id);
        Assert.Equal(seed.Grant.Share.ItemId, ack.Value.Share.ItemId);
        Assert.Equal(seed.Grant.Share.PrincipalId, ack.Value.Share.PrincipalId);
        Assert.Equal(seed.Grant.RecipientEmail, ack.Value.RecipientEmail);
        Assert.Equal(seed.Grant.Share.ExpiresAt, ack.Value.Share.ExpiresAt);
        Assert.Equal(h.Workspace.Actor.ActorId, ack.Value.GrantedByPrincipalId);
        var authorization = await h.Permissions.GetAuthorizationAsync(prepared.Approval.RequestId, token);
        Assert.Equal(HomePermissionRequestState.Succeeded, authorization.State);
        var page = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        var row = page.Items.Single(item => item.Id == seed.Item.Id);
        var reopened = await h.Browser.ReadNativeSharesAsync(page, row, h.Workspace.Actor, () => true, token: token);
        Assert.True(reopened.Grants.Single(item => item.Share.Id == seed.Grant.Share.Id).Share.IsRevoked);
        Assert.Equal(ack.Value, reopened.Grants.Single(item => item.Share.Id == ack.Value.Share.Id));
        var after = await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token);
        Assert.Same(actual, h.Browser.ApplyNativeShareMutationAsync(prepared, token));
        Assert.True((await h.Browser.RetryNativeMutationAuditAsync(prepared, token)).Succeeded);
        Assert.Equal(after, await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token));
    }

    [Fact]
    public async Task Declined_role_change_never_creates_a_replacement_grant()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var seed = await SeedOriginalNativeShareAsync(h, FilesAccessRole.Viewer, token);
        var request = await h.Browser.PrepareNativeShareRoleChangeAsync(seed.Shares, seed.Grant, FilesAccessRole.Editor,
            h.Workspace.Actor, () => true, "files-role-decline", token);
        var before = await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token);
        Assert.True((await h.Permissions.DecideAsync(request.Approval.RequestId, HomeApprovalChoice.Decline,
            cancellationToken: token)).Succeeded);
        Assert.True((await h.Browser.ApplyNativeShareMutationAsync(request, token)).AwaitingHomeReview);
        Assert.Equal(before, await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token));
        Assert.Null(h.Browser.ReadObservedNativeShareMutation(request));
        h.Browser.RetireNativeMutationReview(request);
        Assert.Throws<InvalidOperationException>(() => { _ = h.Browser.ApplyNativeShareMutationAsync(request, token); });
    }

    [Fact]
    public async Task Role_changes_reject_copied_grants_noop_and_owner_scope_without_requesting_permission()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var seed = await SeedOriginalNativeShareAsync(h, FilesAccessRole.Viewer, token);
        var before = await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Browser.PrepareNativeShareRoleChangeAsync(seed.Shares,
            seed.Grant with { }, FilesAccessRole.Editor, h.Workspace.Actor, () => true, "files-role-copy", token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Browser.PrepareNativeShareRoleChangeAsync(seed.Shares,
            seed.Grant, FilesAccessRole.Viewer, h.Workspace.Actor, () => true, "files-role-noop", token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Browser.PrepareNativeShareRoleChangeAsync(seed.Shares,
            seed.Grant, FilesAccessRole.Owner, h.Workspace.Actor, () => true, "files-role-owner", token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => h.Browser.ReadNativeSharesAsync(seed.Page with { },
            seed.Item, h.Workspace.Actor, () => true, token: token));
        Assert.Empty((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
        Assert.Equal(before, await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token));
    }

    [Fact]
    public async Task Original_role_review_cannot_commit_after_lifetime_or_selected_item_revision_changes()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var seed = await SeedOriginalNativeShareAsync(h, FilesAccessRole.Viewer, token);
        var alive = true;
        var retired = await h.Browser.PrepareNativeShareRoleChangeAsync(seed.Shares, seed.Grant, FilesAccessRole.Editor,
            h.Workspace.Actor, () => alive, "files-role-retired", token);
        Assert.True((await h.Permissions.DecideAsync(retired.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        var before = await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token);
        alive = false;
        var denied = h.Browser.ApplyNativeShareMutationAsync(retired, token);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => denied);
        Assert.Same(denied, h.Browser.ApplyNativeShareMutationAsync(retired, token));
        Assert.Equal(before, await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token));
        var stale = await h.Browser.PrepareNativeShareRoleChangeAsync(seed.Shares, seed.Grant, FilesAccessRole.Editor,
            h.Workspace.Actor, () => true, "files-role-stale", token);
        Assert.True((await h.Permissions.DecideAsync(stale.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        var rename = await h.Browser.PrepareNativeMutationAsync(seed.Page, seed.Item, h.Workspace.Actor,
            "Rename", "Changed before role commit", () => true, "files-role-intervening-rename", token);
        Assert.True((await h.Permissions.DecideAsync(rename.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        Assert.True((await h.Browser.ApplyNativeMutationAsync(rename, token)).Operation!.IsSuccess);
        var afterRename = await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token);
        var staleError = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Browser.ApplyNativeShareMutationAsync(stale, token));
        Assert.Equal("The displayed Files folder changed.", staleError.Message);
        Assert.Equal(afterRename, await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token));
        Assert.Null(h.Browser.ReadObservedNativeShareMutation(stale));
    }

    [Fact]
    public async Task Saved_role_ACK_survives_Home_audit_fault_and_retry_cannot_grant_again()
    {
        var token = TestContext.Current.CancellationToken;
        var observer = new PersistedObserver();
        await using var h = await Harness.CreateAsync(token, observer);
        var seed = await SeedOriginalNativeShareAsync(h, FilesAccessRole.Viewer, token);
        var prepared = await h.Browser.PrepareNativeShareRoleChangeAsync(seed.Shares, seed.Grant, FilesAccessRole.Editor,
            h.Workspace.Actor, () => true, "files-role-audit", token);
        Assert.True((await h.Permissions.DecideAsync(prepared.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        var fault = new IOException("Original sharing audit acknowledgement failed after persistence.");
        var companion = new InvalidOperationException("Retain the original sharing audit companion.");
        var faults = 0;
        observer.BeforeAcknowledgement = async (id, revision, ct) =>
        {
            if (id != "home.permissions-trust") return;
            var home = await h.Home.ReadAsync(ct); Assert.True(home.IsSuccess);
            var record = home.State!.Records.Single(item => item.RecordId == id); Assert.Equal(revision, record.Revision);
            var request = record.Payload.GetProperty("Requests").EnumerateArray()
                .Single(item => item.GetProperty("RequestId").GetString() == prepared.Approval.RequestId);
            if (request.GetProperty("State").GetInt32() == (int)HomePermissionRequestState.Succeeded
                && Interlocked.Increment(ref faults) == 1)
            {
                Assert.True(h.Browser.WasNativeShareMutationCommitted(prepared));
                throw new AggregateException(fault, companion);
            }
        };
        var actual = h.Browser.ApplyNativeShareMutationAsync(prepared, token);
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Contains(error.Flatten().InnerExceptions, item => ReferenceEquals(item, fault));
        Assert.Contains(error.Flatten().InnerExceptions, item => ReferenceEquals(item, companion));
        var ack = h.Browser.ReadObservedNativeShareMutation(prepared)!;
        Assert.True(ack.IsSuccess); Assert.Equal(FilesAccessRole.Editor, ack.Value!.Share.Role);
        Assert.True(prepared.HasObservedOutcome); Assert.True(h.Browser.WasNativeShareMutationCommitted(prepared));
        Assert.Same(actual, h.Browser.ApplyNativeShareMutationAsync(prepared, token));
        var beforeRetry = await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token);
        observer.BeforeAcknowledgement = null;
        Assert.True((await h.Browser.RetryNativeMutationAuditAsync(prepared, token)).Succeeded);
        Assert.Same(ack, h.Browser.ReadObservedNativeShareMutation(prepared));
        Assert.Equal(beforeRetry, await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token));
        Assert.Same(actual, h.Browser.ApplyNativeShareMutationAsync(prepared, token));
        Assert.Equal(1, faults);
    }

    [AvaloniaFact]
    public async Task Canonical_role_button_stages_decline_and_apply_without_new_recipient_or_duplicate_effect()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var seed = await SeedOriginalNativeShareAsync(h, FilesAccessRole.Viewer, token);
        await using var view = new FilesNativeBrowserSurface(h.Browser, null, h.Workspace.Actor,
            new Readiness(), CancellationToken.None);
        var window = new Window { Content = view, Width = 920, Height = 760 }; window.Show();
        try
        {
            await view.InitializeAsync(token);
            await view.OpenOriginalLocationAsync(seed.Page.StoreID, h.Parent.Value, token);
            window.UpdateLayout();
            var list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>());
            list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == seed.Item.Id);
            await view.DispatchAsync("9to1.Files.Share.Get", null, token); window.UpdateLayout();
            var change = NativeRoleButton(view); Assert.Equal("Make editor…", change.Content);
            var add = Assert.Single(view.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "Add recipient")); Assert.False(add.IsEnabled);
            var before = await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token);
            change.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await AwaitNativeRoleStateAsync(window, () => view.IsActionAvailable("9to1.Files.Apply") == true, token);
            Assert.False(view.CanPrepareOriginalClose());
            Assert.False(view.IsActionAvailable("9to1.Files.Share.ChangeRole"));
            Assert.False(view.IsActionAvailable("9to1.Files.Refresh"));
            var declined = Assert.Single((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            Assert.Equal("9to1.Files.Share.Grant", declined.Scope.ActionName);
            Assert.Equal(before, await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token));
            Assert.True((await h.Permissions.DecideAsync(declined.RequestId, HomeApprovalChoice.Decline,
                cancellationToken: token)).Succeeded);
            await view.DispatchAsync("9to1.Files.CancelReview", null, token);
            Assert.True(view.CanPrepareOriginalClose());
            Assert.Equal(before, await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token));
            NativeRoleButton(view).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await AwaitNativeRoleStateAsync(window, () => view.IsActionAvailable("9to1.Files.Apply") == true, token);
            var accepted = Assert.Single((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            Assert.True((await h.Permissions.DecideAsync(accepted.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: token)).Succeeded);
            await CaptureComponentSceneAsync(window, "files-existing-recipient-role-review");
            var apply = Assert.Single(view.GetVisualDescendants().OfType<Button>(),
                button => AutomationProperties.GetName(button) == "Apply the original Home-reviewed Files change");
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await AwaitNativeRoleStateAsync(window, () => view.IsActionAvailable("9to1.Files.Apply") == false
                && view.IsActionAvailable("9to1.Files.Refresh") == true, token);
            Assert.True(view.TryGetValue("SharingVisible", out var visible)); Assert.False(Assert.IsType<bool>(visible));
            list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == seed.Item.Id);
            await view.DispatchAsync("9to1.Files.Share.Get", null, token); window.UpdateLayout();
            Assert.Equal("Make viewer…", NativeRoleButton(view).Content);
            var grants = Assert.IsAssignableFrom<IEnumerable<FilesEmailShare>>(ReadNativeValue(view, "SharingGrants")).ToArray();
            Assert.Equal(2, grants.Length);
            var active = Assert.Single(grants, item => !item.Share.IsRevoked);
            Assert.Equal(FilesAccessRole.Editor, active.Share.Role); Assert.NotEqual(seed.Grant.Share.Id, active.Share.Id);
            Assert.Equal(seed.Grant.Share.PrincipalId, active.Share.PrincipalId);
            Assert.Equal(seed.Grant.Share.ExpiresAt, active.Share.ExpiresAt);
            Assert.True(grants.Single(item => item.Share.Id == seed.Grant.Share.Id).Share.IsRevoked);
            Assert.True(view.CanPrepareOriginalClose());
            await CaptureComponentSceneAsync(window, "files-existing-recipient-role-applied");
        }
        finally { await view.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Original_text_preview_location_uses_fresh_issued_rows_and_named_close_with_clean_component_retirement()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var actor = h.Workspace.Actor;
        var folder = (await h.Workspace.Directories.ResolveProfileAsync(Guid.Parse(actor.ProfileId), "write", token)).Value!;
        var text = "// Revision note\nconst answer = 42;\n<script>plain text, never executed</script>";
        var bytes = Encoding.UTF8.GetBytes(text);
        await File.WriteAllBytesAsync(Path.Combine(folder.DirectoryPath, "revision.cs"), bytes, token);
        var parent = (await h.Workspace.Provider.GetAsync(h.Parent, token)).Value!;
        var upload = new FilesUploadedContent(HostedItemId.New(), h.Parent, "revision.cs", "text/plain", new(Guid.NewGuid()),
            null, actor.ActorId, DateTimeOffset.UtcNow, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), "revision.cs");
        var authority = h.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
        var commit = await authority.CaptureCommitAuthorityAsync(actor, h.Workspace.Provider, () => true, token);
        Assert.True((await h.Workspace.Provider.CommitUploadedContentAsync(upload,
            [new(h.Parent, parent.CurrentRevisionId)], commit, token)).IsSuccess);
        var before = await h.Workspace.Provider.GetStoreEvidenceAsync(h.Workspace.Configuration.StoreId, token);
        var originalHome = await File.ReadAllBytesAsync(Path.Combine(h.Root, "home.json"), token);
        await using var view = new FilesNativeBrowserSurface(h.Browser, null, actor,
            new OriginalPreviewFixtureReadiness(h), CancellationToken.None);
        var window = new Window { Content = view, Width = 1060, Height = 800 }; window.Show();
        try
        {
            // No copied item/page or initial guessed selection: the exact target is freshly resolved
            // by the maintained original metadata issuer, then its separate Home READ previews bytes.
            await view.OpenOriginalTextPreviewLocationAsync(h.Workspace.Configuration.StoreId, h.Parent.Value,
                upload.FileId.Value, upload.RevisionId.Value, token);
            window.UpdateLayout();
            Assert.True(view.TryGetOriginalTextPreviewLocation(out var file, out var revision, out var name));
            Assert.Equal(upload.FileId.Value, file); Assert.Equal(upload.RevisionId.Value, revision); Assert.Equal("revision.cs", name);
            Assert.Equal(upload.RevisionId.Value.ToString("N"), upload.RevisionId.ToString());
            Assert.Equal(text, Assert.IsType<string>(ReadNativeValue(view, "TextPreviewContent")));
            Assert.Equal(text, Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(),
                control => control.Name == "files-preview-content").Text);
            await CaptureComponentSceneAsync(window, "files-current-text-preview");
            await Assert.ThrowsAsync<ArgumentException>(() => view.DispatchAsync("9to1.Files.Preview", new object(), token).AsTask());
            var closePreview = Assert.Single(view.GetVisualDescendants().OfType<Button>(), control => control.Name == "files-preview-close");
            Assert.True(closePreview.IsEnabled);
            closePreview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // The actual named CUI action, no direct helper clear.
            await AwaitNativeRoleStateAsync(window, () => !view.TryGetOriginalTextPreviewLocation(out _, out _, out _)
                && view.CanPrepareOriginalClose(), token);
            Assert.Equal("", Assert.IsType<string>(ReadNativeValue(view, "TextPreviewContent")));
            Assert.Equal(before, await h.Workspace.Provider.GetStoreEvidenceAsync(h.Workspace.Configuration.StoreId, token));
            Assert.Equal(originalHome, await File.ReadAllBytesAsync(Path.Combine(h.Root, "home.json"), token));
            var close = view.CloseAndDrainAsync(); Assert.Same(close, view.CloseAndDrainAsync()); await close;
            Assert.False(view.OriginalPublicationIsAlive);
            Assert.Equal(actor, await h.Graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token));
        }
        finally { await view.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Restored_local_source_without_immutable_content_keeps_unavailable_notice_folder_and_clean_close()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var actor = h.Workspace.Actor;
        var folder = (await h.Workspace.Directories.ResolveProfileAsync(Guid.Parse(actor.ProfileId), "write", token)).Value!;
        var sourcePath = Path.Combine(folder.DirectoryPath, "local-source.cs");
        var bytes = Encoding.UTF8.GetBytes("// Existing local source is not an immutable uploaded revision.\n");
        await File.WriteAllBytesAsync(sourcePath, bytes, token);
        var parent = (await h.Workspace.Provider.GetAsync(h.Parent, token)).Value!;
        var source = new FilesOriginalLocalDeveloperSource(HostedItemId.New(), h.Parent, "local-source.cs", "text/plain",
            new(Guid.NewGuid()), actor.ActorId, DateTimeOffset.UtcNow, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
        var authority = h.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
        var commit = await authority.CaptureCommitAuthorityAsync(actor, h.Workspace.Provider, () => true, token);
        Assert.True((await h.Workspace.Provider.RegisterOriginalLocalDeveloperFileAsync(source,
            [new(h.Parent, parent.CurrentRevisionId)], h.Workspace.Configuration.StoreId, commit, token)).IsSuccess);
        var content = (await h.Workspace.Provider.GetCurrentArtifactContentForOriginalStoreAsync(
            h.Workspace.Configuration.StoreId, source.FileId, source.RevisionId, token)).Value!;
        Assert.Null(content.UploadAnchorFolderId); Assert.Null(content.ProviderContentReference);
        var drivePath = Path.Combine(h.Workspace.Configuration.RootDirectory, ".9to1-files", "drive.json");
        var originalDrive = await File.ReadAllBytesAsync(drivePath, token);
        var originalHome = await File.ReadAllBytesAsync(Path.Combine(h.Root, "home.json"), token);
        var view = new FilesNativeBrowserSurface(h.Browser, null, actor,
            new OriginalPreviewFixtureReadiness(h), CancellationToken.None);
        var window = new Window { Content = view, Width = 1060, Height = 800 }; window.Show();
        try
        {
            // This real source is registered as current metadata without publishing immutable bytes.
            // Restoration must retain the producer's refusal, never read the mutable local file.
            var opening = view.OpenOriginalTextPreviewLocationAsync(h.Workspace.Configuration.StoreId, h.Parent.Value,
                source.FileId.Value, source.RevisionId.Value, token);
            await opening; window.UpdateLayout();
            Assert.True(opening.IsCompletedSuccessfully); Assert.True(view.OriginalPublicationIsAlive);
            Assert.True(view.TryGetOriginalLocation(out var store, out var parentId, out _));
            Assert.Equal(h.Workspace.Configuration.StoreId, store); Assert.Equal(h.Parent.Value, parentId);
            Assert.False(view.TryGetOriginalTextPreviewLocation(out _, out _, out _));
            Assert.True(Assert.IsType<bool>(ReadNativeValue(view, "TextPreviewVisible")));
            Assert.Equal("local-source.cs", Assert.IsType<string>(ReadNativeValue(view, "TextPreviewTitle")));
            Assert.Equal("", Assert.IsType<string>(ReadNativeValue(view, "TextPreviewContent")));
            const string notice = "This file has no supported existing local content materialisation.";
            Assert.Equal(notice, Assert.IsType<string>(ReadNativeValue(view, "TextPreviewMessage")));
            Assert.Equal(notice, Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(),
                control => control.Name == "files-preview-message").Text);
            Assert.True(view.CanPrepareOriginalClose());
            var close = view.CloseAndDrainAsync(); Assert.Same(close, view.CloseAndDrainAsync()); await close;
            Assert.True(close.IsCompletedSuccessfully); Assert.False(view.OriginalPublicationIsAlive);
            Assert.Equal(originalDrive, await File.ReadAllBytesAsync(drivePath, token));
            Assert.Equal(originalHome, await File.ReadAllBytesAsync(Path.Combine(h.Root, "home.json"), token));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(sourcePath, token));
        }
        finally { await view.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Restored_uploaded_preview_preserves_actual_missing_file_failure_in_original_close()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var actor = h.Workspace.Actor;
        var file = await CreatePresentationFileAsync(h, "missing-preview.txt", token);
        var row = (await h.Workspace.Provider.GetAsync(file, token)).Value!;
        var content = (await h.Workspace.Provider.GetCurrentArtifactContentForOriginalStoreAsync(
            h.Workspace.Configuration.StoreId, file, row.CurrentRevisionId, token)).Value!;
        Assert.Equal(h.Parent, content.UploadAnchorFolderId); Assert.Equal(row.Name, content.ProviderContentReference);
        var folder = (await h.Workspace.Directories.ResolveProfileAsync(Guid.Parse(actor.ProfileId), "write", token)).Value!;
        var path = Path.Combine(folder.DirectoryPath, row.Name);
        Assert.True(File.Exists(path)); File.Delete(path); Assert.False(File.Exists(path));
        var drivePath = Path.Combine(h.Workspace.Configuration.RootDirectory, ".9to1-files", "drive.json");
        var originalDrive = await File.ReadAllBytesAsync(drivePath, token);
        var originalHome = await File.ReadAllBytesAsync(Path.Combine(h.Root, "home.json"), token);
        var view = new FilesNativeBrowserSurface(h.Browser, null, actor,
            new OriginalPreviewFixtureReadiness(h), CancellationToken.None);
        var window = new Window { Content = view }; window.Show();
        try
        {
            var opening = view.OpenOriginalTextPreviewLocationAsync(h.Workspace.Configuration.StoreId, h.Parent.Value,
                file.Value, row.CurrentRevisionId!.Value.Value, token);
            var failure = await Record.ExceptionAsync(() => opening); Assert.NotNull(failure);
            // The native action retains its original IO failure and withdraws the page. The restore
            // then refuses its missing owner; close joins BOTH originals without replacing either.
            var ownerRefusal = Assert.IsType<UnauthorizedAccessException>(
                Assert.Single(new AggregateException(failure).Flatten().InnerExceptions.Distinct()));
            Assert.Equal("The original Files page is unavailable.", ownerRefusal.Message); Assert.True(opening.IsFaulted);
            Assert.False(view.OriginalPublicationIsAlive); Assert.False(view.TryGetOriginalTextPreviewLocation(out _, out _, out _));
            var close = view.CloseAndDrainAsync(); Assert.Same(close, view.CloseAndDrainAsync());
            var closeFailure = await Record.ExceptionAsync(() => close); Assert.NotNull(closeFailure);
            Assert.True(close.IsFaulted);
            var retained = new AggregateException(closeFailure).Flatten().InnerExceptions.Distinct().ToArray();
            var original = Assert.Single(retained.OfType<FileNotFoundException>());
            Assert.Equal(path, original.FileName);
            Assert.Contains(retained, cause => ReferenceEquals(cause, ownerRefusal));
            Assert.All(retained, cause => Assert.True(ReferenceEquals(cause, original) || ReferenceEquals(cause, ownerRefusal), cause.ToString()));
            var repeatedFailure = await Record.ExceptionAsync(() => view.CloseAndDrainAsync());
            Assert.Same(closeFailure, repeatedFailure);
            Assert.Same(original, Assert.Single(new AggregateException(repeatedFailure!).Flatten().InnerExceptions
                .OfType<FileNotFoundException>().Distinct()));
            Assert.Equal(originalDrive, await File.ReadAllBytesAsync(drivePath, token));
            Assert.Equal(originalHome, await File.ReadAllBytesAsync(Path.Combine(h.Root, "home.json"), token));
            Assert.False(File.Exists(path));
        }
        finally { _ = await Record.ExceptionAsync(() => view.CloseAndDrainAsync()); window.Close(); }
    }

    // Real component dependencies; this is not an installed-peer, full Home-runtime or platform
    // qualification. Ready is derived from the same actual state/profile/Files/receipt graph.
    private sealed class OriginalPreviewFixtureReadiness(Harness h) : ICuiSceneReadiness
    {
        public async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var profiles = h.Graph.GetRequiredService<HomeLocalProfileIdentity>();
            var read = await h.Home.ReadAsync(token);
            var actor = await profiles.GetCurrentAsync(token);
            var workspace = await h.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>()
                .GetCurrentAsync(h.Workspace.Configuration.StoreId, token);
            var owner = h.Graph.GetRequiredService<IResourceStoreOwnershipAuthority>() as IResourceStoreOwnershipReceiptAuthority;
            var receipt = owner is null ? null : await owner.GetVerifiedAsync("files", h.Workspace.Configuration.StoreId.ToString("D"), token);
            var current = read.IsSuccess && read.State is not null && actor == h.Workspace.Actor && workspace?.Actor == actor &&
                ReferenceEquals(workspace.Provider, h.Workspace.Provider) && receipt is not null &&
                await owner!.IsCurrentAsync(receipt, h.Workspace.Actor, token) &&
                await profiles.CheckAsync(read.State, h.Workspace.Actor, HomeStateCommitPhase.Publication, token);
            return current ? new(CuiSceneAvailabilityState.Ready, "ORIGINAL_COMPONENT", "Actual Home/Files component owners are current.")
                : new(CuiSceneAvailabilityState.Unavailable, "COMPONENT_CHANGED", "The original component profile/store ownership changed.");
        }
    }

    [Theory]
    [InlineData("9to1.Files.Share.Grant")]
    [InlineData("9to1.Files.Share.Revoke")]
    public async Task Native_sharing_resolver_admits_only_known_owned_current_write_scopes(string action)
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var itemId = await CreateReviewedFolder(h, h.Parent.Value, "Review access", token);
        var page = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        var row = page.Items.Single(item => item.Id == itemId);
        var scope = new ResourceScope("files.item", row.Id.ToString(), row.CurrentRevisionId!.Value.ToString(), ResourceAccess.Write);
        var resolver = h.Graph.GetRequiredService<FilesArtifactResourceResolver>();
        var observed = await resolver.EvaluateAsync(h.Workspace.Actor, action, scope, token);
        Assert.True(observed.Allowed); Assert.Equal(scope.Revision, observed.ResourceRevision);
        Assert.False((await resolver.EvaluateAsync(h.Workspace.Actor, action + ".Unknown", scope, token)).Allowed);
        Assert.False((await resolver.EvaluateAsync(h.Workspace.Actor, action, scope with { Access = ResourceAccess.Read }, token)).Allowed);
        Assert.False((await resolver.EvaluateAsync(h.Workspace.Actor, action, scope with { Access = ResourceAccess.Execute }, token)).Allowed);
        Assert.False((await resolver.EvaluateAsync(h.Workspace.Actor, action, scope with { Revision = Guid.NewGuid().ToString("N") }, token)).Allowed);
        Assert.False((await resolver.EvaluateAsync(h.Workspace.Actor, action, scope with { Id = Guid.NewGuid().ToString("N") }, token)).Allowed);
        Assert.False((await resolver.EvaluateAsync(h.Workspace.Actor with { ProfileId = Guid.NewGuid().ToString("N") }, action, scope, token)).Allowed);
        Assert.Empty((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
    }

    [Fact]
    public async Task Existing_recipient_expiry_only_change_requires_Home_and_preserves_principal_and_one_shot_ACK()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var seed = await SeedOriginalNativeShareAsync(h, FilesAccessRole.Viewer, token);
        var expiry = DateTimeOffset.UtcNow.AddHours(2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Browser.PrepareNativeShareScopeChangeAsync(seed.Shares,
            seed.Grant with { }, FilesAccessRole.Viewer, expiry, h.Workspace.Actor, () => true, "copied-scope", token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Browser.PrepareNativeShareScopeChangeAsync(seed.Shares,
            seed.Grant, FilesAccessRole.Viewer, DateTimeOffset.UtcNow.AddMinutes(-1), h.Workspace.Actor, () => true, "past-scope", token));
        var prepared = await h.Browser.PrepareNativeShareScopeChangeAsync(seed.Shares, seed.Grant,
            FilesAccessRole.Viewer, expiry, h.Workspace.Actor, () => true, "expiry-only-control", token);
        Assert.Equal(HomePermissionRequestState.PendingApproval, prepared.Approval.State);
        var before = await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token);
        Assert.True((await h.Browser.ApplyNativeShareMutationAsync(prepared, token)).AwaitingHomeReview);
        Assert.Equal(before, await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token));
        Assert.True((await h.Permissions.DecideAsync(prepared.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        var actual = h.Browser.ApplyNativeShareMutationAsync(prepared, token);
        Assert.Same(actual, h.Browser.ApplyNativeShareMutationAsync(prepared, token));
        Assert.True((await actual).AuditRecorded);
        var ack = h.Browser.ReadObservedNativeShareMutation(prepared)!;
        Assert.True(ack.IsSuccess); Assert.True(h.Browser.WasNativeShareMutationCommitted(prepared));
        Assert.Equal(seed.Grant.Share.PrincipalId, ack.Value!.Share.PrincipalId);
        Assert.Equal(seed.Grant.RecipientEmail, ack.Value.RecipientEmail); Assert.Equal(FilesAccessRole.Viewer, ack.Value.Share.Role);
        Assert.Equal(expiry, ack.Value.Share.ExpiresAt); Assert.NotEqual(seed.Grant.Share.Id, ack.Value.Share.Id);
        var after = await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token);
        Assert.Same(actual, h.Browser.ApplyNativeShareMutationAsync(prepared, token));
        Assert.True((await h.Browser.RetryNativeMutationAuditAsync(prepared, token)).Succeeded);
        Assert.Equal(after, await h.Workspace.Provider.GetStoreEvidenceAsync(seed.Page.StoreID, token));
    }

    [AvaloniaFact]
    public async Task Canonical_existing_recipient_scope_editor_uses_real_expiry_switch_UTC_and_Home_review()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var seed = await SeedOriginalNativeShareAsync(h, FilesAccessRole.Viewer, token);
        await using var view = new FilesNativeBrowserSurface(h.Browser, null, h.Workspace.Actor,
            new OriginalPreviewFixtureReadiness(h), CancellationToken.None);
        var window = new Window { Content = view, Width = 1120, Height = 1100 }; window.Show();
        try
        {
            await view.InitializeAsync(token); await view.OpenOriginalLocationAsync(seed.Page.StoreID, h.Parent.Value, token);
            var items = Assert.Single(view.GetVisualDescendants().OfType<ListBox>());
            items.SelectedItem = items.Items.Cast<HostedItemMetadata>().Single(item => item.Id == seed.Item.Id);
            await view.DispatchAsync("9to1.Files.Share.Get", null, token); window.UpdateLayout();
            var edit = Assert.Single(view.GetVisualDescendants().OfType<Button>(), control => control.Name == "files-edit-share-scope");
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await AwaitNativeRoleStateAsync(window, () => Equals(ReadNativeValue(view, "ShareScopeVisible"), true), token);
            Assert.False(view.IsActionAvailable("9to1.Files.Share.ReviewScope")); // No changed scope yet.
            var toggle = Assert.Single(view.GetVisualDescendants().OfType<ToggleSwitch>(), control => control.Name == "files-share-scope-expiry-switch");
            Assert.Equal(true, toggle.IsChecked);
            var role = FilesPresentationControl<ComboBox>(view, "files-share-scope-role");
            var input = FilesPresentationControl<TextBox>(view, "files-share-scope-expiry");
            var originalExpiry = input.Text;
            var expiryField = FilesPresentationControl<Control>(view, "files-share-scope-expiry-field");
            var scopeEditor = FilesPresentationControl<Control>(view, "files-share-scope-editor");
            var sharing = FilesPresentationControl<Control>(view, "files-sharing");
            void RequireReturnedExpiryField()
            {
                Assert.Same(input, FilesPresentationControl<TextBox>(view, "files-share-scope-expiry"));
                Assert.Same(expiryField, FilesPresentationControl<Control>(view, "files-share-scope-expiry-field"));
                Assert.True(expiryField.IsVisible);
                Assert.True(input.IsEffectivelyVisible);
                Assert.Contains(input.GetVisualAncestors(), ancestor => ReferenceEquals(ancestor, view));
                input.BringIntoView(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                RequireOriginalDetailsScrollPresenter(view, input);
                Assert.True(input.Bounds.Width > 0 && input.Bounds.Height > 0, FilesPresentationGeometry(view, input));
                var point = input.TranslatePoint(new Avalonia.Point(input.Bounds.Width / 2, input.Bounds.Height / 2), window);
                Assert.NotNull(point);
                Assert.InRange(point.Value.X, 0, window.ClientSize.Width - 1);
                Assert.InRange(point.Value.Y, 0, window.ClientSize.Height - 1);
            }
            void ClickExpirySwitch()
            {
                // The admitted action can materialise the editor during the previous
                // dispatcher pump. Arrange that real control before requesting its scroll.
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                RequireOriginalDetailsScrollPresenter(view, toggle);
                var beforeScroll = FilesPresentationGeometry(view, toggle);
                toggle.BringIntoView(); window.UpdateLayout();
                var point = toggle.TranslatePoint(new Avalonia.Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window);
                Assert.NotNull(point);
                Assert.InRange(point.Value.X, 0, window.ClientSize.Width - 1);
                Assert.True(point.Value.Y >= 0 && point.Value.Y <= window.ClientSize.Height - 1,
                    "Before BringIntoView: " + beforeScroll + "\nAfter BringIntoView: " + FilesPresentationGeometry(view, toggle));
                window.MouseMove(point.Value); window.MouseDown(point.Value, MouseButton.Left); window.MouseUp(point.Value, MouseButton.Left);
            }
            ClickExpirySwitch();
            await AwaitNativeRoleStateAsync(window, () => Equals(ReadNativeValue(view, "ShareScopeExpiryEnabled"), false), token);
            Assert.Equal(false, toggle.IsChecked);
            // CUI retains authored controls when their section is hidden. A detached
            // input's own effective flag is not evidence that it is on this page.
            Assert.False(expiryField.IsVisible);
            Assert.DoesNotContain(input.GetVisualAncestors(), ancestor => ReferenceEquals(ancestor, view));
            ClickExpirySwitch();
            await AwaitNativeRoleStateAsync(window, () => Equals(ReadNativeValue(view, "ShareScopeExpiryEnabled"), true), token);
            Assert.Equal(true, toggle.IsChecked);
            RequireReturnedExpiryField(); Assert.Equal(originalExpiry, input.Text);
            input.Text = "invalid UTC date";
            Assert.False(view.IsActionAvailable("9to1.Files.Share.ReviewScope"));
            var utc = DateTimeOffset.UtcNow.AddHours(2).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var expected = DateTimeOffset.ParseExact(utc, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            input.Text = utc;
            await AwaitNativeRoleStateAsync(window, () => view.IsActionAvailable("9to1.Files.Share.ReviewScope") == true, token);
            FilesPresentationControl<Button>(view, "files-cancel-share-scope").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await AwaitNativeRoleStateAsync(window, () => Equals(ReadNativeValue(view, "ShareScopeVisible"), false), token);
            Assert.False(scopeEditor.IsVisible);
            Assert.DoesNotContain(input.GetVisualAncestors(), ancestor => ReferenceEquals(ancestor, view));
            FilesPresentationControl<Button>(view, "files-close-access").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await AwaitNativeRoleStateAsync(window, () => Equals(ReadNativeValue(view, "SharingVisible"), false), token);
            Assert.False(sharing.IsVisible);
            Assert.DoesNotContain(role.GetVisualAncestors(), ancestor => ReferenceEquals(ancestor, view));
            await view.DispatchAsync("9to1.Files.Share.Get", null, token);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            FilesPresentationControl<Button>(view, "files-edit-share-scope").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await AwaitNativeRoleStateAsync(window, () => Equals(ReadNativeValue(view, "ShareScopeVisible"), true), token);
            Assert.Same(role, FilesPresentationControl<ComboBox>(view, "files-share-scope-role"));
            Assert.Same(scopeEditor, FilesPresentationControl<Control>(view, "files-share-scope-editor"));
            Assert.Same(sharing, FilesPresentationControl<Control>(view, "files-sharing"));
            RequireReturnedExpiryField();
            Assert.Equal(originalExpiry, input.Text); // Cancel restored the original grant; it did not apply the draft.
            Assert.Empty((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            input.Text = utc;
            await AwaitNativeRoleStateAsync(window, () => view.IsActionAvailable("9to1.Files.Share.ReviewScope") == true, token);
            await CaptureComponentSceneAsync(window, "files-existing-recipient-scope-editor");
            Assert.Single(view.GetVisualDescendants().OfType<Button>(), control => control.Name == "files-review-share-scope")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await AwaitNativeRoleStateAsync(window, () => view.IsActionAvailable("9to1.Files.Apply") == true, token);
            var review = Assert.Single((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            Assert.Equal("9to1.Files.Share.Grant", review.Scope.ActionName);
            Assert.True((await h.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            await view.DispatchAsync("9to1.Files.Apply", null, token);
            var page = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
            var row = page.Items.Single(item => item.Id == seed.Item.Id);
            var shares = await h.Browser.ReadNativeSharesAsync(page, row, h.Workspace.Actor, () => true, token: token);
            var active = Assert.Single(shares.Grants, grant => !grant.Share.IsRevoked);
            Assert.Equal(seed.Grant.Share.PrincipalId, active.Share.PrincipalId); Assert.Equal(seed.Grant.RecipientEmail, active.RecipientEmail);
            Assert.Equal(FilesAccessRole.Viewer, active.Share.Role); Assert.Equal(expected, active.Share.ExpiresAt);
            Assert.True(view.CanPrepareOriginalClose());
        }
        finally { await view.CloseAndDrainAsync(); window.Close(); }
    }

    private static object? ReadNativeValue(FilesNativeBrowserSurface view, string name)
    { Assert.True(view.TryGetValue(name, out var value)); return value; }

    private static Button NativeRoleButton(FilesNativeBrowserSurface view) =>
        Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => button.IsEnabled
            && AutomationProperties.GetName(button) == "Review changing this existing recipient’s access role in Home");

    private static async Task AwaitNativeRoleStateAsync(Window window, Func<bool> ready, CancellationToken token)
    {
        for (var i = 0; i < 1000; i++)
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            if (ready()) return;
            await Task.Delay(5, token);
        }
        Assert.True(ready(), "The actual canonical CUI action did not settle within the bounded UI pump.");
    }

    /// <summary>The initial recipient is an explicitly synthetic trusted fixture seed, not account resolution.
    /// All subsequent role review/claim/commit/audit runs the maintained genuine Home/Files graph.</summary>
    private static async Task<(FilesNativeBrowserPage Page, HostedItemMetadata Item,
        FilesNativeBrowserSharePage Shares, FilesEmailShare Grant)> SeedOriginalNativeShareAsync(
        Harness h, FilesAccessRole role, CancellationToken token)
    {
        var id = await CreateReviewedFolder(h, h.Parent.Value, "Existing shared folder", token);
        var page = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        var item = page.Items.Single(row => row.Id == id);
        var check = await h.Browser.CaptureOriginalPageReadCheckAsync(page, h.Workspace.Actor, () => true, token);
        var seed = new FilesEmailShareRequest(page.StoreID, item.Id, item.CurrentRevisionId!.Value,
            "fixture-recipient-" + Guid.NewGuid().ToString("N"), "existing-recipient@example.test", role,
            DateTimeOffset.UtcNow.AddDays(1), new(Guid.NewGuid()));
        var granted = await h.Workspace.Provider.GrantEmailAccessAsync(seed,
            new FilesCommitAuthorityGuard(h.Workspace.Actor.ActorId, check), token);
        Assert.True(granted.IsSuccess);
        page = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        item = page.Items.Single(row => row.Id == id);
        var shares = await h.Browser.ReadNativeSharesAsync(page, item, h.Workspace.Actor, () => true, token: token);
        return (page, item, shares, Assert.Single(shares.Grants));
    }

    private static async Task CaptureComponentSceneAsync(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        for (var frameIndex = 0; frameIndex < 30; frameIndex++)
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, name + ".png"));
    }

    private sealed class PersistedObserver : IHomePersistedWriteObserver
    {
        internal Func<string, long, CancellationToken, Task>? BeforeAcknowledgement;
        public Task OnPersistedWriteAsync(string id, long revision, CancellationToken token) =>
            BeforeAcknowledgement?.Invoke(id, revision, token) ?? Task.CompletedTask;
    }

    private sealed class Readiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "CONTROL", "Source test host")); }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required string Root { get; init; }
        public required ServiceProvider Graph { get; init; }
        public required HomePermissionTrustService Permissions { get; init; }
        public required FileHomeCoreStateStore Home { get; init; }
        public required NativeFilesWorkspace Workspace { get; init; }
        public required FilesNativeBrowserService Browser { get; init; }
        public HostedItemId Parent => Workspace.Configuration.AppFolders["write"];
        public static async Task<Harness> CreateAsync(CancellationToken token, PersistedObserver? observer = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-files-browser-mutation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var home = observer is null ? new FileHomeCoreStateStore(Path.Combine(root, "home.json"))
                    : new FileHomeCoreStateStore(Path.Combine(root, "home.json"), observer);
                var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var policy = new FilesNativeBrowserActionPolicySource();
                var permissions = new HomePermissionTrustService(home, policy.TryGet);
                var services = new ServiceCollection();
                services.AddSingleton<IHomeCoreStateStore>(home); services.AddSingleton(profiles);
                services.AddSingleton<IAuthenticatedResourceActorSource>(profiles); services.AddSingleton(permissions);
                services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
                services.AddSingleton<HomeLocalStoreOwnership>();
                services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
                services.AddSingleton<ResourceAuthorizationService>(); services.AddSingleton<HomeResourceOperationBroker>();
                services.AddFilesNativeHost();
                var graph = services.BuildServiceProvider();
                try
                {
                    var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
                    await graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen,
                        graph.GetRequiredService<HomeLocalStoreOwnership>(), token);
                    var workspace = await graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(token)
                        ?? throw new InvalidOperationException("The actual Files setup did not produce its owned workspace.");
                    return new() { Root = root, Graph = graph, Permissions = permissions, Home = home, Workspace = workspace,
                        Browser = graph.GetRequiredService<FilesNativeBrowserService>() };
                }
                catch { await graph.DisposeAsync(); throw; }
            }
            catch { Directory.Delete(root, true); throw; }
        }
        public async ValueTask DisposeAsync() { await Graph.DisposeAsync(); Directory.Delete(Root, true); }
    }
}
