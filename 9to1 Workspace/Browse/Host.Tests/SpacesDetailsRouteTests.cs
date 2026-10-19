using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Spaces.Builder;

namespace HavenOS.Browse.Host.Tests;

/// <summary>Actual retained Home/session/editor/Chat owners and canonical authored actions.
/// Headless process-local Home evidence; no native, resource ACL, web or full Builder claim.</summary>
public sealed class SpacesDetailsRouteTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);

    [AvaloniaFact]
    public async Task Authored_details_and_back_actions_keep_the_same_root_and_chat_draft_then_exact_close_joins_all_owners()
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true);
        var custom = await rig.Spaces.CreateAsync("Canonical details");
        custom = await rig.Spaces.UpdateAsync(custom with { ModelName = "retain-policy", Instructions = "Original instructions" }, custom.Revision);
        var actor = await Start(rig);
        var session = rig.Session("9to1://spaces", profileId: actor);
        Window? window = null;
        Exception? primary = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            window = Mount(root);
            var home = Assert.IsType<SpacesHomePage>(root.Content);
            await home.Controller.SelectSpaceAsync(SpaceRegistry.ChatSpaceId);
            await Open(home, session, "open-selected-space");
            // The conversation is mounted inside the retained Spaces Home rail layout, so the
            // icon-only rail and the chat's own history sidebar remain presented together.
            var chat = Assert.IsType<NewChatPage>(home.SpaceWorkspaceContent);
            var conversation = chat.ConversationId;
            chat.SetDraft("Retained unsent Details round trip");
            // The original Home remains the session's canonical discoverability surface.
            // Return to it through the retained session route, then exercise the actual CUI.
            await home.Controller.SelectSpaceAsync(custom.Id);
            await home.Controller.EditDetailsAsync(custom.Id);
            // Home's manage operation opens the one canonical Space surface: a UserCreated Space gets
            // the full Builder graph, not a separate details product.
            var editor = Assert.IsType<SpaceBuilderPage>(root.Content);
            Assert.Equal($"9to1://spaces/builder/{custom.Id:D}", session.Address.Value);
            Assert.Same(root, session.OriginalNativeView);
            Assert.Equal(custom.Id, editor.State.SpaceId);
            Input(editor.Host, "space-builder-name").Text = "Saved same canonical Space";
            Click(editor.Host, "space-builder-save");
            await editor.Host.CaptureOriginalActionSettlement(editor.Show!).WaitAsync(Bound);
            var saved = (await rig.Spaces.ReadExistingAsync(custom.Id))!;
            Assert.Equal("Saved same canonical Space", saved.Name);
            Assert.Equal("retain-policy", saved.ModelName);
            Assert.Equal("Original instructions", saved.Instructions);
            await Back(editor, session);
            Assert.Same(home, root.Content);
            await home.Controller.SelectSpaceAsync(SpaceRegistry.ChatSpaceId);
            // The conversation is mounted inside the retained rail layout, so returning from the
            // canonical Space editor restores that exact page and draft instead of a fresh one, and
            // the contextual sidebar still presents its own history row.
            Assert.Same(chat, home.SpaceWorkspaceContent);
            Assert.Equal(conversation, chat.ConversationId);
            Assert.Equal("Retained unsent Details round trip", ((ChatHavenScene)typeof(NewChatPage).GetField("_scene", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(chat)!).Instruction.Text);
            var prepared = (await session.PrepareCloseAsync().WaitAsync(Bound))!;
            Assert.True(prepared.IsCurrent);
            var close = prepared.BeginClose();
            Assert.Same(close, session.OriginalClose);
            Assert.Same(close, prepared.AbortAsync()); // Accepted close is not reversible.
            await close.WaitAsync(Bound);
            Assert.True(session.CanRetireOriginalView);
            Assert.True(editor.OriginalClose!.IsCompletedSuccessfully);
            Assert.True(home.OriginalClose!.IsCompletedSuccessfully);
            Assert.Null(root.Content);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await Cleanup(session, rig, window, primary); }
    }

    [AvaloniaFact]
    public async Task Public_Home_details_command_waits_the_actual_origin_and_loader_before_in_place_publication()
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true);
        var target = await rig.Spaces.CreateAsync("Typed Home target");
        var actor = await Start(rig); var session = rig.Session("9to1://spaces", profileId: actor);
        Window? window = null;
        Exception? primary = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView); window = Mount(root);
            var home = Assert.IsType<SpacesHomePage>(root.Content);
            await home.Controller.SelectSpaceAsync(target.Id);
            await Open(home, session, "edit-space-details");
            var page = Assert.IsType<SpaceBuilderPage>(root.Content);
            Assert.True(session.OriginalQueuedNavigation!.IsCompletedSuccessfully);
            Assert.Same(root, session.OriginalNativeView);
            Assert.Equal(target.Id, page.State.SpaceId);
            Assert.Equal(0, rig.ConversationWrites); Assert.Equal(0, rig.ModelReads);
            Assert.Equal($"9to1://spaces/builder/{target.Id:D}", session.Address.Value);
            await Back(page, session);
            Assert.Same(home, root.Content);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await Cleanup(session, rig, window, primary); }
    }

    [AvaloniaTheory]
    [InlineData("save")]
    [InlineData("discard")]
    [InlineData("cancel")]
    public async Task Dirty_editor_return_uses_real_visible_three_choice_decision_and_preserves_or_resolves_same_draft(string choice)
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true);
        var target = await rig.Spaces.CreateAsync("Original"); var actor = await Start(rig);
        var session = rig.Session($"9to1://spaces/details/{target.Id:D}", profileId: actor);
        Window? window = null;
        Exception? primary = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView); window = Mount(root);
            var page = Assert.IsType<SpaceDetailsPage>(root.Content);
            Input(page.EditorHost, "space-details-name").Text = "Unsaved return";
            var previous = session.OriginalQueuedNavigation;
            Click(page.EditorHost, "space-details-back");
            var driver = await Navigation(session, previous);
            await Decision(page);
            Assert.Same(page, root.Content); Assert.False(driver.IsCompleted);
            Click(page.DecisionHost!, "space-details-close-" + choice);
            await driver.WaitAsync(Bound);
            Assert.Null(page.OriginalClose);
            if (choice == "cancel")
            {
                Assert.Same(page, root.Content); Assert.True(page.State.HasChanges);
                Assert.Equal("Unsaved return", page.State.Draft.Name);
            }
            else
            {
                Assert.IsType<SpacesHomePage>(root.Content);
                Assert.False(page.State.HasChanges);
            }
            Assert.Equal(choice == "save" ? "Unsaved return" : "Original", (await rig.Spaces.ReadExistingAsync(target.Id))!.Name);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await Cleanup(session, rig, window, primary); }
    }

    [AvaloniaFact]
    public async Task Whole_session_preparation_freezes_Home_actions_and_typing_then_abort_restores_same_actual_view()
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true);
        var actor = await Start(rig); var session = rig.Session("9to1://spaces", profileId: actor);
        Exception? primary = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var home = Assert.IsType<SpacesHomePage>(root.Content);
            var homeGate = typeof(SpacesHomePage).GetField("_gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(home)!;
            var mutationEventUnderAdmission = new List<bool>();
            session.StateChanged += (_, _) => mutationEventUnderAdmission.Add(Monitor.IsEntered(homeGate));
            var lease = (await session.PrepareCloseAsync().WaitAsync(Bound))!;
            Assert.True(lease.IsCurrent);
            Assert.False(home.TrySetValue("spaces.query", "Refuse new typing"));
            var before = home.Controller.State;
            await home.DispatchAsync("spaces.create", null);
            Assert.Same(before, home.Controller.State);
            Assert.False(home.IsActionAvailable("spaces.create"));
            var abort = lease.AbortAsync(); Assert.Same(abort, lease.AbortAsync());
            await abort.WaitAsync(Bound);
            Assert.False(lease.IsCurrent);
            Assert.Throws<InvalidOperationException>(() => { _ = lease.BeginClose(); });
            Assert.Null(session.OriginalClose);
            Assert.Same(home, root.Content);
            Assert.True(home.TrySetValue("spaces.query", "Typing after sibling veto"));
            await home.LatestOriginalMutation!.WaitAsync(Bound);
            Assert.Equal("Typing after sibling veto", home.Controller.State.Query);
            Assert.NotEmpty(mutationEventUnderAdmission);
            Assert.DoesNotContain(true, mutationEventUnderAdmission); // No Home-held callback edge into outer session/registry gates.
            for (var cycle = 0; cycle < 4; cycle++)
            {
                var again = (await session.PrepareCloseAsync().WaitAsync(Bound))!;
                Assert.True(again.IsCurrent); await again.AbortAsync().WaitAsync(Bound);
            }
            var retained = (System.Collections.ICollection)typeof(SpacesBrowseAppContentSession).GetField("_detailsPreparations",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;
            Assert.InRange(retained.Count, 0, 2); // Healthy completed cohorts do not retain historical page/task graphs.
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await Cleanup(session, rig, null, primary); }
    }

    [AvaloniaFact]
    public async Task Hidden_dirty_editor_is_actually_presented_and_sibling_cancel_restores_all_editor_and_Home_admission()
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true);
        var first = await rig.Spaces.CreateAsync("First"); var second = await rig.Spaces.CreateAsync("Second");
        var actor = await Start(rig); var session = rig.Session($"9to1://spaces/details/{first.Id:D}", profileId: actor);
        Window? window = null;
        Exception? primary = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView); window = Mount(root);
            var firstPage = Assert.IsType<SpaceDetailsPage>(root.Content);
            await Back(firstPage, session);
            var home = Assert.IsType<SpacesHomePage>(root.Content);
            await home.Controller.SelectSpaceAsync(second.Id); await Open(home, session, "edit-space-details");
            var secondPage = Assert.IsType<SpaceBuilderPage>(root.Content);
            Assert.True(firstPage.TryEdit(SpaceDetailsField.Name, "Hidden unsaved"));
            Assert.True(secondPage.TryEdit(SpaceBuilderField.Name, "Visible unsaved"));
            var prepare = session.PrepareCloseAsync();
            await Decision(firstPage);
            Assert.Same(firstPage, root.Content); // An actual visible decision, not an invisible bool check.
            Click(firstPage.DecisionHost!, "space-details-close-discard");
            await Decision(secondPage);
            Assert.Same(secondPage, root.Content);
            Click(secondPage.DecisionHost!, "space-details-close-cancel");
            Assert.Null(await prepare.WaitAsync(Bound));
            Assert.Same(secondPage, root.Content);
            Assert.Equal($"9to1://spaces/builder/{second.Id:D}", session.Address.Value);
            Assert.True(firstPage.TryEdit(SpaceDetailsField.Description, "First restored"));
            Assert.True(secondPage.TryEdit(SpaceBuilderField.Description, "Second restored"));
            Assert.Null(firstPage.OriginalClose); Assert.Null(secondPage.OriginalClose); Assert.Null(session.OriginalClose);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await Cleanup(session, rig, window, primary); }
    }

    [AvaloniaTheory]
    [InlineData("missing")]
    [InlineData("builtin")]
    [InlineData("archived")]
    [InlineData("malformed")]
    public async Task Explicit_unavailable_Details_target_is_not_reinterpreted_or_replaced(string unavailable)
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true);
        var target = await rig.Spaces.CreateAsync("Rejected target");
        if (unavailable == "archived") await rig.Spaces.SetArchivedAsync(target.Id, true);
        var id = unavailable == "builtin" ? SpaceRegistry.ChatSpaceId : unavailable == "missing" ? Guid.NewGuid() : target.Id;
        var address = unavailable == "malformed" ? "9to1://spaces/details/not-a-space" : $"9to1://spaces/details/{id:D}";
        var actor = await Start(rig); var session = rig.Session(address, profileId: actor);
        Exception? original = null;
        try
        {
            original = await Assert.ThrowsAnyAsync<Exception>(() => session.InitializeAsync().WaitAsync(Bound));
            Assert.Equal(address, session.Address.Value);
            Assert.Equal(0, rig.ConversationWrites); Assert.Equal(0, rig.ModelReads);
            Assert.False(session.IsPublicationCurrent);
        }
        finally
        {
            try
            {
                var retained = await Assert.ThrowsAnyAsync<Exception>(() => session.CloseAndDrainAsync().WaitAsync(Bound));
                Assert.True(Contains(retained, original!));
            }
            finally { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); }
        }
    }

    [AvaloniaFact]
    public async Task Held_Home_mutation_is_joined_before_yielding_a_lease_and_new_typing_is_fenced_until_abort()
    {
        var store = new HeldSettings();
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, settings: store);
        var target = await rig.Spaces.CreateAsync("Held selection"); var actor = await Start(rig);
        var session = rig.Session("9to1://spaces", profileId: actor);
        Task? mutation = null; Task<HavenOS.Apps.Browse.IBrowseAppContentClosePreparation?>? preparation = null;
        Window? window = null;
        Exception? primary = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView); window = Mount(root);
            var home = Assert.IsType<SpacesHomePage>(root.Content);
            store.HoldNextRead = true;
            mutation = home.DispatchAsync("spaces.select", target.Id).AsTask();
            await store.Entered.Task.WaitAsync(Bound);
            preparation = session.PrepareCloseAsync();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask().WaitAsync(Bound);
            Assert.False(preparation.IsCompleted); Assert.False(mutation.IsCompleted);
            Assert.False(home.TrySetValue("spaces.query", "Cannot overtake the held original"));
            Assert.False(Find<TextBox>(home.OriginalHost, "spaces-search").IsEffectivelyEnabled);
            store.Release.TrySetResult(store.Captured);
            await mutation.WaitAsync(Bound);
            var lease = (await preparation.WaitAsync(Bound))!;
            Assert.True(lease.IsCurrent); Assert.Equal(target.Id, home.Controller.State.Selection!.Id);
            await lease.AbortAsync().WaitAsync(Bound);
            Assert.True(Find<TextBox>(home.OriginalHost, "spaces-search").IsEffectivelyEnabled);
            Assert.True(home.TrySetValue("spaces.query", "New typing after veto"));
            await home.LatestOriginalMutation!.WaitAsync(Bound);
            Assert.Equal("New typing after veto", home.Controller.State.Query);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            store.Release.TrySetResult(store.Captured);
            try
            {
                if (mutation is not null) await mutation.WaitAsync(Bound);
                if (preparation is not null && await preparation.WaitAsync(Bound) is { } lease) await lease.AbortAsync().WaitAsync(Bound);
            }
            catch (Exception failure) { primary = primary is null ? failure : new AggregateException(primary, failure); System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw(); throw; }
        finally { await Cleanup(session, rig, window, primary); }
        }
    }

    [AvaloniaFact]
    public async Task Held_actual_Chat_transition_finishing_during_hidden_editor_decision_restores_fresh_same_page_identity()
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true);
        var custom = await rig.Spaces.CreateAsync("Hidden editor"); var actor = await Start(rig);
        var tab = Guid.NewGuid(); var session = rig.Session($"9to1://spaces/details/{custom.Id:D}", tab, actor);
        var messages = new TaskCompletionSource<IReadOnlyList<ChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? transition = null; Window? window = null;
        Exception? primary = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView); window = Mount(root);
            var details = Assert.IsType<SpaceDetailsPage>(root.Content);
            await Back(details, session);
            var home = Assert.IsType<SpacesHomePage>(root.Content);
            await home.Controller.SelectSpaceAsync(SpaceRegistry.ChatSpaceId); await Open(home, session, "open-selected-space");
            var chat = Assert.IsType<NewChatPage>(home.SpaceWorkspaceContent);
            var changed = chat.CurrentConversation with { Id = Guid.NewGuid(), Title = "Held actual target" };
            rig.Row = changed;
            rig.ReadMessages = id => { Assert.Equal(changed.Id, id); entered.TrySetResult(); return messages.Task; };
            transition = chat.LoadConversationAsync(changed);
            await entered.Task.WaitAsync(Bound);
            Assert.True(details.TryEdit(SpaceDetailsField.Name, "Hidden unsaved draft"));
            var preparation = session.PrepareCloseAsync();
            await Decision(details); Assert.Same(details, root.Content);
            messages.TrySetResult([]); await transition.WaitAsync(Bound);
            Assert.Equal(changed.Id, chat.CurrentConversation.Id);
            Click(details.DecisionHost!, "space-details-close-cancel");
            Assert.Null(await preparation.WaitAsync(Bound));
            // The retained conversation is restored inside the same Spaces Home rail layout it
            // was mounted in, not as a separate root view.
            Assert.Same(chat, home.SpaceWorkspaceContent);
            var expected = changed.SpaceId is { } actualSpace
                ? $"9to1://spaces/selected/{actualSpace:D}/chat/{changed.Id:D}"
                : $"9to1://spaces/chat/{changed.Id:D}";
            Assert.Equal(expected, session.Address.Value);
            var original = new TabSessionSnapshot(tab, "spaces", "Preserved", "{\"extension\":\"keep\"}", "history", 2,
                null, false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            var saved = UnifiedWorkspaceTabState.Merge(original, new BrowserTabState(tab, session.Title, session.Address.Value,
                BrowserTabPrivacy.Standard, string.Empty, DateTimeOffset.UtcNow) { ProfileId = actor });
            Assert.Equal(tab, saved.Id); Assert.Equal(original.NavigationJson, saved.NavigationJson);
            Assert.Equal("keep", System.Text.Json.Nodes.JsonNode.Parse(saved.StateJson)!["extension"]!.GetValue<string>());
            Assert.True(UnifiedWorkspaceTabState.TryProject(saved, actor, out var restored));
            Assert.Equal(expected, restored!.Address);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            messages.TrySetResult([]);
            try { if (transition is not null) await transition.WaitAsync(Bound); }
            catch (Exception failure) { primary = primary is null ? failure : new AggregateException(primary, failure); System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw(); throw; }
        finally { await Cleanup(session, rig, window, primary); }
        }
    }

    [AvaloniaFact]
    public async Task Actual_active_command_refuses_its_own_close_but_the_same_expired_captured_context_can_join_external_close()
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true);
        var target = await rig.Spaces.CreateAsync("Live execution lease"); var actor = await Start(rig);
        var session = rig.Session("9to1://spaces", profileId: actor);
        Window? window = null; Exception? primary = null;
        ExecutionContext? captured = null; Exception? refusal = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView); window = Mount(root);
            var home = Assert.IsType<SpacesHomePage>(root.Content);
            session.StateChanged += (_, _) =>
            {
                if (session.Address.Value != $"9to1://spaces/builder/{target.Id:D}") return;
                captured = ExecutionContext.Capture();
                refusal = Record.Exception(() => { _ = session.CloseAndDrainAsync(); });
            };
            await home.Controller.SelectSpaceAsync(target.Id); await Open(home, session, "edit-space-details");
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.NotNull(captured); Assert.Null(session.OriginalClose);
            Assert.True(session.OriginalQueuedNavigation!.IsCompletedSuccessfully);
            Task? close = null;
            ExecutionContext.Run(captured, _ => close = session.CloseAndDrainAsync(), null);
            Assert.Same(close, session.OriginalClose); Assert.Same(close, session.CloseAndDrainAsync());
            await close!.WaitAsync(Bound);
            Assert.True(session.CanRetireOriginalView);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await Cleanup(session, rig, window, primary); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_queued_read_failure_stays_faulted_and_only_observes_status_for_the_same_current_selection(bool selectNewer)
    {
        var store = new HeldSettings();
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, settings: store);
        var first = await rig.Spaces.CreateAsync("Failing queued target"); var second = await rig.Spaces.CreateAsync("Newer selection");
        var actor = await Start(rig); var session = rig.Session("9to1://spaces", profileId: actor);
        var failure = new IOException("Exact acquired queued registry read failed");
        Window? window = null; Exception? primary = null; Task? driver = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView); window = Mount(root);
            var home = Assert.IsType<SpacesHomePage>(root.Content);
            await home.Controller.SelectSpaceAsync(first.Id);
            var externalContext = ExecutionContext.Capture()!;
            Exception? reentrantRefusal = null;
            var failureCallbacks = 0;
            session.StateChanged += (_, _) =>
            {
                if (!home.Controller.State.StatusMessage.Contains(failure.Message, StringComparison.Ordinal)) return;
                failureCallbacks++;
                // An actual source callback cannot erase its physical provenance by
                // restoring an earlier execution context before requesting its own close.
                ExecutionContext.Run(externalContext, _ =>
                    reentrantRefusal = Record.Exception(() => { _ = session.CloseAndDrainAsync(); }), null);
            };
            store.SkipReadsBeforeHold = 1; // Same canonical command-target resolution succeeds; its separately owned route read is held.
            store.HoldNextRead = true;
            var prior = session.OriginalQueuedNavigation; Click(home.OriginalHost, "edit-space-details");
            driver = await Navigation(session, prior);
            await store.Entered.Task.WaitAsync(Bound);
            Assert.False(driver.IsCompleted); Assert.Same(home, root.Content);
            if (selectNewer) await home.Controller.SelectSpaceAsync(second.Id);
            var currentStatus = home.Controller.State.StatusMessage;
            store.Release.TrySetException(failure);
            var observed = await Assert.ThrowsAnyAsync<Exception>(() => driver.WaitAsync(Bound));
            Assert.True(Contains(observed, failure)); Assert.True(driver.IsFaulted);
            Assert.Same(home, root.Content); Assert.Null(session.OriginalClose);
            if (selectNewer)
            {
                Assert.Equal(second.Id, home.Controller.State.Selection!.Id);
                Assert.Equal(currentStatus, home.Controller.State.StatusMessage);
            }
            else
            {
                Assert.Contains(failure.Message, home.Controller.State.StatusMessage, StringComparison.Ordinal);
                Assert.Equal(1, failureCallbacks);
                Assert.IsType<InvalidOperationException>(reentrantRefusal);
            }
        }
        catch (Exception cause) { primary = cause; throw; }
        finally
        {
            store.Release.TrySetException(failure);
            Exception? joinFailure = null;
            if (driver is not null)
            {
                var retained = await Record.ExceptionAsync(() => driver.WaitAsync(Bound));
                if (retained is not null && !Contains(retained, failure)) joinFailure = retained;
            }
            var prior = primary is null ? joinFailure : joinFailure is null ? primary : new AggregateException(primary, joinFailure);
            await Cleanup(session, rig, window, prior, failure);
            if (joinFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(prior!).Throw();
        }
    }

    [AvaloniaFact]
    public async Task Actual_second_editor_preparation_fault_restores_first_lease_and_retains_the_source_in_failed_session_close()
    {
        var store = new HeldSettings();
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, settings: store);
        var first = await rig.Spaces.CreateAsync("First leased editor"); var second = await rig.Spaces.CreateAsync("Failing second editor");
        var actor = await Start(rig); var session = rig.Session($"9to1://spaces/details/{first.Id:D}", profileId: actor);
        var failure = new IOException("Exact decision discard read failed");
        Window? window = null; Exception? primary = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView); window = Mount(root);
            var firstPage = Assert.IsType<SpaceDetailsPage>(root.Content);
            await Back(firstPage, session);
            var home = Assert.IsType<SpacesHomePage>(root.Content);
            await home.Controller.SelectSpaceAsync(second.Id); await Open(home, session, "edit-space-details");
            var secondPage = Assert.IsType<SpaceBuilderPage>(root.Content);
            Assert.True(secondPage.TryEdit(SpaceBuilderField.Name, "Unsaved source fault"));
            var preparation = session.PrepareCloseAsync(); await Decision(secondPage);
            Assert.False(firstPage.TryEdit(SpaceDetailsField.Description, "First is really leased"));
            store.FailNextRead = failure;
            Click(secondPage.DecisionHost!, "space-details-close-discard");
            var observed = await Assert.ThrowsAnyAsync<Exception>(() => preparation.WaitAsync(Bound));
            Assert.True(Contains(observed, failure)); Assert.True(preparation.IsFaulted);
            Assert.Same(secondPage, root.Content);
            Assert.True(firstPage.TryEdit(SpaceDetailsField.Description, "First restored after sibling fault"));
            Assert.True(secondPage.TryEdit(SpaceBuilderField.Description, "Second restored after actual fault"));
            Assert.Null(firstPage.OriginalClose); Assert.Null(secondPage.OriginalClose); Assert.Null(session.OriginalClose);
            Assert.Equal("Failing second editor", (await rig.Spaces.ReadExistingAsync(second.Id))!.Name);
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await Cleanup(session, rig, window, primary, failure); }
    }

    [AvaloniaFact]
    public async Task Unpublished_session_never_yields_a_strong_preparation_or_fabricates_initialization()
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true);
        var actor = await Start(rig); var session = rig.Session("9to1://spaces", profileId: actor);
        Exception? primary = null;
        try
        {
            Assert.Null(await session.PrepareCloseAsync().WaitAsync(Bound));
            Assert.Null(session.OriginalInitialization); Assert.Null(session.OriginalClose);
            Assert.False(session.IsPublicationCurrent);
            Assert.Equal(0, rig.ConversationWrites); Assert.Equal(0, rig.ModelReads);
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await Cleanup(session, rig, null, primary); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Details_restoration_refuses_private_or_other_profile_before_publishing_an_editor(bool otherProfile)
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true);
        var space = await rig.Spaces.CreateAsync("Profile-scoped fixture"); var actor = await Start(rig);
        var session = rig.Session($"9to1://spaces/details/{space.Id:D}", profileId: otherProfile ? actor + ".other" : actor,
            privacy: otherProfile ? BrowserTabPrivacy.Standard : BrowserTabPrivacy.Private);
        Exception? primary = null; Exception? refused = null;
        try
        {
            refused = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.InitializeAsync().WaitAsync(Bound));
            Assert.False(session.IsPublicationCurrent);
            Assert.Equal($"9to1://spaces/details/{space.Id:D}", session.Address.Value);
            Assert.Equal(0, rig.ConversationWrites); Assert.Equal(0, rig.ModelReads);
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await Cleanup(session, rig, null, primary, refused); }
    }

    [AvaloniaFact]
    public async Task External_session_retirement_joins_the_pending_real_decision_before_detaching_owners()
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true);
        var space = await rig.Spaces.CreateAsync("Saved decision target"); var actor = await Start(rig);
        var session = rig.Session($"9to1://spaces/details/{space.Id:D}", profileId: actor);
        Window? window = null; Exception? primary = null;
        Task<HavenOS.Apps.Browse.IBrowseAppContentClosePreparation?>? prepare = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView); window = Mount(root);
            var page = Assert.IsType<SpaceDetailsPage>(root.Content);
            Assert.True(page.TryEdit(SpaceDetailsField.Name, "Pending unsaved decision"));
            prepare = session.PrepareCloseAsync(); await Decision(page);
            var actualDecision = page.DecisionHost!;
            var close = session.CloseAndDrainAsync();
            Assert.Same(close, session.OriginalClose);
            await close.WaitAsync(Bound);
            Assert.Null(await prepare.WaitAsync(Bound));
            Assert.True(actualDecision.OriginalClose!.IsCompletedSuccessfully);
            Assert.True(page.OriginalClose!.IsCompletedSuccessfully);
            Assert.Null(root.Content);
            Assert.Equal("Saved decision target", (await rig.Spaces.ReadExistingAsync(space.Id))!.Name);
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await Cleanup(session, rig, window, primary); }
    }

    private static async Task<string> Start(SpacesConversationRouteTests.Rig rig)
    {
        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        return (await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None))!.ProfileId;
    }
    private static Window Mount(Control root)
    { var window = new Window { Content = root, Width = 1100, Height = 900 }; window.Show(); return window; }
    private static async Task Cleanup(SpacesBrowseAppContentSession session, SpacesConversationRouteTests.Rig rig, Window? window, Exception? primary = null, Exception? expectedCloseFailure = null)
    {
        var failures = new List<Exception>();
        try
        {
            if (expectedCloseFailure is null) await session.CloseAndDrainAsync().WaitAsync(Bound);
            else
            {
                var failed = await Assert.ThrowsAnyAsync<Exception>(() => session.CloseAndDrainAsync().WaitAsync(Bound));
                Assert.True(Contains(failed, expectedCloseFailure));
                Assert.False(session.CanRetireOriginalView); // This expected-negative check never labels retirement clean.
            }
        }
        catch (Exception cause) { failures.Add(cause); }
        if (window is not null)
        {
            try { window.Content = null; } catch (Exception cause) { failures.Add(cause); }
            try { window.Close(); Dispatcher.UIThread.RunJobs(); } catch (Exception cause) { failures.Add(cause); }
        }
        try { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); }
        catch (Exception cause) { failures.Add(cause); }
        if (failures.Count == 0) return; // The original body exception, if any, is still propagating.
        if (primary is not null) failures.Insert(0, primary);
        if (failures.Count == 1 && failures[0] is not OperationCanceledException)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException("The check and independent owner cleanup retained their actual causes.", failures);
    }
    private static async Task Open(SpacesHomePage page, SpacesBrowseAppContentSession session, string button)
    { var prior = session.OriginalQueuedNavigation; Click(page.OriginalHost, button); await (await Navigation(session, prior)).WaitAsync(Bound); }
    private static async Task Back(SpaceDetailsPage page, SpacesBrowseAppContentSession session)
    { var prior = session.OriginalQueuedNavigation; Click(page.EditorHost, "space-details-back"); await (await Navigation(session, prior)).WaitAsync(Bound); }
    // The reviewed Home manage route publishes the one canonical Space surface (SPUI-201), so the
    // Builder's own back action is the authored return path exercised by these route checks.
    private static async Task Back(SpaceBuilderPage page, SpacesBrowseAppContentSession session)
    { var prior = session.OriginalQueuedNavigation; Click(page.Host, "space-builder-back"); await (await Navigation(session, prior)).WaitAsync(Bound); }
    private static async Task<Task> Navigation(SpacesBrowseAppContentSession session, Task? prior)
    {
        using var deadline = new CancellationTokenSource(Bound);
        while (session.OriginalQueuedNavigation is not { } actual || ReferenceEquals(actual, prior))
        { deadline.Token.ThrowIfCancellationRequested(); await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask().WaitAsync(deadline.Token); }
        return session.OriginalQueuedNavigation!;
    }
    private static async Task Decision(SpaceDetailsPage page)
    {
        using var deadline = new CancellationTokenSource(Bound);
        while (page.DecisionHost?.Availability?.State != CuiSceneAvailabilityState.Ready || !ReferenceEquals(page.Content, page.DecisionHost))
        { deadline.Token.ThrowIfCancellationRequested(); await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask().WaitAsync(deadline.Token); }
    }
    private static async Task Decision(SpaceBuilderPage page)
    {
        using var deadline = new CancellationTokenSource(Bound);
        while (page.DecisionHost?.Availability?.State != CuiSceneAvailabilityState.Ready || !ReferenceEquals(page.Content, page.DecisionHost))
        { deadline.Token.ThrowIfCancellationRequested(); await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask().WaitAsync(deadline.Token); }
    }
    private static T Find<T>(CuiSceneHost host, string id) where T : Control
    {
        var window = Assert.IsType<Window>(TopLevel.GetTopLevel(host));
        for (var pass = 0; pass < 3; pass++)
        { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
        var root = Assert.IsAssignableFrom<Control>(host.Content);
        return Assert.Single(root.GetVisualDescendants().Prepend(root).OfType<T>(), control => control.Name == id || AutomationProperties.GetAutomationId(control) == id);
    }
    private static TextBox Input(CuiSceneHost host, string id) => Find<TextBox>(host, id);
    private static void Click(CuiSceneHost host, string id)
    { var button = Find<Button>(host, id); Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static bool Contains(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException group && group.InnerExceptions.Any(inner => Contains(inner, expected)) ||
        actual.InnerException is { } next && Contains(next, expected);

    private sealed class HeldSettings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        internal bool HoldNextRead;
        internal int SkipReadsBeforeHold;
        internal Exception? FailNextRead;
        internal object? Captured;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<object?> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        {
            var value = _values.GetValueOrDefault(key);
            if (key == "spaces.registry" && FailNextRead is { } failure) { FailNextRead = null; throw failure; }
            if (key == "spaces.registry" && HoldNextRead)
            {
                if (SkipReadsBeforeHold > 0) { SkipReadsBeforeHold--; return value as T; }
                HoldNextRead = false; Captured = value; Entered.TrySetResult();
                return await Release.Task as T; // Actual acquired source deliberately ignores cancellation until released.
            }
            return value as T;
        }
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken token) { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => throw new NotSupportedException();
    }
}
