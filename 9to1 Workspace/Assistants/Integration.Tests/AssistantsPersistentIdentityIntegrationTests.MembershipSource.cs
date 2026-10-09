using System.Runtime.ExceptionServices;
using Haven.Core;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Dev;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Apps.Assistants.Tests;

// SAME real Home/OS-profile/Den/SQLite rig as the maintained persistence tests.
// These validate membership extraction, not installed Dev/model/process execution.
public sealed partial class AssistantsPersistentIdentityIntegrationTests
{
    [Fact]
    public Task Actual_membership_retainer_and_replacing_scope_causes_both_survive() =>
        RunMembershipCallbackControlAsync(async rig =>
        {
            var controller = rig.CreateController();
            await rig.Retain(controller.CreateAsync(new() { Name = "Actual callback custody" }, Guid.NewGuid(), Token));
            var source = Assert.IsType<DenAssistantCanonicalBridge>(controller.OriginalCanonicalBridge).OriginalMembershipSource;
            var originalBody = new IOException("Actual raw Home task retainer rejected publication.");
            var replacingScope = new IOException("Caller caught the retainer and rejected the outer scope.");
            Task? sameRaw = null;
            var actual = rig.Retain(source.OpenHomeWithinSourceAsync(body =>
            {
                try { body(); }
                catch (Exception observed) { Assert.Same(originalBody, observed); throw replacingScope; }
            }, raw =>
            {
                rig.Retain(raw);
                // Home prepublishes its SAME driver to this retainer before Take
                // returns from the factory. Reject only Take's second publication
                // of that exact driver, after real Home admission already owns it.
                if (sameRaw is null) sameRaw = raw;
                else if (ReferenceEquals(sameRaw, raw)) throw originalBody;
            }, Token));
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            rig.Expect(actual, error);
            Assert.True(actual.IsFaulted);
            Assert.Contains(error.InnerExceptions, cause => ReferenceEquals(cause, originalBody));
            Assert.Contains(error.InnerExceptions, cause => ReferenceEquals(cause, replacingScope));
            Assert.NotNull(sameRaw); Assert.True(sameRaw.IsCompletedSuccessfully);
            Assert.Equal(0, rig.ProviderInvocations);
        });

    [Fact]
    public Task Actual_membership_repeated_callback_and_replacing_scope_causes_both_survive() =>
        RunMembershipCallbackControlAsync(async rig =>
        {
            var controller = rig.CreateController();
            await rig.Retain(controller.CreateAsync(new() { Name = "Protocol callback custody" }, Guid.NewGuid(), Token));
            var source = Assert.IsType<DenAssistantCanonicalBridge>(controller.OriginalCanonicalBridge).OriginalMembershipSource;
            var replacingScope = new IOException("Caller caught repeated-callback refusal and replaced it.");
            Exception? originalProtocol = null; Task? sameRaw = null;
            var scopeDepth = new AsyncLocal<int>();
            var actual = rig.Retain(source.OpenHomeWithinSourceAsync(body =>
            {
                var previousDepth = scopeDepth.Value;
                scopeDepth.Value = previousDepth + 1;
                try
                {
                    body();
                    // Exercise this membership callback, not the real Home source's
                    // independent nested callbacks or asynchronously acquired stages.
                    if (previousDepth != 0) return;
                    try { body(); }
                    catch (Exception observed) { originalProtocol = observed; throw replacingScope; }
                }
                finally { scopeDepth.Value = previousDepth; }
            }, raw => { sameRaw ??= raw; rig.Retain(raw); }, Token));
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            rig.Expect(actual, error);
            Assert.IsType<InvalidOperationException>(originalProtocol);
            Assert.Contains(error.InnerExceptions, cause => ReferenceEquals(cause, originalProtocol));
            Assert.Contains(error.InnerExceptions, cause => ReferenceEquals(cause, replacingScope));
            Assert.NotNull(sameRaw); Assert.True(sameRaw.IsCompletedSuccessfully);
            Assert.Equal(0, rig.ProviderInvocations);
        });

