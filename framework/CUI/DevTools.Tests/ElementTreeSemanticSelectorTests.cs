using Haven.CUI.DevTools;
using CakeOS.Cui.Compiler;
using Xunit;

namespace Haven.CUI.DevTools.Tests;

public sealed class ElementTreeSemanticSelectorTests
{
    [Fact]
    public void Find_matches_id_role_and_accessible_name_on_the_same_node()
    {
        var button = new CuiElementNode(new ElementId("button"), "Button", automationId: "save",
            accessibleName: "Save changes", role: "Button", isFocusable: true);
        var root = new CuiElementNode(new ElementId("root"), "Page", children: [button]);
        var tree = new CuiElementTree(root);

        var match = Assert.Single(tree.Find(new CuiElementSelector(Id: "save", Role: "button", AccessibleName: "Save changes")));
        Assert.Equal(button.Id, match.Id);
        Assert.Empty(tree.Find(new CuiElementSelector(Id: "other", Role: "button")));
    }

    [Fact]
    public void Hot_reload_keeps_last_valid_tree_after_a_compile_error()
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "1"));
        var session = new CuiHotReloadSession(new CuiCompiler(), options);
        var initial = session.Update("<Page id=\"home\"><Text id=\"title\" Text=\"Hello\" /></Page>", "home.cui");
        var current = session.Current;

        var failed = session.Update("<Page><Missing /></Page>", "home.cui");

        Assert.True(initial.Applied);
        Assert.False(failed.Applied);
        Assert.Same(current, failed.Current);
        Assert.Equal(1, failed.Revision);
    }
}
