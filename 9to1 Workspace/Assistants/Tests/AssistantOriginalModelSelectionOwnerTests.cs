using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using Xunit;

namespace HavenOS.Apps.Assistants.Tests;

// Injected catalogues test policy boundaries only; they never qualify a production model or host.
public sealed class AssistantOriginalModelSelectionOwnerTests
{
    [Fact]
    public async Task Local_only_privacy_filters_remote_providers_before_catalogue_io()
    {
        var local = new Provider("ollama", true); var remote = new Provider("remote", false);
        var privacy = new Privacy { Current = PrivacyPreferences.Default with { LocalOnlyMode = true } };
        var owner = Owner([local, remote], privacy);
        var choices = await owner.ReadAvailableOriginalAsync(Definition(allowCloud: true), Conversation(), TestContext.Current.CancellationToken);
        Assert.Single(choices); Assert.Equal("ollama", choices[0].ProviderId);
        Assert.Equal(1, local.Calls); Assert.Equal(0, remote.Calls);
    }

    [Fact]
    public async Task Assistant_cloud_opt_out_filters_remote_before_catalogue_io()
    {
        var local = new Provider("ollama", true); var remote = new Provider("remote", false);
        var owner = Owner([local, remote]);
        var choices = await owner.ReadAvailableOriginalAsync(Definition(allowCloud: false), Conversation(), TestContext.Current.CancellationToken);
        Assert.Single(choices); Assert.Equal(0, remote.Calls);
    }

    [Fact]
    public async Task Existing_model_permission_rule_filters_a_requested_file_edit_model()
    {
        var local = new Provider("ollama", true);
        var rule = ModelPermissionRule.Create(ModelPermissionTargetKind.ExactModel, "model", ModelPermissionScope.ThisDevice,
            RestrictedModelCapability.EditFiles);
        var owner = Owner([local], permissions: new PermissionStore { Policy = new([rule]) });
        var choices = await owner.ReadAvailableOriginalAsync(Definition(false) with
        { Configuration = Definition(false).Configuration with { ToolIds = ["write_file"] } }, Conversation(), TestContext.Current.CancellationToken);
        Assert.Empty(choices);
    }

    [Fact]
    public async Task Actor_change_during_catalogue_read_refuses_the_old_choices()
    {
        var actors = new Actors(); var local = new Provider("ollama", true)
        { AfterRead = () => actors.Current = actors.Current! with { AuthenticationRevision = "changed" } };
        var owner = Owner([local], actors: actors);
        await Assert.ThrowsAsync<AssistantCommandRefusedException>(() =>
            owner.ReadAvailableOriginalAsync(Definition(false), Conversation(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Privacy_change_during_catalogue_read_refuses_the_old_choices()
    {
        var privacy = new Privacy(); var local = new Provider("ollama", true)
        { AfterRead = () => privacy.Current = privacy.Current with { LocalOnlyMode = true } };
        var owner = Owner([local], privacy);
        await Assert.ThrowsAsync<AssistantCommandRefusedException>(() =>
            owner.ReadAvailableOriginalAsync(Definition(false), Conversation(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Account_actor_does_not_infer_personal_Home_authority()
    {
        var local = new Provider("ollama", true); var actors = new Actors();
        actors.Current = actors.Current! with { AccountId = Guid.NewGuid() };
        var owner = Owner([local], actors: actors);
        await Assert.ThrowsAsync<AssistantCommandRefusedException>(() =>
            owner.ReadAvailableOriginalAsync(Definition(false), Conversation(), TestContext.Current.CancellationToken));
        Assert.Equal(0, local.Calls);
    }

    [Fact]
    public async Task Privacy_change_while_final_actor_read_yields_refuses_the_old_choices()
    {
        var privacy = new Privacy(); var actors = new Actors(); var local = new Provider("ollama", true);
        actors.BeforeReturn = async call =>
        {
            if (call != 4) return;
            await Task.Yield();
            privacy.Current = privacy.Current with { LocalOnlyMode = true };
        };
        var owner = Owner([local], privacy, actors);
        await Assert.ThrowsAsync<AssistantCommandRefusedException>(() =>
            owner.ReadAvailableOriginalAsync(Definition(false), Conversation(), TestContext.Current.CancellationToken));
        Assert.Equal(4, actors.Calls);
    }

    private static AssistantOriginalModelSelectionOwner Owner(IReadOnlyList<IModelProvider> providers,
        Privacy? privacy = null, Actors? actors = null, PermissionStore? permissions = null) =>
        new(new ModelProviderRegistry(providers), privacy ?? new(), new(permissions ?? new()), actors ?? new(),
            descriptor => descriptor.Model with { Name = descriptor.ProviderId == "ollama" ? descriptor.Name : descriptor.Key });
    private static AssistantDefinitionSnapshot Definition(bool allowCloud) => new(new("den", "personal", "definition"),
        1, ConfiguredIdentityKind.Assistant, new() { Name = "Developer", Model = new(AllowCloud: allowCloud) }, []);
    private static Conversation Conversation() => new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
        "Conversation", null, null, false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Current = new("actor", "profile", null, null, "revision");
        public Func<int, Task>? BeforeReturn;
        public int Calls;
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken)
        {
            var call = ++Calls;
            if (BeforeReturn is { } source) await source(call);
            return Current;
        }
    }
    private sealed class Privacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences preferences, CancellationToken cancellationToken) { Current = preferences; return Task.CompletedTask; }
    }
    private sealed class PermissionStore : IModelPermissionStore
    {
        public ModelPermissionPolicy Policy = ModelPermissionPolicy.Empty;
        public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken cancellationToken) => Task.FromResult(Policy);
        public Task SavePolicyAsync(ModelPermissionPolicy policy, CancellationToken cancellationToken) { Policy = policy; return Task.CompletedTask; }
    }
    private sealed class Provider(string id, bool local) : IModelProvider
    {
        public int Calls;
        public Action? AfterRead;
        public string Id => id;
        public string DisplayName => id;
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => local;
        public bool CanManageModels => false;
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
        {
            Calls++; AfterRead?.Invoke();
            return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([new(id, local,
                new("model", 1, "family", "1B", "test", new HashSet<ToolCapability>(), DateTimeOffset.UnixEpoch))]);
        }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