    private static async Task RunMembershipCallbackControlAsync(Func<Rig, Task> body)
    {
        var rig = new Rig(); var errors = new List<Exception>();
        try { await rig.InitializeActualOwnersAsync(create: true); await body(rig); }
        catch (Exception cause) { Add(errors, cause); }
        finally
        {
            try { await rig.JoinActualOwnersAsync(); } catch (Exception cause) { Add(errors, cause); }
            foreach (var actual in rig.Originals)
            {
                try { await actual; }
                catch (Exception observed)
                { foreach (var cause in actual.Exception?.InnerExceptions.ToArray() ?? [observed]) if (!rig.IsExpected(actual, cause)) Add(errors, cause); }
            }
            // Preserve this actual fixture and every source result. No deletion,
            // storage cleanup or replay forms part of these callback controls.
            Console.WriteLine("Retained original membership callback fixture: " + rig.Root);
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Actual membership callback and independent owner joins failed; fixture retained at " + rig.Root, errors);
    }

    [Fact]
    public Task Transferred_membership_reads_fresh_real_Home_after_original_presentation_closed() =>
        RunOriginalAsync(async rig =>
        {
            var controller = rig.CreateController();
            await rig.Retain(controller.CreateAsync(new() { Name = "Transferred original" }, Guid.NewGuid(), Token));
            var snapshot = await rig.Retain(controller.NewConversationAsync(Guid.NewGuid(), "Original conversation", Guid.NewGuid(), Token));
            var binding = snapshot.ConversationBinding!;
            var bridge = Assert.IsType<DenAssistantCanonicalBridge>(controller.OriginalCanonicalBridge);
            var source = bridge.OriginalMembershipSource;
            await rig.Retain(controller.CloseAndDrainAsync());
            Assert.True(controller.OriginalClose!.IsCompletedSuccessfully);
            var observed = await ReadIndependentMembershipAsync(rig, source, binding);
            Assert.Equal(rig.Actor, observed.Actor);
            Assert.Equal(binding.Conversation, observed.Conversation);
            Assert.Equal(binding.Definition.Identity, observed.Definition.Identity);
            Assert.Equal(binding.Definition.Revision, observed.Definition.Revision);
            Assert.True(source.IsIssuedOriginalBinding(observed.Binding));
            Assert.Null(await rig.Retain(rig.Tasks.GetByContextAsync(binding.Conversation.Id, Token)));
            Assert.Equal(0, rig.ProviderInvocations);
        });

    [Fact]
    public Task Independent_membership_source_refuses_another_actual_bridges_private_binding() =>
        RunOriginalAsync(async rig =>
        {
            var first = rig.CreateController();
            await rig.Retain(first.CreateAsync(new() { Name = "First issuer" }, Guid.NewGuid(), Token));
            var original = await rig.Retain(first.NewConversationAsync(Guid.NewGuid(), "Same durable conversation", Guid.NewGuid(), Token));
            var second = rig.CreateController();
            await rig.Retain(second.OpenAssistantAsync(original.SelectedAssistant!.Identity, Token));
            var foreign = await rig.Retain(second.OpenConversationAsync(original.ConversationBinding!.Conversation.Id, Token));
            var source = Assert.IsType<DenAssistantCanonicalBridge>(first.OriginalCanonicalBridge).OriginalMembershipSource;
            Assert.False(source.IsIssuedOriginalBinding(foreign.ConversationBinding!));
            await ReadIndependentMembershipRefusalAsync(rig, source, foreign.ConversationBinding!);
            Assert.Equal(original.ConversationBinding.Conversation, await rig.Retain(rig.Conversations.GetAsync(original.ConversationBinding.Conversation.Id, Token)));
            Assert.Equal(0, rig.ProviderInvocations);
        });

    [Fact]
    public Task Amending_a_container_free_task_row_does_not_issue_project_membership() =>
        RunOriginalAsync(async rig =>
        {
            var controller = rig.CreateController();
            await rig.Retain(controller.CreateAsync(new() { Name = "Task original" }, Guid.NewGuid(), Token));
            var original = await rig.Retain(controller.NewConversationAsync(Guid.NewGuid(), "No project grant", Guid.NewGuid(), Token, AssistantConversationKind.Task));
            var binding = original.ConversationBinding!;
            Assert.Null(binding.Conversation.ContainerId);
            var source = Assert.IsType<DenAssistantCanonicalBridge>(controller.OriginalCanonicalBridge).OriginalMembershipSource;
            var injected = binding.Conversation with { ContainerId = Guid.NewGuid() };
            await rig.Retain(rig.Conversations.UpsertConversationAsync(injected, Token));
            await ReadIndependentMembershipRefusalAsync(rig, source, binding);
            Assert.Equal(injected, await rig.Retain(rig.Conversations.GetAsync(injected.Id, Token)));
            var home = await rig.Retain(rig.Factory.OpenAsync(Token));
            var session = Assert.Single(await rig.Retain(home.Den.ListAsync<SessionRecord>("personal", Token)));
            Assert.Equal(binding.DenSessionRevision, session.Revision);
            Assert.Null(await rig.Retain(rig.Tasks.GetByContextAsync(injected.Id, Token)));
            Assert.Equal(0, rig.ProviderInvocations);
        });

    [Fact]
    public Task Transferred_membership_refuses_changed_real_Den_session_without_replaying_creation() =>
        RunOriginalAsync(async rig =>
        {
            var controller = rig.CreateController();
            await rig.Retain(controller.CreateAsync(new() { Name = "Session original" }, Guid.NewGuid(), Token));
            var snapshot = await rig.Retain(controller.NewConversationAsync(Guid.NewGuid(), "Existing session", Guid.NewGuid(), Token));
            var binding = snapshot.ConversationBinding!;
            var source = Assert.IsType<DenAssistantCanonicalBridge>(controller.OriginalCanonicalBridge).OriginalMembershipSource;
            await rig.Retain(controller.CloseAndDrainAsync());
            var home = await rig.Retain(rig.Factory.OpenAsync(Token));
            var session = Assert.Single(await rig.Retain(home.Den.ListAsync<SessionRecord>("personal", Token)));
            var changed = await rig.Retain(home.Den.SaveAsync(session with { ActiveBranchId = Guid.NewGuid().ToString("D") },
                session.Revision, "test.original.sessionchange." + Guid.NewGuid().ToString("D"), Token));
            Assert.True(changed.Revision > binding.DenSessionRevision);
            await ReadIndependentMembershipRefusalAsync(rig, source, binding);
            Assert.Equal(binding.Conversation, await rig.Retain(rig.Conversations.GetAsync(binding.Conversation.Id, Token)));
            Assert.Single(await rig.Retain(home.Den.ListAsync<SessionRecord>("personal", Token)));
            Assert.Equal(0, rig.ProviderInvocations);
        });

    [Fact]
    public Task Saved_project_references_do_not_invent_an_authorized_project_catalogue() =>
        RunOriginalAsync(async rig =>
        {
            var controller = rig.CreateController();
            var reference = new DeveloperProjectReference(Guid.NewGuid(), 1, Guid.NewGuid(), 1, Guid.NewGuid(), null);
            var created = await rig.Retain(controller.CreateAsync(new() { Name = "Metadata has no grant", ProjectReferences = [reference] },
                Guid.NewGuid(), Token));
            Assert.Equal(reference, Assert.Single(created.SelectedAssistant!.Configuration.ProjectReferences));
            var actual = rig.Retain(controller.ReadOriginalProjectCandidatesAsync(32, Token));
            var refusal = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => actual);
            Assert.True(controller.IsAcknowledgedOriginalCommandRefusal(actual)); rig.Expect(actual, refusal);
            Assert.Empty(created.Conversations);
            Assert.Equal(0, rig.ProviderInvocations);
        });

