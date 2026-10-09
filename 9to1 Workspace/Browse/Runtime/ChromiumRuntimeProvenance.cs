namespace HavenOS.Apps.Browse.Runtime;

/// <summary>Separates the runtime's reported stamp from independently pinned upstream artifact identity.</summary>
public static class ChromiumRuntimeProvenance
{
    // Observed in official snapshot REVISIONS and downloaded bytes in CI run 37926222011,
    // artifact 11613773362. This is not a local donor compilation or a production promotion.
    public const string SnapshotSourceCommit = "e23cdf4ab7b386d7e5fc58506b2d77f3f4a4817d";
    public const string LinuxSnapshotExecutableSha256 = "3a50438716d738e3a2ee6904cd5dfec1bef8f99750e413cf4dbcc6537c9b32dd";
    public const string LinuxSnapshotArchiveSha256 = "f98d163b68609466e8c89c1a70e80475fb84abaac073ec3c6e23f162b68b9b39";

    public static string VerifyRevision(string expectedSource, string verifiedExecutableSha256, string? reportedRevision, bool isLinux)
    {
        if (string.Equals(reportedRevision, expectedSource, StringComparison.OrdinalIgnoreCase))
            return "Runtime revision matches the approved source; executable SHA-256 verified separately.";

        // Chromium supports an all-zero LASTCHANGE.dummy stamp. Do not treat that stamp
        // as a matching Git commit, and never accept an arbitrary zero-stamped executable.
        if (isLinux && reportedRevision == new string('0', 40) && expectedSource == SnapshotSourceCommit &&
            string.Equals(verifiedExecutableSha256, LinuxSnapshotExecutableSha256, StringComparison.OrdinalIgnoreCase))
            return "Pinned official Linux snapshot bytes and REVISIONS provenance; runtime reports an all-zero stamp, not a source revision.";

        throw new InvalidDataException($"Running Chromium revision {reportedRevision ?? "<missing>"} does not match the approved donor and has no recognised pinned artifact provenance.");
    }
}
