import { FilesHttpError, guid } from './protocol.mjs';

const folderMime = 'application/vnd.google-apps.folder';
const wrap = value => value ? { value } : null;
const badProvider = () => new FilesHttpError(502, 'InvalidProviderMetadata', 'Google Drive returned incomplete file information.');

export class DriveStore {
  constructor(storage, ownerId, { now = Date.now } = {}) {
    this.storage = storage; this.ownerId = ownerId; this.now = now;
    storage.transactionSync(() => {
      this.query('CREATE TABLE IF NOT EXISTS files_state (name TEXT PRIMARY KEY, value TEXT NOT NULL)');
      this.query(`CREATE TABLE IF NOT EXISTS files_items (
        id TEXT PRIMARY KEY, google_id TEXT NOT NULL UNIQUE, google_version TEXT,
        revision_id TEXT, metadata TEXT, provider_metadata TEXT, observed_at INTEGER NOT NULL DEFAULT 0)`);
      this.query(`CREATE TABLE IF NOT EXISTS files_operations (
        id TEXT PRIMARY KEY, intent TEXT NOT NULL, stage TEXT NOT NULL,
        provider_id TEXT, result TEXT, created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL)`);
      this.query(`CREATE TABLE IF NOT EXISTS files_pages (
        id TEXT PRIMARY KEY, context TEXT NOT NULL, provider_token TEXT NOT NULL, expires_at INTEGER NOT NULL)`);
      this.query(`CREATE TABLE IF NOT EXISTS files_changes (
        sequence INTEGER PRIMARY KEY AUTOINCREMENT, event TEXT NOT NULL, created_at INTEGER NOT NULL)`);
      this.query(`CREATE TABLE IF NOT EXISTS files_transfers (
        id TEXT PRIMARY KEY, operation_id TEXT UNIQUE, direction TEXT NOT NULL,
        state TEXT NOT NULL, record TEXT NOT NULL, updated_at INTEGER NOT NULL)`);
      if (!this.get('locationId')) this.set('locationId', crypto.randomUUID());
      const previous = this.get('ownerId');
      if (previous && previous !== ownerId) throw new Error('Files storage belongs to another owner.');
      this.set('ownerId', ownerId);
    });
  }
  query(sql, ...args) { return this.storage.sql.exec(sql, ...args).toArray(); }
  get(name) { const row = this.query('SELECT value FROM files_state WHERE name = ?', name)[0]; return row ? JSON.parse(row.value) : null; }
  set(name, value) { this.query('INSERT INTO files_state(name,value) VALUES (?,?) ON CONFLICT(name) DO UPDATE SET value=excluded.value', name, JSON.stringify(value)); }
  location() { return { id: wrap(this.get('locationId')), name: 'Google Drive', kind: 'ConnectedCloud',
    capabilities: 'Read, Write, Rename, Move, Trash, Search, Streaming, ResumableTransfers', providerId: 'google-drive',
    credentialReference: null, isAvailable: true, unavailableReason: null }; }
  ensureId(googleId, requested = null) {
    return this.storage.transactionSync(() => this.ensureIdInTransaction(googleId, requested));
  }
  ensureIdInTransaction(googleId, requested = null) {
    if (typeof googleId !== 'string' || !/^[A-Za-z0-9_-]{1,256}$/.test(googleId)) throw badProvider();
      const old = this.query('SELECT id FROM files_items WHERE google_id = ?', googleId)[0];
      if (old) { if (requested && old.id !== guid(requested)) throw new FilesHttpError(409, 'IdentityConflict', 'That file already has a different stable identity.'); return old.id; }
      if (this.query('SELECT COUNT(*) AS count FROM files_items')[0].count >= 200000) throw new FilesHttpError(507, 'MetadataCapacity', 'The private file index has reached its limit.');
      const id = requested ? guid(requested) : crypto.randomUUID();
      if (this.query('SELECT id FROM files_items WHERE id = ?', id).length) throw new FilesHttpError(409, 'IdentityConflict', 'The requested file identity is already in use.');
      this.query('INSERT INTO files_items(id,google_id) VALUES (?,?)', id, googleId); return id;
  }
  byId(id) { return this.query('SELECT * FROM files_items WHERE id = ?', guid(id))[0] ?? null; }
  byGoogle(id) { return this.query('SELECT * FROM files_items WHERE google_id = ?', id)[0] ?? null; }
  googleId(id) {
    if (id === null || id === undefined) return this.get('googleRoot') ?? 'root';
    const row = this.byId(id);
    if (!row) throw new FilesHttpError(404, 'ItemNotFound', 'That file is no longer in the current Drive view.', { notStarted: true });
    return row.google_id;
  }
  setRoot(file) { const metadata = this.mapFile(file); this.set('googleRoot', file.id); return metadata; }
  mapFile(file, requestedId = null) {
    return this.storage.transactionSync(() => this.mapFileInTransaction(file, requestedId));
  }
  mapFileInTransaction(file, requestedId = null) {
    if (!file || typeof file.id !== 'string' || typeof file.name !== 'string' || file.name.length > 32768 ||
        typeof file.mimeType !== 'string' || !/^\d{1,24}$/.test(String(file.version)) ||
        !Number.isFinite(Date.parse(file.createdTime)) || !Number.isFinite(Date.parse(file.modifiedTime))) throw badProvider();
    const size = file.size === undefined ? null : Number(file.size);
    if (size !== null && (!Number.isSafeInteger(size) || size < 0)) throw badProvider();
    const id = this.ensureIdInTransaction(file.id, requestedId), row = this.byId(id);
    if (row.google_version && BigInt(row.google_version) > BigInt(file.version)) return JSON.parse(row.metadata);
    const revision = row.google_version === String(file.version) ? row.revision_id : crypto.randomUUID();
    const parentGoogle = file.parents?.[0] ?? null;
    const parentId = parentGoogle && parentGoogle !== this.get('googleRoot') ? this.ensureIdInTransaction(parentGoogle) : null;
    const owner = file.ownedByMe ? this.ownerId : file.owners?.[0]?.permissionId ? 'google:' + file.owners[0].permissionId : 'google:external';
    const metadata = { id: wrap(id), locationId: wrap(this.get('locationId')), parentId: wrap(parentId), name: file.name,
      kind: file.mimeType === folderMime ? 'Folder' : file.mimeType === 'application/vnd.google-apps.shortcut' ? 'Reference' : 'File',
      contentType: file.mimeType, ownerPrincipalId: owner, scope: 'connected-provider', sizeBytes: size,
      createdAt: file.createdTime, modifiedAt: file.modifiedTime, currentRevisionId: wrap(revision), availability: 'CloudOnly',
      isShared: file.shared === true, contentHash: file.sha256Checksum ? 'sha256:' + file.sha256Checksum : file.md5Checksum ? 'md5:' + file.md5Checksum : null,
      metadataSchemaVersion: 1 };
    this.query('UPDATE files_items SET google_version=?,revision_id=?,metadata=?,provider_metadata=?,observed_at=? WHERE id=?',
      String(file.version), revision, JSON.stringify(metadata), JSON.stringify(file), this.now(), id);
    return metadata;
  }
  newPage(context, providerToken) {
    if (typeof providerToken !== 'string' || providerToken.length > 16384) throw badProvider();
    this.query('DELETE FROM files_pages WHERE expires_at <= ?', this.now());
    if (this.query('SELECT COUNT(*) AS count FROM files_pages')[0].count >= 256) throw new FilesHttpError(429, 'TooManyOpenPages', 'Too many folder views are open. Refresh an existing view shortly.', { notStarted: true });
    const id = crypto.randomUUID();
    this.query('INSERT INTO files_pages VALUES (?,?,?,?)', id, JSON.stringify(context), providerToken, this.now() + 15 * 60000);
    return id;
  }
  readPage(id, context) {
    const page = this.query('SELECT * FROM files_pages WHERE id = ?', guid(id))[0];
    if (!page || page.expires_at <= this.now() || page.context !== JSON.stringify(context)) throw new FilesHttpError(409, 'PageExpired', 'This folder page has changed. Refresh the folder.', { notStarted: true });
    return page.provider_token;
  }
  operation(id) { const row = this.query('SELECT * FROM files_operations WHERE id = ?', guid(id))[0]; return row ? { ...row, intent: JSON.parse(row.intent), result: row.result ? JSON.parse(row.result) : null } : null; }
  claimOperation(id, intent) {
    return this.storage.transactionSync(() => {
      id = guid(id); const existing = this.operation(id);
      if (existing) {
        if (JSON.stringify(existing.intent) !== JSON.stringify(intent)) throw new FilesHttpError(409, 'OperationConflict', 'This operation identifier belongs to a different change.', { notStarted: true });
        return existing;
      }
      if (this.query('SELECT COUNT(*) AS count FROM files_operations')[0].count >= 100000) throw new FilesHttpError(507, 'JournalCapacity', 'The operation journal has reached its limit.', { notStarted: true });
      this.query('INSERT INTO files_operations VALUES (?,?,\'prepared\',NULL,NULL,?,?)', id, JSON.stringify(intent), this.now(), this.now());
      return this.operation(id);
    });
  }
  stageOperation(id, stage, providerId = null, result = null) {
    this.query('UPDATE files_operations SET stage=?,provider_id=COALESCE(?,provider_id),result=?,updated_at=? WHERE id=?',
      stage, providerId, result ? JSON.stringify(result) : null, this.now(), guid(id));
  }
  change(event) {
    const [row] = this.query('INSERT INTO files_changes(event,created_at) VALUES (?,?) RETURNING sequence', JSON.stringify(event), this.now());
    return row.sequence;
  }
  changes(after, limit = 100) {
    if (!Number.isSafeInteger(after) || after < 0 || !Number.isInteger(limit) || limit < 1 || limit > 500) throw new FilesHttpError(400, 'InvalidCursor', 'The change cursor is invalid.', { notStarted: true });
    // Read one lookahead row in the same SQLite snapshot, but never expose it or
    // advance the continuation past the last event already delivered.
    const rows = this.query('SELECT sequence,event FROM files_changes WHERE sequence>? ORDER BY sequence LIMIT ?', after, limit + 1);
    const hasMore = rows.length > limit;
    if (hasMore) rows.pop();
    return { items: rows.map(row => ({ ...JSON.parse(row.event), cursor: { revision: row.sequence } })), nextPageToken: hasMore ? String(rows.at(-1).sequence) : null };
  }
  transfer(id) { const row = this.query('SELECT * FROM files_transfers WHERE id = ?', guid(id))[0]; return row ? JSON.parse(row.record) : null; }
  findTransfer(operationId) { const row = this.query('SELECT record FROM files_transfers WHERE operation_id = ?', guid(operationId))[0]; return row ? JSON.parse(row.record) : null; }
  putTransfer(record, direction) {
    if (!this.transfer(record.transferId) && this.query('SELECT COUNT(*) AS count FROM files_transfers')[0].count >= 10000) throw new FilesHttpError(507, 'TransferCapacity', 'The transfer history has reached its limit.', { notStarted: true });
    this.query(`INSERT INTO files_transfers VALUES (?,?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET
      state=excluded.state,record=excluded.record,updated_at=excluded.updated_at`, guid(record.transferId),
    record.operationId ? guid(record.operationId) : null, direction, record.state, JSON.stringify(record), this.now());
  }
}
