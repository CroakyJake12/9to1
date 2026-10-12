using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Infrastructure;
using Haven.UI;
using NineToOne.Web;
using NineToOne.Web.Productivity.Present;

internal static class PresentRenameChecks
{
    internal static async Task<int> Run(IAppPaths paths)
    {
        var count = 0;
        void Check(bool pass, string message)
        { if (!pass) throw new InvalidOperationException(message); count++; Console.WriteLine("ASSERT " + message); }
        var model = PresentDocument.Create("Native rename and zoom");
        var owner = new PresentEditor(model);
        owner.SetSlideTitle(model.Slides[0].Id, "First review");
        var title = new PresentElement { Kind = PresentElementKind.Text, Role = PresentElementRoles.Title,
            Text = "Real authored heading", X = .15, Y = .2, Width = .6, Height = .15 };
        model.Slides[0].Elements.Add(title);
        var second = owner.AddSlide(); owner.SetSlideTitle(second.Id, "Second review");
        var originalContent = JsonSerializer.Serialize(model.Slides.Select(slide => slide.Elements));
        var repository = new PresentRepository(paths);
        await repository.SaveAsync(model, "Native rename fixture", default);
        await using var session = new PresentBrowserSession(repository);
        Check((await session.OpenAsync(model.Id)).Succeeded, "rename fixture opens through the original repository/session");
        var surface = PresentBrowserSurface.Create(new CuiRichParser().Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Present.cui"))), session, () => true);
        using var loader = new CuiControlLoader(surface.ControlRegistry!);
        loader.SetBindingContext(session); loader.SetActionDispatcher(session);
        var (root, diagnostics) = loader.TryLoad(surface.Document);
        Check(root is not null && !diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error), "rename test loads canonical CUI controls");
        loader.WireBindings(root!);
        var window = new Window { Width = 1200, Height = 900, Content = root }; window.Show();
        try
        {
            var ui = new NativeUi(session, loader, root!, window, Check); ui.Flush();
            await ui.OpenRename(1);
            Check(session.Editor!.SelectedSlide.Id == second.Id, "right-click rename selects the exact clicked slide owner before opening the dialog");
            var input = ui.NameInput();
            Check(input.IsFocused && input.SelectedText == "Second review", "rename opens with the current name focused and selected");
            ui.ReplaceName("Review board"); ui.Tap(Key.Home); ui.Tap(Key.Right); window.KeyTextInput("X"); ui.Flush();
            Check(input.Text == "RXeview board" && input.CaretIndex == 2 && ReferenceEquals(input, ui.NameInput()) && input.IsFocused,
                "character-by-character rename and middle insertion retain the actual input, focus and caret");
            Check(!session.IsDirty && session.Editor.SelectedSlide.Title == "Second review" && !session.Editor.CanUndo,
                "dialog typing does not mutate document metadata or undo history before commit");
            ui.Frame(paths, "rename-draft"); await ui.EnterAndWait();
            Check(!session.IsRenamingSlide && session.Editor.SelectedSlide.Title == "RXeview board" && session.Editor.CanUndo,
                "physical Enter commits the name and dismisses the actual rename dialog");
            ui.Tap(Key.Home);
            Check(session.Editor.SelectedSlide.Id == model.Slides[0].Id, "after rename commit keyboard focus returns to the original thumbnail navigator");
            ui.Tap(Key.End);
            await ui.ClickButton("Undo");
            Check(session.Editor.Document.Slides[1].Title == "Second review" && !session.Editor.CanUndo,
                "one physical Undo reverses exactly the single committed rename transaction");
            await ui.ClickButton("Redo");
            Check(session.Editor.Document.Slides[1].Title == "RXeview board", "physical Redo restores the exact committed slide name");
            var committed = JsonSerializer.Serialize(session.GetDocumentSnapshot());
            await ui.OpenRename(1); ui.ReplaceName("Uncommitted draft"); ui.Tap(Key.Escape); await ui.Idle();
            Check(!session.IsRenamingSlide && JsonSerializer.Serialize(session.GetDocumentSnapshot()) == committed,
                "physical Escape cancels rename without changing metadata, content or dirty draft");
            ui.Tap(Key.Home); Check(session.Editor.SelectedSlide.Id == model.Slides[0].Id, "Escape restores rail keyboard focus");
            await ui.OpenRename(1); ui.ReplaceName("   ");
            Check(!ui.Button("Rename").IsEnabled && session.IsActionAvailable("CommitRenameSlide") == false,
                "blank slide names disable the actual commit button and original owner action");
            ui.Tap(Key.Enter); await ui.Idle();
            Check(session.IsRenamingSlide && session.Editor.SelectedSlide.Title == "RXeview board", "Enter on a blank name leaves the dialog open and original metadata unchanged");
            await ui.ClickButton("Cancel");
            Check(!session.IsRenamingSlide && JsonSerializer.Serialize(session.GetDocumentSnapshot()) == committed,
                "physical Cancel discards a blank draft without modifying the saved-content owner");
            Check(JsonSerializer.Serialize(session.Editor.Document.Slides.Select(slide => slide.Elements)) == originalContent,
                "rename, cancellation and undo/redo preserve all authored object IDs, content, styles and geometry");
            await ui.ClickButton("Save");
            Check(!session.IsDirty && (await repository.LoadAsync(model.Id, default))!.Slides[1].Title == "RXeview board",
                "physical Save persists the metadata through the original repository");
            await ui.ClickButton("Close", "Close");
            await ui.ClickButton(model.Title);
            Check(session.DocumentId == model.Id && session.Editor!.Document.Slides[1].Id == second.Id
                && session.Editor.Document.Slides[1].Title == "RXeview board"
                && JsonSerializer.Serialize(session.Editor.Document.Slides.Select(slide => slide.Elements)) == originalContent,
                "physical Close and recent-file reopen retain stable slide/object identities and independent metadata/content");
            await CheckFooter(session, ui, root!, window, Check, paths);
            await session.CloseAsync(); await session.OpenAsync(model.Id, readOnly: true); ui.Flush();
            Check(session.IsActionAvailable("RenameSlide") == false && !session.TrySetValue("RenameSlideName", "Forbidden"),
                "read-only presentations reject rename actions and stale dialog bindings");
            ui.Frame(paths, "rename-reopened-readonly");
        }
        finally { window.Close(); }
        return count;
    }

