#if ASTRA_FORMS_NATIVE_PROBE
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core.Forms;
using Haven.Core.Mathematics;
using Haven.Desktop.Mathematics;
using Haven.Desktop.Views.Pages.Forms;
using Haven.Desktop.Views.Shell;
using HavenOS.Forms;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Validation;

/// <summary>Validation-only normal App/classic Forms consumer observer. All accounts,
/// forms and responses are synthetic within the original producer's fresh private profile.
/// The owning caller must independently prove original native/process/profile custody and drains.</summary>
internal static class FormsMathematicsNativeActivationProbe
{
    private static Task? observation;

    [STAThread]
    internal static int Main(string[] args)
    {
        if (args.Length != 0) throw new ArgumentException("The isolated Forms observer takes no product arguments.");
        var profile = NativeProbeProfileWitness.RequireFreshOriginalProducerProfile();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var exit = Program.BuildAvaloniaApp().AfterSetup(_ =>
            Dispatcher.UIThread.Post(() => observation = ObserveAsync(deadline.Token, profile), DispatcherPriority.Background))
            .StartWithClassicDesktopLifetime([]);
        // Never block a continuation after the actual classic dispatcher has stopped.
        List<Exception> failures = [];
        try
        {
            if (observation is null) throw new InvalidOperationException("The original Forms observer never started.");
            if (!observation.IsCompleted) throw new InvalidOperationException("Classic lifetime returned before its original Forms observer settled; outer original drains remain mandatory.");
            observation.GetAwaiter().GetResult();
        }
        catch (Exception error) { failures.Add(error); }
        try { profile.RequireCurrentOriginalProducerProfile(); }
        catch (Exception error) { failures.Add(error); }
        try { NativeProbeOriginalDesktopShutdown.RequireSettledOriginalTask(); }
        catch (Exception error) { failures.Add(error); }
        if (exit != 0) failures.Add(new InvalidOperationException("The original native lifetime returned a nonzero exit."));
        if (failures.Count != 0) throw new AggregateException("Original Forms observation or shutdown failed.", failures);
        Console.WriteLine("ASTRA_FORMS_ORIGINAL_CLASSIC_MATH_EDIT_SAVE_REOPEN_MARK_OBSERVATION_AND_EXIT_TASK_SETTLED_DRAINS_REQUIRED");
        return 0;
    }

