/*
 * FILE DOCUMENTATION
 * Where: HavenOS Apps/Dev/DeveloperAppDescriptor.cs, at the Dev app boundary.
 * What: Describes the independent Dev surface and the existing developer destinations it may hand off to.
 * Why: Gives shell integration a stable contract without registering shared routes or duplicating developer executors in this lane.
 */

namespace HavenOS.Apps.Dev;

/// <summary>
/// Stable metadata for the independent HavenOS Build (Dev) app surface. The public discovery name is
/// Build; the app id and route key remain "dev" so existing dev identity/links/aliases stay compatible.
/// </summary>
public static class DeveloperAppDescriptor
{
    public const string AppId = "dev";
    public const string DisplayName = "Build";
    public const string StudioModeId = "studio";
    public const string TerminalModeId = "terminal";

    /// <summary>
    /// Existing Haven developer modes that own editing, testing, repair, and command execution.
    /// Dev may navigate to these destinations but does not implement a second execution path.
    /// </summary>
    public static IReadOnlyList<string> ExistingExecutionHandoffs { get; } =
        [StudioModeId, TerminalModeId];
}
