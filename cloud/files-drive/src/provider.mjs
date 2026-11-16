import { FilesHttpError, guid } from './protocol.mjs';

const GOOGLE = 'https://www.googleapis.com/drive/';
const FOLDER = 'application/vnd.google-apps.folder';
const OPERATION_PROPERTY = 'nineOperation';
const PAGE_SIZE = 100;
const MAX_SCAN_PAGES = 5;
const GOOGLE_ID = /^[A-Za-z0-9_-]{1,256}$/;
const VERSION = /^\d{1,24}$/;
const ETAG = /^"[\x21\x23-\x7e]{1,512}"$/;
export const DRIVE_FILE_FIELDS = 'id,name,mimeType,size,createdTime,modifiedTime,parents,version,ownedByMe,owners(permissionId),shared,driveId,capabilities,appProperties,trashed,explicitlyTrashed,md5Checksum,sha256Checksum';
export const DRIVE_V2_PATCH_FIELDS = 'id,etag,version,title,modifiedDate,parents(id),labels(trashed),explicitlyTrashed,properties(key,value,visibility)';
const ACTIONS = new Set(['CreateFolder', 'Rename', 'Move', 'Delete', 'Restore']);
const RATE_REASONS = new Set(['rateLimitExceeded', 'userRateLimitExceeded', 'dailyLimitExceeded', 'quotaExceeded']);
const AUTHORITATIVE = new Set(['ItemNotFound', 'RevisionConflict', 'NameConflict', 'InvalidName', 'InvalidState',
  'DestinationUnavailable', 'ProviderCapabilityUnsupported', 'ConflictRequiresDecision', 'PermissionDenied']);

const wrap = value => value ? { value } : null;
const success = value => ({ value, error: null });

function failure(status, code, message, notStarted = true, retryAt = null) {
  return new FilesHttpError(status, code, message, { notStarted, retryAt });
}

function badMetadata() {
  return failure(502, 'ProviderUnavailable', 'Google Drive returned incomplete verification information.');
}

function uncertain() {
  return failure(503, 'ProviderUnavailable', 'Google Drive has not confirmed this change. Retry with the same operation to check.', false);
}

function capability(message) {
  return failure(409, 'ProviderCapabilityUnsupported', message);
}

function requestUrl(version, path, params) {
  const url = new URL(version + '/' + path, GOOGLE);
  for (const [key, value] of Object.entries(params ?? {})) if (value !== null) url.searchParams.set(key, value);
  return url.href;
}

function duration(retryAt, now) {
  if (!Number.isFinite(retryAt) || retryAt <= now) return null;
  const seconds = Math.ceil((retryAt - now) / 1000);
  const days = Math.floor(seconds / 86400);
  const hhmmss = [Math.floor(seconds / 3600) % 24, Math.floor(seconds / 60) % 60, seconds % 60]
    .map(n => String(n).padStart(2, '0')).join(':');
  return (days ? days + '.' : '') + hhmmss;
}

function resultError(error, action, targetId, now, notStarted, previousUnknown = false) {
  const quota = error?.name === 'QuotaError' || error?.code === 'QuotaExceeded';
  const known = error instanceof FilesHttpError;
  const preserveUnknown = previousUnknown && !quota;
  const typed = ['InvalidIdentifier', 'OperationConflict', 'IdentityConflict'].includes(error?.code) ? 'InvalidState' : error?.code;
  const code = quota ? 'QuotaExceeded' : !preserveUnknown && known && AUTHORITATIVE.has(typed) ? typed : 'ProviderUnavailable';
  const message = preserveUnknown ? uncertain().message : (quota || known) ? error.message : 'Google Drive could not confirm this Files action.';
  const retryAfter = duration(error?.retryAt, now);
  return { value: null, error: {
    code, message, action, targetId, isRecoverable: code !== 'PermissionDenied',
    canRetry: quota ? retryAfter !== null : code === 'ProviderUnavailable', retryAfter,
    details: notStarted ? { notStarted: 'true' } : null,
  } };
}

