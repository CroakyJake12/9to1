using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Merge stage M3 capability evidence: per-item template selection in <c>Repeat</c>.
///
/// HUI's DynamicUI chose a template per item - <c>CreateItem(template, location, id, values)</c>
/// - and the Chat transcript depends on it, because the Messages container interleaves message
/// rows with tool-activity cards that are structurally different. CUI previously instantiated
/// every child of a <c>Repeat</c> for every item, so a mixed list could not be expressed.
///
/// These pin the semantics that make the migration possible, and the two failure modes that
/// would otherwise be silent.
/// </summary>
public sealed class CuiPerItemTemplateTests
{
    /// <summary>
    /// The Chat shape: one list holding both message roles and an interleaved tool-activity card.
    /// </summary>
    private const string MixedMessages = """
        <Cui>
          <Page>
            <Repeat Source="{Binding Messages}" As="msg" Key="{Binding msg.Id}" Template="{Binding msg.Template}">
              <Container Definition="true" Name="UserMessage" Type="Vertical Stack">
                <Text Text="{Binding msg.Body}" />
              </Container>
              <Container Definition="true" Name="AssistantMessage" Type="Vertical Stack">
                <Text Text="{Binding msg.Body}" />
              </Container>
              <Container Definition="true" Name="ToolActivity" Type="Vertical Stack">
                <Text Text="{Binding msg.Body}" />
              </Container>
            </Repeat>
          </Page>
        </Cui>
        """;

    private static Dictionary<string, object?> Message(string id, string template, string body) =>
        new() { ["Id"] = id, ["Template"] = template, ["Body"] = body };

    private static (Avalonia.Controls.Control Root, CuiControlLoader Loader) Run(
        CuiViewModel model, string markup = MixedMessages)
    {
        var loader = new CuiControlLoader();
        loader.SetBindingContext(model);
        var (tree, diagnostics) = loader.LoadMarkup(markup);
        Assert.True(diagnostics.Count == 0,
            string.Join(" | ", diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var root = Assert.IsAssignableFrom<Control>(tree);
        loader.WireBindings(root);
        return (root, loader);
    }

    [Fact]
    public async Task One_list_renders_a_different_template_per_item()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel();
            model.Set("Messages", new[]
            {
                Message("m1", "UserMessage", "Hello"),
                Message("m2", "ToolActivity", "Reading file"),
                Message("m3", "AssistantMessage", "Hi there")
            });

            var (root, loader) = Run(model);
            try
            {
                var texts = root.GetVisualDescendants().OfType<TextBlock>().ToArray();
                Assert.Equal(3, texts.Length);

                // Exactly one TextBlock per row - the selected template and no sibling template.
                Assert.Equal("Hello", texts[0].Text);
                Assert.Equal("Reading file", texts[1].Text);
                Assert.Equal("Hi there", texts[2].Text);
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public async Task Changing_an_items_template_rebuilds_only_that_row()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel();
            model.Set("Messages", new[]
            {
                Message("m1", "UserMessage", "Hello"),
                Message("m2", "AssistantMessage", "Hi there")
            });

            var (root, loader) = Run(model);
            try
            {
                var before = root.GetVisualDescendants().OfType<TextBlock>().ToArray();
                Assert.Equal(2, before.Length);

                // A stable key is not sufficient identity once the shape can change. m2 becomes
                // a tool card; reusing the old control would leave the assistant subtree visible.
                model.Set("Messages", new[]
                {
                    Message("m1", "UserMessage", "Hello"),
                    Message("m2", "ToolActivity", "Reading file")
                });
                await loader.WhenActionsIdleAsync();

                var after = root.GetVisualDescendants().OfType<TextBlock>().ToArray();
                Assert.Equal(2, after.Length);
                Assert.Equal("Reading file", after[1].Text);

                // The untouched row keeps its control instance.
                Assert.Same(before[0], after[0]);
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public async Task An_unknown_template_name_is_refused_rather_than_rendering_an_empty_row()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel();
            model.Set("Messages", new[] { Message("m1", "NoSuchTemplate", "Hello") });

            var loader = new CuiControlLoader();
            loader.SetBindingContext(model);
            try
            {
                // A failing load reports through diagnostics and yields no tree, so the contract
                // under test is the diagnostic, not an exception type.
                var (_, diagnostics) = loader.LoadMarkup(MixedMessages);
                Assert.Contains(diagnostics, d => d.Code == "CUIR054");
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public async Task A_repeat_without_a_template_still_instantiates_every_child()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            const string legacy = """
                <Cui>
                  <Page>
                    <Repeat Source="{Binding Messages}" As="msg" Key="{Binding msg.Id}">
                      <Text Text="{Binding msg.Body}" />
                    </Repeat>
                  </Page>
                </Cui>
                """;

            var model = new CuiViewModel();
            model.Set("Messages", new[]
            {
                Message("m1", "UserMessage", "Hello"),
                Message("m2", "AssistantMessage", "Hi there")
            });

            var (root, loader) = Run(model, legacy);
            try
            {
                // Backward compatible: no Template attribute means today's behaviour, and the
                // Template value on each item is simply ignored.
                var texts = root.GetVisualDescendants().OfType<TextBlock>().ToArray();
                Assert.Equal(2, texts.Length);
                Assert.Equal("Hello", texts[0].Text);
                Assert.Equal("Hi there", texts[1].Text);
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }
}