# Locate and preserve native profile data

Find the active profile through the lifecycle path resolver or the managed launcher receipt. The development copy uses an explicitly chosen data directory. Inspect only the records necessary for the change and keep test fixtures in a separate profile.

SQLite-backed entities go through their owning repository and migrations. Native theme preferences use their own revisioned envelope. Compare-and-swap protects concurrent writers and existing unknown fields need to survive an unrelated write. Profile files, authored documents, cached package data and account credentials have separate owners.

For a persistence fix, record the original revision and object ID, apply a single real change, close normally, reopen the same profile and read the object back. Use a disposable copy to exercise interrupted writes or an old schema. Keep the owner’s normal data and original migration evidence recoverable.

[Profile paths](../../src/Haven.Infrastructure/Lifecycle/AppPaths.cs) · [Database](../../src/Haven.Infrastructure/Database/SqliteDatabase.cs) · [Home preference file](../../../Home/Source/Home/Core/HomeNativePreferenceFile.cs) · [Storage owners](../architecture/state-and-persistence.md)