    [Fact]
    public Task A_foreign_project_choice_cannot_create_membership_without_actual_Home_selection_owner() =>
        RunOriginalAsync(async rig =>
        {
            var controller = rig.CreateController();
            await rig.Retain(controller.CreateAsync(new() { Name = "No inferred project" }, Guid.NewGuid(), Token));
            // Friend test assembly can construct this deliberately foreign object.
            // Production callers cannot mint it, and no real source registry issued it.
            var foreign = new AssistantOriginalProjectChoice(new object(), new object(), null!);
            var id = Guid.NewGuid();
            var actual = rig.Retain(controller.NewProjectConversationOriginalAsync(foreign, id, "Refused foreign choice", Guid.NewGuid(), Token));
            var refusal = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => actual);
            Assert.True(controller.IsAcknowledgedOriginalCommandRefusal(actual)); rig.Expect(actual, refusal);
            Assert.Null(await rig.Retain(rig.Conversations.GetAsync(id, Token)));
            var home = await rig.Retain(rig.Factory.OpenAsync(Token));
            Assert.Empty(await rig.Retain(home.Den.ListAsync<SessionRecord>("personal", Token)));
            Assert.Equal(0, rig.ProviderInvocations);
        });

    private static async Task<AssistantCanonicalMembershipSource.Observation> ReadIndependentMembershipAsync(Rig rig,
        AssistantCanonicalMembershipSource source, AssistantConversationBinding binding)
    {
        var work = new AssistantPresentationOriginals();
        var errors = new List<Exception>();
        AssistantCanonicalMembershipSource.Observation? observed = null;
        try
        {
            var actual = rig.Retain(work.Admit(() => source.ValidateOriginalWithinSourceAsync(binding,
                body => work.Invoke(() => { body(); return true; }), raw => { work.Retain(raw); rig.Retain(raw); }, Token)));
            observed = await actual;
        }
        catch (Exception cause) { Add(errors, cause); }
        finally
        {
            try { await rig.Retain(work.CloseAndDrainAsync()); }
            catch (Exception cause) { Add(errors, cause); }
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Independent original membership and same custody close failed.", errors);
        return observed ?? throw new InvalidOperationException("No actual independent membership was observed.");
    }

    private static async Task ReadIndependentMembershipRefusalAsync(Rig rig,
        AssistantCanonicalMembershipSource source, AssistantConversationBinding binding)
    {
        var work = new AssistantPresentationOriginals();
        var errors = new List<Exception>();
        try
        {
            var actual = rig.Retain(work.Admit(() => source.ValidateOriginalWithinSourceAsync(binding,
                body => work.Invoke(() => { body(); return true; }), raw => { work.Retain(raw); rig.Retain(raw); }, Token)));
            var refusal = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => actual);
            rig.Expect(actual, refusal);
        }
        catch (Exception cause) { Add(errors, cause); }
        finally
        {
            try { await rig.Retain(work.CloseAndDrainAsync()); }
            catch (Exception cause) { Add(errors, cause); }
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original membership refusal and independent custody close failed.", errors);
    }
}
