using Product = Haven.Desktop.App.OriginalUnifiedProduct;

namespace Haven.Desktop.Tests;

/// <summary>Focused controls over the actual product selector and host-choice
/// functions. They construct no native app/Home and confer no startup grant.</summary>
public sealed class UnifiedPresentInitialRouteTests
{
    [Theory]
    [InlineData("present", true, true)]
    [InlineData("present", false, false)]
    [InlineData(null, true, true)]
    [InlineData(null, false, true)]
    [InlineData("canvas", true, true)]
    [InlineData("canvas", false, true)]
    [InlineData("boards", true, true)]
    [InlineData("boards", false, true)]
    [InlineData("write", true, false)]
    [InlineData("write", false, false)]
    [InlineData("files", true, false)]
    [InlineData("files", false, false)]
    [InlineData("sites", true, false)]
    [InlineData("dev", true, false)]
    [InlineData("assistants", true, false)]
    public void Host_choice_adds_Linux_Present_without_retargeting_existing_Windows_or_other_products(
        string? product, bool linux, bool unified) =>
        Assert.Equal(unified, App.UsesOriginalUnifiedWorkspace(product, linux));

    [Fact]
    public void Explicit_Present_entry_keeps_one_immutable_compiled_identity_and_canonical_route()
    {
        var entry = new App.OriginalUnifiedProductEntry();
        entry.Capture(Product.Present, [new("9to1.InitialApp", "present")], false);
        Assert.Equal(Product.Present, entry.Product);
        Assert.Equal("present", App.ResolveOriginalInitialProductDeclaration([new("9to1.InitialApp", "present")], entry.Product));
        Assert.Equal("9to1://present", App.OriginalUnifiedProductRoute(entry.Product));
        foreach (var other in new[] { Product.Canvas, Product.Boards, Product.Present })
            Assert.Throws<InvalidOperationException>(() => entry.Capture(other, [], false));
        Assert.Equal(Product.Present, entry.Product);
    }

    [Fact]
    public void Generic_compiled_Present_identity_still_resolves_before_the_same_host_choice()
    {
        var product = App.ResolveOriginalInitialProductDeclaration([new("9to1.InitialApp", "present")], Product.None);
        Assert.Equal("present", product);
        Assert.True(App.UsesOriginalUnifiedWorkspace(product, true));
        Assert.False(App.UsesOriginalUnifiedWorkspace(product, false));
    }

    [Theory]
    [InlineData("canvas")]
    [InlineData("boards")]
    [InlineData("write")]
    [InlineData("Present")]
    [InlineData("unknown")]
    public void Refused_Present_declaration_cannot_reuse_a_partially_selected_process(string other)
    {
        var entry = new App.OriginalUnifiedProductEntry();
        Assert.Throws<InvalidDataException>(() => entry.Capture(Product.Present, [new("9to1.InitialApp", other)], false));
        Assert.Equal(Product.Present, entry.Product);
        Assert.Throws<InvalidOperationException>(() => entry.Capture(Product.Present, [new("9to1.InitialApp", "present")], false));
    }

    [Fact]
    public void Already_started_application_cannot_capture_Present_or_consume_the_unstarted_selector()
    {
        var entry = new App.OriginalUnifiedProductEntry();
        Assert.Throws<InvalidOperationException>(() => entry.Capture(Product.Present, [new("9to1.InitialApp", "present")], true));
        Assert.Equal(Product.None, entry.Product);
    }

    [Fact]
    public async Task Concurrent_native_entries_keep_exactly_one_winner_across_all_three_products()
    {
        var entry = new App.OriginalUnifiedProductEntry();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(Product Product, Exception? Failure)> Capture(Product product)
        {
            await start.Task;
            return (product, Record.Exception(() => entry.Capture(product, [], false)));
        }
        var tasks = new[] { Capture(Product.Canvas), Capture(Product.Boards), Capture(Product.Present) };
        start.SetResult(); var results = await Task.WhenAll(tasks);
        var winner = Assert.Single(results.Where(result => result.Failure is null));
        var refused = results.Where(result => result.Failure is not null).ToArray();
        Assert.Equal(2, refused.Length);
        Assert.All(refused, result => Assert.IsType<InvalidOperationException>(result.Failure));
        Assert.Equal(winner.Product, entry.Product);
        Assert.Throws<InvalidOperationException>(() => entry.Capture(winner.Product, [], false));
    }
}
