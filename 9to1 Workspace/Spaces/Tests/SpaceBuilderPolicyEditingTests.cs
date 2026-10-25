using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Builder;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpaceBuilderPolicyEditingTests
{
    [Fact]
    public async Task Policy_controls_round_trip_the_same_space_and_preserve_unexposed_fields()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var registry = files.OpenRegistry();
        var original = await registry.CreateAsync("Policies", cancellationToken: token);
        original = await registry.UpdateAsync(original with
        {
            ContextPolicy = new(MaximumRetrievedItems: 7),
            ModelPolicy = new("configured-model", ["text"], "configured-provider"),
            ExamplePairs = [new("Original prompt", "Original example response")],
            LayoutDocument = new([new(Guid.NewGuid(), "Harness", "Keep authored logic")], []),
            CapabilityBindings = [new("keep", SpaceCapabilityKind.Plugin, SpaceCapabilityOverride.Disabled, "1")]
        }, original.Revision, token);
        var editor = new SpaceBuilderController(registry, original.Id);
        Assert.True(await editor.LoadAsync(token));
        var bindings = Bindings(editor, setModelCapabilityRequired: (capability, required) => editor.SetModelCapabilityRequired(capability, required),
            setPersonalMemoryReadAllowed: allowed => editor.SetPersonalMemoryReadAllowed(allowed),
            setCapabilityOverride: (kind, id, value) => editor.SetCapabilityOverride(kind, id, value),
            updateExamplePair: editor.UpdateExamplePair);
        Assert.True(bindings.TrySetValue("builder.context.webAccess", (int)SpaceWebAccessPolicy.Disabled));
        Assert.True(bindings.TrySetValue("builder.context.sourceOnly", true));
        Assert.True(bindings.TrySetValue("builder.context.includeSelection", false));
        Assert.True(bindings.TrySetValue("builder.model.defaultId", "ollama:configured-model-2"));
        Assert.True(bindings.TrySetValue("builder.model.provider", "ollama"));
        Assert.True(bindings.TryGetValue("builder.model.capabilities", out var capabilityItems));
        var vision = Assert.IsType<SpaceBuilderModelCapabilityRow>(Assert.Single(
            Assert.IsAssignableFrom<object[]>(capabilityItems), item => ((SpaceBuilderModelCapabilityRow)item).id == "Vision"));
        Assert.False(vision.required);
        Assert.True(bindings.TrySetItemValue(vision, "required", true));
        Assert.False(bindings.TrySetItemValue(vision, "required", false)); // Stale repeat item cannot undo the edit.
        Assert.True(bindings.TryGetValue("builder.memory.personalRead", out var personalMemory));
        Assert.Equal(false, personalMemory);
        Assert.True(bindings.TrySetValue("builder.memory.personalRead", true));
        Assert.True(bindings.TryGetValue("builder.memory.personalRead", out personalMemory));
        Assert.Equal(true, personalMemory);
        Assert.True(bindings.TryGetValue("builder.capabilities", out var capabilityRows));
        var attached = Assert.IsType<SpaceBuilderCapabilityRow>(Assert.Single(
            Assert.IsAssignableFrom<object[]>(capabilityRows), item => ((SpaceBuilderCapabilityRow)item).key == "Plugin:keep"));
        Assert.Equal((int)SpaceCapabilityOverride.Disabled, attached.policy);
        Assert.True(bindings.TrySetItemValue(attached, "policy", (int)SpaceCapabilityOverride.Enabled));
        Assert.False(bindings.TrySetItemValue(attached, "policy", (int)SpaceCapabilityOverride.Disabled)); // Stale row cannot restore the old policy.
        Assert.True(bindings.TryGetValue("builder.examplePairs", out var exampleItems));
        var example = Assert.IsType<SpaceBuilderExamplePairRow>(Assert.Single(Assert.IsAssignableFrom<object[]>(exampleItems)));
        Assert.True(bindings.TrySetItemValue(example, "user", "Updated prompt"));
        Assert.False(bindings.TrySetItemValue(example, "assistant", "Stale response")); // A stale row cannot overwrite a newer edit.
        Assert.True(bindings.TryGetValue("builder.examplePairs", out exampleItems));
        example = Assert.IsType<SpaceBuilderExamplePairRow>(Assert.Single(Assert.IsAssignableFrom<object[]>(exampleItems)));
        Assert.True(bindings.TrySetItemValue(example, "assistant", "Updated response"));
        Assert.True(editor.AddExamplePair());
        var emptyExample = new SpaceExamplePair(string.Empty, string.Empty);
        var addedExample = new SpaceExamplePair("Second prompt", "Second response");
        Assert.True(editor.UpdateExamplePair(1, emptyExample, addedExample));
        Assert.True(editor.RemoveExamplePair(1, addedExample));
        Assert.Single(editor.State.Draft.ExamplePairs);
        Assert.Equal("ollama:configured-model-2", editor.State.Draft.ModelPolicy!.DefaultModelId);
        Assert.Equal("ollama", editor.State.Draft.ModelPolicy.ProviderConstraint);
        Assert.Equal(new[] { "text", "Vision" }, editor.State.Draft.ModelPolicy.RequiredCapabilities);
        foreach (var key in new[] { "sources", "apps", "models", "memory" })
            Assert.True(bindings.TrySetValue("builder.inherit." + key, true));
        foreach (var key in new[] { "plugins", "skills", "agents" })
            Assert.True(bindings.TrySetValue("builder.inherit." + key, false));
        Assert.True(await editor.SaveAsync(token));
        var saved = (await files.OpenRegistry().GetAsync(original.Id, token))!;
        Assert.Equal(original.Id, saved.Id);
        Assert.Equal(original.Revision, saved.Revision);
        Assert.Equal(original.ActiveConfigurationRevision, saved.ActiveConfigurationRevision);
        Assert.Equal(new SpaceContextPolicy(SpaceWebAccessPolicy.Disabled, true, false, 7), saved.DraftConfiguration!.ContextPolicy);
        Assert.Equal(new SpaceInheritancePolicy(true, true, true, true, false, false, false), saved.DraftConfiguration.Inheritance);
        Assert.Equal(original.ModelPolicy?.DefaultModelId, saved.ModelPolicy?.DefaultModelId);
        Assert.Equal(original.ModelPolicy?.ProviderConstraint, saved.ModelPolicy?.ProviderConstraint);
        var originalRequiredCapabilities = Assert.IsType<SpaceModelPolicy>(original.ModelPolicy).RequiredCapabilities;
        var savedRequiredCapabilities = Assert.IsType<SpaceModelPolicy>(saved.ModelPolicy).RequiredCapabilities;
        Assert.NotNull(originalRequiredCapabilities);
        Assert.NotNull(savedRequiredCapabilities);
        Assert.Equal(originalRequiredCapabilities.ToArray(), savedRequiredCapabilities.ToArray());
        var originalMemoryScopes = Assert.IsType<SpaceMemoryPolicy>(original.MemoryPolicy).AllowedScopes;
        var savedMemoryScopes = Assert.IsType<SpaceMemoryPolicy>(saved.MemoryPolicy).AllowedScopes;
        Assert.NotNull(originalMemoryScopes);
        Assert.NotNull(savedMemoryScopes);
        Assert.Equal(originalMemoryScopes.ToArray(), savedMemoryScopes.ToArray());
        Assert.Equal(JsonSerializer.Serialize(original.ExamplePairs), JsonSerializer.Serialize(saved.ExamplePairs));
        Assert.Equal("ollama:configured-model-2", saved.DraftConfiguration.ModelPolicy!.DefaultModelId);
        Assert.Equal("ollama", saved.DraftConfiguration.ModelPolicy.ProviderConstraint);
        var draftRequiredCapabilities = saved.DraftConfiguration.ModelPolicy.RequiredCapabilities;
        Assert.NotNull(draftRequiredCapabilities);
        Assert.Equal(new[] { "text", "Vision" }, draftRequiredCapabilities.ToArray());
        var draftMemoryScopes = Assert.IsType<SpaceMemoryPolicy>(saved.DraftConfiguration.MemoryPolicy).AllowedScopes;
        Assert.NotNull(draftMemoryScopes);
        Assert.Equal(new[] { SpaceMemoryScope.Conversation, SpaceMemoryScope.Space, SpaceMemoryScope.Personal }, draftMemoryScopes.ToArray());
        var savedExample = Assert.Single(saved.DraftConfiguration.ExamplePairs);
        Assert.Equal("Updated prompt", savedExample.User);
        Assert.Equal("Updated response", savedExample.Assistant);
        Assert.Equal(JsonSerializer.Serialize(original.LayoutDocument), JsonSerializer.Serialize(saved.LayoutDocument));
        Assert.Equal(JsonSerializer.Serialize(original.CapabilityBindings), JsonSerializer.Serialize(saved.CapabilityBindings));
        Assert.Equal(SpaceCapabilityOverride.Enabled, Assert.Single(saved.DraftConfiguration.CapabilityBindings).Override);
        var reopened = new SpaceBuilderController(files.OpenRegistry(), original.Id);
        Assert.True(await reopened.LoadAsync(token));
        Assert.Equal(saved.DraftConfiguration.ContextPolicy, reopened.State.Draft.ContextPolicy);
        Assert.Equal(saved.DraftConfiguration.Inheritance, reopened.State.Draft.Inheritance);
        Assert.Equal(saved.DraftConfiguration.ModelPolicy?.DefaultModelId, reopened.State.Draft.ModelPolicy?.DefaultModelId);
        Assert.Equal(saved.DraftConfiguration.ModelPolicy?.ProviderConstraint, reopened.State.Draft.ModelPolicy?.ProviderConstraint);
        var savedDraftCapabilities = saved.DraftConfiguration.ModelPolicy?.RequiredCapabilities;
        var reopenedDraftCapabilities = reopened.State.Draft.ModelPolicy?.RequiredCapabilities;
        Assert.NotNull(savedDraftCapabilities);
        Assert.NotNull(reopenedDraftCapabilities);
        Assert.Equal(savedDraftCapabilities.ToArray(), reopenedDraftCapabilities.ToArray());
        Assert.Equal(saved.DraftConfiguration.MemoryPolicy!.AllowedScopes, reopened.State.Draft.MemoryPolicy!.AllowedScopes);
        Assert.Equal("Updated prompt", Assert.Single(reopened.State.Draft.ExamplePairs).User);
        Assert.Equal("Updated response", Assert.Single(reopened.State.Draft.ExamplePairs).Assistant);
        Assert.Equal(SpaceCapabilityOverride.Enabled, Assert.Single(reopened.State.Draft.CapabilityBindings).Override);
        Assert.False(reopened.State.HasChanges);
        Assert.Equal(saved.ActiveConfigurationRevision, reopened.State.ActiveRevision); // Saved configuration is not activation.
    }

    [Fact]
    public async Task Conflicting_or_stale_ui_cannot_replace_saved_policy()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Conflict", cancellationToken: token);
        var first = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        var second = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await first.LoadAsync(token));
        Assert.True(await second.LoadAsync(token));
        Assert.True(first.SetField(SpaceBuilderField.SourceOnly, "true"));
        Assert.True(await first.SaveAsync(token));
        Assert.True(second.SetField(SpaceBuilderField.InheritPlugins, "false"));
        var draft = second.State.Draft;
        Assert.False(await second.SaveAsync(token));
        Assert.True(second.State.HasConflict);
        Assert.Equal(draft, second.State.Draft);
        var saved = (await files.OpenRegistry().GetAsync(space.Id, token))!;
        Assert.True(saved.DraftConfiguration!.ContextPolicy!.SourceOnly);
        Assert.True(saved.DraftConfiguration.Inheritance!.Plugins);
        Assert.False(saved.ContextPolicy!.SourceOnly); // Draft saves do not change the active resolver view.
        var current = true;
        var bindings = Bindings(first, () => current);
        Assert.False(bindings.TrySetValue("builder.context.webAccess", -1));
        Assert.False(bindings.TrySetValue("builder.context.webAccess", 3));
        Assert.False(bindings.TrySetValue("builder.inherit.skills", "false"));
        Assert.True(bindings.IsActionAvailable("space-builder.skill.attach"));
        current = false;
        Assert.False(bindings.TrySetValue("builder.context.sourceOnly", false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("space-builder.plugin.attach", "package", token).AsTask());
        Assert.False(first.State.HasChanges);
    }

    [Fact]
    public async Task Resolver_keeps_parent_disabled_categories_and_inherited_source_restrictions()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var registry = files.OpenRegistry();
        var parent = await registry.CreateAsync("Parent", cancellationToken: token);
        await registry.UpdateAsync(parent with
        {
            CapabilityCategoryPolicy = new(Skills: SpaceCapabilityCategoryState.Disabled, Plugins: SpaceCapabilityCategoryState.Disabled),
            ContextPolicy = new(SpaceWebAccessPolicy.Disabled, true, false, 2)
        }, parent.Revision, token);
        var child = await registry.CreateAsync("Child", null, parent.Id, token);
        var editor = new SpaceBuilderController(registry, child.Id);
        Assert.True(await editor.LoadAsync(token));
        Assert.True(editor.SetField(SpaceBuilderField.InheritSources, "true"));
        Assert.True(editor.SetField(SpaceBuilderField.WebAccess, "Allowed"));
        Assert.True(editor.SetField(SpaceBuilderField.InheritPlugins, "false"));
        Assert.True(editor.SetCategoryState(SpaceBuilderCategory.Plugins, SpaceCapabilityCategoryState.Enabled));
        Assert.True(editor.AttachCapability(new("local-plugin", SpaceCapabilityKind.Plugin, SpaceCapabilityOverride.Enabled)));
        Assert.True(await editor.SaveAsync(token));
        var effective = await new SpacePolicyResolver(files.OpenRegistry()).ResolveAsync(child.Id, token);
        Assert.Equal(SpaceWebAccessPolicy.Disabled, effective.Context.WebAccess);
        Assert.True(effective.Context.SourceOnly);
        Assert.False(effective.Context.IncludeCurrentSelection);
        Assert.Equal(2, effective.Context.MaximumRetrievedItems);
        Assert.Contains(SpaceCapabilityKind.Plugin, effective.DisabledCapabilityKinds);
        Assert.Contains(SpaceCapabilityKind.Skill, effective.DisabledCapabilityKinds);
        Assert.Empty(effective.Capabilities);
        // Canonical resolver test; model/request activation remains a separate gap.
    }

    [Fact]
    public async Task Edits_during_a_real_held_commit_remain_unsaved()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Concurrent", cancellationToken: token);
        var source = new ControlledDetailsSettings(files.OpenStore());
        var editor = new SpaceBuilderController(new SpaceRegistry(source), space.Id);
        Assert.True(await editor.LoadAsync(token));
        Assert.True(editor.SetField(SpaceBuilderField.SourceOnly, "true"));
        source.HoldNextCommit = true;
        var saving = editor.SaveAsync(token);
        try
        {
            await source.CommitStarted.Task.WaitAsync(token);
            Assert.True(editor.SetField(SpaceBuilderField.InheritAgents, "false"));
            source.ReleaseCommit.TrySetResult();
            Assert.True(await saving);
            var saved = (await files.OpenRegistry().GetAsync(space.Id, token))!;
            Assert.True(saved.DraftConfiguration!.ContextPolicy!.SourceOnly);
            Assert.True(saved.DraftConfiguration.Inheritance!.Agents);
            Assert.False(editor.State.Draft.Inheritance.Agents);
            Assert.True(editor.State.HasChanges);
            Assert.True(await editor.SaveAsync(token));
            Assert.False((await files.OpenRegistry().GetAsync(space.Id, token))!.DraftConfiguration!.Inheritance!.Agents);
        }
        finally { source.ReleaseCommit.TrySetResult(); await saving; }
    }

    [Fact]
    public async Task Authored_attach_buttons_dispatch_live_skill_and_package_ids()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Attachment UI", cancellationToken: token);
        var editor = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await editor.LoadAsync(token));
        var skill = new ExtensionSkillCatalogEntry("package/skill", "package", "1.2.3", "Fixture skill", "", ExtensionInstallState.Enabled, true, ["spaces"]);
        var manifest = new ExtensionPackageManifest("sidebar-package", ".", "Fixture sidebar package", ExtensionPackageType.Plugin,
            "2.3.4", "*", "", "Fixture", "Fixture", null, null, ExtensionPermission.None, [], [], [], null);
        var package = new InstalledExtensionPackage(Guid.NewGuid(), Guid.NewGuid(), manifest, ".", ExtensionPermission.None,
            ExtensionInstallState.Enabled, true, false, "fixture-content-hash", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var calls = new List<(string Command, object? Parameter)>();
        var bindings = new SpaceBuilderCuiBindings(() => editor.State, (field, value) => editor.SetField(field, value),
            (category, value) => editor.SetCategoryState(category, value), (kind, id) => editor.DetachCapability(kind, id),
            () => [skill], () => [package], (command, parameter, _) => { calls.Add((command, parameter)); return ValueTask.CompletedTask; },
            isCurrent: () => true, canGoBack: () => false);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            var (tree, diagnostics) = loader.TryLoad(SpaceBuilderCuiDocument.Load());
            Assert.Empty(diagnostics);
            var root = Assert.IsAssignableFrom<Control>(tree);
            loader.WireBindings(root);
            var window = new Window { Content = root, Width = 1000, Height = 900 };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Button Find(string id) => root.GetVisualDescendants().OfType<Button>().Single(button =>
                    button.Name == id || AutomationProperties.GetAutomationId(button) == id);
                var attachSkill = Find("space-builder-attach-skill");
                var attachPlugin = Find("space-builder-attach-plugin");
                Assert.True(attachSkill.IsEnabled);
                Assert.True(attachPlugin.IsEnabled);
                attachSkill.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync();
                attachPlugin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync();
                Assert.Equal(new[] { ("space-builder.skill.attach", (object?)skill.SkillId),
                    ("space-builder.plugin.attach", (object?)manifest.PackageId) }, calls);
                Assert.Empty(package.Manifest.Capabilities);
            }
            finally { window.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, token);
    }

    private static SpaceBuilderCuiBindings Bindings(SpaceBuilderController editor, Func<bool>? isCurrent = null,
        Action<ToolCapability, bool>? setModelCapabilityRequired = null,
        Action<bool>? setPersonalMemoryReadAllowed = null,
        Action<SpaceCapabilityKind, string, SpaceCapabilityOverride>? setCapabilityOverride = null,
        Func<int, SpaceExamplePair, SpaceExamplePair, bool>? updateExamplePair = null) =>
        new(() => editor.State, (field, value) => editor.SetField(field, value),
            (category, value) => editor.SetCategoryState(category, value), (kind, id) => editor.DetachCapability(kind, id),
            () => [], () => [], (_, _, _) => ValueTask.CompletedTask, isCurrent,
            setModelCapabilityRequired: setModelCapabilityRequired,
            setPersonalMemoryReadAllowed: setPersonalMemoryReadAllowed,
            setCapabilityOverride: setCapabilityOverride,
            updateExamplePair: updateExamplePair);
}
