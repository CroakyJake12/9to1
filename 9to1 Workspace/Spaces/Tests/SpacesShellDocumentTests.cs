using CakeOS.Cui;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpacesShellDocumentTests
{
    [Fact]
    public void Shared_document_retains_command_identities_and_separates_the_two_dialog_owners()
    {
        var document = SpacesShellCuiDocument.Load();
        var all = Descendants(document.Components).ToArray();
        Assert.Single(all, c => c.Name == "spaces-shell");
        Assert.Single(all, c => c.Name == "space-navigation");
        Assert.Single(all, c => c.Name == "chat-workspace");
        Assert.DoesNotContain(all, c => c.Name == "chat-header");
        Assert.Equal("nav.open-entry", document.Actions["nav.open-entry"].Command);
        Assert.Equal("chat.message-edit", document.Actions["chat.message-edit"].Command);
        var navigation = Descendants([Assert.Single(all, c => c.Name == "space-navigation")]);
        var paths = navigation.SelectMany(c => c.Properties.Values.OfType<CuiBindingValue>()).Select(b => b.Path).ToArray();
        Assert.Contains("navDialog.visible", paths); Assert.DoesNotContain(paths, path => path.StartsWith("dialog.", StringComparison.Ordinal));
        Assert.Contains(all.SelectMany(c => c.Properties.Values.OfType<CuiBindingValue>()), binding => binding.Path == "dialog.visible");
    }
    [Fact]
    public void Host_capability_omits_only_original_generated_mount_and_never_substitutes_a_renderer()
    {
        var native = Descendants(SpacesShellCuiDocument.Load().Components).ToArray();
        var browser = Descendants(SpacesShellCuiDocument.Load(includeOriginalGeneratedChildren: false).Components).ToArray();
        Assert.Single(native, IsGenerated); Assert.DoesNotContain(browser, IsGenerated);
        Assert.Equal(native.Where(c => !IsGenerated(c)).Select(c => c.Name ?? c.Type), browser.Select(c => c.Name ?? c.Type));
        Assert.Contains(browser, c => c.Type == "Object" && c.Properties["Type"] is CuiLiteralValue { Value: "ChatMessageContent" });
    }
    [Fact]
    public void Native_frame_uses_only_two_explicit_original_surface_slots_and_the_same_header()
    {
        var all = Descendants(SpacesShellCuiDocument.LoadForOriginalNativeSlots().Components).ToArray();
        var types = all.Where(c => c.Type == "Object").Select(c => ((CuiLiteralValue)c.Properties["Type"]).Value).ToArray();
        Assert.Equal(["OriginalSpacesNavigation", "OriginalSpacesWorkspace"], types);
        Assert.Single(all, c => c.Name == "spaces-model");
        Assert.DoesNotContain(all, c => c.Name == "chat-workspace");
    }
    private static bool IsGenerated(CuiComponent c) => c.Type == "Object" && c.Properties.TryGetValue("Type", out var type) && type is CuiLiteralValue { Value: "OriginalChatGeneratedChildren" };
    private static IEnumerable<CuiComponent> Descendants(IEnumerable<CuiComponent> components)
    {
        foreach (var component in components)
        {
            yield return component;
            foreach (var child in Descendants(component.Children.Concat(component.ElseChildren))) yield return child;
        }
    }
}
