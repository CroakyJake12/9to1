using Haven.Application;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Tests;

public sealed class MailDraftRecoveryPolicyTests
{
    [Fact]
    public void SelectLatestDraftForAccount_IgnoresServiceOrderAndOtherAccounts()
    {
        var accountId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var otherAccountId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var older = Draft(accountId, "30000000-0000-0000-0000-000000000003", "older", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newerOtherAccount = Draft(otherAccountId, "40000000-0000-0000-0000-000000000004", "other account", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var newest = Draft(accountId, "50000000-0000-0000-0000-000000000005", "newest", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        var selected = MailPageViewModel.SelectLatestDraftForAccount(
            [older, newerOtherAccount, newest], accountId);

        Assert.Same(newest, selected);
    }

    [Fact]
    public void SelectLatestDraftForAccount_BreaksTimestampTiesByLocalId()
    {
        var accountId = Guid.NewGuid();
        var updatedAt = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var higherId = Draft(accountId, "ffffffff-ffff-ffff-ffff-ffffffffffff", "higher", updatedAt);
        var lowerId = Draft(accountId, "00000000-0000-0000-0000-000000000001", "lower", updatedAt);

        var selected = MailPageViewModel.SelectLatestDraftForAccount([higherId, lowerId], accountId);

        Assert.Same(lowerId, selected);
    }

    private static MailDraft Draft(Guid accountId, string localId, string subject, DateTimeOffset updatedAt) => new(
        accountId, null, MailResponseKind.New, null, null, [], [], [], subject, string.Empty, false, [],
        LocalId: Guid.Parse(localId), UpdatedAt: updatedAt);
}