function providerError(result, now) {
  const { status, value, headers } = result;
  const reason = value?.error?.errors?.[0]?.reason;
  if (status === 429 || status === 403 && RATE_REASONS.has(reason)) {
    const header = headers?.get('retry-after');
    const retry = /^\d{1,8}$/.test(header ?? '') ? now + Number(header) * 1000 : Date.parse(header ?? '');
    const retryAt = Number.isFinite(retry) ? Math.min(now + 300_000, Math.max(now + 1000, retry)) : now + 30_000;
    return failure(429, 'QuotaExceeded', 'Google Drive paused requests. Try again after its allowance refreshes.', true, retryAt);
  }
  if (status === 401) return failure(409, 'DriveReconnectRequired', 'Connect Google Drive again in Files.');
  if (status === 403) return failure(403, 'PermissionDenied', 'Google Drive denied access to this item.');
  if (status === 404) return failure(404, 'ItemNotFound', 'Google Drive could not find this item.');
  if (status === 412) return failure(409, 'RevisionConflict', 'This item changed. Refresh before trying the change again.');
  if (status === 409) return failure(409, 'NameConflict', 'Google Drive refused this conflicting change.');
  if (status === 400 && ['conditionNotMet', 'unsupportedOperation', 'notSupportedForFile'].includes(reason)) {
    return capability('Google Drive rejected the required version protection.');
  }
  if (status === 400) return failure(400, 'InvalidState', 'Google Drive rejected this action for the current item.');
  return failure(503, 'ProviderUnavailable', 'Google Drive could not confirm this Files action.', false);
}

function intentFrom(operation, suppliedName, owner) {
  if (!operation || typeof operation !== 'object' || operation.actorId !== owner) {
    throw failure(403, 'PermissionDenied', 'This operation does not belong to the signed-in Files owner.');
  }
  const action = operation.operation;
  if (!ACTIONS.has(action) || operation.state !== 'Pending' || operation.resultRevisionId != null || operation.error != null) {
    throw failure(400, 'InvalidState', 'The file operation is not a pending change that Files supports.');
  }
  const id = guid(operation.id);
  const itemId = guid(operation.itemId);
  const sourceParentId = guid(operation.sourceParentId, true);
  const destinationParentId = guid(operation.destinationParentId, true);
  const baseRevisionId = guid(operation.baseRevisionId, true);
  const usesName = action === 'CreateFolder' || action === 'Rename';
  const newName = operation.payload?.newName ?? null;
  if ((suppliedName ?? null) !== newName || (usesName && (typeof newName !== 'string' ||
      newName.trim().length === 0 || newName.length > 255 || /[\u0000-\u001f\u007f]/.test(newName) || newName === '.' || newName === '..')) ||
      (!usesName && newName !== null)) {
    throw failure(400, 'InvalidName', 'Enter a valid name for this Google Drive item.');
  }
  if ((action === 'CreateFolder' ? baseRevisionId !== null || sourceParentId !== null : baseRevisionId === null) ||
      (!['CreateFolder', 'Move'].includes(action) && destinationParentId !== null) ||
      (action === 'Move' && (destinationParentId === sourceParentId || destinationParentId === itemId))) {
    throw failure(400, 'InvalidState', 'This file operation does not have a valid source, destination and revision.');
  }
  return { id, actorId: owner, itemId, sourceParentId, destinationParentId, operation: action, baseRevisionId, newName };
}

function remoteId(id) {
  if (typeof id !== 'string' || !GOOGLE_ID.test(id)) throw badMetadata();
  return id;
}

function validVersion(value) {
  return typeof value === 'string' && VERSION.test(value);
}

function requireFile(file, expected) {
  if (!file || remoteId(file.id) !== (expected ?? file.id) || !validVersion(file.version) ||
      typeof file.name !== 'string' || file.name.length > 32768 || typeof file.mimeType !== 'string' ||
      !Number.isFinite(Date.parse(file.createdTime)) || !Number.isFinite(Date.parse(file.modifiedTime)) ||
      typeof file.trashed !== 'boolean' ||
      (file.size !== undefined && (!/^\d{1,16}$/.test(String(file.size)) || !Number.isSafeInteger(Number(file.size)))) ||
      (file.parents !== undefined && (!Array.isArray(file.parents) || file.parents.length > 1))) throw badMetadata();
  file.parents?.forEach(remoteId);
  return file;
}

function firstParent(file) {
  return file.parents?.[0] ?? null;
}

