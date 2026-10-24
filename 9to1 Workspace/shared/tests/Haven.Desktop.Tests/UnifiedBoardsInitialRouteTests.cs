using Product = Haven.Desktop.App.OriginalUnifiedProduct;

namespace Haven.Desktop.Tests;

/// <summary>Actual shared process-entry boundary controls. These launch no native
/// app and attest no Home grants, installed dependency, migration or donor parity.
/// Native startup/session/close acceptance remains on the original owners.</summary>
public sealed class UnifiedBoardsInitialRouteTests
{
    [Theory]
    [InlineData((int)Product.Boards, "boards", "9to1://boards")]
    [InlineData((int)Product.Canvas, "canvas", "9to1://canvas")]
    public void Each_compiled_entry_selects_only_its_canonical_shared_root(int selected, string declaration, string route)
    {
        var entry = (Product)selected;
        Assert.Equal(declaration, App.ValidateOriginalInitialProductDeclaration([new("9to1.InitialApp", declaration)]));
        Assert.Equal(declaration, App.ResolveOriginalInitialProductDeclaration([], entry));
        Assert.Equal(declaration, App.ResolveOriginalInitialProductDeclaration([new("9to1.InitialApp", declaration)], entry));
        Assert.Equal(route, App.OriginalUnifiedProductRoute(entry));
    }

    [Theory]
    [InlineData("files")]
    [InlineData("sites")]
    [InlineData("dev")]
    [InlineData("write")]
    [InlineData("present")]
    [InlineData("assistants")]
    [InlineData("canvas")]
    [InlineData("boards")]
    public void No_executable_selector_preserves_the_original_compiled_declaration(string declaration) =>
        Assert.Equal(declaration, App.ResolveOriginalInitialProductDeclaration([new("9to1.InitialApp", declaration)], Product.None));

    [Fact]
    public void Missing_selector_and_declaration_preserve_original_Home_startup()
    {
        var entry = new App.OriginalUnifiedProductEntry();
        Assert.Equal(Product.None, entry.Product);
        Assert.Null(App.ResolveOriginalInitialProductDeclaration([new("unrelated", "boards")], entry.Product));
    }

    [Theory]
    [InlineData((int)Product.Boards, "canvas")]
    [InlineData((int)Product.Boards, "write")]
    [InlineData((int)Product.Canvas, "boards")]
    [InlineData((int)Product.Canvas, "files")]
    public void Conflicting_compiled_metadata_cannot_retarget_a_selected_entry(int selected, string other)
    {
        var product = (Product)selected;
        var entry = new App.OriginalUnifiedProductEntry();
        Assert.Throws<InvalidDataException>(() => entry.Capture(product, [new("9to1.InitialApp", other)], false));
        Assert.Equal(product, entry.Product);
        Assert.Throws<InvalidOperationException>(() => entry.Capture(product, [], false));
    }

    [Theory]
    [InlineData((int)Product.Boards, "boards")]
    [InlineData((int)Product.Canvas, "canvas")]
    public void Duplicate_metadata_is_refused_even_when_both_rows_match_the_entry(int selected, string value)
    {
        var product = (Product)selected;
        var entry = new App.OriginalUnifiedProductEntry();
        Assert.Throws<InvalidDataException>(() => entry.Capture(product,
            [new("9to1.InitialApp", value), new("9to1.InitialApp", value)], false));
        Assert.Equal(product, entry.Product);
    }

    [Theory]
    [InlineData((int)Product.Boards)]
    [InlineData((int)Product.Canvas)]
    public void Both_executables_share_one_immutable_process_selection(int selected)
    {
        var product = (Product)selected;
        var entry = new App.OriginalUnifiedProductEntry();
        entry.Capture(product, [], false);
        foreach (var next in new[] { Product.Canvas, Product.Boards })
            Assert.Throws<InvalidOperationException>(() => entry.Capture(next, [], false));
        Assert.Equal(product, entry.Product);
    }

    [Fact]
    public void Already_started_native_application_cannot_capture_an_entry()
    {
        var entry = new App.OriginalUnifiedProductEntry();
        Assert.Throws<InvalidOperationException>(() => entry.Capture(Product.Boards, [], true));
        Assert.Equal(Product.None, entry.Product);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99)]
    public void Only_the_closed_native_pair_can_select_or_resolve_a_route(int value)
    {
        var entry = new App.OriginalUnifiedProductEntry();
        Assert.Throws<ArgumentOutOfRangeException>(() => entry.Capture((Product)value, [], false));
        Assert.Throws<ArgumentOutOfRangeException>(() => App.OriginalUnifiedProductRoute((Product)value));
        Assert.Equal(Product.None, entry.Product);
    }

    [Theory]
    [InlineData("Board.9to1board")]
    [InlineData("saved board.9TO1BOARD")]
    public void Unimplemented_legacy_direct_open_is_explicitly_refused_before_original_bootstrap(string path)
    {
        var failure = Assert.Throws<NotSupportedException>(() => App.DemandSupportedOriginalBoardsArguments([path]));
        Assert.Contains(".9to1board", failure.Message);
    }

    [Fact]
    public void Original_Home_locator_arguments_are_left_to_the_original_parser()
    {
        var arguments = new[] { "--home-root-host", "original-pipe", "--home-machine-state", "/original/directory.9to1board" };
        App.DemandSupportedOriginalBoardsArguments(arguments);
        Assert.Equal(new[] { "--home-root-host", "original-pipe", "--home-machine-state", "/original/directory.9to1board" }, arguments);
        App.DemandSupportedOriginalBoardsArguments([]);
    }

    [Fact]
    public async Task Concurrent_executable_capture_keeps_exactly_one_winner_without_resetting_the_real_process()
    {
        var entry = new App.OriginalUnifiedProductEntry();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(Product Product, Exception? Failure)> Capture(Product selected)
        {
            await start.Task;
            return (selected, Record.Exception(() => entry.Capture(selected, [], false)));
        }
        var canvas = Capture(Product.Canvas); var boards = Capture(Product.Boards);
        start.SetResult();
        var results = await Task.WhenAll(canvas, boards);
        var winner = Assert.Single(results, result => result.Failure is null);
        Assert.IsType<InvalidOperationException>(Assert.Single(results, result => result.Failure is not null).Failure);
        Assert.Equal(winner.Product, entry.Product);
        Assert.Throws<InvalidOperationException>(() => entry.Capture(winner.Product, [], false));
    }
}
