using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Merge stage M3 feasibility evidence (owner directive 2026-10-14: CUI is authoritative, HUI
/// merges into it, spec takes precedence).
///
/// The largest migration target is the Chat transcript, which today renders through HUI
/// DynamicUI. These pin that CUI's declarative <c>Repeat</c> can carry that surface, so M3 is a
/// consolidation rather than a rebuild.
///
/// The requirements are the ones D04 and the identity invariants actually impose: canonical
/// message identity must survive refresh and reordering, a removed event must not disturb its
/// siblings, and duplicate canonical identities must be refused rather than silently rendered.
/// </summary>
public sealed class CuiTranscriptRepeatCapabilityTests
{
    private const string Transcript = """
        <Cui>
          <Page>
            <Repeat Source="{Binding Messages}" As="msg" Key="{Binding msg.Id}">
              <Text id="Row" Text="{Binding msg.Text}" />
            </Repeat>
          </Page>
        </Cui>
        """;

    [Fact]
    public async Task Reordering_the_transcript_preserves_each_message_control_by_canonical_id()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel();
            var work = Event("m2", "Work");
            var answer = Event("m3", "Answer");
            var request = Event("m1", "Request");
            model.Set("Messages", new[] { work, answer, request });

            using var loader = new CuiControlLoader();
            loader.SetBindingContext(model);
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(Transcript);
                Assert.True(diagnostics.Count == 0, string.Join(" | ", diagnostics.Select(d => $"{d.Code}:{d.Message}")));
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);

                var before = root.GetVisualDescendants().OfType<TextBlock>().ToArray();
                Assert.Equal(3, before.Length);
                Assert.Equal("Work", before[0].Text);

                // Chronology is corrected (D04): the same records, re-ranked by created order.
                model.Set("Messages", new[] { request, work, answer });
                await loader.WhenActionsIdleAsync();

                var after = root.GetVisualDescendants().OfType<TextBlock>().ToArray();
                Assert.Equal(3, after.Length);

                // Same canonical id -> the SAME control instance, not a rebuilt one. Rebuilding
                // would discard transient state such as caret, focus and scroll for that row.
                Assert.Same(before[0], after[1]);   // m2 "Work"
                Assert.Same(before[1], after[2]);   // m3 "Answer"
                Assert.Same(before[2], after[0]);   // m1 "Request"
                Assert.Equal("Request", after[0].Text);
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public async Task Removing_one_message_leaves_sibling_identity_intact()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel();
            var first = Event("m1", "Request");
            var work = Event("m2", "Work");
            var result = Event("m3", "Result");
            model.Set("Messages", new[] { first, work, result });

            using var loader = new CuiControlLoader();
            loader.SetBindingContext(model);
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(Transcript);
                Assert.Empty(diagnostics);
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);
                var before = root.GetVisualDescendants().OfType<TextBlock>().ToArray();
                Assert.Equal(3, before.Length);

                model.Set("Messages", new[] { first, result });
                await loader.WhenActionsIdleAsync();

                var after = root.GetVisualDescendants().OfType<TextBlock>().ToArray();
                Assert.Equal(2, after.Length);
                Assert.Same(before[0], after[0]);
                Assert.Same(before[2], after[1]);
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public async Task Duplicate_canonical_message_ids_are_refused_rather_than_silently_rendered()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel();
            // The same canonical identity twice would make "which row am I editing?" ambiguous.
            model.Set("Messages", new[] { Event("m1", "First"), Event("m1", "Duplicate") });

            using var loader = new CuiControlLoader();
            loader.SetBindingContext(model);
            try
            {
                var exception = Assert.ThrowsAny<Exception>(() =>
                {
                    var (tree, _) = loader.LoadMarkup(Transcript);
                    var root = Assert.IsAssignableFrom<Control>(tree);
                    loader.WireBindings(root);
                });
                // CUIR052 is raised for a duplicated Repeat key; the wrapper may add context,
                // so match the diagnostic code or its wording rather than an exact string.
                var message = exception.ToString();
                Assert.True(
                    message.Contains("CUIR052") || message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase),
                    $"Duplicate canonical ids were not refused with a duplicate-key diagnostic: {message}");
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }

    private static Dictionary<string, object?> Event(string id, string text) =>
        new() { ["Id"] = id, ["Text"] = text };
}