using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Dulche.Runtime.Agents;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.AIStudio.Tests;

public sealed partial class StudioNativeHostTests
{
    [Fact]
    public Task Actual_configuration_scene_creates_owned_Den_and_explicit_Agent_then_mounts_editor() => ExerciseHostAsync(false);
    [Fact]
    public Task Actual_picker_import_preserves_bytes_in_owned_Den_and_native_authoring_saves_reference() => ExerciseHostAsync(true);
    private static async Task ExerciseHostAsync(bool importAvatar)
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-native-host-" + Guid.NewGuid().ToString("N"));
        var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
        var avatarBytes = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");
        var avatarPath = Path.Combine(root, "picked-avatar.gif");
        await File.WriteAllBytesAsync(avatarPath, avatarBytes);
        try
        {
            await using var native = HeadlessUnitTestSession.StartNew(typeof(StudioTestApplication));
            await native.Dispatch(async () =>
            {
                var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
                var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var actors = new Actors(profiles);
                var permissions = new HomePermissionTrustService(home, (app, action) => null);
                StudioNativeWindow? window = null;
                await using var den = new StudioDenLifetime(actors, ct => window?.RetireSelectedContextAsync(ct) ?? Task.CompletedTask);
                var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([den]), permissions);
                var receipts = new HomeResourceStoreOwnershipAuthority(ownership, profiles);
                await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, profiles)]);
                var services = new ServiceCollection();
                services.AddSingleton(runtime); services.AddSingleton(profiles); services.AddSingleton<IAuthenticatedResourceActorSource>(actors);
                services.AddSingleton(permissions); services.AddSingleton(den); services.AddSingleton(ownership);
                services.AddSingleton<IResourceStoreOwnershipReceiptAuthority>(receipts);
                services.AddSingleton(SelectedFolder(chosen, avatarPath));
                services.AddTransient<ICanonicalAgentBuilderAdapter>(p => new DenCanonicalAgentBuilderAdapter(
                    ct => p.GetRequiredService<StudioDenLifetime>().OpenBoundSessionAsync(p.GetRequiredService<IResourceStoreOwnershipReceiptAuthority>(), ct)));
                await using var provider = services.BuildServiceProvider();
                window = new StudioNativeWindow(provider); window.Show();
                try
                {
                    await window.Initialization;
                    FindButton(window, "Create a Den in an empty folder…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(async () => { try { await den.OpenBoundSessionAsync(receipts); return true; } catch (UnauthorizedAccessException) { return false; } catch (InvalidOperationException) { return false; } });
                    var session = await den.OpenBoundSessionAsync(receipts);
                    Assert.Empty(await session.Den.ListAsync<AgentDefinitionRecord>("personal"));
                    var name = All(window).OfType<TextBox>().Single(item => AutomationProperties.GetName(item) == "New Agent name");
                    name.Text = "Native authored Agent";
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                    FindButton(window, "Create Agent").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(async () => (await session.Den.ListAsync<AgentDefinitionRecord>("personal")).Count == 1);
                    await Until(() => Task.FromResult(All(window).OfType<Button>().Any(item => item.Content?.ToString() == "Save to Agent")));
                    var agent = Assert.Single(await session.Den.ListAsync<AgentDefinitionRecord>("personal"));
                    Assert.Equal("Native authored Agent", agent.DisplayName);
                    Assert.Null(agent.Presentation);
                    if (importAvatar)
                    {
                        FindButton(window, "Import an avatar image into this Agent…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var assetField = All(window).OfType<TextBox>().Single(item => AutomationProperties.GetName(item) == "Static fallback asset reference");
                        await Until(() => Task.FromResult(!string.IsNullOrEmpty(assetField.Text)));
                        var reference = (await session.Den.GetAsync<BlobReferenceRecord>("personal", assetField.Text!))!;
                        Assert.Equal(agent.Id, reference.OwnerId);
                        Assert.Equal(DenAgentPresentationAssets.AgentOwnerKind, reference.OwnerKind);
                        Assert.Equal("image/gif", reference.MediaType);
                        var imported = await session.Den.ReadAttachmentAsync("personal", reference.Id);
                        try { Assert.Equal(avatarBytes, imported); } finally { Array.Clear(imported); }
                        var accessible = All(window).OfType<TextBox>().Single(item => AutomationProperties.GetName(item) == "Accessible avatar name");
                        accessible.Text = "Imported native avatar";
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                        FindButton(window, "Apply avatar identity").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        FindButton(window, "Save to Agent").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await Until(async () => (await session.Den.GetAsync<AgentDefinitionRecord>("personal", agent.Id))!.Presentation is not null);
                        Assert.Equal(reference.Id, (await session.Den.GetAsync<AgentDefinitionRecord>("personal", agent.Id))!.Presentation!.StaticFallbackAssetReference);
                    }
                    Assert.False(await session.Den.AccessPolicy.IsAllowedAsync(session.Actor.ActorId, "personal", agent.Id, DenPermission.Execute));
                    actors.Changed = session.Actor with { AuthenticationRevision = "changed-native-fixture-session" };
                    var denied = await Record.ExceptionAsync(() => window.ValidateWorkspaceAsync(default));
                    Assert.True(denied is UnauthorizedAccessException or DenException { Code: DenErrorCode.Forbidden });
                    Assert.Empty(All(window).OfType<ComboBox>().Single(item => AutomationProperties.GetName(item) == "Choose Agent").Items);
                    Assert.DoesNotContain(All(window).OfType<Button>(), item => item.Content?.ToString() == "Save to Agent");
                }
                finally { await CloseOriginalWindowAsync(window); }
                return true;
            }, default);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Immediate_authored_native_input_is_saved_and_reopened_by_the_original_create_and_save_buttons()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-native-immediate-" + Guid.NewGuid().ToString("N"));
        var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
        HeadlessUnitTestSession? native = null; Exception? primary = null; var outerCleanup = new List<Exception>();
        try
        {
            native = HeadlessUnitTestSession.StartNew(typeof(StudioTestApplication));
            await native.Dispatch(async () =>
            {
                var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
                var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var actors = new Actors(profiles);
                var permissions = new HomePermissionTrustService(home, (app, action) => null);
                StudioNativeWindow? window = null; ServiceProvider? provider = null;
                StudioDenLifetime? den = null; HomeCoreRuntime? runtime = null;
                Task? originalAction = null; Exception? actionPrimary = null; var cleanup = new List<Exception>();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    den = new StudioDenLifetime(actors, ct => window?.RetireSelectedContextAsync(ct) ?? Task.CompletedTask);
                    var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([den]), permissions);
                    var receipts = new HomeResourceStoreOwnershipAuthority(ownership, profiles);
                    runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, profiles)]);
                    var services = new ServiceCollection();
                    services.AddSingleton(runtime); services.AddSingleton(profiles); services.AddSingleton<IAuthenticatedResourceActorSource>(actors);
                    services.AddSingleton(permissions); services.AddSingleton(den); services.AddSingleton(ownership);
                    services.AddSingleton<IResourceStoreOwnershipReceiptAuthority>(receipts);
                    services.AddSingleton(SelectedFolder(chosen, Path.Combine(root, "unused-avatar.gif")));
                    services.AddTransient<ICanonicalAgentBuilderAdapter>(p => new DenCanonicalAgentBuilderAdapter(
                        ct => p.GetRequiredService<StudioDenLifetime>().OpenBoundSessionAsync(p.GetRequiredService<IResourceStoreOwnershipReceiptAuthority>(), ct)));
                    provider = services.BuildServiceProvider(); window = new StudioNativeWindow(provider); window.Show();
                    await window.Initialization.WaitAsync(deadline.Token);
                    async Task ClickOriginal(string caption)
                    {
                        FindButton(window, caption).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        originalAction = window.WhenActionsIdleAsync();
                        await originalAction.WaitAsync(deadline.Token);
                        originalAction = null;
                    }
                    void Edit(string name, string value) => All(window).OfType<TextBox>().Single(input => input.Name == "studio-" + name).Text = value;
                    await ClickOriginal("Create a Den in an empty folder…");
                    var session = await den.OpenBoundSessionAsync(receipts, deadline.Token);
                    Assert.Empty(await session.Den.ListAsync<AgentDefinitionRecord>("personal", deadline.Token));
                    // Deliberately no dispatcher pump or model assignment between visible edits and the original button.
                    Edit("AgentName", "Native immediate Agent"); Edit("AgentDescription", "Visible immediate purpose");
                    Edit("AgentInstructions", "Visible immediate instructions"); Edit("ModelID", "exact-model"); Edit("ProviderID", "exact-provider");
                    Edit("RequiredCapabilities", "Text"); Edit("MaxTokens", "500"); Edit("MaxTimeSeconds", "10.125");
                    Edit("MaxSteps", "20"); Edit("MaxToolCalls", "3"); Edit("MaxCost", "0.125");
                    Edit("ToolIDs", "tool.exact"); Edit("SkillIDs", "skill.exact"); Edit("PluginIDs", "plugin.exact");
                    Edit("McpCapabilityIDs", "mcp.exact"); Edit("AllowedPermissions", "resource.read.exact");
                    All(window).OfType<CheckBox>().Single(input => input.Content?.ToString() == "Inherit the current model policy").IsChecked = false;
                    await ClickOriginal("Create Agent");
                    var created = Assert.Single(await session.Den.ListAsync<AgentDefinitionRecord>("personal", deadline.Token));
                    Assert.Equal("Native immediate Agent", created.DisplayName); Assert.Equal("Visible immediate purpose", created.Description);
                    Assert.Equal("Visible immediate instructions", created.Instructions); Assert.False(created.Enabled); Assert.Null(created.Presentation);
                    var originalId = created.Id; var originalVersion = created.Version;
                    var firstPolicy = JsonSerializer.Deserialize<AgentModelPolicy>(created.ModelPolicyJson!, DenJson.Options)!;
                    var firstBudget = JsonSerializer.Deserialize<AgentBudgetLimits>(created.BudgetJson!, DenJson.Options)!;
                    Assert.False(firstPolicy.Inherit); Assert.Equal("exact-model", firstPolicy.ModelId); Assert.Equal("exact-provider", firstPolicy.ProviderId);
                    Assert.Equal(new[] { "Text" }, firstPolicy.RequiredCapabilities!.ToArray());
                    Assert.Equal(500, firstBudget.MaxTokens); Assert.Equal(TimeSpan.FromTicks(101250000), firstBudget.MaxTime);
                    Assert.Equal(20, firstBudget.MaxSteps); Assert.Equal(3, firstBudget.MaxToolCalls); Assert.Equal(0.125m, firstBudget.MaxCost);
                    Assert.Equal(new[] { "tool.exact" }, created.ToolIds); Assert.Equal(new[] { "skill.exact" }, created.SkillIds);
                    Assert.Equal(new[] { "plugin.exact" }, created.PluginIds); Assert.Equal(new[] { "mcp.exact" }, created.McpCapabilityIds);
                    Assert.Equal(new[] { "resource.read.exact" }, created.AllowedPermissions);
                    Edit("AgentName", "Immediate saved Agent"); Edit("AgentDescription", "Immediate saved purpose");
                    Edit("AgentInstructions", "Immediate saved instructions"); Edit("MaxTokens", "900");
                    await ClickOriginal("Save canonical Agent draft");
                    var saved = Assert.Single(await session.Den.ListAsync<AgentDefinitionRecord>("personal", deadline.Token));
                    Assert.Equal(originalId, saved.Id); Assert.Equal(originalVersion, saved.Version); Assert.True(saved.Revision > created.Revision);
                    Assert.Equal("Immediate saved Agent", saved.DisplayName); Assert.Equal("Immediate saved purpose", saved.Description);
                    Assert.Equal("Immediate saved instructions", saved.Instructions); Assert.False(saved.Enabled); Assert.Null(saved.Presentation);
                    Assert.Equal(900, JsonSerializer.Deserialize<AgentBudgetLimits>(saved.BudgetJson!, DenJson.Options)!.MaxTokens);
                    await ClickOriginal("Open selected Agent");
                    Assert.Equal("Immediate saved Agent", All(window).OfType<TextBox>().Single(input => input.Name == "studio-AgentName").Text);
                    Assert.Equal("Immediate saved purpose", All(window).OfType<TextBox>().Single(input => input.Name == "studio-AgentDescription").Text);
                    Assert.Equal("Immediate saved instructions", All(window).OfType<TextBox>().Single(input => input.Name == "studio-AgentInstructions").Text);
                    Assert.Equal("900", All(window).OfType<TextBox>().Single(input => input.Name == "studio-MaxTokens").Text);
                    Assert.Equal(saved.Revision, (await session.Den.GetAsync<AgentDefinitionRecord>("personal", originalId, deadline.Token))!.Revision);
                    Assert.False(await session.Den.AccessPolicy.IsAllowedAsync(session.Actor.ActorId, "personal", originalId, DenPermission.Execute, deadline.Token));
                    Assert.Empty(await session.Den.ListAsync<AgentRunRecord>("personal", deadline.Token));
                    Assert.True(File.Exists(Path.Combine(root, "home.json")));
                }
                catch (Exception error) { actionPrimary = error; }
                finally
                {
                    // Cancel the host, settle the SAME accepted original pipeline, and retire every original resource independently.
                    if (window is not null) try { await CloseOriginalWindowAsync(window); } catch (Exception error) { cleanup.Add(error); }
                    if (originalAction is not null) try { await originalAction; } catch (Exception error) { cleanup.Add(error); }
                    if (window is not null) try { await window.RetireWorkspaceAsync(default); } catch (Exception error) { cleanup.Add(error); }
                    if (provider is not null) try { await provider.DisposeAsync(); } catch (Exception error) { cleanup.Add(error); }
                    if (den is not null) try { await den.DisposeAsync(); } catch (Exception error) { cleanup.Add(error); }
                    if (runtime is not null) try { await runtime.DisposeAsync(); } catch (Exception error) { cleanup.Add(error); }
                }
                ThrowOriginalAndCleanup(actionPrimary, cleanup);
                return true;
            }, default);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            if (native is not null) try { await native.DisposeAsync(); } catch (Exception error) { outerCleanup.Add(error); }
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception error) { outerCleanup.Add(error); }
        }
        ThrowOriginalAndCleanup(primary, outerCleanup);
    }

    [Fact]
    public async Task Immediate_native_avatar_input_saves_coding_and_joke_states_on_the_same_Agent_and_reopens_with_static_fallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-native-avatar-immediate-" + Guid.NewGuid().ToString("N"));
        var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
        var avatarPath = Path.Combine(root, "picked-avatar.gif");
        var avatarBytes = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");
        HeadlessUnitTestSession? native = null; Exception? primary = null; var outerCleanup = new List<Exception>();
        try
        {
            await File.WriteAllBytesAsync(avatarPath, avatarBytes);
            native = HeadlessUnitTestSession.StartNew(typeof(StudioTestApplication));
            await native.Dispatch(async () =>
            {
                var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
                var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var actors = new Actors(profiles);
                var permissions = new HomePermissionTrustService(home, (app, action) => null);
                StudioNativeWindow? window = null; ServiceProvider? provider = null;
                StudioDenLifetime? den = null; HomeCoreRuntime? runtime = null;
                Task? originalInitialization = null; Task? originalAction = null;
                Exception? actionPrimary = null; var cleanup = new List<Exception>();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    den = new StudioDenLifetime(actors, ct => window?.RetireSelectedContextAsync(ct) ?? Task.CompletedTask);
                    var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([den]), permissions);
                    var receipts = new HomeResourceStoreOwnershipAuthority(ownership, profiles);
                    runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, profiles)]);
                    var services = new ServiceCollection();
                    services.AddSingleton(runtime); services.AddSingleton(profiles); services.AddSingleton<IAuthenticatedResourceActorSource>(actors);
                    services.AddSingleton(permissions); services.AddSingleton(den); services.AddSingleton(ownership);
                    services.AddSingleton<IResourceStoreOwnershipReceiptAuthority>(receipts);
                    services.AddSingleton(SelectedFolder(chosen, avatarPath));
                    services.AddTransient<ICanonicalAgentBuilderAdapter>(p => new DenCanonicalAgentBuilderAdapter(
                        ct => p.GetRequiredService<StudioDenLifetime>().OpenBoundSessionAsync(p.GetRequiredService<IResourceStoreOwnershipReceiptAuthority>(), ct)));
                    provider = services.BuildServiceProvider(); window = new StudioNativeWindow(provider); window.Show();
                    originalInitialization = window.Initialization; await originalInitialization.WaitAsync(deadline.Token); originalInitialization = null;
                    async Task ClickOriginal(string caption, bool avatar = false)
                    {
                        FindButton(window, caption).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        originalAction = avatar ? window.WhenAvatarActionsIdleAsync() : window.WhenActionsIdleAsync();
                        await originalAction.WaitAsync(deadline.Token); originalAction = null;
                    }
                    TextBox Input(string field) => All(window).OfType<TextBox>().Single(input => input.Name == "studio-avatar-" + field);
                    void Edit(string field, string value) => Input(field).Text = value;
                    await ClickOriginal("Create a Den in an empty folder…");
                    var session = await den.OpenBoundSessionAsync(receipts, deadline.Token);
                    All(window).OfType<TextBox>().Single(input => input.Name == "studio-AgentName").Text = "Immediate avatar Agent";
                    await ClickOriginal("Create Agent");
                    var created = Assert.Single(await session.Den.ListAsync<AgentDefinitionRecord>("personal", deadline.Token));
                    var originalId = created.Id;
                    Assert.False(Input("NamespaceID").IsEnabled); Assert.False(Input("AgentID").IsEnabled);
                    Assert.False(FindButton(window, "Open Agent presentation").IsEnabled);
                    var assets = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var (state, label) in new[] { ("idle", "Idle"), ("coding", "Coding"), ("laugh", "Laugh") })
                    {
                        await ClickOriginal("Import an avatar image into this Agent…");
                        var referenceId = Input("StaticAsset").Text!;
                        var reference = (await session.Den.GetAsync<BlobReferenceRecord>("personal", referenceId, deadline.Token))!;
                        Assert.Equal(originalId, reference.OwnerId); Assert.Equal(DenAgentPresentationAssets.AgentOwnerKind, reference.OwnerKind);
                        var bytes = await session.Den.ReadAttachmentAsync("personal", referenceId, deadline.Token);
                        try { Assert.Equal(avatarBytes, bytes); } finally { Array.Clear(bytes); }
                        assets.Add(state, referenceId);
                        if (state == "idle")
                        {
                            Edit("AccessibleName", "Immediate accessible avatar");
                            All(window).OfType<CheckBox>().Single(input => input.Content?.ToString() == "Animate avatar").IsChecked = false;
                            await ClickOriginal("Apply avatar identity", true);
                        }
                        // No dispatcher pump/model assignment between each actual Text property edit and the original button.
                        Edit("StateID", state); Edit("StateLabel", label); Edit("StateAsset", referenceId);
                        await ClickOriginal("Add state", true);
                    }
                    Assert.Equal(3, assets.Values.Distinct(StringComparer.Ordinal).Count());
                    Edit("StaticAsset", assets["idle"]); Edit("AccessibleName", "Immediate accessible avatar"); Edit("InitialState", "idle");
                    All(window).OfType<CheckBox>().Single(input => input.Content?.ToString() == "Animate avatar").IsChecked = true;
                    await ClickOriginal("Apply avatar identity", true);
                    Edit("FromState", "idle"); Edit("ToState", "coding"); Edit("TransitionEvent", AgentPresentationEvents.Coding);
                    await ClickOriginal("Apply transition", true);
                    Edit("ReactionEvent", AgentPresentationEvents.UserJoke); Edit("ReactionState", "laugh");
                    await ClickOriginal("Apply reaction", true);
                    await ClickOriginal("Assign avatar to this Agent", true);
                    var saved = (await session.Den.GetAsync<AgentDefinitionRecord>("personal", originalId, deadline.Token))!;
                    Assert.Equal(originalId, saved.Id); Assert.Equal(created.Version, saved.Version); Assert.True(saved.Revision > created.Revision);
                    Assert.False(saved.Enabled); Assert.Equal("Immediate avatar Agent", saved.DisplayName);
                    var presentation = Assert.IsType<AgentPresentationDefinition>(saved.Presentation);
                    Assert.Equal("Immediate accessible avatar", presentation.AccessibleName); Assert.Equal(AgentIconPresentation.Animated, presentation.Mode);
                    Assert.Equal("idle", presentation.InitialStateId); Assert.Equal(assets["idle"], presentation.StaticFallbackAssetReference);
                    Assert.Equal(new[] { "idle", "coding", "laugh" }, presentation.States.Select(state => state.StateId));
                    Assert.Equal(new[] { "Idle", "Coding", "Laugh" }, presentation.States.Select(state => state.Label));
                    Assert.Equal(assets.Values, presentation.States.Select(state => state.AssetReference));
                    Assert.Contains(presentation.Transitions, transition => transition.FromStateId == "idle" && transition.ToStateId == "coding" && transition.EventId == AgentPresentationEvents.Coding);
                    Assert.Contains(presentation.Reactions, reaction => reaction.StateId == "laugh" && reaction.EventId == AgentPresentationEvents.UserJoke);
                    await ClickOriginal("Open selected Agent");
                    Assert.Equal(originalId, Input("AgentID").Text); Assert.Equal("personal", Input("NamespaceID").Text);
                    Assert.Equal("Immediate accessible avatar", Input("AccessibleName").Text); Assert.Equal("idle", Input("InitialState").Text);
                    Edit("StateID", "");
                    await ClickOriginal("Preview coding activity", true);
                    Assert.Equal(assets["coding"], All(window).OfType<TextBlock>().Single(input => AutomationProperties.GetName(input) == "Preview asset reference").Text);
                    await ClickOriginal("Preview joke reaction", true);
                    Assert.Equal(assets["laugh"], All(window).OfType<TextBlock>().Single(input => AutomationProperties.GetName(input) == "Preview asset reference").Text);
                    All(window).OfType<CheckBox>().Single(input => input.Content?.ToString() == "Preview reduced motion").IsChecked = true;
                    await ClickOriginal("Preview coding activity", true);
                    Assert.Equal(assets["idle"], All(window).OfType<TextBlock>().Single(input => AutomationProperties.GetName(input) == "Preview asset reference").Text);
                    Assert.Equal(saved.Revision, (await session.Den.GetAsync<AgentDefinitionRecord>("personal", originalId, deadline.Token))!.Revision);
                    Assert.False(await session.Den.AccessPolicy.IsAllowedAsync(session.Actor.ActorId, "personal", originalId, DenPermission.Execute, deadline.Token));
                    Assert.Empty(await session.Den.ListAsync<AgentRunRecord>("personal", deadline.Token));
                    var otherId = Guid.NewGuid().ToString("D");
                    var other = await session.Den.SaveAsync(created with { Id = otherId, DisplayName = "Other authorised Agent" }, 0, Guid.NewGuid().ToString("N"), deadline.Token);
                    var canonical = new AgentPresentationService(session.Den, new DenAgentPresentationAssets(session.Den));
                    var bound = new AgentAvatarEditor(canonical, boundAgent: ("personal", originalId));
                    await bound.OpenAsync("personal", originalId, deadline.Token);
                    var beforeIdentity = bound.CurrentAgentIdentity;
                    var refusal = await Assert.ThrowsAsync<DenException>(() => bound.OpenAsync("personal", otherId, deadline.Token));
                    Assert.Equal(DenErrorCode.Conflict, refusal.Code);
                    bound.Bindings.Set("AgentID", otherId);
                    refusal = await Assert.ThrowsAsync<DenException>(() => bound.DispatchAsync("OpenAvatar", null, deadline.Token).AsTask());
                    Assert.Equal(DenErrorCode.Conflict, refusal.Code); Assert.Equal(beforeIdentity, bound.CurrentAgentIdentity);
                    var standalone = new AgentAvatarEditor(canonical);
                    await standalone.OpenAsync("personal", otherId, deadline.Token);
                    Assert.Equal(other.Id, standalone.CurrentAgentIdentity!.Value.AgentID);
                    Assert.Equal(saved.Revision, (await session.Den.GetAsync<AgentDefinitionRecord>("personal", originalId, deadline.Token))!.Revision);
                    Assert.Equal(other.Revision, (await session.Den.GetAsync<AgentDefinitionRecord>("personal", otherId, deadline.Token))!.Revision);
                }
                catch (Exception error) { actionPrimary = error; }
                finally
                {
                    if (window is not null) try { await CloseOriginalWindowAsync(window); } catch (Exception error) { cleanup.Add(error); }
                    if (originalInitialization is not null) try { await originalInitialization; } catch (Exception error) { cleanup.Add(error); }
                    if (originalAction is not null) try { await originalAction; } catch (Exception error) { cleanup.Add(error); }
                    if (window is not null) try { await window.RetireWorkspaceAsync(default); } catch (Exception error) { cleanup.Add(error); }
                    if (provider is not null) try { await provider.DisposeAsync(); } catch (Exception error) { cleanup.Add(error); }
                    if (den is not null) try { await den.DisposeAsync(); } catch (Exception error) { cleanup.Add(error); }
                    if (runtime is not null) try { await runtime.DisposeAsync(); } catch (Exception error) { cleanup.Add(error); }
                }
                ThrowOriginalAndCleanup(actionPrimary, cleanup); return true;
            }, default);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            if (native is not null) try { await native.DisposeAsync(); } catch (Exception error) { outerCleanup.Add(error); }
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception error) { outerCleanup.Add(error); }
            Array.Clear(avatarBytes);
        }
        ThrowOriginalAndCleanup(primary, outerCleanup);
    }

    private static void ThrowOriginalAndCleanup(Exception? primary, IEnumerable<Exception> cleanup)
    {
        var failures = new List<Exception>();
        if (primary is not null) failures.Add(primary);
        foreach (var error in cleanup) if (!failures.Any(previous => ReferenceEquals(previous, error))) failures.Add(error);
        if (failures.Count > 1) throw new AggregateException(failures);
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
    }
    private sealed class Actors(HomeLocalProfileIdentity profiles) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Changed { get; set; }
        public Func<CancellationToken, ValueTask<AuthenticatedResourceActor?>>? ObservedCurrent { get; set; }
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) =>
            ObservedCurrent is { } observed ? observed(ct) :
            Changed is null ? profiles.GetCurrentAsync(ct) : ValueTask.FromResult<AuthenticatedResourceActor?>(Changed);
    }
    private static IEnumerable<Control> All(Control root) => root.GetLogicalDescendants().OfType<Control>().Prepend(root);
    private static Button FindButton(Control root, string name) => All(root).OfType<Button>().Single(item => item.Content?.ToString() == name);
    private static async Task Until(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await condition()) await Task.Delay(10, timeout.Token);
    }
    private static IStorageProvider SelectedFolder(string path, string avatarPath)
    {
        var folder = DispatchProxy.Create<IStorageFolder, StudioStorageTestProxy>();
        ((StudioStorageTestProxy)(object)folder).Handler = (method, _) => method.Name switch
        {
            "get_Path" => new Uri(path + Path.DirectorySeparatorChar),
            "get_Name" => Path.GetFileName(path),
            "Dispose" => null,
            _ => throw new NotSupportedException(method.Name)
        };
        var file = DispatchProxy.Create<IStorageFile, StudioStorageTestProxy>();
        ((StudioStorageTestProxy)(object)file).Handler = (method, _) => method.Name switch
        {
            "get_Path" => new Uri(avatarPath),
            "get_Name" => Path.GetFileName(avatarPath),
            "OpenReadAsync" => Task.FromResult<Stream>(File.OpenRead(avatarPath)),
            "Dispose" => null,
            _ => throw new NotSupportedException(method.Name)
        };
        var picker = DispatchProxy.Create<IStorageProvider, StudioStorageTestProxy>();
        ((StudioStorageTestProxy)(object)picker).Handler = (method, args) => method.Name switch
        {
            "get_CanPickFolder" => true,
            "get_CanOpen" => true,
            "OpenFilePickerAsync" when args![0] is FilePickerOpenOptions { AllowMultiple: false } => Task.FromResult<IReadOnlyList<IStorageFile>>([file]),
            "OpenFolderPickerAsync" when args![0] is FolderPickerOpenOptions { AllowMultiple: false } => Task.FromResult<IReadOnlyList<IStorageFolder>>([folder]),
            _ => throw new NotSupportedException(method.Name)
        };
        return picker;
    }
}
public class StudioStorageTestProxy : DispatchProxy
{
    internal Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
}
