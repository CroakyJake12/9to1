namespace HavenOS.Home.Apps;
public sealed partial class HomePackageDatabase
{
    // Pure validation only, over existing canonical protocol; no authority or IO.
    public static HomePackageDatabaseFailure? ValidateOriginalProtectedSnapshot(HomePackageDatabaseSnapshot sameObserved) => Validate(sameObserved);
}
