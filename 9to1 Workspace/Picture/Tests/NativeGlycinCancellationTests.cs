using System.Buffers.Binary;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class NativeGlycinCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_submitted_native_frame_cancellation_or_close_is_terminal_and_source_reopens(bool close)
    {
        // A real, bounded BMP keeps the donor busy long enough to observe submitted native work.
        // No decoder hook, artificial sleep, sandbox bypass or substitute codec is involved.
        var encoded = Bmp(4096, 4096);
        using var session = new PictureGlycinDecoder().OpenFrames(encoded);
        Array.Clear(encoded);
        using var cancellation = new CancellationTokenSource();
        var decoding = Task.Run(() => session.NextFrame(cancellation.Token), TestContext.Current.CancellationToken);
        Assert.True(SpinWait.SpinUntil(() => session.IsNativeOperationActive || decoding.IsCompleted, TimeSpan.FromSeconds(10)));
        Assert.True(session.IsNativeOperationActive, "The fixture must observe an actual submitted native frame operation before cancellation.");
        var closing = close ? Task.Run(session.Dispose, TestContext.Current.CancellationToken) : Task.CompletedTask;
        if (!close) cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => decoding.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        if (close) Assert.Throws<ObjectDisposedException>(() => session.NextFrame(TestContext.Current.CancellationToken));
        else Assert.Throws<OperationCanceledException>(() => session.NextFrame(TestContext.Current.CancellationToken)); // Native GCancellable is terminal even with a fresh caller token.
        using var reopened = new PictureGlycinDecoder().OpenFrames(Bmp(2, 1), true, TestContext.Current.CancellationToken);
        var frame = reopened.NextFrame(TestContext.Current.CancellationToken);
        try { Assert.Equal(2u, frame.Width); Assert.Equal(1u, frame.Height); }
        finally { Array.Clear(frame.BgraPremultipliedPixels); if (frame.IccProfile is not null) Array.Clear(frame.IccProfile); }
    }

    private static byte[] Bmp(int width, int height)
    {
        var bytes = new byte[checked(54 + width * height * 4)];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 32);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34), bytes.Length - 54);
        return bytes;
    }
}
