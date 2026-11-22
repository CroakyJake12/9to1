# Recover a personal task on Windows

Recovery reopens a recorded Task/Run under the original personal profile. Validate the task, run, workspace and current actor before reopening associated files or granting access to stored drafts. A journal entry or a surviving process is only one part of that identity.

Use the native personal recovery store and the same SQLite task owner that created the record. Retain the original receipt, unresolved conflict and a retryable error when an expected source or generation has changed. Exercise normal shutdown and crash recovery separately.

## Windows activation and private storage

`HAVEN_NATIVE_PERSONAL_TASK_COLD_RECOVERY=1` requests configuration. A missing value or `0` leaves ordinary Task behavior active. Other values refuse configuration. `NativePersonalTaskRecoveryStore` and `SqliteTaskRunColdRecoveryJournal` validate the actual store and actor independently of that setting.

The Windows setup option `--create-native-private-store` requests a new private store for the resolved AppPaths location before SQLite opens. The owner’s current SID, protected ACLs, file identity and the WAL/SHM/journal relationship all matter. A changed path or foreign owner must not acquire an authenticated recovery record. A successful setup and an actual authenticated recovery are separate outcomes.

[Storage and recovery](state-and-persistence.md) · [Development task and draft ownership](../../../Dev/README.md) · [Windows store checks](../../src/Haven.Infrastructure/Persistence/SQLite/Recovery/NativePersonalTaskRecoveryStore.Windows.cs)
