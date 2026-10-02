using System.Security.Cryptography;
namespace HavenOS.Apps.Canvas;
/// <summary>Immutable detached donor bytes and exact materialization data; never authority.</summary>
public sealed class RnoteSplitCandidate
{
    private readonly byte[] _native;
    private readonly byte[] _receipt;
    internal RnoteSplitCandidate(byte[] native, byte[] receipt)
    {
        _native = native.ToArray(); _receipt = receipt.ToArray();
        NativeHash = Convert.ToHexString(SHA256.HashData(_native));
        ReceiptHash = Convert.ToHexString(SHA256.HashData(_receipt));
    }
    public string NativeHash { get; }
    public string ReceiptHash { get; }
    public byte[] CopyNative() => _native.ToArray();
    public byte[] CopyReceipt() => _receipt.ToArray();
}
