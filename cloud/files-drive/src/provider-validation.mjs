import { DRIVE_FILE_FIELDS, DRIVE_V2_PATCH_FIELDS, DriveProvider, driveOperationProperties, strongDriveETag } from './provider.mjs';
import { UUID } from './protocol.mjs';

const RECORD = 'providerValidationV1';
const HISTORY = RECORD + ':archivedRunIds';
const MAX_RUNS = 8;
const API = 'https://www.googleapis.com/drive/';
const FOLDER = 'application/vnd.google-apps.folder';
const PROBE_MARKER = 'nineProviderValidation';
const CANARY = 'nineValidationCanary';
const PROVIDER_ID = /^[A-Za-z0-9_-]{1,256}$/;
const VERSION = /^\d{1,24}$/;
const TAG_TEXT = /^[\x21-\x7e]{1,514}$/;
const GOOGLE_REASONS = new Set(['authError', 'accessNotConfigured', 'backendError', 'dailyLimitExceeded',
  'forbidden', 'insufficientPermissions', 'internalError', 'invalid', 'notFound', 'quotaExceeded',
  'rateLimitExceeded', 'required', 'userRateLimitExceeded']);
const wrap = value => value ? { value } : null;

export const DRIVE_VALIDATION_TIMEOUT_MS = 45_000;

export function validValidationRun(value) {
  return typeof value === 'string' && UUID.test(value);
}

export function validationReceipt(store, active = false) {
  const receipt = store.get(RECORD);
  if (!receipt) return { state: 'NotStarted' };
  if (receipt.state === 'Running' && !active) return { ...receipt, state: 'Indeterminate', reason: 'Interrupted' };
  return receipt;
}

class ProbeMismatch extends Error {
  constructor(code) { super(code); this.code = code; }
}

function requireEvidence(expected, code) {
  if (!expected) throw new ProbeMismatch(code);
}

function url(path, params) {
  const target = new URL(path, API);
  target.search = new URLSearchParams(params);
  return target.href;
}

function privateFolder(file) {
  return file?.mimeType === FOLDER && file.ownedByMe === true && file.shared === false && file.driveId == null;
}

function etagEvidence(value) {
  if (value == null) return { kind: 'Missing', value: null };
  if (typeof value !== 'string' || !TAG_TEXT.test(value)) return { kind: 'Redacted', value: null };
  return {
    kind: /^"[\x21\x23-\x7e]{1,512}"$/.test(value) ? 'Strong' :
      /^W\/"[\x21\x23-\x7e]{1,510}"$/.test(value) ? 'Weak' : 'Malformed',
    value,
  };
}

function metadataEvidence(result, id, expectedVersion) {
  const header = result.headers?.get('etag');
  const body = result.value?.etag;
  const version = result.value?.version;
  const reason = result.value?.error?.errors?.[0]?.reason;
  return {
    httpStatus: result.status,
    identityMatches: result.value?.id === id,
    expectedVersion,
    receivedVersion: (typeof version === 'string' && VERSION.test(version)) ||
      (typeof version === 'number' && Number.isSafeInteger(version) && version >= 0) ? version : null,
    receivedVersionType: version === null ? 'null' : typeof version,
    versionMatches: version === expectedVersion,
    headerETag: etagEvidence(header),
    bodyETag: etagEvidence(body),
    headerBodyMatch: header != null && body != null ? header === body : null,
    acceptedStrongETag: Boolean(strongDriveETag(result)),
    errorReason: GOOGLE_REASONS.has(reason) ? reason : null,
  };
}

// Each explicitly configured run is a one-shot owner-approved compatibility proof.
// Callers supply no file IDs, selectors, HTTP methods or bodies.
export class DriveValidationProbe {
  #store;
  #oauth;
  #transport;
  #provider;
  #now;
  #running = false;

  constructor(store, oauth, transport, { now = Date.now } = {}) {
    this.#store = store;
    this.#oauth = oauth;
    this.#transport = transport;
    this.#now = now;
    this.#provider = new DriveProvider(store, oauth, transport, { now, conditionalWrites: true });
  }