export function driveOperationProperties(properties, id) {
  if (properties !== undefined && !Array.isArray(properties) || properties?.length > 100) throw badMetadata();
  const updated = [];
  for (const property of properties ?? []) {
    if (!property || typeof property.key !== 'string' || typeof property.value !== 'string' ||
        !['PRIVATE', 'PUBLIC'].includes(property.visibility)) throw badMetadata();
    if (property.key !== OPERATION_PROPERTY || property.visibility !== 'PRIVATE') {
      updated.push({ key: property.key, value: property.value, visibility: property.visibility });
    }
  }
  return [...updated, { key: OPERATION_PROPERTY, value: id, visibility: 'PRIVATE' }];
}

export function strongDriveETag(result) {
  const header = result.headers?.get('etag');
  const body = result.value?.etag;
  if (body === undefined) return typeof header === 'string' && ETAG.test(header) ? header : null;
  if (typeof body !== 'string' || !ETAG.test(body)) return null;
  // Drive v2 can send a weak representation header with the same opaque
  // value as its strong resource tag. Use the actual strong resource tag.
  return header == null || header === body || header === 'W/' + body ? body : null;
}

function patchAck(v2, before, expectedId) {
  if (!v2 || v2.id !== expectedId || !validVersion(v2.version) || BigInt(v2.version) <= BigInt(before.version) ||
      typeof v2.title !== 'string' || !Number.isFinite(Date.parse(v2.modifiedDate)) || typeof v2.labels?.trashed !== 'boolean' ||
      !Array.isArray(v2.properties) || (v2.parents !== undefined && !Array.isArray(v2.parents))) throw uncertain();
  const marker = v2.properties.find(p => p?.visibility === 'PRIVATE' && p.key === OPERATION_PROPERTY);
  if (!marker) throw uncertain();
  return requireFile({
    ...before, version: v2.version, name: v2.title, modifiedTime: v2.modifiedDate,
    parents: (v2.parents ?? []).map(parent => remoteId(parent?.id)), trashed: v2.labels.trashed,
    explicitlyTrashed: v2.explicitlyTrashed,
    appProperties: { ...before.appProperties, [OPERATION_PROPERTY]: marker.value },
  }, expectedId);
}

export class DriveProvider {
  #store;
  #oauth;
  #transport;
  #now;
  #conditionalWrites;
  #active = new Set();

  constructor(store, oauth, transport, { now = Date.now, conditionalWrites = false } = {}) {
    this.#store = store;
    this.#oauth = oauth;
    this.#transport = transport;
    this.#now = now;
    this.#conditionalWrites = conditionalWrites;
  }

