namespace Haven.Application;

/// <summary>Durable owning store UUID. NewlyCreated is host lifecycle evidence, never an importable request field.</summary>
public sealed record ResourceStoreIdentity(int SchemaVersion, Guid StoreId, DateTimeOffset CreatedAtUtc, bool NewlyCreated);
public interface IResourceStoreIdentitySource
{
    ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken cancellationToken);
}
