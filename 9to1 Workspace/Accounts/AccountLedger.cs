using System.Text.Json;

namespace NineToOne.Accounts;

public enum ReservationState { Reserved, Settled, Released }
public sealed record UsageReservation(Guid ReservationID, Guid AccountID, string RequestID, long ReservedDust,
    long ChargedDust, ReservationState State, DateTimeOffset CreatedAt, string RequiredBand, string BandVersion, bool PersonalAPI);
public sealed record AccountUsage(long DustSpent, long DustReserved, long StorageUsedBytes, int HostedSites, int SinglePageSites);
public sealed record AccountRecord(SubscriptionState Subscription, AccountUsage Usage,
    IReadOnlyList<UsageReservation> Reservations, IReadOnlyList<SubscriptionPurchase>? Purchases = null);

/// <summary>Single-host, cross-process durable trusted-service ledger. Never shipped as client quota authority.</summary>
public sealed partial class AccountLedger
{
    private readonly string directory;
    private readonly object gate = new();
    private readonly TimeProvider clock;
    public AccountLedger(string directory,TimeProvider? clock=null) { this.directory = Path.GetFullPath(directory); this.clock=clock??TimeProvider.System; Directory.CreateDirectory(this.directory); }
    public AccountRecord Get(Guid accountID) { lock (gate) { using var lease = DurableState.Acquire(directory); return Read(accountID); } }
    public void Provision(SubscriptionState subscription)
    {
        SubscriptionPolicy.Evaluate(subscription);
        lock (gate)
        {
            using var lease = DurableState.Acquire(directory);
            if (File.Exists(PathFor(subscription.AccountID))) throw new InvalidOperationException("account_exists");
            Write(new(subscription, new(0, 0, 0, 0, 0), []));
        }
    }
    public UsageReservation Reserve(Guid accountID, string requestID, long dust, string requiredBand, bool personalAPI = false)
    {
        if (dust <= 0 || string.IsNullOrWhiteSpace(requestID)) throw new ArgumentException("invalid_reservation");
        lock (gate)
        {
            using var lease = DurableState.Acquire(directory);
            var account = Read(accountID);
            var previous = account.Reservations.SingleOrDefault(r => r.RequestID == requestID);
            if (previous is not null)
            {
                if (previous.ReservedDust != dust || previous.RequiredBand != requiredBand || previous.PersonalAPI != personalAPI || previous.BandVersion != SubscriptionPolicy.ModelBandVersion) throw new InvalidOperationException("idempotency_conflict");
                throw new InvalidOperationException("request_already_reserved_or_completed");
            }
            var entitlements = SubscriptionPolicy.Evaluate(account.Subscription);
            string[] bands = ["free", "boost", "pro", "ultra"];
            int required = Array.IndexOf(bands, requiredBand), available = Array.IndexOf(bands, entitlements.ModelBand);
            if (required < 0 || required > available || personalAPI && !entitlements.PersonalAPI)
                throw new UnauthorizedAccessException("model_or_api_not_entitled");
            if (dust > account.Subscription.Resources.AIDustAllocated - account.Usage.DustSpent - account.Usage.DustReserved)
                throw new InvalidOperationException("dust_exhausted");
            var reservation = new UsageReservation(Guid.NewGuid(), accountID, requestID, dust, 0,
                ReservationState.Reserved, DateTimeOffset.UtcNow, requiredBand, SubscriptionPolicy.ModelBandVersion, personalAPI);
            Write(account with { Usage = account.Usage with { DustReserved = checked(account.Usage.DustReserved + dust) },
                Reservations = account.Reservations.Append(reservation).ToArray() });
            return reservation;
        }
    }
    public UsageReservation Settle(Guid accountID, Guid reservationID, long actualDust)
    {
        lock (gate)
        {
            using var lease = DurableState.Acquire(directory);
            var account = Read(accountID);
            var reservation = account.Reservations.Single(r => r.ReservationID == reservationID);
            if (actualDust < 0 || actualDust > reservation.ReservedDust) throw new ArgumentOutOfRangeException(nameof(actualDust));
            if (reservation.State != ReservationState.Reserved)
            {
                if (reservation.ChargedDust != actualDust) throw new InvalidOperationException("settlement_conflict");
                return reservation;
            }
            var settled = reservation with { ChargedDust = actualDust,
                State = actualDust == 0 ? ReservationState.Released : ReservationState.Settled };
            Write(account with { Usage = account.Usage with {
                DustReserved = account.Usage.DustReserved - reservation.ReservedDust,
                DustSpent = checked(account.Usage.DustSpent + actualDust) },
                Reservations = account.Reservations.Select(r => r.ReservationID == reservationID ? settled : r).ToArray() });
            return settled;
        }
    }
    public AccountUsage UpdateHostedUsage(Guid accountID, long storageDelta, int fullSiteDelta, int singlePageDelta)
    {
        lock (gate)
        {
            using var lease = DurableState.Acquire(directory);
            var account = Read(accountID);
            var next = account.Usage with { StorageUsedBytes = checked(account.Usage.StorageUsedBytes + storageDelta),
                HostedSites = checked(account.Usage.HostedSites + fullSiteDelta),
                SinglePageSites = checked(account.Usage.SinglePageSites + singlePageDelta) };
            var limits = account.Subscription.Resources;
            if (next.StorageUsedBytes < 0 || next.StorageUsedBytes > limits.StorageAllocatedBytes ||
                next.HostedSites < 0 || next.HostedSites > limits.HostedSiteLimit ||
                next.SinglePageSites < 0 || next.SinglePageSites > limits.SinglePageSiteLimit)
                throw new InvalidOperationException("hosted_quota_exceeded");
            Write(account with { Usage = next });
            return next;
        }
    }
    private string PathFor(Guid id) => Path.Combine(directory, id.ToString("N") + ".json");
    private AccountRecord Read(Guid id)
    {
        var account = DurableState.Read<AccountRecord>(PathFor(id));
        if (account.Subscription is null || account.Subscription.AccountID != id || account.Usage is null || account.Reservations is null)
            throw new InvalidDataException("invalid_account_identity");
        var usage=account.Usage;var subscription=account.Subscription;
        if(id==Guid.Empty||subscription.Revision<1||subscription.Resources is null||subscription.Resources.AdditionalResources is null||subscription.EntitlementPriceMonthly<0||
            subscription.Resources.AIDustAllocated<0||subscription.Resources.StorageAllocatedBytes<0||subscription.Resources.HostedSiteLimit<0||subscription.Resources.SinglePageSiteLimit<0||
            usage.DustSpent<0||usage.DustReserved<0||usage.StorageUsedBytes<0||usage.HostedSites<0||usage.SinglePageSites<0||
            account.Reservations.Any(r=>r is null||r.ReservationID==Guid.Empty||r.AccountID!=id||string.IsNullOrWhiteSpace(r.RequestID)||r.ReservedDust<=0||r.ChargedDust<0||r.ChargedDust>r.ReservedDust||
                !Enum.IsDefined(r.State)||r.CreatedAt==default||r.RequiredBand is not ("free" or "boost" or "pro" or "ultra")||string.IsNullOrWhiteSpace(r.BandVersion)||
                r.State==ReservationState.Reserved&&r.ChargedDust!=0||r.State==ReservationState.Released&&r.ChargedDust!=0||r.State==ReservationState.Settled&&r.ChargedDust==0)||
            account.Reservations.Select(r=>r.ReservationID).Distinct().Count()!=account.Reservations.Count||account.Reservations.Select(r=>r.RequestID).Distinct(StringComparer.Ordinal).Count()!=account.Reservations.Count)
            throw new InvalidDataException("invalid_account_state");
        try
        {
            var reserved=account.Reservations.Where(r=>r.State==ReservationState.Reserved).Sum(r=>r.ReservedDust);
            var spent=account.Reservations.Where(r=>r.State==ReservationState.Settled).Sum(r=>r.ChargedDust);
            _=checked(reserved+spent);
            if(reserved!=usage.DustReserved||spent!=usage.DustSpent)throw new InvalidDataException("invalid_account_counters");
        }
        catch(OverflowException e){throw new InvalidDataException("invalid_account_counter_range",e);}
        ValidatePurchases(account);
        return account;
    }
    private void Write(AccountRecord account) => DurableState.Write(PathFor(account.Subscription.AccountID), account);
}
