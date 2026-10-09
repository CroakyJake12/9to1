namespace HavenOS.Files;

/// <summary>Point-in-time validation; this does not claim handle-based confinement against a hostile
/// concurrent filesystem writer. The explicit Files binding must not traverse any redirecting ancestor.</summary>
internal static class FilesPhysicalDirectory
{
    public static bool IsDirectDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return false;
        try
        {
            for (string? path = Path.GetFullPath(directory); !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