    private static async Task ObserveAsync(CancellationToken originalToken, NativeProbeProfileWitness profile)
    {
        Dispatcher.UIThread.VerifyAccess();
        var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var window = desktop?.MainWindow;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(originalToken);
        var token = lifetime.Token;
        var allowClose = false;
        Exception? closingFailure = null;
        void DeferClose(object? sender, WindowClosingEventArgs args)
        {
            if (allowClose) return;
            args.Cancel = true;
            try { lifetime.Cancel(); }
            catch (Exception error) { closingFailure = closingFailure is null ? error : new AggregateException(closingFailure, error); }
        }
        if (window is not null) window.Closing += DeferClose;
        Task? originalHostAction = null, originalResponseAction = null;
        FormHostSession? session = null;
        Exception? primary = null;
        try
        {
            token.ThrowIfCancellationRequested();
            Require(Avalonia.Application.Current is App && desktop is not null && window is not null, "Actual App/classic MainWindow is unavailable.");
            var owner = window!;
            var services = App.Services ?? throw new InvalidOperationException("Actual App DI was not initialized.");
            var paths = services.GetRequiredService<IAppPaths>();
            profile.RequireInitializedAppDataDirectory(paths.DataDirectory);
            Require(owner.IsVisible && owner.IsEffectivelyVisible && owner.DataContext is MainView { IsDisposed: false }, "Actual MainWindow/MainView is not visible and current.");
            var shell = (MainView)owner.DataContext!;
            var actors = services.GetRequiredService<IAuthenticatedResourceActorSource>();
            var actor = await actors.GetCurrentAsync(token) ?? throw new UnauthorizedAccessException("Actual local OS actor is unavailable.");
            var provider = services.GetRequiredService<IFormNativeMathematicsProvider>();
            Require(provider is FormNativeMathematicsProvider, "Normal App did not register the shared native mathematics provider.");
            Require(services.GetRequiredService<IFormProjectPublicationValidator>() is FormNativePublicationValidator,
                "Normal App did not register the actual Forms publication validator.");
            var settings = services.GetRequiredService<IVersionedSettingsStore>();
            var identities = settings as IResourceStoreIdentitySource ?? throw new InvalidOperationException("Actual Forms settings store identity is unavailable.");
            var store = await identities.GetStoreIdentityAsync(token);
            var publications = services.GetRequiredService<FormPublicationService>();
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Original native mathematics validation", FormModeKind.Form, now);
            var number = new FormField(Guid.NewGuid(), FormFieldKind.Mathematical, "Number", null,
                JsonSerializer.SerializeToElement(new { }), true, new(), Mathematics: new(Guid.NewGuid(), 1, "x"));
            var question = new GraphDefinition(Guid.NewGuid(), 1, new(-10, 10, -10, 10), [], [], [GraphResponseTool.PlacePoint]);
            var target = new GraphPoint(Guid.NewGuid(), new(2, 3));
            var point = new FormField(Guid.NewGuid(), FormFieldKind.Graph, "Place the point", null,
                JsonSerializer.SerializeToElement(new { }), true, new(), Graph: question,
                Assessment: new(3, 1, [], Graph: new(Guid.NewGuid(), target.PrimitiveID),
                    ExpectedGraph: question with { GraphID = Guid.NewGuid(), Revision = 1, Primitives = [target] }));
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, number, now);
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, point, now);
            Require(provider.Supports(number) && provider.Supports(point), "Shared native provider refused configured question tools.");
            Require((await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project), token)).Code == "PermissionDenied",
                "Fresh normal Forms store granted publication before actual local ownership binding.");
            await services.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync("forms", store.StoreId.ToString("D"), token);
            session = await publications.OpenHostSessionAsync(actor, token);
            Require((await session.Publications.CreateAsync(project.FormID, FormProjectEditor.Project(project), token)).Success,
                "Actual owner-issued Forms session could not create the synthetic project.");
            await RequireOriginalAsync();
            await shell.OpenFormsAsync(token, project.FormID);
            owner.UpdateLayout();
            var host = owner.GetVisualDescendants().OfType<FormsNativeWorkspaceHost>().Single(x => x.IsEffectivelyVisible);
            Require(TopLevel.GetTopLevel(host) == owner && host.Bounds.Width > 0 && host.Bounds.Height > 0,
                "Actual normal Forms tab is not visibly mounted.");
            Require(host.Content is Control { DataContext: FormsCuiWorkspace workspace } && workspace.FormID == project.FormID,
                "Actual embedded Forms workspace did not open the selected canonical form.");
            using (var embedded = typeof(FormsCuiWorkspace).Assembly.GetManifestResourceStream("HavenOS.Forms.UI.FormsWorkspace.cui")
                ?? throw new InvalidDataException("Actual embedded Forms CUI is unavailable."))
            {
                Require(Convert.ToHexString(await SHA256.HashDataAsync(embedded, token)).ToLowerInvariant() ==
                    "7e086acd20e054f5ae61d59ebcfff3f47f6ca50f2b5943ac51d926cbca5baaa8", "Actual embedded Forms document differs from its reviewed source.");
            }

            // Enter the actual authored edit button; its original CUI action stays pending through the child dialog.
            ClickName(host, "forms-edit-mathematics"); originalHostAction = host.WhenActionsIdleAsync();
            var editing = await DialogAsync(owner, originalHostAction, token);
            var editor = editing.GetVisualDescendants().OfType<FormMathematicsAuthoringControl>().Single();
            var math = editing.GetVisualDescendants().OfType<SharedMathEditorControl>().Single();
            Require(!originalHostAction.IsCompleted, "Native question dialog ended its original action before editing.");
            ClickName(math, "math-mode"); await math.WhenActionsIdleAsync().WaitAsync(token);
            Text(math, "math-source").Text = "\\frac{3}{2}";
            ClickName(math, "math-apply-source"); await math.WhenActionsIdleAsync().WaitAsync(token);
            Require(math.Snapshot.LastValid.ExpressionID == number.Mathematics!.ExpressionID && math.Snapshot.LastValid.LaTeX.Contains("\\frac", StringComparison.Ordinal),
                "Actual shared editor did not retain expression identity and authored source.");
            editing.GetVisualDescendants().OfType<CheckBox>().Single(x => x.Name == "forms-math-graded").IsChecked = true;
            Text(editing, "forms-math-expected-number").Text = "1.5";
            Text(editing, "forms-math-required-units").Text = "kg";
            Text(editing, "forms-math-points").Text = "2";
            ClickName(editing, "forms-math-save"); await editor.WhenActionsIdleAsync().WaitAsync(token);
            await originalHostAction.WaitAsync(token); await RequireOriginalAsync();
            var authored = await session.Publications.ReadAsync(project.FormID, token);
            Require(authored.Success, "Actual native authoring save was not durable.");
            var savedProject = ReadProject(authored.Publication!.Draft);
            var savedNumber = savedProject.Fields.Single(x => x.FieldID == number.FieldID);
            Require(savedNumber.Revision == number.Revision + 1 && savedNumber.Mathematics!.ExpressionID == number.Mathematics.ExpressionID &&
                savedNumber.Mathematics.Revision == number.Mathematics.Revision + 1 && savedNumber.Assessment!.Mathematics!.Expected == 1.5m &&
                savedNumber.Assessment.Mathematics.Units == "kg" && savedNumber.Assessment.MaximumPoints == 2m,
                "Normal factory did not persist canonical question versions and private grading policy.");
            await PhysicalKeyAsync("forms.publication.v1." + project.FormID.ToString("N"));
            ClickLabel(host, "Publish version"); originalHostAction = host.WhenActionsIdleAsync(); await originalHostAction.WaitAsync(token);
            var published = await session.Publications.ReadAsync(project.FormID, token);
            Require(published.Success && published.Publication!.State == FormPublicationState.Published,
                "Actual native Publish button did not pass the registered mathematics publication validator.");
            await RequireOriginalAsync();

            ClickLabel(host, "Open my response"); originalHostAction = host.WhenActionsIdleAsync();
            var responding = await DialogAsync(owner, originalHostAction, token);
            var surface = ResponseSurface(responding);
            var responseID = surface.Response?.ResponseID ?? throw new InvalidOperationException("Actual response surface has no durable response identity.");
            Text(responding, "math-answer-number").Text = "1.50";
            Text(responding, "math-answer-units").Text = "kg";
            var graph = responding.GetVisualDescendants().OfType<SharedGraphEditorControl>().Single();
            responding.UpdateLayout();
            await Task.Delay(50, token);
            var actualPlot = graph.GetVisualDescendants().OfType<ScottPlot.Avalonia.AvaPlot>().Single();
            Require(actualPlot.IsEffectivelyVisible && actualPlot.Bounds.Width > 0 && actualPlot.Bounds.Height > 0 &&
                actualPlot.Plot.LastRender.DataRect.HasArea, "Actual maintained native graph has no rendered coordinate area.");
            Text(graph, "math-graph-x").Text = "1"; Text(graph, "math-graph-y").Text = "1";
            ClickName(graph, "math-place-point"); await graph.WhenActionsIdleAsync().WaitAsync(token);
            var first = graph.LastResponse ?? throw new InvalidOperationException("Actual native point input did not create a canonical response.");
            Text(graph, "math-graph-x").Text = "2"; Text(graph, "math-graph-y").Text = "3";
            ClickName(graph, "math-place-point"); await graph.WhenActionsIdleAsync().WaitAsync(token);
            var final = graph.LastResponse!;
            Require(first.ResponseID == final.ResponseID && final.Revision == first.Revision + 1 &&
                first.Actions.Single().Primitive.PrimitiveID == final.Actions.Single().Primitive.PrimitiveID &&
                final.GraphID == question.GraphID && final.GraphRevision == question.Revision && surface.UnsavedAnswerCount == 2,
                "Actual repeated point edits did not preserve response and question identities.");
            ClickLabel(responding, "Save answers"); originalResponseAction = surface.WhenActionsIdleAsync();
            await originalResponseAction.WaitAsync(token); await RequireOriginalAsync();
            var saved = await session.Responses.ReadSessionAsync(project.FormID, responseID, token);
            Require(saved.Success && surface.UnsavedAnswerCount == 0 && surface.StatusCode is null &&
                saved.Presentation!.Fields.All(x => x.Assessment is null), "Actual saved response or stripped respondent policy is incorrect.");
            var answer = Read<MathAnswer>(saved.Response!.Answers.Single(x => x.FieldID == number.FieldID).Value);
            Require(answer.Value is NumericMathAnswer { Literal: "1.50", Units: "kg" } &&
                MathObjectCodec.Encode(final).SequenceEqual(MathObjectCodec.Encode(Read<GraphResponse>(saved.Response.Answers.Single(x => x.FieldID == point.FieldID).Value))),
                "Actual typed answers differ from the native editor's canonical values.");
            var responseKey = "forms.response-sessions.v1." + project.FormID.ToString("N");
            var beforeInvalid = await PhysicalKeyAsync(responseKey);
            Text(responding, "math-answer-number").Text = "0.12345678901234567890123456789";
            ClickLabel(responding, "Save answers"); originalResponseAction = surface.WhenActionsIdleAsync();
            await originalResponseAction.WaitAsync(token);
            Require(surface.StatusCode == "MathParseError" && surface.UnsavedAnswerCount == 1 &&
                Text(responding, "math-answer-number").Text == "0.12345678901234567890123456789" &&
                beforeInvalid == await PhysicalKeyAsync(responseKey), "Unsupported precision did not preserve the owning durable response and native draft.");
            Text(responding, "math-answer-number").Text = "1.50";
            ClickLabel(responding, "Save answers"); originalResponseAction = surface.WhenActionsIdleAsync();
            await originalResponseAction.WaitAsync(token);
            Require(surface.UnsavedAnswerCount == 0 && surface.StatusCode is null, "Actual repaired draft could not be saved.");
            responding.Close(); await originalHostAction.WaitAsync(token);
            await RequireOriginalAsync();

            // Reopen through the same actual normal workspace, preserving its original durable respondent ID.
            ClickLabel(host, "Open my response"); originalHostAction = host.WhenActionsIdleAsync();
            var reopened = await DialogAsync(owner, originalHostAction, token);
            var resumed = ResponseSurface(reopened);
            Require(resumed.Response!.ResponseID == responseID && Text(reopened, "math-answer-number").Text == "1.50" &&
                Text(reopened, "math-answer-units").Text == "kg", "Actual reopened normal response changed its identity or answer.");
            Require(Read<MathAnswer>(resumed.Response.Answers.Single(x => x.FieldID == number.FieldID).Value).AnswerID == answer.AnswerID,
                "Actual reopened numeric answer changed its canonical identity.");
            var reopenedGraph = reopened.GetVisualDescendants().OfType<SharedGraphEditorControl>().Single();
            Require(MathObjectCodec.Encode(final).SequenceEqual(MathObjectCodec.Encode(reopenedGraph.LastResponse!)), "Actual reopened native graph changed its canonical response.");
            ClickLabel(reopened, "Submit response"); originalResponseAction = resumed.WhenActionsIdleAsync();
            await originalResponseAction.WaitAsync(token); await RequireOriginalAsync();
            Require(resumed.Response!.State == FormResponseState.Submitted && resumed.Response.AwardedPoints == 5m &&
                resumed.Response.ReleasedResults[number.FieldID].Outcome == FormMarkingOutcome.Correct &&
                resumed.Response.ReleasedResults[point.FieldID].Outcome == FormMarkingOutcome.Correct &&
                !Text(reopened, "math-answer-number").IsEffectivelyEnabled &&
                reopened.GetVisualDescendants().OfType<SharedGraphEditorControl>().Single().IsActionAvailable("PlacePoint") == false,
                "Actual submission did not mark both canonical answers and disable retired response editing.");
            await PhysicalKeyAsync(responseKey);
            reopened.Close(); await originalHostAction.WaitAsync(token);
            await RequireOriginalAsync();

            async Task RequireOriginalAsync()
            {
                token.ThrowIfCancellationRequested();
                profile.RequireCurrentOriginalProducerProfile();
                Require(ReferenceEquals(App.Services, services) && !shell.IsDisposed && owner.IsVisible &&
                    await actors.GetCurrentAsync(token) == actor, "Original native App provider/actor/host changed during an awaited operation.");
                await session!.RequireCurrentAsync(token);
            }

            async Task<string> PhysicalKeyAsync(string key)
            {
                var exported = await settings.ExportAsync(token);
                Require(exported.Settings.TryGetValue(key, out var value), "Owning Forms settings key was not exported.");
                using var physical = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(paths.DataDirectory, "settings.json"), token));
                Require(physical.RootElement.GetProperty("Settings").TryGetProperty(key, out var durable) && durable.GetString() == value,
                    "Owning Forms key differs from its actual physically saved settings file.");
                // Other normal startup keys may change concurrently; compare the actual owner key only.
                return value!;
            }
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            List<Exception> cleanup = [];
            if (closingFailure is not null) cleanup.Add(closingFailure);
            try { lifetime.Cancel(); } catch (Exception error) { cleanup.Add(error); }
            try { if (window is not null) foreach (var child in window.OwnedWindows.ToArray()) child.Close(); }
            catch (Exception error) { cleanup.Add(error); }
            // Preserve each original accepted callback task's fault or cancellation. Canceling
            // the observer does not attest the cause of a preexisting original task cancellation.
            // Task completion never substitutes for saved/marked state checked above.
            foreach (var task in new[] { originalResponseAction, originalHostAction }.Where(x => x is not null).Distinct())
            {
                try { await task!; }
                catch (Exception error) { if (!ReferenceEquals(error, primary)) cleanup.Add(error); }
            }
            try { session?.Dispose(); } catch (Exception error) { cleanup.Add(error); }
            try
            {
                allowClose = true;
                if (window is not null) { window.Closing -= DeferClose; window.Close(); }
                else desktop?.Shutdown(1);
            }
            catch (Exception error) { cleanup.Add(error); }
            if (cleanup.Count != 0) throw new AggregateException("Original Forms native cleanup failed.", primary is null ? cleanup : new[] { primary }.Concat(cleanup));
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static TextBox Text(Control root, string name) => root.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == name);
    private static void ClickName(Control root, string name) => Click(root.GetVisualDescendants().OfType<Button>().Single(x => x.Name == name));
    private static void ClickLabel(Control root, string label) => Click(root.GetVisualDescendants().OfType<Button>().Single(x => x.Content is string value && value == label));
    private static void Click(Button button)
    {
        Require(button.IsEffectivelyEnabled, "The actual selected native button is disabled.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task<Window> DialogAsync(Window owner, Task original, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var dialogs = owner.OwnedWindows.Where(x => x.IsVisible).ToArray();
            if (dialogs.Length == 1) { dialogs[0].UpdateLayout(); return dialogs[0]; }
            Require(dialogs.Length == 0, "Actual owning child dialog is ambiguous.");
            if (original.IsCompleted) { await original; throw new InvalidOperationException("Original native action ended without its actual child dialog."); }
            await Task.Delay(25, token);
        }
    }
    private static FormNativeResponseSurface ResponseSurface(Window window) => window.GetVisualDescendants()
        .OfType<Control>().Select(x => x.DataContext).OfType<FormNativeResponseSurface>().Distinct().Single();
    private static FormProject ReadProject(JsonElement value) => FormProjectCodec.Decode(Encoding.UTF8.GetBytes(value.GetRawText()));
    private static T Read<T>(JsonElement value) where T : class => MathObjectCodec.Decode<T>(Encoding.UTF8.GetBytes(value.GetRawText()));
}
#endif
