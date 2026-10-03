using Haven.Application;

namespace HavenOS.Apps.Terminal;

/// <summary>Trusted session factory admission, rechecked for every owning input or process mutation.</summary>
public interface ITerminalSessionAdmission
{
    ValueTask RequireSessionAsync(TerminalSessionMetadata session, CancellationToken cancellationToken);
}
