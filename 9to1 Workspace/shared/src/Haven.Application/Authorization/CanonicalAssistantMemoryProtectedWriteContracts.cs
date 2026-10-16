namespace Haven.Application;

/// <summary>Only meaningful after the canonical producer verifies the exact issued intent.
/// The UI preview is in memory. Durable storage and Home audit must never serialize its
/// plaintext Candidate/OriginalRecord; the authenticated envelope is already prepared.</summary>
public interface ICanonicalAssistantMemoryProtectedWriteIntent
{
    string? EncryptedDurableReceipt { get; }
}
