using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core.Forms;
using Haven.Core.Mathematics;
using Haven.Desktop.Mathematics;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;
using HavenOS.Forms;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class FormsCanonicalMathematicsNativeTests
{
    [AvaloniaFact]
    public async Task Actual_authoring_dialog_edits_shared_expression_and_home_save_reopens_same_typed_question()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-forms-math-author-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Window? owner = null;
        try
        {
            await using var graph = Services(root);
            var settings = graph.GetRequiredService<IVersionedSettingsStore>();
            var identity = await Assert.IsAssignableFrom<IResourceStoreIdentitySource>(graph.GetRequiredService<IVersionedSettingsStore>()).GetStoreIdentityAsync(token);
            var publications = graph.GetRequiredService<FormPublicationService>();
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Math authoring", FormModeKind.Form, now);
            var field = new FormField(Guid.NewGuid(), FormFieldKind.Mathematical, "Calculate", null,
                JsonSerializer.SerializeToElement(new { }), true, new(), Mathematics:new(Guid.NewGuid(), 1, "x"));
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, now);
            Assert.Equal("PermissionDenied", (await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project), token)).Code);
            await graph.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync("forms", identity.StoreId.ToString("D"), token);
            var actor = (await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token))!;
            var session = await publications.OpenHostSessionAsync(actor, token);
            Assert.True((await session.Publications.CreateAsync(project.FormID, FormProjectEditor.Project(project), token)).Success);
            var mounted = await MainView.CreateFormsDocumentWorkspaceAsync(graph, actor, project.FormID,
                () => owner, () => true, token);
            using var host = mounted.Host;
            owner = new Window { Content = host, Width = 1000, Height = 850 }; owner.Show(); Pump(owner);
            var pending = mounted.Workspace.DispatchAsync("9to1.Forms.EditMathematics", null, token).AsTask();
            var dialog = await DialogAsync(owner, pending, token); Pump(dialog);
            Assert.False(pending.IsCompleted);
            var question = Assert.Single(dialog.GetVisualDescendants().OfType<SharedMathEditorControl>());
            Click(question, "math-mode"); await question.WhenActionsIdleAsync(); Pump(dialog);
            Text(question, "math-source").Text = "\\frac{3}{2}";
            Click(question, "math-apply-source"); await question.WhenActionsIdleAsync(); Pump(dialog);
            Assert.Contains("\\frac", question.Snapshot.LastValid.LaTeX);
            Assert.Equal(field.Mathematics!.ExpressionID, question.Snapshot.LastValid.ExpressionID);
            dialog.GetVisualDescendants().OfType<CheckBox>().Single(x => x.Name == "forms-math-graded").IsChecked = true;
            Text(dialog, "forms-math-expected-number").Text = "1.5";
            Text(dialog, "forms-math-required-units").Text = "kg";
            Text(dialog, "forms-math-points").Text = "2";
            Click(dialog, "forms-math-save");
            await pending.WaitAsync(token);
            var loaded = await session.Publications.ReadAsync(project.FormID, token);
            Assert.True(loaded.Success);
            var saved = ReadProject(loaded.Publication!.Draft);
            var savedField = Assert.Single(saved.Fields);
            Assert.Equal(field.FieldID, savedField.FieldID); Assert.Equal(field.Revision + 1, savedField.Revision);
            Assert.Equal(field.Mathematics.ExpressionID, savedField.Mathematics!.ExpressionID);
            Assert.Equal(field.Mathematics.Revision + 1, savedField.Mathematics.Revision);
            Assert.Contains("\\frac", savedField.Mathematics.LaTeX);
            Assert.Equal(1.5m, savedField.Assessment!.Mathematics!.Expected);
            Assert.Equal("kg", savedField.Assessment.Mathematics.Units);
            Assert.Equal(2m, savedField.Assessment.MaximumPoints);
            Assert.NotNull((await settings.ExportAsync(token)).Settings["forms.publication.v1."+project.FormID.ToString("N")]);
            var beforeCancel = await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"), token);
            var cancelled = mounted.Workspace.DispatchAsync("9to1.Forms.EditMathematics", null, token).AsTask();
            var secondDialog = await DialogAsync(owner, cancelled, token); Pump(secondDialog);
            var secondQuestion = Assert.Single(secondDialog.GetVisualDescendants().OfType<SharedMathEditorControl>());
            Assert.Equal(savedField.Mathematics.ExpressionID, secondQuestion.Snapshot.LastValid.ExpressionID);
            Assert.Equal(savedField.Mathematics.Revision, secondQuestion.Snapshot.LastValid.Revision);
            Click(secondQuestion, "math-mode"); await secondQuestion.WhenActionsIdleAsync(); Pump(secondDialog);
            Text(secondQuestion, "math-source").Text = "\\frac{";
            Click(secondQuestion, "math-apply-source"); await secondQuestion.WhenActionsIdleAsync();
            Assert.NotNull(secondQuestion.Snapshot.Diagnostic);
            Assert.Equal(savedField.Mathematics, secondQuestion.Snapshot.LastValid);
            Click(secondQuestion, "math-restore"); await secondQuestion.WhenActionsIdleAsync();
            var oversized = new string('x', new MathServiceLimits().MaxSourceCharacters + 1);
            Text(secondQuestion, "math-source").Text = oversized;
            Click(secondQuestion, "math-apply-source"); await secondQuestion.WhenActionsIdleAsync();
            Assert.Equal(oversized, Text(secondQuestion, "math-source").Text);
            Assert.Equal(savedField.Mathematics, secondQuestion.Snapshot.LastValid);
            Click(secondDialog, "forms-math-save");
            await Assert.Single(secondDialog.GetVisualDescendants().OfType<FormMathematicsAuthoringControl>()).WhenActionsIdleAsync();
            Assert.False(cancelled.IsCompleted);
            Assert.Equal(beforeCancel, await File.ReadAllBytesAsync(Path.Combine(root,"settings.json"),token));
            Click(secondDialog, "forms-math-cancel"); await cancelled.WaitAsync(token);
            Assert.Equal(beforeCancel, await File.ReadAllBytesAsync(Path.Combine(root,"settings.json"),token));
            host.Dispose();
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await mounted.Workspace.DispatchAsync("9to1.Forms.EditMathematics", null, token));
            Assert.Equal(beforeCancel, await File.ReadAllBytesAsync(Path.Combine(root,"settings.json"),token));
        }
        finally { owner?.Close(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root,true); }
    }

    [AvaloniaFact]
    public async Task Real_native_numeric_and_graph_answers_save_reopen_mark_and_disable_retired_or_submitted_inputs()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-forms-math-response-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Window? window = null;
        try
        {
            await using var graph = Services(root);
            var provider = graph.GetRequiredService<IFormNativeMathematicsProvider>();
            var identities = Assert.IsAssignableFrom<IResourceStoreIdentitySource>(graph.GetRequiredService<IVersionedSettingsStore>());
            var identity = await identities.GetStoreIdentityAsync(token);
            await graph.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync("forms", identity.StoreId.ToString("D"), token);
            var actor = (await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token))!;
            var session = await graph.GetRequiredService<FormPublicationService>().OpenHostSessionAsync(actor, token);
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Shared typed answers", FormModeKind.Form, now);
            var number = new FormField(Guid.NewGuid(), FormFieldKind.Mathematical, "Number", null,
                JsonSerializer.SerializeToElement(new { }), true, new(), Mathematics:new(Guid.NewGuid(),1,"1+0.2"),
                Assessment:new(2,1,[], Mathematics:new(Guid.NewGuid(),1.2m,MathNumericComparison.Exact,SignificantFigures:3)));
            var question = new GraphDefinition(Guid.NewGuid(),1,new(-10,10,-10,10),[],[],[GraphResponseTool.PlacePoint]);
            var target = new GraphPoint(Guid.NewGuid(),new(2,3));
            var point = new FormField(Guid.NewGuid(),FormFieldKind.Graph,"Place the point",null,
                JsonSerializer.SerializeToElement(new { }),true,new(),Graph:question,
                Assessment:new(3,1,[],Graph:new(Guid.NewGuid(),target.PrimitiveID),ExpectedGraph:question with {GraphID=Guid.NewGuid(),Revision=1,Primitives=[target]}));
            project = FormProjectEditor.AddField(project,project.Revision,project.Pages[0].PageID,number,now);
            project = FormProjectEditor.AddField(project,project.Revision,project.Pages[0].PageID,point,now);
            var created = await session.Publications.CreateAsync(project.FormID,FormProjectEditor.Project(project),token);
            Assert.True(created.Success);
            var published = await session.Publications.PublishAsync(project.FormID,created.Publication!.Revision,token);
            Assert.True(published.Success);
            var started = await session.Responses.StartAsync(project.FormID,published.Publication!.Revision,token);
            Assert.True(started.Success);
            var responseID = started.Response!.ResponseID;
            var opened = await FormNativeResponseSurface.OpenAsync(session.Responses,project.FormID,responseID,token,null,provider);
            Assert.True(opened.Success);
            using var surface = opened.Surface!;
            using var loader = Mount(surface,out var control);
            window = new Window {Content=control,Width=1000,Height=950};window.Show();Pump(window);
            var originalNumber = Text(window,"math-answer-number");originalNumber.Text="1.20";
            var originalGraph = Assert.Single(window.GetVisualDescendants().OfType<SharedGraphEditorControl>());
            Text(originalGraph,"math-graph-x").Text="1";Text(originalGraph,"math-graph-y").Text="1";
            Click(originalGraph,"math-place-point");await originalGraph.WhenActionsIdleAsync();
            var firstPoint=originalGraph.LastResponse!;
            Text(originalGraph,"math-graph-x").Text="2";Text(originalGraph,"math-graph-y").Text="3";
            Click(originalGraph,"math-place-point");await originalGraph.WhenActionsIdleAsync();
            var finalPoint=originalGraph.LastResponse!;
            Assert.Equal(firstPoint.ResponseID,finalPoint.ResponseID);
            Assert.Equal(firstPoint.Actions[0].Primitive.PrimitiveID,finalPoint.Actions[0].Primitive.PrimitiveID);
            Assert.Equal(question.Revision,finalPoint.GraphRevision);
            Assert.Equal(2,surface.UnsavedAnswerCount);
            await surface.DispatchAsync("Save",null,token);
            Pump(window);
            Assert.Equal(0,surface.UnsavedAnswerCount);
            var saved = await session.Responses.ReadSessionAsync(project.FormID,responseID,token);
            Assert.True(saved.Success);Assert.Null(saved.Presentation!.Fields.Single(x=>x.FieldID==point.FieldID).Assessment);
            var numeric = Read<MathAnswer>(saved.Response!.Answers.Single(x=>x.FieldID==number.FieldID).Value);
            Assert.Equal("1.20",Assert.IsType<NumericMathAnswer>(numeric.Value).Literal);
            Assert.Equal(MathObjectCodec.Encode(finalPoint),MathObjectCodec.Encode(Read<GraphResponse>(saved.Response.Answers.Single(x=>x.FieldID==point.FieldID).Value)));
            var beforeRetired=await File.ReadAllBytesAsync(Path.Combine(root,"settings.json"),token);
            originalNumber.Text="777";
            await Assert.ThrowsAsync<ObjectDisposedException>(async ()=>await originalGraph.DispatchAsync("PlacePoint",null,token));
            Assert.Equal(0,surface.UnsavedAnswerCount);
            Assert.Equal(beforeRetired,await File.ReadAllBytesAsync(Path.Combine(root,"settings.json"),token));
            var currentNumber=Text(window,"math-answer-number");currentNumber.Text="0.12345678901234567890123456789";
            await surface.DispatchAsync("Save",null,token);
            Pump(window);
            Assert.Equal("MathParseError",surface.StatusCode);Assert.Equal(1,surface.UnsavedAnswerCount);
            Assert.Equal("0.12345678901234567890123456789",Text(window,"math-answer-number").Text);
            Assert.Equal(beforeRetired,await File.ReadAllBytesAsync(Path.Combine(root,"settings.json"),token));
            Text(window,"math-answer-number").Text="1.20";
            await surface.DispatchAsync("Save",null,token);
            Pump(window);
            Assert.Equal(0,surface.UnsavedAnswerCount);
            var recovered=await session.Responses.ReadSessionAsync(project.FormID,responseID,token);
            Assert.True(recovered.Success);
            Assert.Equal(numeric.AnswerID,Read<MathAnswer>(recovered.Response!.Answers.Single(x=>x.FieldID==number.FieldID).Value).AnswerID);
            Text(window,"math-answer-number").Text="uncommitted invalid draft";
            await surface.DispatchAsync("Reload",null,token);Pump(window);
            Assert.Equal("1.20",Text(window,"math-answer-number").Text);
            window.Close();window=null;
            await using var fresh=Services(root);
            var freshActor=(await fresh.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token))!;
            var freshSession=await fresh.GetRequiredService<FormPublicationService>().OpenHostSessionAsync(freshActor,token);
            var freshOpen=await FormNativeResponseSurface.OpenAsync(freshSession.Responses,project.FormID,responseID,token,null,
                fresh.GetRequiredService<IFormNativeMathematicsProvider>());
            Assert.True(freshOpen.Success);
            using var reopened=freshOpen.Surface!;using var freshLoader=Mount(reopened,out var freshControl);
            window=new Window {Content=freshControl,Width=1000,Height=950};window.Show();Pump(window);
            Assert.Equal(responseID,reopened.Response!.ResponseID);
            Assert.Equal("1.20",Text(window,"math-answer-number").Text);
            var reopenedGraph=Assert.Single(window.GetVisualDescendants().OfType<SharedGraphEditorControl>());
            Assert.Equal(MathObjectCodec.Encode(finalPoint),MathObjectCodec.Encode(reopenedGraph.LastResponse!));
            await reopened.DispatchAsync("Submit",null,token);Pump(window);
            Assert.Equal(FormResponseState.Submitted,reopened.Response!.State);
            Assert.Equal(5m,reopened.Response.AwardedPoints);
            Assert.Equal(FormMarkingOutcome.Correct,reopened.Response.ReleasedResults[number.FieldID].Outcome);
            Assert.Equal(FormMarkingOutcome.Correct,reopened.Response.ReleasedResults[point.FieldID].Outcome);
            var submittedNumber=Text(window,"math-answer-number");Assert.False(submittedNumber.IsEffectivelyEnabled);
            var submittedGraph=Assert.Single(window.GetVisualDescendants().OfType<SharedGraphEditorControl>());
            Assert.False(submittedGraph.IsActionAvailable("PlacePoint"));
            var beforeClosed=await File.ReadAllBytesAsync(Path.Combine(root,"settings.json"),token);
            submittedNumber.Text="999";
            await Assert.ThrowsAsync<InvalidOperationException>(async()=>await submittedGraph.DispatchAsync("PlacePoint",null,token));
            Assert.Equal(0,reopened.UnsavedAnswerCount);
            Assert.Equal(beforeClosed,await File.ReadAllBytesAsync(Path.Combine(root,"settings.json"),token));
        }
        finally {window?.Close();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    [AvaloniaFact]
    public async Task Actual_authoring_factory_refuses_unsupported_existing_grading_without_changing_the_question_or_policy()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-forms-math-existing-grading-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Window? owner = null;
        try
        {
            await using var graph = Services(root);
            var identity = await Assert.IsAssignableFrom<IResourceStoreIdentitySource>(graph.GetRequiredService<IVersionedSettingsStore>()).GetStoreIdentityAsync(token);
            await graph.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync("forms", identity.StoreId.ToString("D"), token);
            var actor = (await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token))!;
            var session = await graph.GetRequiredService<FormPublicationService>().OpenHostSessionAsync(actor, token);
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Existing mathematical grading", FormModeKind.Test, now);
            var rule = new FormMarkingRule(Guid.NewGuid(), FormMarkingRuleKind.AcceptedText, 3, AcceptedTexts:["old expected answer"]);
            var field = new FormField(Guid.NewGuid(), FormFieldKind.Mathematical, "Existing question", null,
                JsonSerializer.SerializeToElement(new { }), true, new(), Mathematics:new(Guid.NewGuid(), 1, "x"),
                Assessment:new(3, 2, [rule], FormResultRelease.AfterReview));
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, now);
            Assert.True((await session.Publications.CreateAsync(project.FormID, FormProjectEditor.Project(project), token)).Success);
            var mounted = await MainView.CreateFormsDocumentWorkspaceAsync(graph, actor, project.FormID,
                () => owner, () => true, token);
            using var host = mounted.Host;
            owner = new Window { Content = host, Width = 1000, Height = 850 }; owner.Show(); Pump(owner);
            var original = await session.Publications.ReadAsync(project.FormID, token);
            var originalProject = FormProjectCodec.Encode(ReadProject(original.Publication!.Draft));
            var originalFile = await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"), token);
            var refusal = await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await mounted.Workspace.DispatchAsync("9to1.Forms.EditMathematics", null, token));
            Assert.Contains("grading policy", refusal.Message);
            Assert.Empty(owner.OwnedWindows);
            Assert.True(mounted.Workspace.TryGetValue("Status", out var status));
            Assert.Contains("preserved", Assert.IsType<string>(status));
            var unchanged = await session.Publications.ReadAsync(project.FormID, token);
            Assert.Equal(original.Publication.Revision, unchanged.Publication!.Revision);
            Assert.Equal(originalProject, FormProjectCodec.Encode(ReadProject(unchanged.Publication.Draft)));
            Assert.Equal(originalFile, await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"), token));
            var preserved = Assert.Single(ReadProject(unchanged.Publication.Draft).Fields);
            Assert.Equal(field.FieldID, preserved.FieldID); Assert.Equal(field.Revision, preserved.Revision);
            Assert.Equal(field.Mathematics, preserved.Mathematics);
            Assert.Equal(3m, preserved.Assessment!.MaximumPoints); Assert.Equal(2m, preserved.Assessment.Weight);
            Assert.Equal(FormResultRelease.AfterReview, preserved.Assessment.Release);
            Assert.Equal(rule.RuleID, Assert.Single(preserved.Assessment.Rules).RuleID);
            Assert.Equal(rule.AcceptedTexts, preserved.Assessment.Rules[0].AcceptedTexts);
        }
        finally { owner?.Close(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root,true); }
    }

    [AvaloniaFact]
    public async Task Actual_graph_authoring_save_and_reopen_keep_independent_question_and_expected_graph_identities()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-forms-graph-private-grading-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Window? owner = null;
        try
        {
            await using var graph = Services(root);
            var identity = await Assert.IsAssignableFrom<IResourceStoreIdentitySource>(graph.GetRequiredService<IVersionedSettingsStore>()).GetStoreIdentityAsync(token);
            await graph.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync("forms", identity.StoreId.ToString("D"), token);
            var actor = (await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token))!;
            var session = await graph.GetRequiredService<FormPublicationService>().OpenHostSessionAsync(actor, token);
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Private graph grading", FormModeKind.Test, now);
            var question = new GraphDefinition(Guid.NewGuid(), 5, new(-10, 10, -10, 10), [], [], [GraphResponseTool.PlacePoint]);
            var field = new FormField(Guid.NewGuid(), FormFieldKind.Graph, "Place a point", null,
                JsonSerializer.SerializeToElement(new { }), true, new(), Graph:question);
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, now);
            Assert.True((await session.Publications.CreateAsync(project.FormID, FormProjectEditor.Project(project), token)).Success);
            var mounted = await MainView.CreateFormsDocumentWorkspaceAsync(graph, actor, project.FormID,
                () => owner, () => true, token);
            using var host = mounted.Host;
            owner = new Window { Content = host, Width = 1000, Height = 850 }; owner.Show(); Pump(owner);
            var pending = mounted.Workspace.DispatchAsync("9to1.Forms.EditMathematics", null, token).AsTask();
            var dialog = await DialogAsync(owner, pending, token); Pump(dialog);
            var controls = dialog.GetVisualDescendants().OfType<SharedGraphEditorControl>().ToArray();
            Assert.Equal(2, controls.Length);
            var prompt = Assert.Single(controls, x => x.Snapshot.GraphID == question.GraphID);
            var expectedEditor = Assert.Single(controls, x => x.Snapshot.GraphID != question.GraphID);
            var expectedID = expectedEditor.Snapshot.GraphID;
            Assert.Equal(1, expectedEditor.Snapshot.Revision);
            Assert.Equal(MathObjectCodec.Encode(question), MathObjectCodec.Encode(prompt.Snapshot));
            dialog.GetVisualDescendants().OfType<CheckBox>().Single(x => x.Name == "forms-math-graded").IsChecked = true;
            Text(dialog, "forms-math-points").Text = "3";
            Text(expectedEditor, "math-graph-x").Text = "2"; Text(expectedEditor, "math-graph-y").Text = "3";
            Click(expectedEditor, "math-place-point"); await expectedEditor.WhenActionsIdleAsync(); Pump(dialog);
            var firstExpectedRevision = expectedEditor.Snapshot.Revision;
            Assert.Equal(2, firstExpectedRevision);
            Click(dialog, "forms-math-save"); await pending.WaitAsync(token);
            var firstSaved = Assert.Single(ReadProject((await session.Publications.ReadAsync(project.FormID, token)).Publication!.Draft).Fields);
            Assert.Equal(MathObjectCodec.Encode(question), MathObjectCodec.Encode(firstSaved.Graph!));
            Assert.Equal(expectedID, firstSaved.Assessment!.ExpectedGraph!.GraphID);
            Assert.Equal(firstExpectedRevision, firstSaved.Assessment.ExpectedGraph.Revision);
            Assert.NotEqual(firstSaved.Graph!.GraphID, firstSaved.Assessment.ExpectedGraph.GraphID);
            var reopened = mounted.Workspace.DispatchAsync("9to1.Forms.EditMathematics", null, token).AsTask();
            var secondDialog = await DialogAsync(owner, reopened, token); Pump(secondDialog);
            var expectedAgain = Assert.Single(secondDialog.GetVisualDescendants().OfType<SharedGraphEditorControl>(),
                x => x.Snapshot.GraphID == expectedID);
            Assert.Equal(firstExpectedRevision, expectedAgain.Snapshot.Revision);
            Text(expectedAgain, "math-graph-x").Text = "4"; Text(expectedAgain, "math-graph-y").Text = "5";
            Click(expectedAgain, "math-place-point"); await expectedAgain.WhenActionsIdleAsync(); Pump(secondDialog);
            Click(secondDialog, "forms-math-save"); await reopened.WaitAsync(token);
            var secondSaved = Assert.Single(ReadProject((await session.Publications.ReadAsync(project.FormID, token)).Publication!.Draft).Fields);
            Assert.Equal(field.FieldID, secondSaved.FieldID); Assert.Equal(firstSaved.Revision + 1, secondSaved.Revision);
            Assert.Equal(MathObjectCodec.Encode(question), MathObjectCodec.Encode(secondSaved.Graph!));
            Assert.Equal(expectedID, secondSaved.Assessment!.ExpectedGraph!.GraphID);
            Assert.Equal(firstExpectedRevision + 1, secondSaved.Assessment.ExpectedGraph.Revision);
            var target = Assert.IsType<GraphPoint>(Assert.Single(secondSaved.Assessment.ExpectedGraph.Primitives,
                x => x.PrimitiveID == secondSaved.Assessment.Graph!.ExpectedPrimitiveID));
            Assert.Equal(new GraphCoordinate(4, 5), target.Position);
            Assert.NotNull((await graph.GetRequiredService<IVersionedSettingsStore>().ExportAsync(token))
                .Settings["forms.publication.v1."+project.FormID.ToString("N")]);
            var beforeCancel = await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"), token);
            var cancelled = mounted.Workspace.DispatchAsync("9to1.Forms.EditMathematics", null, token).AsTask();
            var thirdDialog = await DialogAsync(owner, cancelled, token); Pump(thirdDialog);
            var finalExpected = Assert.Single(thirdDialog.GetVisualDescendants().OfType<SharedGraphEditorControl>(),
                x => x.Snapshot.GraphID == expectedID);
            Assert.Equal(secondSaved.Assessment.ExpectedGraph.Revision, finalExpected.Snapshot.Revision);
            Click(thirdDialog, "forms-math-cancel"); await cancelled.WaitAsync(token);
            Assert.Equal(beforeCancel, await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"), token));
        }
        finally { owner?.Close(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root,true); }
    }

    [AvaloniaFact]
    public void Authored_numeric_textbox_preserves_a_maximum_revision_answer_and_reports_the_refused_draft()
    {
        var original = new MathAnswer(Guid.NewGuid(), long.MaxValue, new NumericMathAnswer("1.20", "m"));
        var originalBytes = MathObjectCodec.Encode(original);
        using var editor = new NumericMathAnswerEditorControl(original);
        var events = new List<MathAnswerDraftChangedEventArgs>();
        editor.DraftChanged += (_, args) => events.Add(args);
        var window = new Window { Content = editor, Width = 500, Height = 400 };
        try
        {
            window.Show(); Pump(window);
            Text(editor, "math-answer-number").Text = "2.00";
            Pump(window);
            Assert.Equal("2.00", Text(editor, "math-answer-number").Text);
            Assert.Equal("2.00", editor.DraftLiteral);
            Assert.Equal(originalBytes, MathObjectCodec.Encode(editor.LastValid!));
            Assert.Null(Assert.Single(events).Answer);
            Assert.Equal("MathAnswerRevisionLimit", events[0].Diagnostic);
            var status = editor.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "math-answer-status");
            Assert.Contains("revision limit", status.Text);
            Assert.True(status.IsEffectivelyVisible);
            Text(editor, "math-answer-units").Text = "kg"; Pump(window);
            Assert.Equal("kg", editor.DraftUnits); Assert.Equal(2, events.Count);
            Assert.All(events, entry => { Assert.Null(entry.Answer); Assert.Equal("MathAnswerRevisionLimit", entry.Diagnostic); });
            Assert.Equal(originalBytes, MathObjectCodec.Encode(editor.LastValid!));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Native_explicit_point_button_refuses_maximum_response_revision_without_losing_the_coordinate_draft()
    {
        var token = TestContext.Current.CancellationToken;
        var question = new GraphDefinition(Guid.NewGuid(), 1, new(-10, 10, -10, 10), [], [], [GraphResponseTool.PlacePoint]);
        var point = new GraphPoint(Guid.NewGuid(), new(2, 3));
        var original = new GraphResponse(Guid.NewGuid(), long.MaxValue, question.GraphID, question.Revision, [],
            [new(GraphResponseTool.PlacePoint, point)]);
        using var editor = SharedGraphEditorControl.ForResponse(question, original);
        var originalBytes = MathObjectCodec.Encode(original); var displayBytes = MathObjectCodec.Encode(editor.Snapshot);
        var window = new Window { Content = editor, Width = 700, Height = 700 };
        try
        {
            window.Show(); Pump(window);
            Text(editor, "math-graph-x").Text = "4"; Text(editor, "math-graph-y").Text = "5";
            Click(editor, "math-place-point"); await editor.WhenActionsIdleAsync().WaitAsync(token); Pump(window);
            Assert.True(editor.HasUncommittedCoordinates);
            Assert.Equal(("4", "5"), editor.CoordinateDraft);
            Assert.Equal("4", Text(editor, "math-graph-x").Text); Assert.Equal("5", Text(editor, "math-graph-y").Text);
            Assert.Equal(originalBytes, MathObjectCodec.Encode(editor.LastResponse!));
            Assert.Equal(displayBytes, MathObjectCodec.Encode(editor.Snapshot));
            var status = editor.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "math-graph-status");
            Assert.Contains("revision limit", status.Text); Assert.True(status.IsEffectivelyVisible);
            Assert.Equal(question.GraphID, editor.LastResponse!.GraphID); Assert.Equal(question.Revision, editor.LastResponse.GraphRevision);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Optional_native_number_can_be_cleared_saved_and_reopened_while_nonblank_invalid_drafts_are_preserved()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-forms-optional-math-clear-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Window? window = null;
        try
        {
            await using var graph = Services(root);
            var identity = await Assert.IsAssignableFrom<IResourceStoreIdentitySource>(graph.GetRequiredService<IVersionedSettingsStore>()).GetStoreIdentityAsync(token);
            await graph.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync("forms", identity.StoreId.ToString("D"), token);
            var actor = (await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token))!;
            var session = await graph.GetRequiredService<FormPublicationService>().OpenHostSessionAsync(actor, token);
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Optional number", FormModeKind.Form, now);
            var field = new FormField(Guid.NewGuid(), FormFieldKind.Mathematical, "Optional length", null,
                JsonSerializer.SerializeToElement(new { }), false, new(), Mathematics:new(Guid.NewGuid(), 1, "x"));
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, now);
            var created = await session.Publications.CreateAsync(project.FormID, FormProjectEditor.Project(project), token);
            var published = await session.Publications.PublishAsync(project.FormID, created.Publication!.Revision, token);
            Assert.True(published.Success);
            var started = await session.Responses.StartAsync(project.FormID, published.Publication!.Revision, token);
            Assert.True(started.Success); var responseID = started.Response!.ResponseID;
            var opened = await FormNativeResponseSurface.OpenAsync(session.Responses, project.FormID, responseID, token, null,
                graph.GetRequiredService<IFormNativeMathematicsProvider>());
            Assert.True(opened.Success); using var surface = opened.Surface!;
            using var loader = Mount(surface, out var control);
            window = new Window { Content = control, Width = 800, Height = 800 }; window.Show(); Pump(window);
            Text(window, "math-answer-number").Text = "1.20"; Text(window, "math-answer-units").Text = "m";
            await surface.DispatchAsync("Save", null, token); Pump(window);
            var saved = await session.Responses.ReadSessionAsync(project.FormID, responseID, token);
            Assert.Equal("1.20", Assert.IsType<NumericMathAnswer>(Read<MathAnswer>(Assert.Single(saved.Response!.Answers).Value).Value).Literal);
            Text(window, "math-answer-number").Text = ""; Text(window, "math-answer-units").Text = "";
            await surface.DispatchAsync("Save", null, token); Pump(window);
            Assert.Equal(0, surface.UnsavedAnswerCount);
            var cleared = await session.Responses.ReadSessionAsync(project.FormID, responseID, token);
            Assert.True(cleared.Success); Assert.Equal(JsonValueKind.Null, Assert.Single(cleared.Response!.Answers).Value.ValueKind);
            var clearBytes = await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"), token);
            window.Close(); window = null;
            await using var fresh = Services(root);
            var freshActor = (await fresh.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token))!;
            var freshSession = await fresh.GetRequiredService<FormPublicationService>().OpenHostSessionAsync(freshActor, token);
            var freshOpened = await FormNativeResponseSurface.OpenAsync(freshSession.Responses, project.FormID, responseID, token, null,
                fresh.GetRequiredService<IFormNativeMathematicsProvider>());
            Assert.True(freshOpened.Success); using var reopened = freshOpened.Surface!;
            using var freshLoader = Mount(reopened, out var freshControl);
            window = new Window { Content = freshControl, Width = 800, Height = 800 }; window.Show(); Pump(window);
            Assert.Equal(responseID, reopened.Response!.ResponseID);
            Assert.Equal("", Text(window, "math-answer-number").Text); Assert.Equal("", Text(window, "math-answer-units").Text);
            Text(window, "math-answer-number").Text = " ";
            await reopened.DispatchAsync("Save", null, token); Pump(window);
            Assert.Equal("MathParseError", reopened.StatusCode); Assert.Equal(1, reopened.UnsavedAnswerCount);
            Assert.Equal(" ", Text(window, "math-answer-number").Text);
            Assert.Equal(clearBytes, await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"), token));
            Assert.Equal(JsonValueKind.Null, Assert.Single((await freshSession.Responses.ReadSessionAsync(project.FormID, responseID, token)).Response!.Answers).Value.ValueKind);
            Text(window, "math-answer-number").Text = "";
            await reopened.DispatchAsync("Save", null, token); Pump(window);
            Assert.Equal(0, reopened.UnsavedAnswerCount);
            Assert.Equal(JsonValueKind.Null, Assert.Single((await freshSession.Responses.ReadSessionAsync(project.FormID, responseID, token)).Response!.Answers).Value.ValueKind);
        }
        finally { window?.Close(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root,true); }
    }

    [AvaloniaFact]
    public async Task Actual_existing_graph_authoring_save_without_policy_edits_preserves_tolerance_reversal_and_private_target_versions()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-forms-graph-existing-policy-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Window? owner = null;
        try
        {
            await using var graph = Services(root);
            var identity = await Assert.IsAssignableFrom<IResourceStoreIdentitySource>(graph.GetRequiredService<IVersionedSettingsStore>()).GetStoreIdentityAsync(token);
            await graph.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync("forms", identity.StoreId.ToString("D"), token);
            var actor = (await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token))!;
            using var session = await graph.GetRequiredService<FormPublicationService>().OpenHostSessionAsync(actor, token);
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Preserve existing graph policy", FormModeKind.Test, now);
            var question = new GraphDefinition(Guid.NewGuid(), 5, new(-10, 10, -10, 10), [], [], [GraphResponseTool.PlacePoint]);
            var target = new GraphPoint(Guid.NewGuid(), new(2, 3));
            var expected = question with { GraphID = Guid.NewGuid(), Revision = 7, Primitives = [target] };
            var rule = new GraphCoordinateMarkingRule(Guid.NewGuid(), target.PrimitiveID, 0.125m, AllowReversedLine:false);
            var field = new FormField(Guid.NewGuid(), FormFieldKind.Graph, "Existing point policy", null,
                JsonSerializer.SerializeToElement(new { }), true, new(), Graph:question,
                Assessment:new(3, 2, [], FormResultRelease.AfterReview, Graph:rule, ExpectedGraph:expected));
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, now);
            Assert.True((await session.Publications.CreateAsync(project.FormID, FormProjectEditor.Project(project), token)).Success);
            var mounted = await MainView.CreateFormsDocumentWorkspaceAsync(graph, actor, project.FormID,
                () => owner, () => true, token);
            using var host = mounted.Host;
            owner = new Window { Content = host, Width = 1000, Height = 850 }; owner.Show(); Pump(owner);
            for (var save = 1; save <= 2; save++)
            {
                // Enter the actual authored normal CUI button and retain its original callback task.
                Click(host, "forms-edit-mathematics");
                var pending = host.WhenActionsIdleAsync();
                var dialog = await DialogAsync(owner, pending, token); Pump(dialog);
                Assert.False(pending.IsCompleted);
                Assert.Equal("0.125", Text(dialog, "forms-math-tolerance").Text);
                var privateEditor = Assert.Single(dialog.GetVisualDescendants().OfType<SharedGraphEditorControl>(),
                    x => x.Snapshot.GraphID == expected.GraphID);
                Assert.Equal(MathObjectCodec.Encode(expected), MathObjectCodec.Encode(privateEditor.Snapshot));
                Click(dialog, "forms-math-save"); await pending.WaitAsync(token);
                var saved = Assert.Single(ReadProject((await session.Publications.ReadAsync(project.FormID, token)).Publication!.Draft).Fields);
                Assert.Equal(field.FieldID, saved.FieldID); Assert.Equal(field.Revision + save, saved.Revision);
                Assert.Equal(MathObjectCodec.Encode(question), MathObjectCodec.Encode(saved.Graph!));
                Assert.Equal(rule, saved.Assessment!.Graph);
                Assert.Equal(0.125m, saved.Assessment.Graph!.CoordinateTolerance);
                Assert.False(saved.Assessment.Graph.AllowReversedLine);
                Assert.Equal(MathObjectCodec.Encode(expected), MathObjectCodec.Encode(saved.Assessment.ExpectedGraph!));
                Assert.Equal(3m, saved.Assessment.MaximumPoints); Assert.Equal(2m, saved.Assessment.Weight);
                Assert.Equal(FormResultRelease.AfterReview, saved.Assessment.Release); Assert.Empty(saved.Assessment.Rules);
            }
            var exported = await graph.GetRequiredService<IVersionedSettingsStore>().ExportAsync(token);
            using var physical = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"), token));
            var key = "forms.publication.v1."+project.FormID.ToString("N");
            Assert.Equal(exported.Settings[key], physical.RootElement.GetProperty("Settings").GetProperty(key).GetString());
        }
        finally { owner?.Close(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root,true); }
    }

    private static ServiceProvider Services(string root)
    {
        var services=new ServiceCollection().AddHavenInfrastructure();
        services.AddSingleton<IAppPaths>(new Paths(root));
        services.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root,"home.json")));
        var provider=new FormNativeMathematicsProvider();services.AddSingleton<IFormNativeMathematicsProvider>(provider);
        services.AddHavenFormsPublication(new FormNativePublicationValidator(provider));return services.BuildServiceProvider();
    }
    private static CakeOS.Cui.Runtime.CuiControlLoader Mount(FormNativeResponseSurface surface,out Control control)
    {
        var registry=new CakeOS.Cui.Runtime.CuiControlRegistry();surface.Register(registry);
        var loader=new CakeOS.Cui.Runtime.CuiControlLoader(registry);loader.SetBindingContext(surface);loader.SetActionDispatcher(surface);
        var loaded=loader.TryLoad(surface.CreateDocument());
        Assert.DoesNotContain(loaded.Diagnostics,x=>x.Severity==CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
        control=loaded.Root ?? throw new InvalidDataException("Response root missing");
        loader.WireBindings(control);return loader;
    }
    private static void Pump(Window window){window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
    private static TextBox Text(Avalonia.Visual root,string name)=>root.GetVisualDescendants().OfType<TextBox>().Single(x=>x.Name==name);
    private static void Click(Avalonia.Visual root,string name)=>root.GetVisualDescendants().OfType<Button>().Single(x=>x.Name==name)
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task<Window> DialogAsync(Window owner,Task pending,CancellationToken token)
    {
        var deadline=DateTime.UtcNow.AddSeconds(10);
        while(!owner.OwnedWindows.Any()&&!pending.IsCompleted&&DateTime.UtcNow<deadline)
        {Dispatcher.UIThread.RunJobs();await Task.Delay(10,token);}
        return Assert.Single(owner.OwnedWindows);
    }
    private static FormProject ReadProject(JsonElement value)=>FormProjectCodec.Decode(Encoding.UTF8.GetBytes(value.GetRawText()));
    private static T Read<T>(JsonElement value)where T:class=>MathObjectCodec.Decode<T>(Encoding.UTF8.GetBytes(value.GetRawText()));
    private sealed class Paths(string root):IAppPaths
    {
        public string DataDirectory=>root;public string DatabasePath=>Path.Combine(root,"app.db");
        public string BrowserProfileDirectory=>Path.Combine(root,"browser");public string AttachmentsDirectory=>Path.Combine(root,"attachments");
        public string LogsDirectory=>Path.Combine(root,"logs");public string LegacyStatePath=>Path.Combine(root,"legacy.json");
    }
}
