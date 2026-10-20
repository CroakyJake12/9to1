using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Avalonia.Automation;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NativeButton = Avalonia.Controls.Button;
using NativeTextBox = Avalonia.Controls.TextBox;
using NativeWindow = Avalonia.Controls.Window;
using Haven.Application;
using Haven.Core;
using Haven.Desktop;
using Haven.Desktop.Events;
using Haven.Desktop.Overlay;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.UI;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.Browse.Host.Tests;

public sealed class OverlayChatInitializationTests
{
    // This is the production NewChatPage, factory and original-work lifetime, with
    // only external I/O controlled. The held model read exposes the construction race.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conversation_selection_waits_for_actual_page_initialization_then_settled_page_closes(bool restore)
    {
        using var rig = new Rig();
        Task<NewChatPage?> creating = restore
            ? rig.Factory.RestoreAsync(rig.Saved.Id, CancellationToken.None)
            : AsNullable(rig.Factory.CreateNewAsync(CancellationToken.None));
        NewChatPage? page = null;
        try
        {
            await rig.ModelsEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Fresh persists its new target; Restore reads the selected target's history.
            // Neither may happen while construction still owns its earlier target.
            Assert.Equal(0, rig.ConversationSelections);
            Assert.False(creating.IsCompleted);
            rig.Models.TrySetResult([]);
            page = await creating.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(page);
            Assert.True(page.OriginalInitialization.IsCompletedSuccessfully);
            Assert.Equal(1, rig.ConversationSelections);
            if (restore) Assert.Equal(rig.Saved.Id, page.ConversationId);
            // This case models a settled UI, not closing while queued background scroll
            // publication is still pending (that separate failure remains unaccepted).
            await SettleAdmittedPageOriginalsAsync(page).WaitAsync(TimeSpan.FromSeconds(10));
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            rig.Models.TrySetResult([]);
            page ??= await creating.WaitAsync(TimeSpan.FromSeconds(10));
            if (page is not null) await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    // Overlay chat must publish the SAME routed model catalogue as the normal Haven chat page
    // (IProviderModelClient), never the distinct raw Ollama transport. The rig registers the
    // routed source and the raw transport as SEPARATE identities, so a faulty Overlay factory
    // that resolved the raw Ollama slot would bind the raw transport and be detected here.
    [AvaloniaFact]
    public async Task Overlay_chat_binds_the_same_routed_model_client_as_normal_chat()
    {
        using var rig = new Rig();
        rig.Models.TrySetResult([Model("llama3")]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Same(rig.Routed, page.ModelClientSource);
            Assert.True(page.ModelClientSource is IProviderModelClient);
            Assert.Same(rig.Routed, (IProviderModelClient)page.ModelClientSource);
            // Every discovery read went through the shared routed source; the raw Ollama
            // transport was never touched by the Overlay composition.
            Assert.Equal(1, rig.ModelReadAttempts);
            Assert.Equal(0, rig.RawCallCount);
        }
        finally
        {
            await DrainBackgroundPageOriginalsAsync(page);
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    // The typed local-model recovery action must open a real management route while the
    // conversation, draft and refusal state survive; it never clears the draft or fabricates
    // a model or a send admission.
    [AvaloniaFact]
    public async Task Manage_local_models_action_routes_while_draft_and_refusal_survive()
    {
        using var rig = new Rig();
        rig.Models.TrySetException(new HttpRequestException("Connection refused (127.0.0.1:11434)",
            new SocketException((int)SocketError.ConnectionRefused)));
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        var requested = false;
        page.ManageLocalModelsRequested += (_, _) => requested = true;
        try
        {
            Assert.Equal(ChatModelAvailability.Unavailable, page.ModelAvailability);
            page.SetDraft("offline model recovery draft");
            page.RequestManageLocalModels();
            Assert.True(requested);
            Assert.Equal("offline model recovery draft", ComposerText(page));
            Assert.False(SendEnabled(page));
        }
        finally
        {
            await DrainBackgroundPageOriginalsAsync(page);
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    // The recovery action is a REAL rendered CUI button (ChatRecovery.cui) mounted under the chat
    // scene's RecoveryHost native host. This tests drives that actual button and verifies the host
    // seam routes to the genuine Home Models destination while the draft and truthful refusal survive.
    [AvaloniaFact]
    public async Task Rendered_recovery_button_routes_to_home_models_while_draft_survives()
    {
        using var rig = new Rig();
        rig.Models.TrySetException(new HttpRequestException("Connection refused (127.0.0.1:11434)",
            new SocketException((int)SocketError.ConnectionRefused)));
        AppContentAddress? routedDestination = null;
        rig.Factory.WireHost = page => page.ManageLocalModelsRequested += (_, _) =>
        {
            // Mirror the production Spaces host seam: request the genuine Home Models destination.
            routedDestination = AppContentAddress.Parse("9to1://home/models");
        };
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        rig.Mount(page);
        try
        {
            Assert.Equal(ChatModelAvailability.Unavailable, page.ModelAvailability);
            page.SetDraft("offline model recovery draft");
            var recoveryButton = RecoveryButton(page);
            Assert.True(recoveryButton.IsEffectivelyVisible);
            recoveryButton.RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
            await Scene(page).RecoveryView.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(routedDestination);
            Assert.Equal("home", routedDestination.Authority);
            Assert.Equal(new[] { "models" }, routedDestination.ResourceSegments);
            // Draft and truthful refusal survive the recovery route.
            Assert.Equal("offline model recovery draft", ComposerText(page));
            Assert.False(SendEnabled(page));
        }
        finally
        {
            await DrainBackgroundPageOriginalsAsync(page);
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    // The rendered recovery affordance is offered exactly while model discovery is a truthful
    // terminal unavailable observation; it stays hidden when a model is available.
    [AvaloniaFact]
    public async Task Recovery_button_is_hidden_when_models_are_available()
    {
        using var rig = new Rig();
        rig.Models.TrySetResult([Model("llama3")]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        rig.Mount(page);
        try
        {
            Assert.Equal(ChatModelAvailability.Available, page.ModelAvailability);
            Assert.False(RecoveryButton(page).IsEffectivelyVisible);
        }
        finally
        {
            await DrainBackgroundPageOriginalsAsync(page);
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public async Task Cancelled_creation_joins_held_original_model_read_before_returning_failure()
    {
        using var rig = new Rig();
        using var cancellation = new CancellationTokenSource();
        var creating = rig.Factory.CreateNewAsync(cancellation.Token);
        try
        {
            await rig.ModelsEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await rig.ModelsCancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(creating.IsCompleted); // The actual I/O deliberately has not acknowledged stop.
            Assert.Equal(0, rig.ConversationSelections);
            rig.Models.TrySetResult([]);
            var failure = await Record.ExceptionAsync(() => creating.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.NotNull(failure);
            Assert.Contains(AllCauses(failure), error => error is OperationCanceledException cancelled
                && cancelled.CancellationToken == cancellation.Token);
            Assert.Equal(0, rig.ConversationSelections);
        }
        finally
        {
            rig.Models.TrySetResult([]);
            await Record.ExceptionAsync(() => creating.WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    // The held second read exposes the discovery-admission visual gate and the stale
    // selector without constructing a second rig or a fake page.
    [AvaloniaFact]
    public async Task Held_retry_publishes_the_visual_gate_and_a_stale_selector_cannot_send()
    {
        using var rig = new Rig();
        NewChatPage? page = null;
        HeldModelRead? held = null;
        var creating = rig.Factory.CreateNewWithConstructionJoinAsync(
            DrainBackgroundPageOriginalsAsync, CancellationToken.None);
        try
        {
            await rig.ModelsEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var offered = Model("llama3");
            rig.Models.TrySetResult([offered]);
            page = await creating.WaitAsync(TimeSpan.FromSeconds(10));
            rig.Mount(page);

            Assert.Equal(ChatModelAvailability.Available, page.ModelAvailability);
            Assert.Equal("llama3", page.SelectedModelName);
            // The current, still-offered catalogue entry is fully actionable with a genuine
            // eligible draft entered (an empty composer is not an actionable send).
            page.SelectModel(offered);
            page.SetDraft("eligible draft");
            Assert.True(SendEnabled(page));

            // A held retry: the provider read never settles until this test releases it.
            held = new HeldModelRead();
            rig.NextModelRead = held;
            var refresh = page.RefreshModelsAsync();
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => page.ModelAvailability == ChatModelAvailability.Discovering);

            // Discovery admission publishes the truthful held state BEFORE the provider await,
            // so the previously enabled Send control is visibly gated during the retry.
            Assert.False(SendEnabled(page));

            // An older picker's descriptor cannot promote a held retry.
            page.SelectModel(offered);
            Assert.Equal(ChatModelAvailability.Discovering, page.ModelAvailability);
            Assert.False(SendEnabled(page));

            // A queued instruction stays queued: the backend gate holds and the draft survives.
            await page.SubmitOriginalInstructionAsync("preserved draft").WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("preserved draft", ComposerText(page));

            // The held read settles as a real expected provider-unavailability observation.
            var refused = new HttpRequestException("Connection refused (127.0.0.1:11434)",
                new SocketException((int)SocketError.ConnectionRefused));
            held.Result.SetException(refused);
            await refresh.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(ChatModelAvailability.Unavailable, page.ModelAvailability);
            Assert.Same(refused, page.ModelAvailabilityCause);
            Assert.False(SendEnabled(page));

            // The stale selector neither promotes availability nor clears the real cause.
            page.SelectModel(offered);
            Assert.Equal(ChatModelAvailability.Unavailable, page.ModelAvailability);
            Assert.Same(refused, page.ModelAvailabilityCause);
            Assert.False(SendEnabled(page));

            // No pending instruction is dispatched on a stale selector's strength.
            await Task.Delay(200);
            Assert.False(page.IsSending);
            Assert.False(page.HasStarted);
            Assert.Equal("preserved draft", ComposerText(page));

            await DrainBackgroundPageOriginalsAsync(page);
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            rig.Models.TrySetResult([]);
            // A failed assertion must not leave the deliberately held provider read blocking the
            // cleanup join: release the SAME held read so the actual assertion stays the reported
            // failure instead of being masked by a close timeout.
            held?.Result.TrySetResult([]);
            page ??= await creating.WaitAsync(TimeSpan.FromSeconds(10));
            if (page is not null) await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public async Task Successful_catalogue_replacement_refuses_an_older_pickers_removed_descriptor()
    {
        using var rig = new Rig();
        NewChatPage? page = null;
        HeldModelRead? held = null;
        Task? refresh = null;
        Exception? originalFailure = null;
        var creating = rig.Factory.CreateNewWithConstructionJoinAsync(
            DrainBackgroundPageOriginalsAsync, CancellationToken.None);
        try
        {
            await rig.ModelsEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var removed = Model("llama3");
            rig.Models.TrySetResult([removed]);
            page = await creating.WaitAsync(TimeSpan.FromSeconds(10));
            rig.Mount(page);
            Assert.Equal("llama3", page.SelectedModelName);

            // A newer successful catalogue no longer offers the older picker's model.
            var replacement = Model("qwen2");
            held = new HeldModelRead();
            rig.NextModelRead = held;
            refresh = page.RefreshModelsAsync();
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            held.Result.SetResult([replacement]);
            await refresh.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(ChatModelAvailability.Available, page.ModelAvailability);
            Assert.Equal("qwen2", page.SelectedModelName);
            page.SelectModel(replacement);
            page.SetDraft("eligible draft");
            Assert.True(SendEnabled(page)); // The current catalogue entry stays actionable.

            // The still-open old picker hands back its now-removed descriptor. The user's
            // configuration is preserved, but the stale descriptor never becomes actionable.
            page.SelectModel(removed);
            Assert.Equal("llama3", page.SelectedModelName);
            Assert.Equal(ChatModelAvailability.Available, page.ModelAvailability);
            Assert.False(SendEnabled(page));

            await page.SubmitOriginalInstructionAsync("stale picker gate").WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(200);
            Assert.False(page.IsSending);
            Assert.False(page.HasStarted);
            Assert.Equal("stale picker gate", ComposerText(page)); // Not dispatched; no send occurred.

            await DrainBackgroundPageOriginalsAsync(page);
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception failure)
        {
            originalFailure = failure;
            throw;
        }
        finally
        {
            rig.Models.TrySetResult([]);
            // Release the SAME read even if entry/refresh assertions fail, then independently
            // join it and the acquired page. Cleanup failures retain the original assertion.
            held?.Result.TrySetResult([]);
            var refreshFailure = refresh is null ? null
                : await Record.ExceptionAsync(() => refresh.WaitAsync(TimeSpan.FromSeconds(10)));
            var closeFailure = await Record.ExceptionAsync(async () =>
            {
                page ??= await creating.WaitAsync(TimeSpan.FromSeconds(10));
                if (page is not null) await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
            });
            if (refreshFailure is not null || closeFailure is not null)
                throw new AggregateException("Catalogue test work or cleanup failed.",
                    new[] { originalFailure, refreshFailure, closeFailure }.OfType<Exception>());
        }
    }

    [AvaloniaFact]
    public async Task Configured_unavailable_endpoint_stays_honest_across_fresh_restore_and_closes_cleanly()
    {
        using var rig = new Rig();
        NewChatPage? page = null;
        var refused = new HttpRequestException("Connection refused (127.0.0.1:11434)",
            new SocketException((int)SocketError.ConnectionRefused));
        // The configured provider endpoint fails before the page is constructed. This is the
        // settled failed read from a complete terminal observation, not a close/cancellation
        // fault: it must be reported honestly and must not become a retirement failure.
        rig.Models.TrySetException(refused);
        var creating = rig.Factory.CreateNewWithConstructionJoinAsync(
            DrainBackgroundPageOriginalsAsync, CancellationToken.None);
        try
        {
            page = await creating.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ChatModelAvailability.Unavailable, page.ModelAvailability);
            rig.Mount(page);
            Assert.Same(refused, page.ModelAvailabilityCause);
            Assert.False(SendEnabled(page));

            // Model-backed work is refused while the draft and navigation stay usable.
            page.SetDraft("offline draft");
            Assert.Equal("offline draft", ComposerText(page));
            Assert.False(SendEnabled(page));
            await page.SubmitOriginalInstructionAsync("offline draft").WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(200);
            Assert.False(page.IsSending);
            Assert.False(page.HasStarted);
            Assert.Equal("offline draft", ComposerText(page));
            var drafting = page.CurrentConversation;
            await WaitUntilAsync(() => rig.Drafts.Draft(drafting.Id)?.Content == "offline draft");

            // Fresh preserves the unavailable observation and the exact cause. The outgoing
            // draft moves to the canonical store (never silently discarded) and the new
            // conversation starts empty.
            await page.StartFreshConversationAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ChatModelAvailability.Unavailable, page.ModelAvailability);
            Assert.Same(refused, page.ModelAvailabilityCause);
            Assert.Equal(string.Empty, ComposerText(page));
            Assert.Equal("offline draft", rig.Drafts.Draft(drafting.Id)?.Content);
            Assert.False(SendEnabled(page));

            // Loading the drafting conversation restores its canonical text even though no model
            // is available. The safety publish on load must not re-enable Send from that text.
            await page.LoadConversationAsync(drafting).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(drafting.Id, page.ConversationId);
            Assert.Equal("offline draft", ComposerText(page));
            Assert.Equal(ChatModelAvailability.Unavailable, page.ModelAvailability);
            Assert.Same(refused, page.ModelAvailabilityCause);
            Assert.False(SendEnabled(page));

            // A truthful unavailable observation is inspectable but does not block a clean
            // physical retirement: the failed read was fully settled and classified.
            await DrainBackgroundPageOriginalsAsync(page);
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            rig.Models.TrySetResult([]);
            page ??= await creating.WaitAsync(TimeSpan.FromSeconds(10));
            if (page is not null) await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public async Task Typed_text_draft_persists_and_restores_in_the_same_conversation()
    {
        using var rig = new Rig();
        rig.Models.TrySetResult([Model("llama3")]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        var conversation = page.CurrentConversation;
        try
        {
            page.SetDraft("half-written thought");
            await WaitUntilAsync(() => rig.Drafts.Draft(conversation.Id)?.Content == "half-written thought");
            var saved = rig.Drafts.Draft(conversation.Id)!;
            Assert.Equal(conversation.Id, saved.ConversationId);
            Assert.NotNull(saved.BranchId);
            Assert.NotEqual(Guid.Empty, saved.BranchId!.Value);

            await DrainBackgroundPageOriginalsAsync(page);
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

            // A SECOND real page opening the SAME conversation restores the canonical draft and
            // keeps the existing conversation identifier. A second page on this rig performs one
            // more real model read, which this test explicitly expects.
            rig.ExpectLaterModelRead([Model("llama3")]);
            var restored = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                await restored.LoadConversationAsync(conversation).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(conversation.Id, restored.ConversationId);
                Assert.Equal("half-written thought", ComposerText(restored));
            }
            finally
            {
                await DrainBackgroundPageOriginalsAsync(restored);
                await restored.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        finally
        {
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public async Task Held_draft_write_is_joined_by_close_never_cancelled_or_falsely_clean()
    {
        using var rig = new Rig();
        rig.Models.TrySetResult([Model("llama3")]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        var conversation = page.CurrentConversation;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? closing = null;
        try
        {
            rig.Drafts.HoldSave(held);
            page.SetDraft("held draft");
            await WaitUntilAsync(() => rig.Drafts.SaveCount > 0);

            // The real durability write is still held open. The REAL shell retirement order is
            // exercised here: the page requests retirement and returns, and the external owner
            // joins CloseAndDrainAsync afterwards. Close must JOIN that SAME write rather than
            // cancel it, and must not report a clean receipt while it is still pending.
            page.RequestRetirement();
            closing = page.CloseAndDrainAsync();
            await Task.Delay(100);
            Assert.False(closing.IsCompleted);
            Assert.Null(rig.Drafts.Draft(conversation.Id));

            held.TrySetResult();
            await closing.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("held draft", rig.Drafts.Draft(conversation.Id)?.Content);
        }
        finally
        {
            held.TrySetResult();
            rig.Drafts.ReleaseHeldSave();
            if (closing is not null) await closing.WaitAsync(TimeSpan.FromSeconds(10));
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public async Task Failed_draft_write_retains_its_real_cause_and_never_closes_clean()
    {
        using var rig = new Rig();
        rig.Models.TrySetResult([Model("llama3")]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var failure = new IOException("draft store offline");
            rig.Drafts.FailSave(failure);
            page.SetDraft("must not be silently lost");
            await WaitUntilAsync(() => rig.Drafts.SaveCount > 0);

            var error = await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.NotNull(error);
            Assert.Contains(failure, AllCauses(error!));
        }
        finally
        {
            await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    [AvaloniaFact]
    public async Task Held_older_draft_write_cannot_overwrite_the_newer_user_text()
    {
        using var rig = new Rig();
        rig.Models.TrySetResult([Model("llama3")]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        var conversation = page.CurrentConversation;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // The OLDER snapshot is held open while the user keeps typing. Every canonical draft
            // mutation shares ONE ordering chain, so the older write can never land after (and
            // overwrite) the newer user text.
            rig.Drafts.HoldSave(held);
            page.SetDraft("older text");
            await WaitUntilAsync(() => rig.Drafts.SaveCount == 1);
            page.SetDraft("newer text");
            await Task.Delay(100);
            Assert.Equal(1, rig.Drafts.SaveCount);          // the newer snapshot waits its turn
            Assert.Null(rig.Drafts.Draft(conversation.Id)); // nothing has landed yet

            rig.Drafts.ReleaseHeldSave();
            await WaitUntilAsync(() => rig.Drafts.Draft(conversation.Id)?.Content == "newer text");
            Assert.Equal("newer text", rig.Drafts.Draft(conversation.Id)!.Content);
            Assert.Equal(2, rig.Drafts.SaveCount); // the older write landed FIRST, then the newer one
        }
        finally
        {
            rig.Drafts.ReleaseHeldSave();
            await DrainBackgroundPageOriginalsAsync(page);
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public async Task Refused_send_keeps_the_unsent_text_and_its_canonical_draft()
    {
        using var rig = new Rig();
        rig.Models.TrySetResult([Model("llama3")]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        rig.Mount(page);
        var conversation = page.CurrentConversation;
        try
        {
            page.SetDraft("unsent text");
            await WaitUntilAsync(() => rig.Drafts.Draft(conversation.Id)?.Content == "unsent text");
            Assert.True(SendEnabled(page));

            // A genuine send whose admission fails BEFORE any canonical user message is accepted:
            // the configured store/provider call is not available in this rig. The send's own
            // programmatic composer clear must not delete the canonical draft, and the text must
            // come back to the composer instead of being lost.
            rig.UnexpectedIo.Clear();
            await page.SubmitOriginalInstructionAsync("unsent text").WaitAsync(TimeSpan.FromSeconds(10));
            await DrainBackgroundPageOriginalsAsync(page);
            await Task.Delay(200);

            Assert.False(page.IsSending);
            // The send really ran: it cleared its own composer and reached the real storage
            // boundary, which this rig refuses before any accepted user message exists.
            Assert.NotEmpty(rig.UnexpectedIo);
            Assert.Null(rig.Drafts.Deleted);
            Assert.Equal("unsent text", rig.Drafts.Draft(conversation.Id)?.Content);
            Assert.Equal("unsent text", ComposerText(page));
        }
        finally
        {
            await DrainBackgroundPageOriginalsAsync(page);
            await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    [AvaloniaFact]
    public async Task Conversation_transition_flushes_only_the_outgoing_conversation_draft()
    {
        using var rig = new Rig();
        rig.Models.TrySetResult([Model("llama3")]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        var outgoing = page.CurrentConversation;
        try
        {
            page.SetDraft("text for the first chat");
            await WaitUntilAsync(() => rig.Drafts.Draft(outgoing.Id)?.Content == "text for the first chat");

            await page.LoadConversationAsync(rig.Saved).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(rig.Saved.Id, page.ConversationId);
            // The outgoing draft is flushed under its OWN conversation; the incoming target is
            // never polluted by a write redirected after the replacement.
            Assert.Equal("text for the first chat", rig.Drafts.Draft(outgoing.Id)?.Content);
            Assert.Null(rig.Drafts.Draft(rig.Saved.Id));
            Assert.Equal(string.Empty, ComposerText(page));
        }
        finally
        {
            await DrainBackgroundPageOriginalsAsync(page);
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public async Task Accepted_send_retires_exactly_the_draft_its_commit_consumed()
    {
        using var rig = new Rig();
        rig.AcceptMessages = true;
        rig.Models.TrySetResult([Model("llama3")]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        var conversation = page.CurrentConversation;
        var held = new HeldConversationWrite();
        try
        {
            page.SetDraft("text that will be accepted");
            await WaitUntilAsync(() => rig.Drafts.Draft(conversation.Id)?.Content == "text that will be accepted");

            // The real committed-input acknowledgement only fires after the actual conversation
            // and message writes both succeed. Hold the conversation write so the retirement is
            // decided by the genuine post-write acknowledgement, not by the send's own clear.
            rig.NextConversationWrite = held;
            await page.SubmitOriginalInstructionAsync("text that will be accepted").WaitAsync(TimeSpan.FromSeconds(10));
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(rig.Drafts.Deleted); // The send's own clear is not a deletion.
            held.Released.TrySetResult();

            await WaitUntilAsync(() => rig.Drafts.Deleted == conversation.Id);
            Assert.Single(rig.AddedMessages);
            Assert.Equal("text that will be accepted", rig.AddedMessages[0].Content);
            Assert.Null(rig.Drafts.Draft(conversation.Id));
            // The accepted input owns the text now: it must NOT be resurrected as an unsent draft.
            Assert.Equal(string.Empty, ComposerText(page));
        }
        finally
        {
            held.Released.TrySetResult();
            await DrainBackgroundPageOriginalsAsync(page);
            await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Null(rig.Drafts.Draft(conversation.Id));
        }
    }

    [AvaloniaFact]
    public async Task Accepted_send_joins_the_actual_held_draft_branch_resolution_before_retiring_it()
    {
        using var rig = new Rig();
        rig.AcceptMessages = true;
        rig.Models.TrySetResult([Model("llama3")]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        var conversation = page.CurrentConversation;
        var branch = rig.Drafts.SelectBranch(conversation.Id);
        var resolving = new HeldConversationWrite();
        var committing = new HeldConversationWrite();
        const string input = "Can you generate UI?";
        try
        {
            // The real autosave has acquired its actual branch read but has NOT resolved it.
            // The send must capture consumption during that interval, not only after Save entry.
            rig.Drafts.HoldCurrentBranch(resolving);
            page.SetDraft(input);
            await resolving.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(rig.Drafts.Draft(conversation.Id, branch));
            rig.NextConversationWrite = committing;
            await page.SubmitOriginalInstructionAsync(input).WaitAsync(TimeSpan.FromSeconds(10));
            await committing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            resolving.Released.TrySetResult();
            await WaitUntilAsync(() => rig.Drafts.Draft(conversation.Id, branch)?.Content == input);
            Assert.Null(rig.Drafts.Deleted);
            committing.Released.TrySetResult();
            await WaitUntilAsync(() => rig.Drafts.Deleted == conversation.Id);
            await DrainBackgroundPageOriginalsAsync(page);
            Assert.Equal(input, Assert.Single(rig.AddedMessages, message => message.Role == MessageRole.User).Content);
            Assert.Null(rig.Drafts.Draft(conversation.Id, branch));
            Assert.Equal(string.Empty, ComposerText(page));
        }
        finally
        {
            resolving.Released.TrySetResult();
            committing.Released.TrySetResult();
            try { await DrainBackgroundPageOriginalsAsync(page); }
            finally { await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        }
    }

    [AvaloniaFact]
    public async Task Newer_draft_typed_while_an_accepted_send_commits_is_never_retired()
    {
        using var rig = new Rig();
        rig.AcceptMessages = true;
        rig.Models.TrySetResult([Model("llama3")]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        var conversation = page.CurrentConversation;
        var held = new HeldConversationWrite();
        try
        {
            page.SetDraft("sent text A");
            await WaitUntilAsync(() => rig.Drafts.Draft(conversation.Id)?.Content == "sent text A");

            rig.NextConversationWrite = held;
            await page.SubmitOriginalInstructionAsync("sent text A").WaitAsync(TimeSpan.FromSeconds(10));
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // The user types a NEWER draft while the accepted send is still committing. The
            // commit's identity fence (conversation + revision + captured attachments) must refuse
            // to retire it: an older send can never delete newer user data.
            page.SetDraft("newer draft B");
            await WaitUntilAsync(() => rig.Drafts.Draft(conversation.Id)?.Content == "newer draft B");
            held.Released.TrySetResult();

            await WaitUntilAsync(() => rig.AddedMessages.Count == 1);
            await Task.Delay(100);
            Assert.Null(rig.Drafts.Deleted);
            Assert.Equal("newer draft B", rig.Drafts.Draft(conversation.Id)?.Content);
            Assert.Equal("newer draft B", ComposerText(page));
        }
        finally
        {
            held.Released.TrySetResult();
            await DrainBackgroundPageOriginalsAsync(page);
            await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    [AvaloniaFact]
    public async Task Rendered_empty_stop_remains_actionable_during_a_held_catalogue_refresh()
    {
        using var rig = new Rig();
        rig.AcceptMessages = true;
        var model = Model("llama3");
        rig.Models.TrySetResult([model]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        rig.Mount(page);
        var send = SendButton(page);
        var input = ComposerTextBox(page);
        var heldWrite = new HeldConversationWrite();
        var heldModels = new HeldModelRead();
        Task? refreshing = null;
        try
        {
            page.SetDraft("request to stop");
            await WaitUntilAsync(() => rig.Drafts.Draft(page.ConversationId)?.Content == "request to stop");
            rig.NextConversationWrite = heldWrite;
            await page.SubmitOriginalInstructionAsync("request to stop").WaitAsync(TimeSpan.FromSeconds(10));
            await heldWrite.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(page.IsSending);
            Assert.Equal(string.Empty, input.Text);

            var cancellation = Assert.IsAssignableFrom<CancellationTokenSource>(typeof(NewChatPage)
                .GetField("_sendCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
            Assert.False(cancellation.IsCancellationRequested);

            rig.NextModelRead = heldModels;
            refreshing = page.RefreshModelsAsync();
            await heldModels.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ChatModelAvailability.Discovering, page.ModelAvailability);
            Assert.False(refreshing.IsCompleted);

            // This is the current response's Stop, not admission of a new request through an
            // unavailable catalogue. The real click must reach its existing cancellation owner.
            Assert.True(send.IsEnabled);
            send.RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
            Assert.True(cancellation.IsCancellationRequested);
        }
        finally
        {
            heldModels.Result.TrySetResult([model]);
            if (refreshing is not null) await refreshing.WaitAsync(TimeSpan.FromSeconds(10));
            // Seal the page before releasing the held write, as in the queued-draft regression:
            // cleanup must not accidentally admit a new provider call after this check ends.
            page.RequestRetirement();
            var closing = page.CloseAndDrainAsync();
            heldWrite.Released.TrySetResult();
            await closing.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public async Task Queued_unsent_message_survives_close_in_its_canonical_draft()
    {
        using var rig = new Rig();
        rig.AcceptMessages = true;
        rig.Models.TrySetResult([Model("llama3")]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        rig.Mount(page);
        var input = ComposerTextBox(page);
        var send = SendButton(page);
        var conversation = page.CurrentConversation;
        var held = new HeldConversationWrite();
        try
        {
            page.SetDraft("first sent text");
            await WaitUntilAsync(() => rig.Drafts.Draft(conversation.Id)?.Content == "first sent text");

            rig.NextConversationWrite = held;
            await page.SubmitOriginalInstructionAsync("first sent text").WaitAsync(TimeSpan.FromSeconds(10));
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // The user types the next message while the first response is still in flight and
            // presses Send: the REAL queue-aware control parks it in the in-memory queue and
            // clears the composer. That queued text has NOT been sent, so it must survive the
            // close in the canonical draft instead of being deleted with the empty composer.
            // Exercise the physical input's synchronous change event and the already rendered
            // button in the same callback: no port setter or dispatcher pump can hide a lost edit.
            input.Text = "queued unsent text";
            Assert.True(send.IsEnabled);
            send.RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
            await WaitUntilAsync(() => ComposerText(page).Length == 0);
            Assert.Equal("queued unsent text", rig.Drafts.Draft(conversation.Id)?.Content);

            // Close while the first input's REAL conversation commit is still held. Retirement
            // seals the queue before that send can finish and dispatch another queued message.
            // The same close must join the held commit; no provider fault is waived as clean.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle);
            page.RequestRetirement();
            var closing = page.CloseAndDrainAsync();
            Assert.False(closing.IsCompleted);
            held.Released.TrySetResult();
            await WaitUntilAsync(() => rig.AddedMessages.Count == 1);

            await closing.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Single(rig.AddedMessages);
            Assert.Null(rig.Drafts.Deleted);
            Assert.Equal("queued unsent text", rig.Drafts.Draft(conversation.Id)?.Content);

            // A SECOND real page opening the SAME conversation restores the queued text from the
            // canonical draft: the in-memory queue was disposed, but its recoverable text was not.
            rig.ExpectLaterModelRead([Model("llama3")]);
            var restored = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            rig.Mount(restored);
            try
            {
                await restored.LoadConversationAsync(conversation).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(conversation.Id, restored.ConversationId);
                Assert.Equal("queued unsent text", ComposerText(restored));
            }
            finally
            {
                await DrainBackgroundPageOriginalsAsync(restored);
                await restored.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        finally
        {
            held.Released.TrySetResult();
            await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    [AvaloniaFact]
    public async Task Typing_during_a_held_outgoing_flush_stays_owned_by_the_outgoing_conversation()
    {
        using var rig = new Rig();
        rig.Models.TrySetResult([Model("llama3")]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        var outgoing = page.CurrentConversation;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? loading = null;
        try
        {
            page.SetDraft("outgoing text");
            await WaitUntilAsync(() => rig.Drafts.Draft(outgoing.Id)?.Content == "outgoing text");

            // The outgoing draft flush of the incoming Load is held open. The composer is still
            // the OUTGOING conversation's composer while the page already points at the incoming
            // conversation, so typing now must be autosaved under the OUTGOING id.
            // An unchanged, already-acknowledged snapshot may legitimately deduplicate. Admit
            // an actual NEW save and observe its entry before Load joins that exact held write.
            rig.Drafts.HoldSave(held, entered);
            page.SetDraft("outgoing text awaiting acknowledgement");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            loading = page.LoadConversationAsync(rig.Saved);
            Assert.False(loading.IsCompleted);

            page.SetDraft("typed during the held flush");
            await Task.Delay(100);

            rig.Drafts.ReleaseHeldSave();
            await loading.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => rig.Drafts.Draft(outgoing.Id)?.Content == "typed during the held flush");

            Assert.Equal(rig.Saved.Id, page.ConversationId);
            Assert.Equal("typed during the held flush", rig.Drafts.Draft(outgoing.Id)?.Content);
            Assert.Null(rig.Drafts.Draft(rig.Saved.Id));
            // Newer typing wins: it is neither cleared nor published under the incoming id.
            Assert.Equal("typed during the held flush", ComposerText(page));

            // A LATER selection of that same outgoing conversation keeps the newer text durable.
            await DrainBackgroundPageOriginalsAsync(page);
            await page.LoadConversationAsync(outgoing).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(outgoing.Id, page.ConversationId);
            Assert.Equal("typed during the held flush", ComposerText(page));
            Assert.Equal("typed during the held flush", rig.Drafts.Draft(outgoing.Id)?.Content);
        }
        finally
        {
            held.TrySetResult();
            rig.Drafts.ReleaseHeldSave();
            try
            {
                if (loading is not null) await loading.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                try { await DrainBackgroundPageOriginalsAsync(page); }
                finally { await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
    }

    [AvaloniaFact]
    public async Task Equal_text_after_a_held_unresolved_save_is_written_to_the_new_actual_branch()
    {
        using var rig = new Rig();
        rig.Models.TrySetResult([Model("llama3")]);
        var page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        var conversation = page.CurrentConversation;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? loading = null;
        try
        {
            // Fresh has not resolved a branch yet. The real text edit captures an unresolved
            // mutation, then its canonical storage write resolves A and remains genuinely held.
            rig.Drafts.HoldSave(held, entered);
            page.SetDraft("same text on two branches");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var branchA = rig.Drafts.CurrentBranch(conversation.Id)!.Value;
            Assert.Null(rig.Drafts.Draft(conversation.Id, branchA));
            rig.Drafts.ReleaseHeldSave();
            await WaitUntilAsync(() => rig.Drafts.Draft(conversation.Id, branchA)?.Content == "same text on two branches");
            await DrainBackgroundPageOriginalsAsync(page);

            // Another canonical branch becomes current. Loading the SAME conversation uses
            // the real branch read/restore path, retaining its existing conversation identity.
            var branchB = rig.Drafts.SelectBranch(conversation.Id);
            await page.LoadConversationAsync(conversation).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(conversation.Id, page.ConversationId);
            Assert.Equal("same text on two branches", ComposerText(page));
            Assert.Null(rig.Drafts.Draft(conversation.Id, branchB));
            await DrainBackgroundPageOriginalsAsync(page);

            // A second genuine outgoing flush now targets B. It must not reuse the completed
            // unresolved mutation that wrote A merely because its text/attachments are equal.
            held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Drafts.HoldSave(held, entered);
            loading = page.LoadConversationAsync(conversation);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(loading.IsCompleted);
            Assert.Null(rig.Drafts.Draft(conversation.Id, branchB));
            Assert.Equal("same text on two branches", rig.Drafts.Draft(conversation.Id, branchA)?.Content);
            rig.Drafts.ReleaseHeldSave();
            await loading.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("same text on two branches", rig.Drafts.Draft(conversation.Id, branchB)?.Content);
            Assert.Equal(conversation.Id, page.ConversationId);
        }
        finally
        {
            held.TrySetResult();
            rig.Drafts.ReleaseHeldSave();
            try
            {
                if (loading is not null) await loading.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                try { await DrainBackgroundPageOriginalsAsync(page); }
                finally { await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
    }

    [AvaloniaFact]
    public async Task Fresh_chat_has_no_automatic_dual_control_and_draft_still_works()
    {
        using var rig = new Rig();
        rig.Models.TrySetResult([Model("llama3")]);
        var servicesProperty = typeof(Haven.Desktop.App).GetProperty("Services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousServices = servicesProperty.GetValue(null);
        // Arm the REAL old injection precondition: the global app provider supplies Dual.
        // Without this the test also passed the old implementation.
        using var appServices = new ServiceCollection()
            .AddSingleton(new DualModelService(Proxy<IOllamaClient>()))
            .BuildServiceProvider();
        NewChatPage? page = null;
        try
        {
            servicesProperty.SetValue(null, appServices);
            page = await rig.Factory.CreateNewAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            var scene = Scene(page);
            Assert.Null(typeof(ChatHavenScene).GetField("_dualToggle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene));
            Assert.Null(typeof(NewChatPage).GetField("_dual", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
            page.SetDraft("fresh chat draft");
            await WaitUntilAsync(() => rig.Drafts.Draft(page.CurrentConversation.Id)?.Content == "fresh chat draft");
            Assert.Equal("fresh chat draft", ComposerText(page));
        }
        finally
        {
            try
            {
                if (page is not null) await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { servicesProperty.SetValue(null, previousServices); }
        }
    }

    private static ModelDescriptor Model(string name) =>
        new(name, 1024, "llama", "1B", "Q4_0", new HashSet<ToolCapability>(), DateTimeOffset.UnixEpoch);

    // These lookups require the real page to have been mounted before the operation under test.
    // Missing/duplicate controls must fail positive AND negative assertions. Never pump jobs here:
    // doing so between the physical edit and click would hide the original ordering regression.
    private static NativeButton SendButton(NewChatPage page) => Scene(page).ComposerView
        .GetVisualDescendants().OfType<NativeButton>()
        .Single(button => AutomationProperties.GetAutomationId(button) == "send-button");

    private static NativeTextBox ComposerTextBox(NewChatPage page) => Scene(page).ComposerView
        .GetVisualDescendants().OfType<NativeTextBox>()
        .Single(input => AutomationProperties.GetAutomationId(input) == "composer-input");

    /// <summary>One later GetModelsAsync read the test explicitly arms and releases.</summary>
    private sealed class HeldModelRead
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<IReadOnlyList<ModelDescriptor>> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static ChatHavenScene Scene(NewChatPage page) =>
        (ChatHavenScene)typeof(NewChatPage)
            .GetField("_scene", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

    private static ChatRecoveryCuiView RecoveryView(NewChatPage page) => Scene(page).RecoveryView;

    private static NativeButton RecoveryButton(NewChatPage page) => RecoveryView(page)
        .GetVisualDescendants().OfType<NativeButton>()
        .Single(button => AutomationProperties.GetAutomationId(button) == "recovery-manage-button");

    private static bool SendEnabled(NewChatPage page) =>
        SendButton(page).IsEnabled;

    private static string ComposerText(NewChatPage page) => Scene(page).Input.Text ?? string.Empty;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static IEnumerable<Exception> AllCauses(Exception error)
    {
        yield return error;
        if (error is AggregateException aggregate)
            foreach (var inner in aggregate.InnerExceptions)
                foreach (var cause in AllCauses(inner)) yield return cause;
        else if (error.InnerException is { } inner)
            foreach (var cause in AllCauses(inner)) yield return cause;
    }

    private static Task SettleAdmittedPageOriginalsAsync(NewChatPage page) =>
        Task.WhenAll(AdmittedPageOriginalTasks(page));

    private static Task[] AdmittedPageOriginalTasks(NewChatPage page)
    {
        // Test-only observation of the real owner's exact tasks. Dispatcher idle alone
        // does not join their asynchronous continuations. With no further input admitted,
        // this establishes the settled-page precondition without a delay or a fake owner.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var owner = (DesktopOriginalWorkLifetime)typeof(NewChatPage).GetField("_originalWork", flags)!.GetValue(page)!;
        var gate = typeof(DesktopOriginalWorkLifetime).GetField("_gate", flags)!.GetValue(owner)!;
        lock (gate)
            return ((IEnumerable<DesktopOriginalWorkLifetime.Original>)typeof(DesktopOriginalWorkLifetime)
                .GetField("_originals", flags)!.GetValue(owner)!).Select(original => original.Task).ToArray();
    }

    /// <summary>The real page also queues a background-priority transcript scroll whose admitted
    /// original is created lazily on the dispatcher. Flush that background work and join the SAME
    /// admitted originals until no page original is still pending, so a close never races a
    /// scroll that was still waiting for its dispatcher callback. This reuses the existing settle
    /// observation and adds no second owner or fake page. Only pending tasks are joined here;
    /// already-completed or retained causes still require the real external close to inspect.</summary>
    private static async Task DrainBackgroundPageOriginalsAsync(NewChatPage page)
    {
        for (var pass = 0; pass != 8; pass++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var pending = AdmittedPageOriginalTasks(page).Where(task => !task.IsCompleted).ToArray();
            if (pending.Length == 0) return;
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.DoesNotContain(AdmittedPageOriginalTasks(page), task => !task.IsCompleted);
    }

    private static async Task<NewChatPage?> AsNullable(Task<NewChatPage> task) => await task;

    private sealed class Rig : IDisposable
    {
        public readonly TaskCompletionSource<IReadOnlyList<ModelDescriptor>> Models = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ModelsEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ModelsCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly Conversation Saved = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Saved", null, null, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public readonly DraftStore Drafts = new();
        public int ConversationSelections;
        public OverlayChatSessionFactory Factory { get; }
        /// <summary>The SAME routed source the Overlay factory must bind NewChatPage's model
        /// client to; it is a distinct identity from the raw Ollama transport.</summary>
        public IProviderModelClient Routed { get; } = null!;
        /// <summary>The distinct raw Ollama identity. Routing correctness is proven by asserting
        /// this transport stays unused while the routed source receives every discovery read.</summary>
        public IOllamaClient Raw { get; } = null!;
        /// <summary>The ARMED next conversation write, if any, so a test can hold the exact real
        /// storage boundary that an accepted send's committed input must pass.</summary>
        public HeldConversationWrite? NextConversationWrite;
        /// <summary>When true the real conversation repository accepts the accepted send's user
        /// message write, so the page reaches a GENUINE committed-input acknowledgement. It stays
        /// false by default: an unavailable message-write boundary is the honest control for a
        /// send that fails AFTER the presentation-only event but BEFORE acceptance.</summary>
        public bool AcceptMessages;
        /// <summary>The exact user messages this rig's repository accepted, in order.</summary>
        public readonly List<ChatMessage> AddedMessages = [];
        // The ARMED next GetModelsAsync read, if any, so a test can hold a later retry open.
        public HeldModelRead? NextModelRead;
        private readonly Queue<IReadOnlyList<ModelDescriptor>> _expectedLaterModelReads = new();
        private readonly object _readGate = new();
        /// <summary>Every unexpected external I/O method the page actually attempted, in order.
        /// Proves a send advanced past its own composer clear to the real storage/provider gate.</summary>
        public readonly List<string> UnexpectedIo = [];
        /// <summary>Records and returns the exact refusal for one unexpected external I/O call.</summary>
        private InvalidOperationException RecordUnexpected(string name)
        { lock (_readGate) UnexpectedIo.Add(name); return Unexpected(name); }
        /// <summary>Makes exactly ONE later GetModelsAsync read expected (a SECOND real page
        /// constructed on this same rig) and settles it with the supplied list.</summary>
        public void ExpectLaterModelRead(IReadOnlyList<ModelDescriptor> models)
        { lock (_readGate) _expectedLaterModelReads.Enqueue(models); }
        private bool TryDequeueExpectedLaterRead(out IReadOnlyList<ModelDescriptor> models)
        {
            lock (_readGate)
            {
                if (_expectedLaterModelReads.Count == 0) { models = []; return false; }
                models = _expectedLaterModelReads.Dequeue();
                return true;
            }
        }
        private int _modelReadCount;
        /// <summary>Total provider model reads the page actually attempted through the routed
        /// client; used to prove discovery went through the shared routed source and never the
        /// raw Ollama transport.</summary>
        public int ModelReadAttempts => _modelReadCount;
        private int _rawCallCount;
        /// <summary>Total calls the raw Ollama transport would have received. In the correct
        /// routed composition this stays 0; any non-zero value proves the Overlay factory fell
        /// through to the raw transport instead of the shared routed catalogue.</summary>
        public int RawCallCount => _rawCallCount;
        private readonly ServiceProvider _services;
        private readonly List<NativeWindow> _windows = [];
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "9to1-chat-initialization-" + Guid.NewGuid().ToString("N"));

        public Rig()
        {
            Directory.CreateDirectory(_directory);
            var conversations = Proxy<IConversationRepository>((name, args) => name switch
            {
                "GetAsync" => Task.FromResult<Conversation?>(Saved),
                "GetMessagesAsync" => SelectHistory(),
                "GetContextMessagesAsync" => SelectHistory(),
                "UpsertConversationAsync" => UpsertConversationAsync(),
                "AddMessageAsync" => AddMessageAsync((ChatMessage)args![0]!),
                "GetContextEntriesAsync" => Task.FromResult<IReadOnlyList<ConversationContextEntry>>([]),
                _ => throw RecordUnexpected(name)
            });
            var routed = Proxy<IProviderModelClient>((name, args) =>
            {
                if (name != "GetModelsAsync")
                {
                    throw RecordUnexpected(name);
                }
                var token = (CancellationToken)args![0]!;
                if (Interlocked.Increment(ref _modelReadCount) == 1)
                {
                    token.Register(() => ModelsCancelled.TrySetResult());
                    ModelsEntered.TrySetResult();
                    return Models.Task;
                }
                // A later retry is explicitly armed by the test that wants to hold it open;
                // an unarmed extra read is unexpected provider I/O.
                if (NextModelRead is not null)
                {
                    var held = NextModelRead;
                    NextModelRead = null;
                    token.Register(() => held.Cancelled.TrySetResult());
                    held.Entered.TrySetResult();
                    return held.Result.Task;
                }
                if (TryDequeueExpectedLaterRead(out var expected))
                    return Task.FromResult(expected);
                throw Unexpected("GetModelsAsync (unarmed later read)");
            });
            // The raw Ollama transport is a DISTINCT identity in production
            // (IOllamaClient = raw Ollama; IProviderModelClient = the resilient routed client).
            // The Overlay factory must bind NewChatPage to the routed source explicitly, so any
            // accidental fall-through to this raw transport is foreign, observable I/O here.
            var raw = Proxy<IOllamaClient>((name, _) =>
            {
                Interlocked.Increment(ref _rawCallCount);
                throw RecordUnexpected("RawOllama." + name);
            });
            var safety = Proxy<IConversationSafetyService>((name, args) => name switch
            {
                "GetSnapshotAsync" => Task.FromResult(new ConversationSafetySnapshot((Guid)args![0]!, 0, ConversationSafetyState.Active, null, 0)),
                "EnsureMayActAsync" => Task.CompletedTask,
                _ => throw RecordUnexpected(name)
            });
            var paths = Proxy<IAppPaths>((name, _) => name == "get_DataDirectory" ? _directory : throw Unexpected(name));
            var locals = new GenUiLocalActionRegistry();
            var instances = new GenUiInstanceStore();
            var services = new ServiceCollection()
                .AddSingleton(new HavenEventBus())
                .AddSingleton(conversations).AddSingleton(safety)
                // The SAME routed source backs both the IOllamaClient slot NewChatPage binds
                // (through the Overlay factory's explicit routed-client injection) and the
                // IProviderModelClient identity the production composition uses. There is no
                // separate raw Ollama client in this Overlay rig, so Overlay discovery can
                // never accidentally fall through to a raw transport.
                .AddSingleton<IProviderModelClient>(routed)
                .AddSingleton<IOllamaClient>(raw)
                .AddSingleton(Proxy<IConversationProductionRepository>(Drafts.Invoke))
                .AddSingleton(Proxy<IConversationVersioningService>())
                .AddSingleton(new UserPreferencesService(paths))
                .AddSingleton(new ChatSessionService(conversations, routed, new CapabilityPreflightService(), safety,
                    new WorkspaceToolRuntime(Proxy<IWorkspaceToolService>()), new ComputerToolRuntime(Proxy<IComputerToolService>())))
                .AddSingleton(locals).AddSingleton(instances)
                .AddSingleton(new GenerativeUiEventRouter([], Proxy<IGenUiEventAuditSink>(), instances));
            foreach (var type in new[] {
                typeof(CalculatorTemplateRuntime), typeof(StructuredFormTemplateRuntime), typeof(ChoicePromptTemplateRuntime),
                typeof(ChecklistTemplateRuntime), typeof(DataGridTemplateRuntime), typeof(CardDeckTemplateRuntime),
                typeof(GraphTemplateRuntime), typeof(TaskListTemplateRuntime), typeof(DashboardTemplateRuntime),
                typeof(AssessmentTemplateRuntime), typeof(WorkflowTemplateRuntime), typeof(CustomTemplateRuntime) })
                services.AddSingleton(type);
            _services = services.BuildServiceProvider();
            Routed = _services.GetRequiredService<IProviderModelClient>();
            Raw = _services.GetRequiredService<IOllamaClient>();
            var capabilities = new CapabilityRegistryService(Proxy<ICapabilityRepository>((name, _) =>
                name == "GetCapabilitiesAsync" ? Task.FromResult<IReadOnlyList<CapabilityDefinition>>([]) : throw Unexpected(name)));
            var catalog = Proxy<ICatalogRepository>((name, _) => name switch {
                "GetAgentsAsync" => Task.FromResult<IReadOnlyList<AgentDefinition>>([]),
                "GetPromptsAsync" => Task.FromResult<IReadOnlyList<PromptDefinition>>([]),
                _ => throw Unexpected(name) });
            var modes = Proxy<IModeRegistry>((name, _) => name == "GetModesAsync"
                ? Task.FromResult<IReadOnlyList<ModeDefinition>>([]) : throw Unexpected(name));
            Factory = new OverlayChatSessionFactory(_services, conversations, capabilities, catalog, modes);
        }
        private async Task UpsertConversationAsync()
        {
            HeldConversationWrite? held;
            lock (_readGate) { held = NextConversationWrite; NextConversationWrite = null; }
            if (held is not null)
            {
                held.Entered.TrySetResult();
                await held.Released.Task;
            }
            ConversationSelections++;
        }

        private Task AddMessageAsync(ChatMessage message)
        {
            if (!AcceptMessages) throw RecordUnexpected("AddMessageAsync");
            lock (_readGate) AddedMessages.Add(message);
            return Task.CompletedTask;
        }

        private Task SelectFresh() { ConversationSelections++; return Task.CompletedTask; }
        private Task<IReadOnlyList<ChatMessage>> SelectHistory() { ConversationSelections++; return Task.FromResult<IReadOnlyList<ChatMessage>>([]); }
        public void Mount(NewChatPage page)
        {
            var window = new NativeWindow { Width = 1100, Height = 800, Content = page };
            _windows.Add(window);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(ComposerTextBox(page).Bounds.Width > 0);
            Assert.True(SendButton(page).Bounds.Width > 0);
        }

        public void Dispose()
        {
            // Each test drains its real page originals in finally before the fixture detaches it.
            foreach (var window in _windows) window.Close();
            _services.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>One conversation write the test explicitly arms and releases: the real storage
    /// boundary the canonical committed-input acknowledgement sits behind.</summary>
    public sealed class HeldConversationWrite
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>The canonical draft/attachment surface the real page reaches through
    /// IConversationProductionRepository. It records exactly what the page wrote, can hold one
    /// real write open and can fail one write, so held/failed persistence and transition routing
    /// are exercised against the page's real save/restore/close handling.</summary>
    private sealed class DraftStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, Guid> _branches = [];
        private readonly Dictionary<(Guid ConversationId, Guid BranchId), ConversationDraft> _drafts = [];
        public TaskCompletionSource? HeldSave;
        private TaskCompletionSource? _heldSaveEntered;
        private HeldConversationWrite? _heldCurrentBranch;
        public Exception? SaveFailure;
        public int SaveCount;
        /// <summary>The conversation id of the LAST canonical draft deletion the page issued, if
        /// any. Proves a pre-acceptance composer clear never removed an unsent draft.</summary>
        public Guid? Deleted;

        public ConversationDraft? Draft(Guid conversationId, Guid? branchId = null)
        {
            lock (_gate)
                return _drafts.GetValueOrDefault((conversationId, branchId ?? _branches.GetValueOrDefault(conversationId)));
        }

        public Guid? CurrentBranch(Guid conversationId) { lock (_gate) return _branches.TryGetValue(conversationId, out var id) ? id : null; }
        public Guid SelectBranch(Guid conversationId) { lock (_gate) return _branches[conversationId] = Guid.NewGuid(); }
        public void HoldSave(TaskCompletionSource held, TaskCompletionSource? entered = null)
        { lock (_gate) { HeldSave = held; _heldSaveEntered = entered; } }
        public void HoldCurrentBranch(HeldConversationWrite held) { lock (_gate) _heldCurrentBranch = held; }
        public void FailSave(Exception failure) { lock (_gate) SaveFailure = failure; }
        public void ReleaseHeldSave() { TaskCompletionSource? held; lock (_gate) { held = HeldSave; HeldSave = null; } held?.TrySetResult(); }

        public object? Invoke(string name, object?[]? args) => name switch
        {
            "GetCurrentBranchAsync" => CurrentAsync((Guid)args![0]!),
            "EnsureRootBranchAsync" => Task.FromResult(Ensure((Guid)args![0]!)),
            "GetDraftAsync" => Task.FromResult(Draft((Guid)args![0]!, args[1] as Guid?)),
            "SaveDraftAsync" => SaveAsync((ConversationDraft)args![0]!),
            "DeleteDraftAsync" => DeleteAsync((Guid)args![0]!, args[1] as Guid?),
            "GetAttachmentsAsync" => Task.FromResult<IReadOnlyList<MessageAttachment>>([]),
            _ => throw Unexpected(name)
        };

        private ConversationBranch? Current(Guid conversationId)
        {
            lock (_gate) return _branches.TryGetValue(conversationId, out var id) ? Branch(id, conversationId) : null;
        }

        private async Task<ConversationBranch?> CurrentAsync(Guid conversationId)
        {
            HeldConversationWrite? held;
            ConversationBranch? actual;
            lock (_gate) { held = _heldCurrentBranch; _heldCurrentBranch = null; actual = Current(conversationId); }
            if (held is not null)
            {
                held.Entered.TrySetResult();
                await held.Released.Task;
            }
            return actual;
        }

        private ConversationBranch Ensure(Guid conversationId)
        {
            lock (_gate)
            {
                if (!_branches.TryGetValue(conversationId, out var id))
                {
                    id = Guid.NewGuid();
                    _branches[conversationId] = id;
                }
                return Branch(id, conversationId);
            }
        }

        private static ConversationBranch Branch(Guid id, Guid conversationId) =>
            new(id, conversationId, null, null, "main", ConversationBranchReason.Root, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        private async Task SaveAsync(ConversationDraft draft)
        {
            Interlocked.Increment(ref SaveCount);
            Task? held;
            lock (_gate) { held = HeldSave?.Task; _heldSaveEntered?.TrySetResult(); }
            if (held is not null) await held;
            lock (_gate)
            {
                if (SaveFailure is { } failure) throw failure;
                _drafts[(draft.ConversationId, draft.BranchId ?? Guid.Empty)] = draft;
            }
        }

        private Task DeleteAsync(Guid conversationId, Guid? branchId)
        {
            lock (_gate)
            {
                Deleted = conversationId;
                foreach (var key in _drafts.Keys
                    .Where(key => key.ConversationId == conversationId && (branchId is null || key.BranchId == branchId))
                    .ToArray())
                    _drafts.Remove(key);
            }
            return Task.CompletedTask;
        }
    }

    private static InvalidOperationException Unexpected(string name) => new("Unexpected external I/O: " + name);
    private static T Proxy<T>(Func<string, object?[]?, object?>? invoke = null) where T : class
    {
        var value = DispatchProxy.Create<T, ControlledIo>();
        ((ControlledIo)(object)value).Call = invoke ?? ((name, _) => throw Unexpected(name));
        return value;
    }
    public class ControlledIo : DispatchProxy
    {
        public Func<string, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args);
    }
}