  status() { return validationReceipt(this.#store, this.#running); }

  canStart(configuredRun) { return this.#eligibleRun(configuredRun) !== null; }

  #eligibleRun(configuredRun) {
    if (!validValidationRun(configuredRun) || this.#running) return null;
    const runId = configuredRun.toLowerCase();
    const previous = this.#store.get(RECORD);
    const archived = this.#store.get(HISTORY) ?? [];
    if (!Array.isArray(archived) || archived.some(id => !validValidationRun(id) || id !== id.toLowerCase()) ||
        new Set(archived).size !== archived.length || archived.length >= MAX_RUNS - 1 ||
        archived.includes(runId) || this.#store.get(RECORD + ':' + runId)) return null;
    if (!previous) return archived.length === 0 ? { runId, previous, archived } : null;
    if (previous.state !== 'Failed' || !validValidationRun(previous.runId) ||
        previous.runId !== previous.runId.toLowerCase() || previous.runId === runId ||
        archived.includes(previous.runId) || this.#store.get(RECORD + ':' + previous.runId)) return null;
    return { runId, previous, archived };
  }

  async inspect(configuredRun, signal) {
    const receipt = this.status();
    const parent = receipt.folders?.parent;
    const child = receipt.folders?.child;
    const checked = step => receipt.steps?.some(saved => saved.name === step && saved.state === 'Passed');
    const baseline = receipt.steps?.at(-1);
    if (!validValidationRun(configuredRun) || receipt.runId !== configuredRun.toLowerCase() ||
        receipt.state !== 'Failed' || receipt.reason !== 'StrongETagUnavailable' ||
        baseline?.name !== 'ReadBaselineETag' || baseline.state !== 'Stopped' ||
        !checked('CreateParent') || !checked('CreateChild') ||
        !validValidationRun(child?.id) || !PROVIDER_ID.test(child?.googleId ?? '') || !PROVIDER_ID.test(parent?.googleId ?? '') ||
        !VERSION.test(child.lastVerified?.version ?? '') || this.#store.googleId(child.id) !== child.googleId) {
      return { state: 'Unavailable', reason: 'NoEligibleProbeFolder' };
    }
    signal.throwIfAborted();
    const fresh = await this.#provider.readRaw(child.id, signal);
    if (!privateFolder(fresh) || fresh.id !== child.googleId || fresh.name !== child.name ||
        fresh.parents?.length !== 1 || fresh.parents[0] !== parent.googleId || fresh.trashed !== false ||
        fresh.appProperties?.[PROBE_MARKER] !== receipt.runId || fresh.appProperties?.[CANARY] !== 'keep' ||
        fresh.version !== child.lastVerified.version) {
      return { state: 'Unavailable', reason: 'ProbeFolderChanged' };
    }
    const contents = await this.#send('v3/files', {
      q: "'" + child.googleId + "' in parents", pageSize: '1', fields: 'files(id),nextPageToken,incompleteSearch',
    }, { kind: 'list', signal });
    if (contents.status !== 200 || !Array.isArray(contents.value?.files) ||
        contents.value.files.length !== 0 || contents.value.nextPageToken || contents.value.incompleteSearch) {
      return { state: 'Unavailable', reason: 'ProbeFolderNotVerifiedEmpty' };
    }
    const path = 'v2/files/' + child.googleId;
    const original = await this.#send(path, { fields: DRIVE_V2_PATCH_FIELDS }, { kind: 'metadata', signal });
    const minimal = original.status === 400
      ? await this.#send(path, { fields: 'id,etag,version' }, { kind: 'metadata', signal }) : null;
    return {
      state: 'Observed',
      original: metadataEvidence(original, child.googleId, fresh.version),
      minimal: minimal ? metadataEvidence(minimal, child.googleId, fresh.version) : null,
    };
  }

  async run(configuredRun, signal) {
    const receipt = this.#store.storage.transactionSync(() => {
      const eligible = this.#eligibleRun(configuredRun);
      if (!eligible) return null;
      const { runId, previous, archived } = eligible;
      if (previous) {
        this.#store.set(RECORD + ':' + previous.runId, previous);
        this.#store.set(HISTORY, [...archived, previous.runId]);
      }
      const next = {
        runId, state: 'Running', updatedAt: this.#now(), steps: [],
        folders: {
          parent: { id: crypto.randomUUID(), name: '9to1 Files verification ' + runId },
          child: { id: crypto.randomUUID(), name: 'Empty validation folder' },
        },
      };
      this.#store.set(RECORD, next);
      return next;
    });
    if (!receipt) return;
    this.#running = true;
    try {
      await this.#store.storage.sync();
      await this.#exercise(receipt, signal);
      receipt.state = 'Passed';
    } catch (error) {
      const last = receipt.steps.at(-1);
      if (last?.state === 'Running') last.state = 'Stopped';
      receipt.state = error instanceof ProbeMismatch ? 'Failed' : 'Indeterminate';
      receipt.reason = typeof error?.code === 'string' && /^[A-Za-z0-9_]{1,80}$/.test(error.code)
        ? error.code : signal.aborted ? 'Interrupted' : 'AttemptUnconfirmed';
    } finally {
      this.#running = false;
      await this.#save(receipt);
    }
  }

  async #save(receipt) {
    receipt.updatedAt = this.#now();
    this.#store.set(RECORD, receipt);
    await this.#store.storage.sync();
  }

