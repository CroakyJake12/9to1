namespace CakeOS.Cui.Runtime;

/// <summary>
/// Narrow typed contract for an action failure that has conclusively acquired no
/// owner, session, view, subscription or other unresolved effect, so it must not
/// permanently poison ordinary host close. Implementing this interface is a
/// declaration only; the host that owns the action must independently verify the
/// attached proof before CuiSceneHost honours it. Arbitrary code that implements
/// the marker without the typed proof is never honoured.
/// </summary>
public interface ICuiSettledActionFailure
{
    /// <summary>True only when this instance carries verified proof of having
    /// acquired no effect. The implementation is responsible for the proof; the
    /// host's validator is responsible for trusting it.</summary>
    bool IsVerified { get; }
}
