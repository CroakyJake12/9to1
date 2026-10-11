using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiConditionalIdentityTests
{
    [Fact]
    public void Unchanged_condition_keeps_editor_identity_and_caret_while_bindings_refresh()
    {
        var state = new State();
        using var loader = new CuiControlLoader();
        loader.SetBindingContext(state);
        var (root, diagnostics) = loader.LoadMarkup("""
            <Cui><If condition="{Binding Visible}"><Input Type="Text" Value="{Binding Text}" /></If></Cui>
            """);
        Assert.Empty(diagnostics);
        loader.WireBindings(root!);
        var editor = root!.GetLogicalDescendants().OfType<TextBox>().Single();
        editor.CaretIndex = 3;
        state.NotifyAll();
        Assert.Same(editor, root!.GetLogicalDescendants().OfType<TextBox>().Single());
        Assert.Equal(3, editor.CaretIndex);
        state.Text = "Updated text";
        state.NotifyAll();
        Assert.Same(editor, root!.GetLogicalDescendants().OfType<TextBox>().Single());
        Assert.Equal("Updated text", editor.Text);
        state.Visible = false; state.NotifyAll();
        Assert.Empty(root!.GetLogicalDescendants().OfType<TextBox>());
        state.Visible = true; state.NotifyAll();
        Assert.NotSame(editor, root!.GetLogicalDescendants().OfType<TextBox>().Single());
    }

    [Fact]
    public void Retained_object_is_not_registered_again_for_an_unchanged_branch()
    {
        var state = new State();
        var nativeEditor = new TextBox { Text = "Retained editor" };
        var registry = new CuiControlRegistry();
        var acquisitions = 0;
        registry.RegisterObjectRenderer("RetainedEditor", _ => { acquisitions++; return nativeEditor; });
        using var loader = new CuiControlLoader(registry);
        loader.SetBindingContext(state);
        var (root, diagnostics) = loader.LoadMarkup("""
            <Cui><If condition="{Binding Visible}"><Object Type="RetainedEditor" /></If></Cui>
            """);
        Assert.Empty(diagnostics);
        loader.WireBindings(root!);
        for (var index = 0; index < 10; index++) state.NotifyAll();
        Assert.Equal(1, acquisitions);
        Assert.Same(nativeEditor, root!.GetLogicalDescendants().OfType<TextBox>().Single());
    }

    private sealed class State : ICuiBindingContext, INotifyPropertyChanged
    {
        public bool Visible = true;
        public string Text = "Editable text";
        public event PropertyChangedEventHandler? PropertyChanged;
        public void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        public bool TryGetValue(string path, out object? value)
        { value = path switch { "Visible" => Visible, "Text" => Text, _ => null }; return value is not null; }
    }
}