  async #step(receipt, name, action, signal, operationId) {
    signal.throwIfAborted();
    const state = { name, state: 'Running' };
    if (operationId) state.operationId = operationId;
    receipt.steps.push(state);
    await this.#save(receipt);
    signal.throwIfAborted();
    const result = await action(state);
    state.state = 'Passed';
    await this.#save(receipt);
    return result;
  }

  async #send(path, params, options) {
    const token = await this.#oauth.getAccessToken({ signal: options.signal });
    const result = await this.#transport.json(url(path, params), {
      ...options, headers: { authorization: 'Bearer ' + token, ...options.headers },
    });
    if (result.status === 401) await this.#oauth.rejectAccessToken(token);
    return result;
  }

  async #exercise(receipt, signal) {
    const { runId, folders: { parent, child } } = receipt;
    const root = await this.#step(receipt, 'VerifyPrivateRoot', async () => {
      const file = await this.#provider.readParent(null, signal);
      requireEvidence(privateFolder(file) && file.trashed === false, 'RootNotPrivate');
      return file.id;
    }, signal);
    await this.#step(receipt, 'AllocateFolderIds', async state => {
      const result = await this.#send('v3/files/generateIds', { count: '2', space: 'drive', type: 'files' }, { kind: 'metadata', signal });
      state.httpStatus = result.status;
      const ids = result.value?.ids;
      requireEvidence(result.status === 200 && Array.isArray(ids) && ids.length === 2 &&
        ids.every(id => typeof id === 'string' && PROVIDER_ID.test(id) && id !== root && !this.#store.byGoogle(id)) &&
        ids[0] !== ids[1], 'InvalidAllocatedIds');
      [parent.googleId, child.googleId] = ids;
      this.#store.storage.transactionSync(() => {
        this.#store.ensureIdInTransaction(parent.googleId, parent.id);
        this.#store.ensureIdInTransaction(child.googleId, child.id);
        this.#store.set(RECORD, receipt);
      });
      await this.#store.storage.sync();
    }, signal);
    await this.#create(receipt, 'CreateParent', parent, root, signal);
    await this.#read(receipt, parent, root, parent.name, false, null, signal);
    await this.#create(receipt, 'CreateChild', child, parent.googleId, signal);
    const original = await this.#read(receipt, child, parent.googleId, child.name, false, null, signal);
    const baseline = await this.#step(receipt, 'ReadBaselineETag', () => this.#etag(child.googleId, original.version, signal), signal);
    const changedName = child.name + ' renamed';
    const renamed = await this.#update(receipt, 'Rename', 'Rename', original, child, parent.googleId, changedName, signal);
    const after = await this.#step(receipt, 'ReadUpdatedETag', () => this.#etag(child.googleId, renamed.version, signal), signal);
    requireEvidence(after.etag !== baseline.etag && BigInt(renamed.version) > BigInt(original.version), 'ETagDidNotAdvance');
    const staleId = crypto.randomUUID();
    await this.#step(receipt, 'RejectStalePatch', async state => {
      const result = await this.#send('v2/files/' + child.googleId, { fields: DRIVE_V2_PATCH_FIELDS }, {
        kind: 'mutate', method: 'PATCH', signal,
        headers: { 'content-type': 'application/json', 'if-match': baseline.etag },
        body: JSON.stringify({ title: child.name + ' unsafe stale write', properties: driveOperationProperties(baseline.properties, staleId) }),
      });
      state.httpStatus = result.status;
      if (result.status >= 500) throw new Error('Stale patch response could not be confirmed.');
      requireEvidence(result.status === 412, 'StalePatchDidNotReturn412');
      const unchanged = await this.#read(receipt, child, parent.googleId, changedName, false, renamed.appProperties.nineOperation, signal);
      requireEvidence(unchanged.version === renamed.version, 'StalePatchChangedFolder');
    }, signal, staleId);
    await this.#empty(receipt, child, signal);
    const moved = await this.#update(receipt, 'MoveToRoot', 'Move', renamed, child, root, null, signal);
    await this.#empty(receipt, child, signal);
    const returned = await this.#update(receipt, 'MoveBack', 'Move', moved, child, parent.googleId, null, signal);
    await this.#empty(receipt, child, signal);
    const trashed = await this.#update(receipt, 'Trash', 'Delete', returned, child, parent.googleId, null, signal);
    await this.#update(receipt, 'Restore', 'Restore', trashed, child, parent.googleId, null, signal);
  }

  async #create(receipt, name, folder, parent, signal) {
    await this.#step(receipt, name, async state => {
      const result = await this.#send('v3/files', { fields: DRIVE_FILE_FIELDS }, {
        kind: 'mutate', method: 'POST', signal, headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          id: folder.googleId, name: folder.name, mimeType: FOLDER, parents: [parent],
          appProperties: { [PROBE_MARKER]: receipt.runId, [CANARY]: 'keep' },
        }),
      });
      state.httpStatus = result.status;
      if (result.status >= 500) throw new Error('Folder creation could not be confirmed.');
      requireEvidence([200, 201].includes(result.status) && result.value?.id === folder.googleId, 'FolderCreationUnverified');
    }, signal);
  }

  async #read(receipt, folder, parent, name, trashed, operationId, signal) {
    const file = await this.#provider.readRaw(folder.id, signal);
    requireEvidence(privateFolder(file) && file.id === folder.googleId && file.parents?.length === 1 &&
      file.parents[0] === parent && file.name === name && file.trashed === trashed &&
      (trashed ? file.explicitlyTrashed === true : file.explicitlyTrashed !== true) &&
      file.appProperties?.[PROBE_MARKER] === receipt.runId && file.appProperties?.[CANARY] === 'keep' &&
      (operationId === null || file.appProperties.nineOperation === operationId), 'FolderReadbackMismatch');
    folder.lastVerified = { name: file.name, parentGoogleId: file.parents[0], version: file.version, trashed: file.trashed };
    return file;
  }

  async #etag(id, version, signal) {
    const result = await this.#send('v2/files/' + id, { fields: DRIVE_V2_PATCH_FIELDS }, { kind: 'metadata', signal });
    const etag = strongDriveETag(result);
    requireEvidence(result.status === 200 && result.value?.id === id && VERSION.test(result.value?.version ?? '') &&
      result.value.version === version && etag, 'StrongETagUnavailable');
    return { etag, properties: result.value.properties };
  }

  async #empty(receipt, child, signal) {
    await this.#step(receipt, 'CheckEmptyFolder', async state => {
      const result = await this.#send('v3/files', {
        q: "'" + child.googleId + "' in parents", pageSize: '1', fields: 'files(id),nextPageToken,incompleteSearch',
      }, { kind: 'list', signal });
      state.httpStatus = result.status;
      requireEvidence(result.status === 200 && Array.isArray(result.value?.files) &&
        result.value.files.length === 0 && !result.value.nextPageToken && !result.value.incompleteSearch, 'FolderNotVerifiedEmpty');
    }, signal);
  }

  async #update(receipt, name, action, prior, child, expectedParent, newName, signal) {
    const id = crypto.randomUUID();
    return await this.#step(receipt, name, async () => {
      const current = await this.#read(receipt, child, prior.parents[0], prior.name, prior.trashed,
        prior.appProperties?.nineOperation ?? null, signal);
      requireEvidence(current.version === prior.version, 'ProbeFolderChangedOutsideTest');
      const metadata = this.#store.mapFile(current, child.id);
      const destination = action === 'Move' && expectedParent === receipt.folders.parent.googleId
        ? wrap(receipt.folders.parent.id) : null;
      const result = await this.#provider.mutate({
        operation: {
          id: wrap(id), itemId: wrap(child.id), actorId: this.#store.ownerId, operation: action, state: 'Pending',
          sourceParentId: metadata.parentId, destinationParentId: destination,
          baseRevisionId: metadata.currentRevisionId, payload: newName ? { newName } : null,
        },
        newName,
      }, signal);
      if (result.error?.code === 'ProviderUnavailable' && !result.error.details?.notStarted) {
        throw new Error('Provider did not confirm the probe update.');
      }
      requireEvidence(result.value?.state === 'Committed' && result.value.id?.value === id, result.error?.code ?? 'UpdateUnverified');
      const file = await this.#read(receipt, child, expectedParent, newName ?? prior.name, action === 'Delete', id, signal);
      requireEvidence(BigInt(file.version) > BigInt(prior.version) &&
        this.#store.byId(child.id)?.google_version === file.version, 'UpdateRevisionUnverified');
      return file;
    }, signal, id);
  }
}