    private static async Task CheckFooter(PresentBrowserSession session, NativeUi ui, Control root, Window window, Action<bool, string> check, IAppPaths paths)
    {
        var before = JsonSerializer.Serialize(session.GetDocumentSnapshot());
        var slider = root.GetVisualDescendants().OfType<Slider>().Single(control => AutomationProperties.GetName(control) == "Zoom percentage");
        var readout = root.GetVisualDescendants().OfType<TextBlock>().Single(control => AutomationProperties.GetName(control) == "Current zoom");
        var control = root.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single();
        void Readout(string stage)
        {
            ui.Flush();
            var bridge = new BrowserAccessibilityBridge(); bridge.Bind(root);
            using var snapshot = JsonDocument.Parse(bridge.ReadSnapshot());
            var range = snapshot.RootElement.GetProperty("elements").EnumerateArray().Single(element => element.GetProperty("name").GetString() == "Zoom percentage");
            check(slider.Maximum == 400 && range.GetProperty("maximum").GetDouble() == 400,
                "native and accessible slider preserve the authored 400 percent maximum " + stage);
            check(readout.Text == $"{control.EffectiveZoom * 100:0}%" && Math.Abs(slider.Value - control.EffectiveZoom * 100) < .01,
                "footer percentage and slider reflect actual rendered slide scale " + stage);
        }
        ui.Flush(); Readout("in initial Fit");
        check(session.IsFitToWindow && slider.IsEffectivelyVisible && slider.IsEnabled && slider.Bounds.Width >= 64,
            "footer exposes an enabled accessible zoom slider in initial Fit mode");
        var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
        var start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
        var oldZoom = control.EffectiveZoom;
        window.MouseDown(start, MouseButton.Left); window.MouseMove(new Point(start.X + 25, start.Y)); window.MouseUp(new Point(start.X + 25, start.Y), MouseButton.Left); ui.Flush();
        check(!session.IsFitToWindow && session.CanvasZoom > oldZoom && Math.Abs(session.CanvasZoom - control.EffectiveZoom) < .001,
            "physical footer thumb drag changes authoritative slide zoom and exits Fit");
        Readout("after physical slider drag");
        // Pointer interaction establishes the real slider keyboard focus; End must
        // reach its authored endpoint after rename/save/reopen notifications.
        ui.Tap(Key.End);
        var maximumScene = control.GetVisualDescendants().OfType<HavenSceneControl>().Single();
        var maximumPaper = (HavenRect)maximumScene.Root!.GetType().GetMethod("SlideRectLocal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(maximumScene.Root, null)!;
        check(!session.IsFitToWindow && session.CanvasZoom == 4 && control.EffectiveZoom == 4 && slider.Value == 400
            && Math.Abs(maximumPaper.Width - 5120) < .1,
            "physical slider End reaches genuine 400 percent slide pixels after rename, save and reopen");
        Readout("at the physical 400 percent endpoint");
        oldZoom = session.CanvasZoom; await ui.ClickButton("Zoom out");
        check(session.CanvasZoom < oldZoom, "physical footer minus reduces actual manual zoom"); Readout("after minus");
        oldZoom = session.CanvasZoom; await ui.ClickButton("Zoom in");
        check(session.CanvasZoom > oldZoom, "physical footer plus increases actual manual zoom"); Readout("after plus");
        await ui.ClickButton("Fit slide");
        check(session.IsFitToWindow, "physical footer Fit restores automatic viewport measurement");
        foreach (var width in new[] { 800d, 390d, 1200d })
        {
            window.Width = width; window.Height = width == 390 ? 844 : 700; ui.Flush(); Readout("after resize to " + width);
            var scene = control.GetVisualDescendants().OfType<HavenSceneControl>().Single(); var canvas = scene.Root!;
            var paper = (HavenRect)canvas.GetType().GetMethod("SlideRectLocal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, null)!;
            check(Math.Abs(canvas.Bounds.Width - scene.Bounds.Width) < .01 && Math.Abs(canvas.Bounds.Height - scene.Bounds.Height) < .01
                && paper.X >= 0 && paper.Y >= 0 && paper.Right <= scene.Bounds.Width + .1 && paper.Bottom <= scene.Bounds.Height + .1
                && Math.Abs(paper.Width / paper.Height - 16d / 9) < .001,
                "reopened slide is wholly contained in the real measured editing stage at width " + width);
            foreach (var footerControl in new Control[] { slider, readout, ui.Button("Zoom out"), ui.Button("Zoom in"), ui.Button("Fit slide") })
            {
                var point = footerControl.TranslatePoint(default, window)!.Value;
                check(point.X >= 0 && point.X + footerControl.Bounds.Width <= window.ClientSize.Width + .1
                    && point.Y >= 0 && point.Y + footerControl.Bounds.Height <= window.ClientSize.Height + .1,
                    "footer " + AutomationProperties.GetName(footerControl) + " remains within width " + width);
            }
            ui.Frame(paths, "rename-zoom-fit-" + width);
        }
        check(JsonSerializer.Serialize(session.GetDocumentSnapshot()) == before && !session.IsDirty,
            "slider, plus, minus, Fit and resize preserve document data and saved state");
    }

    internal sealed class NativeUi(PresentBrowserSession session, CuiControlLoader loader, Control root, Window window, Action<bool, string> check)
    {
        internal void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using var frame = window.CaptureRenderedFrame(); }
        internal async Task Idle() { await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(5)); Flush(); }
        internal void Tap(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        { var physical = key switch { Key.Enter => PhysicalKey.Enter, Key.Right => PhysicalKey.ArrowRight, Key.Left => PhysicalKey.ArrowLeft, _ => Enum.Parse<PhysicalKey>(key.ToString()) }; window.KeyPress(key, modifiers, physical, key.ToString()); window.KeyRelease(key, modifiers, physical, key.ToString()); Flush(); }
        internal IEnumerable<Control> Authored()
        {
            var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance); var pending = new Stack<Control>(); pending.Push(root);
            while (pending.TryPop(out var current))
            {
                if (!seen.Add(current)) continue; yield return current;
                foreach (var child in current.GetLogicalChildren().OfType<Control>()) pending.Push(child);
                if (current is CuiContainerControl container) foreach (var child in container.AuthoredChildren) pending.Push(child);
                if (current is ItemsControl items) foreach (var child in items.Items.OfType<Control>()) pending.Push(child);
                if (current.ContextMenu is { } menu) pending.Push(menu);
            }
        }
        internal Button Button(string name, string? tag = null) => Authored().OfType<Button>().First(button => button.IsEffectivelyVisible
            && TopLevel.GetTopLevel(button) is not null && (Equals(button.Content, name) || AutomationProperties.GetName(button) == name
                || AutomationProperties.GetName(button)?.StartsWith(name + " (", StringComparison.Ordinal) == true)
            && (tag is null || Equals(button.Tag, tag)));
        private void PointerClick(Control control)
        {
            control.BringIntoView(); Flush(); var target = TopLevel.GetTopLevel(control)!;
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), target)!.Value;
            var hit = target.InputHitTest(point) as Visual;
            check(control.IsEnabled && (ReferenceEquals(hit, control) || hit?.GetVisualAncestors().Contains(control) == true),
                "physical pointer targets the real " + (AutomationProperties.GetName(control) ?? (control as MenuItem)?.Header?.ToString() ?? (control as Button)?.Content?.ToString()) + " control");
            target.MouseDown(point, MouseButton.Left); target.MouseUp(point, MouseButton.Left); Flush();
        }
        internal async Task ClickButton(string name, string? tag = null)
        {
            var button = Authored().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyVisible
                && TopLevel.GetTopLevel(button) is not null && (Equals(button.Content, name) || AutomationProperties.GetName(button) == name
                    || AutomationProperties.GetName(button)?.StartsWith(name + " (", StringComparison.Ordinal) == true)
                && (tag is null || Equals(button.Tag, tag)));
            if (button is not null) PointerClick(button);
            else
            {
                var command = Authored().OfType<MenuItem>().Where(item => Equals(item.Header, name))
                    .OrderBy(item => item.GetLogicalAncestors().OfType<Menu>().Any(menu => menu.Name == "present-menu-bar") ? 0 : 1).First();
                var parents = new List<MenuItem>(); Control child = command;
                while (Authored().OfType<MenuItem>().FirstOrDefault(item => item.Items.Contains(child)) is { } parent)
                { parents.Add(parent); child = parent; }
                foreach (var parent in parents.AsEnumerable().Reverse())
                {
                    if (parent.IsSubMenuOpen) continue;
                    PointerClick(parent); await Task.Delay(250); Flush();
                    check(parent.IsSubMenuOpen, "physical pointer opens the real parent menu for " + name);
                }
                PointerClick(command);
            }
            await Idle();
        }
        internal async Task OpenRename(int index)
        {
            Flush(); var bridge = new BrowserAccessibilityBridge(); bridge.Bind(root);
            using var snapshot = JsonDocument.Parse(bridge.ReadSnapshot());
            var slideId = session.Editor!.Document.Slides[index].Id;
            var item = snapshot.RootElement.GetProperty("elements").EnumerateArray().Single(element => element.GetProperty("automationId").GetString() == "Present.Rail." + slideId.ToString("N"));
            var rect = item.GetProperty("bounds"); var point = new Point(rect.GetProperty("x").GetDouble() + rect.GetProperty("width").GetDouble() / 2,
                rect.GetProperty("y").GetDouble() + rect.GetProperty("height").GetDouble() / 2);
            window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); Flush();
            var menu = Authored().OfType<ContextMenu>().Single(menu => menu.IsOpen);
            var rename = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Rename slide"));
            PointerClick(rename); await Idle();
            check(session.IsRenamingSlide && NameInput().IsEffectivelyVisible, "physical thumbnail context menu opens the real rename dialog");
        }
        internal TextBox NameInput() => root.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Slide name" && box.IsEffectivelyVisible);
        internal void ReplaceName(string name)
        {
            var input = NameInput(); Tap(Key.A, RawInputModifiers.Control);
            foreach (var character in name) { window.KeyTextInput(character.ToString()); Flush(); }
            check(input.Text == name && input.IsFocused && input.CaretIndex == name.Length, "physical rename typing preserves focus, caret and all entered characters");
        }
        internal async Task EnterAndWait() { Tap(Key.Enter); await Idle(); }
        internal void Frame(IAppPaths paths, string name) { Flush(); using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(paths.DataDirectory, name + ".png")); }
    }
}
