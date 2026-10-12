-- Account-owned presentation preferences. Browser copies are caches, never authority.
CREATE TABLE accountThemePreference (
  accountId TEXT NOT NULL PRIMARY KEY REFERENCES user(id) ON DELETE CASCADE,
  revision INTEGER NOT NULL CHECK (revision > 0 AND revision <= 9007199254740991),
  preferenceJson TEXT NOT NULL CHECK (json_valid(preferenceJson)),
  updatedAt TEXT NOT NULL,
  CHECK (json_extract(preferenceJson, '$.revision') = revision),
  CHECK (json_extract(preferenceJson, '$.scope.scope') = 1),
  CHECK (json_extract(preferenceJson, '$.scope.ownerId') = accountId)
);
