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
            LayoutDocument = new([new(Guid.NewGuid(), "Harness", "Keep authored logic")], []),
            CapabilityBindings = [new("keep", SpaceCapabilityKind.Plugin, SpaceCapabilityOverride.Disabled, "1")]
        }, original.Revision, token);
        var editor = new SpaceBuilderController(registry, original.Id);
        Assert.True(await editor.LoadAsync(token));
        var bindings = Bindings(editor);
        Assert.True(bindings.TrySetValue("builder.context.webAccess", (int)SpaceWebAccessPolicy.Disabled));
        Assert.True(bindings.TrySetValue("builder.context.sourceOnly", true));
        Assert.True(bindings.TrySetValue("builder.context.includeSelection", false));
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
        Assert.Equal(JsonSerializer.Serialize(original.ModelPolicy), JsonSerializer.Serialize(saved.ModelPolicy));
        Assert.Equal(JsonSerializer.Serialize(original.LayoutDocument), JsonSerializer.Serialize(saved.LayoutDocument));
        Assert.Equal(JsonSerializer.Serialize(original.CapabilityBindings), JsonSerializer.Serialize(saved.CapabilityBindings));
        var reopened = new SpaceBuilderController(files.OpenRegistry(), original.Id);
        Assert.True(await reopened.LoadAsync(token));
        Assert.Equal(saved.DraftConfiguration.ContextPolicy, reopened.State.Draft.ContextPolicy);
        Assert.Equal(saved.DraftConfiguration.Inheritance, reopened.State.Draft.Inheritance);
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
            () => [skill], () => [package], (command, parameter, _) => { calls.Add((command, parameter)); return ValueTask.CompletedTask; });
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

    private static SpaceBuilderCuiBindings Bindings(SpaceBuilderController editor, Func<bool>? isCurrent = null) =>
        new(() => editor.State, (field, value) => editor.SetField(field, value),
            (category, value) => editor.SetCategoryState(category, value), (kind, id) => editor.DetachCapability(kind, id),
            () => [], () => [], (_, _, _) => ValueTask.CompletedTask, isCurrent);
}
