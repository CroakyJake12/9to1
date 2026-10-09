namespace Haven.Application;

/// <summary>Pure canonical format validation by the already configured catalogue.
/// It performs no IO, publishes no VM content and grants no resource access.</summary>
public interface ICanonicalMiniComputerCatalogIdentityFormat
{
    void ValidateOriginalIdentitySetupBytes(ReadOnlyMemory<byte> originalBytes);
}