  async list({ parentId = null, query = null, pageToken = null, trash = false } = {}, signal) {
    parentId = guid(parentId, true);
    if (typeof trash !== 'boolean' || (trash && parentId !== null) ||
        (query !== null && (typeof query !== 'string' || query.length > 512))) {
      throw failure(400, 'InvalidState', 'The requested file listing is invalid.');
    }
    const orderBy = trash ? 'modifiedTime desc,name' : 'name';
    const context = { parentId, query: query ?? '', trash, ...(trash ? { orderBy } : {}) };
    let cursor = pageToken == null ? null : this.#store.readPage(pageToken, context);
    const token = await this.#oauth.getAccessToken({ signal });
    await this.#root(token, signal);
    const parent = trash ? null : remoteId(this.#store.googleId(parentId));
    const q = trash ? 'trashed = true' : "'" + parent + "' in parents and trashed = false";
    const match = context.query ? new RegExp(RegExp.escape(context.query), 'iu') : null;
    const visited = new Set([cursor]);
    for (let number = 0; number < MAX_SCAN_PAGES; number++) {
      const value = await this.#get('v3', 'files', {
        q, spaces: 'drive', pageSize: String(PAGE_SIZE), pageToken: cursor, orderBy,
        fields: 'nextPageToken,incompleteSearch,files(' + DRIVE_FILE_FIELDS + ')',
      }, token, signal, 'list');
      if (!Array.isArray(value?.files) || value.files.length > PAGE_SIZE || value.incompleteSearch === true) throw badMetadata();
      const next = value.nextPageToken ?? null;
      if (next !== null && (typeof next !== 'string' || !next || next.length > 16384 || visited.has(next))) throw badMetadata();
      visited.add(next);
      const files = value.files.map(file => requireFile(file));
      if (files.some(file => typeof file.name !== 'string' || file.trashed !== trash)) throw badMetadata();
      const items = files.filter(file => !match || match.test(file.name)).map(file => this.#store.mapFile(file));
      cursor = next;
      if (items.length || !cursor || number === MAX_SCAN_PAGES - 1) {
        return { items, nextPageToken: cursor ? this.#store.newPage(context, cursor) : null };
      }
    }
  }

  async get(id, signal) {
    try {
      id = guid(id);
      const file = await this.readRaw(id, signal);
      return success(this.#store.mapFile(file));
    } catch (error) {
      return resultError(error, 'Get', typeof id === 'string' ? id : null, this.#now(), true);
    }
  }

  // Internal transfer entry points. Google IDs, permission proofs and bearer
  // credentials must stay on the server.
  async readRaw(id, signal) {
    const canonical = guid(id);
    const token = await this.#oauth.getAccessToken({ signal });
    await this.#root(token, signal);
    return this.#file(this.#store.googleId(canonical), token, signal);
  }

  async readParent(id, signal) {
    const canonical = guid(id, true);
    const token = await this.#oauth.getAccessToken({ signal });
    await this.#root(token, signal);
    const folder = await this.#folder(this.#store.googleId(canonical), token, signal);
    if (folder.capabilities?.canAddChildren !== true) throw capability('Google Drive does not allow writing to that folder.');
    return folder;
  }

  async mutate({ operation, newName } = {}, signal) {
    let intent;
    try {
      intent = intentFrom(operation, newName, this.#store.ownerId);
      if (this.#active.has(intent.id)) return resultError(uncertain(), intent.operation, intent.itemId, this.#now(), false);
      this.#active.add(intent.id);
    } catch (error) {
      return resultError(error, 'Mutate', null, this.#now(), true);
    }
    let writeMayHaveRun = false;
    try {
      const row = this.#store.claimOperation(intent.id, intent);
      if (row.stage === 'committed' || row.stage === 'rejected') return row.result;
      writeMayHaveRun = row.stage === 'issuing';
      const token = await this.#oauth.getAccessToken({ signal });
      await this.#root(token, signal);
      if (writeMayHaveRun) return await this.#reconcile(intent, row.provider_id, token, signal);
      if (row.stage !== 'prepared') throw failure(400, 'InvalidState', 'This operation belongs to another Files action.');
      const plan = intent.operation === 'CreateFolder'
        ? await this.#createPlan(intent, row.provider_id, token, signal)
        : await this.#updatePlan(intent, token, signal);
      signal?.throwIfAborted();
      this.#store.stageOperation(intent.id, 'issuing', plan.googleId);
      await this.#store.storage.sync();
      writeMayHaveRun = true;
      let result;
      try {
        result = await this.#request(plan.url, token, {
          kind: 'mutate', method: plan.method, signal,
          headers: { 'content-type': 'application/json', ...plan.headers },
          body: JSON.stringify(plan.body),
        });
      } catch (error) {
        if (error?.notStarted === true) {
          this.#store.stageOperation(intent.id, 'prepared');
          writeMayHaveRun = false;
        }
        throw error;
      }
      if (result.status === 409 && !plan.before) return await this.#reconcile(intent, plan.googleId, token, signal);
      if (![200, 201].includes(result.status)) {
        const error = providerError(result, this.#now());
        if (!error.notStarted) throw error;
        const denied = resultError(error, intent.operation, intent.itemId, this.#now(), false);
        if (denied.error.code === 'QuotaExceeded' || error.code === 'DriveReconnectRequired') this.#store.stageOperation(intent.id, 'prepared');
        else this.#store.stageOperation(intent.id, 'rejected', null, denied);
        return denied;
      }
      const file = plan.before ? patchAck(result.value, plan.before, plan.googleId) : requireFile(result.value, plan.googleId);
      return this.#commit(intent, plan.googleId, file);
    } catch (error) {
      return resultError(error, intent.operation, intent.itemId, this.#now(), !writeMayHaveRun, writeMayHaveRun);
    } finally {
      this.#active.delete(intent.id);
    }
  }

  async #request(url, token, options) {
    const result = await this.#transport.json(url, {
      ...options, headers: { authorization: 'Bearer ' + token, ...options.headers },
    });
    if (result.status === 401) await this.#oauth.rejectAccessToken?.(token);
    return result;
  }

  async #get(version, path, params, token, signal, kind = 'metadata') {
    const result = await this.#request(requestUrl(version, path, params), token, { kind, signal });
    if (result.status !== 200) throw providerError(result, this.#now());
    return result.value;
  }

  async #file(id, token, signal) {
    remoteId(id);
    const value = await this.#get('v3', 'files/' + id, { fields: DRIVE_FILE_FIELDS }, token, signal);
    return requireFile(value, id === 'root' ? undefined : id);
  }

  async #root(token, signal) {
    if (this.#store.get('googleRoot')) return;
    const root = await this.#file('root', token, signal);
    if (root.id === 'root' || root.mimeType !== FOLDER || root.ownedByMe !== true ||
        root.trashed !== false || root.driveId != null || firstParent(root) !== null) throw badMetadata();
    this.#store.setRoot(root);
  }

  async #folder(googleId, token, signal) {
    const folder = await this.#file(googleId, token, signal);
    if (folder.mimeType !== FOLDER || folder.trashed !== false) {
      throw failure(409, 'DestinationUnavailable', 'The requested Google Drive folder is unavailable.');
    }
    return folder;
  }

  async #createPlan(intent, reserved, token, signal) {
    const parent = remoteId(this.#store.googleId(intent.destinationParentId));
    const folder = await this.#folder(parent, token, signal);
    if (folder.capabilities?.canAddChildren !== true) throw capability('Google Drive does not allow creating items in that folder.');
    const mapping = this.#store.byId(intent.itemId);
    if (mapping && (mapping.google_id !== reserved || mapping.metadata !== null)) {
      throw failure(409, 'InvalidState', 'The folder identity is already in use.');
    }
    let googleId = reserved;
    if (!googleId) {
      const data = await this.#get('v3', 'files/generateIds', { count: '1', space: 'drive', type: 'files' }, token, signal);
      if (!Array.isArray(data?.ids) || data.ids.length !== 1) throw badMetadata();
      googleId = remoteId(data.ids[0]);
    }
    // The folder can be listed while the POST reply is missing. Reserve its
    // local identity in the same transaction as the provider's generated ID.
    this.#store.storage.transactionSync(() => {
      this.#store.ensureIdInTransaction(googleId, intent.itemId);
      this.#store.stageOperation(intent.id, 'prepared', googleId);
    });
    await this.#store.storage.sync();
    return {
      googleId, method: 'POST', url: requestUrl('v3', 'files', { fields: DRIVE_FILE_FIELDS }),
      body: { id: googleId, mimeType: FOLDER, name: intent.newName, parents: [parent],
        appProperties: { [OPERATION_PROPERTY]: intent.id } },
    };
  }

