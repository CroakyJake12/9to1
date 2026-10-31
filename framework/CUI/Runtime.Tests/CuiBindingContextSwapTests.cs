using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Automation;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Swapping the binding context is how a host replaces the document behind an already mounted surface:
/// Boards activates another board, Data replaces a query result set. The measured defect this pins (owner
/// report 31 October 2026, "the actual board part of boards isn't working") is that the mounted tree kept
/// the retired document's state forever, because every recorded live binding and conditional branch named
/// the old context object. Boards therefore left the splash on screen with a board genuinely open, and its
/// save commands stayed unreachable.
/// </summary>
public sealed class CuiBindingContextSwapTests
{
    private const string Markup = """
        <Cui><Container Type="Vertical Stack">
          <Text ID="EditorPane" Text="Board surface" Active="{Binding ShowEditor}" />
          <Text ID="BoardName" Text="{Binding BoardTitle, fallback=none}" />
          <If Condition="{Binding ShowEditor}"><Text ID="EditorBranch" Text="Editing this board" /></If>
        </Container></Cui>
        """;

    [Fact]
    public void Context_swap_repoints_bindings_and_conditional_branches_to_the_replacing_document()
    {
        var first = new Board(showEditor: true, title: "First board");
        using var loader = new CuiControlLoader();
        loader.SetBindingContext(first);
        var (root, diagnostics) = loader.LoadMarkup(Markup);
        Assert.Empty(diagnostics);
        loader.WireBindings(root!);

        Assert.NotNull(TryFind(root!, "EditorPane"));
        Assert.True(TryFind(root!, "EditorPane")!.IsVisible);
        Assert.NotNull(TryFind(root!, "EditorBranch"));
        Assert.Equal("First board", TextNamed(root!, "BoardName"));

        // Exactly the sequence the Boards host runs when it activates another board: no extra notification,
        // the surface is rewired around the new document.
        var second = new Board(showEditor: false, title: "Second board");
        loader.SetBindingContext(second);
        loader.SetActionDispatcher(second);
        loader.WireBindings(root!);

        Assert.True(TryFind(root!, "EditorPane") is not { IsVisible: true },
            "the editor surface of the retired board stayed on screen after the document was replaced");
        Assert.True(TryFind(root!, "EditorBranch") is null,
            "the conditional editor branch of the retired board survived the document replacement");
        Assert.Equal("Second board", TextNamed(root!, "BoardName"));
    }

    [Fact]
    public void The_replacing_document_can_show_the_surface_the_retired_one_hid()
    {
        var first = new Board(showEditor: false, title: "First board");
        using var loader = new CuiControlLoader();
        loader.SetBindingContext(first);
        var (root, diagnostics) = loader.LoadMarkup(Markup);
        Assert.Empty(diagnostics);
        loader.WireBindings(root!);

        var second = new Board(showEditor: true, title: "Second board");
        loader.SetBindingContext(second);
        loader.WireBindings(root!);

        Assert.NotNull(TryFind(root!, "EditorPane"));
        Assert.True(TryFind(root!, "EditorPane")!.IsVisible);
        Assert.NotNull(TryFind(root!, "EditorBranch"));
        Assert.Equal("Second board", TextNamed(root!, "BoardName"));

        // A later notification from the new document must keep reaching the mounted surface.
        second.BoardTitle = "Renamed board";
        second.ShowEditor = false;
        second.NotifyAll();
        Assert.Equal("Renamed board", TextNamed(root!, "BoardName"));
        Assert.True(TryFind(root!, "EditorPane") is not { IsVisible: true });
    }

    private static string? TextNamed(Control root, string identity)
        => TryFind(root, identity) is TextBlock text ? text.Text : null;

    private static Control? TryFind(Control root, string identity)
        => new[] { root }.Concat(root.GetLogicalDescendants().OfType<Control>()).SingleOrDefault(control =>
            control.Name == identity || AutomationProperties.GetAutomationId(control) == identity);

    private sealed class Board : ICuiBindingContext, ICuiActionDispatcher, INotifyPropertyChanged
    {
        public bool ShowEditor;
        public string BoardTitle;
        public Board(bool showEditor, string title) { ShowEditor = showEditor; BoardTitle = title; }

        public event PropertyChangedEventHandler? PropertyChanged;
        public void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "ShowEditor" => ShowEditor,
                "BoardTitle" => BoardTitle,
                _ => null,
            };
            return value is not null;
        }

        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
