using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperCuiTextChildEditingTests
{
    private const string Prefix = "<Cui>\r\n<!-- keep <Text id='fake' /> -->\r\n<Page id='page'><Container id='box' Type='Vertical Stack' margin='9'>\r\n  ";
    private const string First = "<Text id='first' text='A &amp; B' accessible-name='literal /> &gt;' />";
    private const string Between = "\r\n  <!-- this comment stays in its source slot -->\r\n\t";
    private const string Second = "<Text id=\"second\" text=\"C &quot; D\" />";
    private const string Suffix = "\r\n</Container></Page><!-- keep suffix -->\r\n</Cui>";

    [Fact]
    public void Move_swaps_only_exact_original_element_lexemes_preserving_comments_entities_quotes_and_CRLF()
    {
        var source = Prefix + First + Between + Second + Suffix;
        var inspection = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "first");
        Assert.Null(inspection.UnavailableReason); Assert.True(inspection.CanRemoveText); Assert.False(inspection.CanMoveTextUp); Assert.True(inspection.CanMoveTextDown);
        var moved = DeveloperCuiPropertyEditor.EditTextChild(inspection, source, DeveloperCuiTextChildEdit.MoveDown);
        Assert.Null(moved.Error); Assert.Equal(Prefix + Second + Between + First + Suffix, moved.Text);
        var current = DeveloperCuiPropertyEditor.Inspect("Main.cui", moved.Text, "first"); Assert.True(current.CanMoveTextUp); Assert.False(current.CanMoveTextDown);
        var returned = DeveloperCuiPropertyEditor.EditTextChild(current, moved.Text, DeveloperCuiTextChildEdit.MoveUp);
        Assert.Null(returned.Error); Assert.Equal(source, returned.Text);
    }

    [Fact]
    public void Remove_changes_only_selected_element_and_keeps_other_text_and_all_surrounding_source()
    {
        var source = Prefix + First + Between + Second + Suffix;
        var removed = DeveloperCuiPropertyEditor.EditTextChild(DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "first"), source, DeveloperCuiTextChildEdit.Remove);
        Assert.Null(removed.Error); Assert.Equal(Prefix + Between + Second + Suffix, removed.Text);
        var retained = DeveloperCuiPropertyEditor.Inspect("Main.cui", removed.Text, "second"); Assert.Null(retained.UnavailableReason); Assert.True(retained.CanRemoveText);
    }

    [Fact]
    public void Stale_inspection_and_duplicate_target_or_parent_ID_refuse_every_operation_without_source_loss()
    {
        var source = Prefix + First + Between + Second + Suffix;
        var inspection = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "first");
        foreach (var edit in Enum.GetValues<DeveloperCuiTextChildEdit>())
        {
            var stale = DeveloperCuiPropertyEditor.EditTextChild(inspection, source + " ", edit);
            Assert.NotNull(stale.Error); Assert.Equal(source + " ", stale.Text);
        }
        foreach (var ambiguous in new[] { source.Replace(Second, "<Text id='first' text='duplicate' />", StringComparison.Ordinal),
            source.Replace("</Page>", "<Container id='box' Type='Vertical Stack' /></Page>", StringComparison.Ordinal) })
        {
            var refused = DeveloperCuiPropertyEditor.EditTextChild(DeveloperCuiPropertyEditor.Inspect("Main.cui", ambiguous, "first"), ambiguous, DeveloperCuiTextChildEdit.Remove);
            Assert.NotNull(refused.Error); Assert.Equal(ambiguous, refused.Text);
        }
    }

    [Theory]
    [InlineData("<Cui><Page id='page'><Text id='first' text='{Binding Value}' /></Page></Cui>")]
    [InlineData("<Cui><Page id='page'><Text id='first' text='Text'></Text></Page></Cui>")]
    [InlineData("<Cui><Page><Text id='first' text='Text' /></Page></Cui>")]
    [InlineData("<Cui><Page id='page'><Text #first text='Text' /></Page></Cui>")]
    [InlineData("<Cui><Page id='page'><Repeat id='repeat' Source='{Binding Items}' As='item' Key='{Binding item.Id}'><Text id='first' text='Text' /></Repeat></Page></Cui>")]
    [InlineData("<Cui><Page id='page'><Text id='first' text='Text' action='command' /></Page></Cui>")]
    [InlineData("<Cui><Page id='page'><Text id='first' name='other' text='Text' /></Page></Cui>")]
    public void Code_defined_or_unanchored_Text_forms_stay_unchanged(string source)
    {
        var inspection = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "first"); Assert.False(inspection.CanRemoveText);
        var result = DeveloperCuiPropertyEditor.EditTextChild(inspection, source, DeveloperCuiTextChildEdit.Remove);
        Assert.NotNull(result.Error); Assert.Equal(source, result.Text);
    }

    [Fact]
    public void Adjacent_non_Text_control_and_end_of_list_are_not_silently_crossed()
    {
        const string source = "<Cui><Page id='page'><Text id='first' text='Text' /><Container id='other' Type='Vertical Stack' /></Page></Cui>";
        var inspection = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "first");
        Assert.True(inspection.CanRemoveText); Assert.False(inspection.CanMoveTextUp); Assert.False(inspection.CanMoveTextDown);
        foreach (var edit in new[] { DeveloperCuiTextChildEdit.MoveUp, DeveloperCuiTextChildEdit.MoveDown })
        { var result = DeveloperCuiPropertyEditor.EditTextChild(inspection, source, edit); Assert.NotNull(result.Error); Assert.Equal(source, result.Text); }
    }

    [Fact]
    public void Forged_capabilities_cannot_bypass_fresh_hierarchy_and_bounded_original_XML_checks()
    {
        const string source = "<Cui><Page id='page'><Text id='first' text='Text' /></Page></Cui>";
        var forged = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "first") with { CanMoveTextUp = true, CanMoveTextDown = true };
        var refused = DeveloperCuiPropertyEditor.EditTextChild(forged, source, DeveloperCuiTextChildEdit.MoveDown);
        Assert.NotNull(refused.Error); Assert.Equal(source, refused.Text);
        foreach (var bounded in new[] {
            "<Cui><Page id='page'>" + string.Concat(Enumerable.Repeat("<Container Type='Vertical Stack'>", 34)) + "<Text id='first' text='Text' />" + string.Concat(Enumerable.Repeat("</Container>", 34)) + "</Page></Cui>",
            "<Cui><Page id='page'><Text id='first' text='Text' />" + string.Concat(Enumerable.Repeat("<Text text='Other' />", 500)) + "</Page></Cui>" })
        {
            var captured = forged with { ContentSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bounded))).ToLowerInvariant() };
            var result = DeveloperCuiPropertyEditor.EditTextChild(captured, bounded, DeveloperCuiTextChildEdit.Remove);
            Assert.NotNull(result.Error); Assert.Equal(bounded, result.Text);
        }
    }

    [Fact]
    public void Whole_proposed_source_must_compile_and_original_cancellation_is_honoured()
    {
        const string source = "<Cui><Page id='page'><Text id='first' text='First' /><Text id='second' text='Second' /><Text text='Invalid sibling' BogusDesignerProperty='x' /></Page></Cui>";
        var inspection = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "first"); Assert.True(inspection.CanMoveTextDown);
        var result = DeveloperCuiPropertyEditor.EditTextChild(inspection, source, DeveloperCuiTextChildEdit.MoveDown);
        Assert.Contains("did not compile", result.Error!); Assert.Equal(source, result.Text);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() => { _ = DeveloperCuiPropertyEditor.EditTextChild(inspection, source, DeveloperCuiTextChildEdit.Remove, cancelled.Token); }));
    }
}
