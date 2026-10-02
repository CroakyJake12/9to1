using System.Text;
using NineToOne.Launcher;

namespace Haven.Android;

/// <summary>Bounded import bytes on the privately displayed original Home session. Never commits a layout.</summary>
public static class AndroidLauncherLayoutDocumentReader
{
    public static async Task<LauncherLayout> ReadAsync(HomeLauncherSession session, LauncherSessionSnapshot original,
        Stream input, Func<bool> originalSelectionCurrent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(originalSelectionCurrent);
        async Task RequireOriginal()
        {
            ct.ThrowIfCancellationRequested();
            if (!originalSelectionCurrent() || !await session.IsCurrentAsync(original, ct) || !originalSelectionCurrent())
                throw new UnauthorizedAccessException("The original launcher backup selection changed. Select it again.");
        }
        await RequireOriginal();
        using var bytes = new MemoryStream(); var chunk = new byte[8192];
        while (true)
        {
            await RequireOriginal();
            var count = await input.ReadAsync(chunk.AsMemory(), ct);
            await RequireOriginal();
            if (count == 0) break;
            if (bytes.Length + count > LauncherLayoutExchange.MaximumBytes)
                throw new InvalidDataException("Select a launcher backup of at most 4 MiB.");
            bytes.Write(chunk, 0, count);
        }
        await RequireOriginal();
        return LauncherLayoutExchange.Import(new UTF8Encoding(false, true).GetString(bytes.ToArray()), original.Layout.AuthorityId);
    }
}
