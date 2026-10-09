using System.Linq;
namespace HavenOS.Home.Core;
public sealed partial class FileHomeCoreStateStore
{
    public static bool IsOriginalProtectedStateValid(HomeCoreStoredState observed) =>
        observed.SchemaVersion == CurrentSchemaVersion && observed.Revision >= 0 && observed.Records is not null &&
        observed.Records.All(record => record is not null && ValidateRecord(record) is null) &&
        observed.Records.Select(record => record.RecordId).Distinct(System.StringComparer.Ordinal).Count() == observed.Records.Count;
    // Pure configured identity only; kernel path/ACL/file custody remains required.
    public bool IsOriginalConfiguredFile(string actualPath) =>
        System.String.Equals(_path, System.IO.Path.GetFullPath(actualPath), System.OperatingSystem.IsWindows()
            ? System.StringComparison.OrdinalIgnoreCase : System.StringComparison.Ordinal);
}