  async #updatePlan(intent, token, signal) {
    if (!this.#conditionalWrites) throw capability('Google Drive version protection is unavailable for this action.');
    const row = this.#store.byId(intent.itemId);
    if (!row) throw failure(404, 'ItemNotFound', 'Google Drive could not find this item.');
    const googleId = remoteId(row.google_id);
    const root = this.#store.get('googleRoot');
    if (googleId === root) throw capability('The top level of Google Drive cannot be changed here.');
    const before = await this.#file(googleId, token, signal);
    const expectedSource = intent.sourceParentId ? this.#store.googleId(intent.sourceParentId) : root;
    if (row.revision_id !== intent.baseRevisionId || String(before.version) !== row.google_version ||
        firstParent(before) !== expectedSource) {
      this.#store.mapFile(before);
      throw failure(409, 'RevisionConflict', 'This item changed. Refresh before trying the change again.');
    }
    const { operation } = intent;
    const capabilityName = { Rename: 'canRename', Move: 'canMoveItemWithinDrive', Delete: 'canTrash', Restore: 'canUntrash' }[operation];
    if (before.capabilities?.[capabilityName] !== true) throw capability('Google Drive does not allow this change on the selected item.');
    if (before.trashed !== (operation === 'Restore') || (operation === 'Restore' && before.explicitlyTrashed !== true)) {
      throw failure(409, 'InvalidState', 'This action does not match the item’s current Trash state.');
    }
    const params = { fields: DRIVE_V2_PATCH_FIELDS };
    const body = {};
    if (operation === 'Rename') body.title = intent.newName;
    if (operation === 'Delete' || operation === 'Restore') body.labels = { trashed: operation === 'Delete' };
    if (operation === 'Restore') await this.#folder(expectedSource, token, signal);
    if (operation === 'Move') {
      const destination = remoteId(this.#store.googleId(intent.destinationParentId));
      const source = await this.#folder(expectedSource, token, signal);
      const target = await this.#folder(destination, token, signal);
      if ([before, source, target].some(file => file.ownedByMe !== true || file.shared !== false || file.driveId != null)) {
        throw failure(409, 'ConflictRequiresDecision', 'Moving between these folders requires a permission review in Google Drive.');
      }
      if (source.capabilities?.canRemoveChildren !== true || target.capabilities?.canAddChildren !== true) {
        throw capability('Google Drive does not allow moving the item between these folders.');
      }
      params.addParents = destination;
      params.removeParents = expectedSource;
    }
    const path = 'files/' + googleId;
    const current = await this.#request(requestUrl('v2', path, { fields: DRIVE_V2_PATCH_FIELDS }), token, { kind: 'metadata', signal });
    if (current.status !== 200) throw providerError(current, this.#now());
    const v2 = current.value;
    if (v2?.id !== googleId || !validVersion(v2.version)) throw badMetadata();
    if (String(v2.version) !== String(before.version)) {
      throw failure(409, 'RevisionConflict', 'This item changed. Refresh before trying the change again.');
    }
    const tag = strongDriveETag(current);
    if (!tag) {
      throw capability('Google Drive did not provide the strong version protection needed to save safely.');
    }
    body.properties = driveOperationProperties(v2.properties, intent.id);
    return {
      googleId, before, method: 'PATCH', url: requestUrl('v2', path, params),
      headers: { 'if-match': tag }, body,
    };
  }

  async #reconcile(intent, id, token, signal) {
    if (!id) throw uncertain();
    const file = await this.#file(id, token, signal);
    return this.#commit(intent, id, file);
  }

  #commit(intent, id, file) {
    const { operation } = intent;
    if (file.id !== id || file.appProperties?.[OPERATION_PROPERTY] !== intent.id) throw uncertain();
    const parent = this.#store.googleId(
      operation === 'CreateFolder' || operation === 'Move' ? intent.destinationParentId : intent.sourceParentId);
    if (firstParent(file) !== parent || file.trashed !== (operation === 'Delete') ||
        (operation === 'Delete' && file.explicitlyTrashed !== true) ||
        (intent.newName !== null && file.name !== intent.newName) || (operation === 'CreateFolder' && file.mimeType !== FOLDER)) throw uncertain();
    const metadata = this.#store.mapFile(file, intent.itemId);
    const stored = this.#store.byId(intent.itemId);
    if (!metadata.currentRevisionId || stored?.google_version !== String(file.version) ||
        metadata.currentRevisionId.value === intent.baseRevisionId) throw uncertain();
    return this.#store.storage.transactionSync(() => {
      const old = this.#store.operation(intent.id);
      if (old?.stage === 'committed') return old.result;
      if (old?.stage !== 'issuing') throw uncertain();
      const createdAt = new Date(old.created_at).toISOString();
      const updatedAt = new Date(this.#now()).toISOString();
      const baseRevisionId = wrap(intent.baseRevisionId);
      const sequence = this.#store.change({
        eventId: intent.id, operationId: wrap(intent.id), itemId: wrap(intent.itemId),
        actorId: intent.actorId, kind: operation, baseRevisionId, resultRevisionId: metadata.currentRevisionId,
        occurredAt: updatedAt, metadata,
      });
      const result = success({
        id: wrap(intent.id), actorId: intent.actorId, itemId: wrap(intent.itemId), sourceParentId: wrap(intent.sourceParentId),
        destinationParentId: wrap(intent.destinationParentId), operation, baseRevisionId, resultRevisionId: metadata.currentRevisionId,
        state: 'Committed', createdAt, updatedAt, inversePayload: null, error: null, sequence,
        payload: intent.newName === null ? null : { newName: intent.newName },
      });
      this.#store.stageOperation(intent.id, 'committed', id, result);
      return result;
    });
  }
}
