namespace NineToOne.Accounts;

public sealed class SubscriptionQuotes(string statePath,SubscriptionBuilder builder,Guid authenticatedAccountID,Guid? organisationID=null)
{
    private sealed record OwnedQuote(Guid AccountID,Guid? OrgID,SubscriptionQuote Quote);
    private sealed record QuoteState(IReadOnlyList<OwnedQuote> Quotes);
    public PricingResult Preview(BuilderSelection selection)
    {
        var result=builder.Quote(selection);
        if(result.Quote is not {} quote)return result;
        using var lease=DurableState.Acquire(statePath);
        var state=Read();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statePath))!);
        DurableState.Write(statePath,new QuoteState(state.Quotes.Where(q=>q.Quote.ExpiresAt>DateTimeOffset.UtcNow).Append(new OwnedQuote(authenticatedAccountID,organisationID,quote)).ToArray()));
        return result;
    }
    public PricingResult Checkout(Guid quoteID)
    {
        using var lease=DurableState.Acquire(statePath);
        var owned=Read().Quotes.SingleOrDefault(q=>q.Quote.QuoteID==quoteID && q.AccountID==authenticatedAccountID && q.OrgID==organisationID);
        var quote=owned?.Quote;
        return quote is null?new(null,[new("Quote","Authoritative quote not found.")]):builder.RevalidateForCheckout(quote);
    }
    private QuoteState Read()=>File.Exists(statePath)?DurableState.Read<QuoteState>(statePath):new([]);
}
