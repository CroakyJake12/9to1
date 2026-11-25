using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>IX-BUILD-002: a stale completion must revalidate or refuse; it cannot overwrite newly typed text or mark the old preview Current.
/// Proves a completion applied against unchanged text succeeds, one whose target span changed is refused as stale, and a newer buffer version
/// marks the old proposal non-current.</summary>
public sealed class DeveloperCompletionCurrencyTests
{
    private static readonly Guid Doc = Guid.NewGuid();

    // Buffer: "var t = Task.Titl;" — the completion replaces the trailing "Titl" span with "Title".
    private const string Original = "var t = Task.Titl;";
    private static DeveloperCompletionProposal TitlToTitle(long version) => new(Doc, version, 15, 4, "Titl", "Title", "Title");

    [Fact]
    public void CompletionAgainstUnchangedTextApplies()
    {
        var proposal = TitlToTitle(1);
        var buffer = new DeveloperCompletionBuffer(Doc, 1, Original);
        Assert.Equal(DeveloperCompletionCurrency.Current, DeveloperCompletionCurrencyService.Evaluate(proposal, buffer));

        var result = DeveloperCompletionCurrencyService.Apply(proposal, buffer);
        Assert.True(result.Applied);
        Assert.Equal("var t = Task.Title;", result.Text);
    }

    [Fact]
    public void EditedIdentifierRefusesStaleWithoutOverwriting()
    {
        var proposal = TitlToTitle(1);
        // The user edited the identifier before the result arrived: span now reads "Titl2".
        var edited = new DeveloperCompletionBuffer(Doc, 1, "var t = Task.Titl2;");

        Assert.Equal(DeveloperCompletionCurrency.Stale, DeveloperCompletionCurrencyService.Evaluate(proposal, edited));
        var result = DeveloperCompletionCurrencyService.Apply(proposal, edited);
        Assert.False(result.Applied);
        Assert.Null(result.Text);
        // The newly typed text is preserved untouched.
        Assert.Contains("changed", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NewerBufferVersionMarksProposalNonCurrent()
    {
        var proposal = TitlToTitle(1);
        // Same span content but the buffer advanced to version 2.
        var newer = new DeveloperCompletionBuffer(Doc, 2, Original);
        Assert.Equal(DeveloperCompletionCurrency.Stale, DeveloperCompletionCurrencyService.Evaluate(proposal, newer));
        Assert.False(DeveloperCompletionCurrencyService.Apply(proposal, newer).Applied);
    }

    [Fact]
    public void DifferentDocumentIsStale()
    {
        var proposal = TitlToTitle(1);
        var other = new DeveloperCompletionBuffer(Guid.NewGuid(), 1, Original);
        Assert.Equal(DeveloperCompletionCurrency.Stale, DeveloperCompletionCurrencyService.Evaluate(proposal, other));
    }

    [Fact]
    public void SpanBeyondEndOfTextIsStale()
    {
        var proposal = TitlToTitle(1);
        // Text shrank so the recorded span no longer fits.
        var shorter = new DeveloperCompletionBuffer(Doc, 1, "var t = Task.");
        Assert.Equal(DeveloperCompletionCurrency.Stale, DeveloperCompletionCurrencyService.Evaluate(proposal, shorter));
    }

    [Fact]
    public void InvalidProposalAndBufferAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => new DeveloperCompletionProposal(Guid.Empty, 1, 0, 1, "a", "b", "c").Capture());
        Assert.Throws<InvalidDataException>(() => new DeveloperCompletionProposal(Doc, 0, 0, 1, "a", "b", "c").Capture());
        Assert.Throws<InvalidDataException>(() => new DeveloperCompletionBuffer(Guid.Empty, 1, "x").Capture());
    }
}
