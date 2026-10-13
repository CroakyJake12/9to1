// Release support gate, not runtime acceptance. Reads only an explicit public
// bundle and SDK-produced build metadata; never scans account/profile storage.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const zlib = require('node:zlib');
const assert = require('node:assert/strict');

const sha256 = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const MAX_EXPANDED_FILE = 256 * 1024 * 1024;
const MAX_EXPANDED_TOTAL = 1024 * 1024 * 1024;
// Fixture ownership identifiers, not model names or arbitrary user vocabulary.
const forbidden = [
  ['fixture assembly', 'BrowserSpaces.Visual'],
  ['fixture backend', 'RestaurantVisualBackend'],
  ['fixture input type', 'RestaurantVisualInput'],
  ['fixture data file', 'restaurant.fixture.json'],
  ['fixture identity namespace', '9to1-visual-only-'],
  ['fixture provenance', 'Visual-only deterministic data, transcribed from the owner'],
  ['fixture model provenance', 'Visual-only reference model metadata']
];
// A pair of these reference-content fields in shipped code/data also detects a
// renamed or copied seed. Model/speaker names are deliberately not signatures.
const referenceFields = [
  ['restaurant title', 'Nearby Restaurants'],
  ['restaurant prompt', 'Can you look for nearby restaurants?'],
  ['restaurant response', 'Here are the top 5 restaurants that you should try!'],
  ['example composer', 'Which one should I pick?'],
  ['example recent chat', 'Diet Plans for 2026'],
  ['example recent chat two', 'Track Sales Tax'],
  ['example tool observation', 'Searching for restaurants'],
  ['example tool observation two', 'Creating a Shortlist']
];
function encodings(text) {
  const little = Buffer.from(text, 'utf16le');
  return [Buffer.from(text, 'utf8'), little, Buffer.from(little).swap16()];
}
const signatures = [...forbidden.map(([label, value]) => ({ label, bytes: encodings(value), forbidden: true })),
  ...referenceFields.map(([label, value]) => ({ label, bytes: encodings(value), forbidden: false }))];
const forbiddenPath = value => /(?:^|\/)(?:tests?|fixture-data)(?:\/|$)/i.test(value.replaceAll('\\', '/'))
  || /(?:^|[/\\])[^/\\]*\.(?:tests?)\.(?:dll|wasm|csproj)(?:\.|$)/i.test(value)
  || forbidden.some(([, marker]) => value.includes(marker));
function readRegular(file) {
  assert(fs.lstatSync(file).isFile() && !fs.lstatSync(file).isSymbolicLink(), `Regular file required: ${file}`);
  return fs.readFileSync(file);
}
function publicFiles(root, prefix = '') {
  assert(fs.lstatSync(root).isDirectory() && !fs.lstatSync(root).isSymbolicLink(), 'Real public directory required');
  const rows = [];
  for (const entry of fs.readdirSync(root, { withFileTypes: true })) {
    const full = path.join(root, entry.name), relative = prefix ? prefix + '/' + entry.name : entry.name;
    assert(!entry.isSymbolicLink(), `Public links are not permitted: ${relative}`);
    if (entry.isDirectory()) rows.push(...publicFiles(full, relative));
    else { assert(entry.isFile(), `Unsupported public entry: ${relative}`); rows.push({ full, relative }); }
  }
  return rows.sort((a, b) => a.relative < b.relative ? -1 : a.relative > b.relative ? 1 : 0);
}
function verifyProductionAssets(bundlePath) {
  const root = path.resolve(bundlePath), rows = publicFiles(root);
  assert(rows.length > 0, 'An empty directory is not a production bundle');
  const inventory = [], referenceHits = new Map();
  let compressedFiles = 0, expandedBytes = 0;
  for (const { full, relative } of rows) {
    assert(!forbiddenPath(relative), `Production asset includes isolated test/visual ownership: ${relative}`);
    const original = readRegular(full);
    assert(original.length <= MAX_EXPANDED_FILE, `Public asset exceeds scan budget: ${relative}`);
    inventory.push({ path: relative, bytes: original.length, sha256: sha256(original) });
    const inspect = bytes => {
      expandedBytes += bytes.length;
      assert(expandedBytes <= MAX_EXPANDED_TOTAL, 'Production asset scan exceeds expanded budget');
      for (const signature of signatures) {
        if (!signature.bytes.some(value => bytes.includes(value))) continue;
        assert(!signature.forbidden, `Production asset contains ${signature.label}: ${relative}`);
        if (!referenceHits.has(signature.label)) referenceHits.set(signature.label, relative);
      }
      assert(referenceHits.size < 2, `Production assets contain combined visual reference data: ${[...referenceHits.keys()].join(', ')}`);
    };
    inspect(original); // Includes embedded UTF-8 and CLR UTF-16 user strings.
    const gzip = /\.gz$/i.test(relative) || original[0] === 0x1f && original[1] === 0x8b;
    const brotli = /\.br$/i.test(relative);
    if (gzip || brotli) {
      let expanded;
      try { expanded = gzip ? zlib.gunzipSync(original, { maxOutputLength: MAX_EXPANDED_FILE })
        : zlib.brotliDecompressSync(original, { maxOutputLength: MAX_EXPANDED_FILE }); }
      catch (error) { throw new Error(`Cannot verify compressed production asset: ${relative}`, { cause: error }); }
      compressedFiles++; inspect(expanded);
    }
  }
  return { scope: 'Known visual fixture isolation in explicit public artifacts; not proof of all data provenance or runtime acceptance',
    bundlePath: root, assetFiles: rows.length, compressedFiles, expandedBytes,
    inventorySha256: sha256(Buffer.from(JSON.stringify(inventory))),
    referenceFieldsObserved: referenceHits.size, fixtureIsolationVerified: true,
    modelNamesRestricted: false, accountStorageRead: false };
}
function verifyProductionProjectClosure(projectAssetsPath, expectedProjectPath, evaluatedItemsPath) {
  const source = readRegular(projectAssetsPath), assets = JSON.parse(source);
  const actualProject = path.resolve(assets.project?.restore?.projectPath ?? '');
  assert.equal(actualProject, path.resolve(expectedProjectPath), 'SDK assets belong to a different entry project');
  assert([3, 4].includes(assets.version), 'Supported actual SDK assets schema required');
  assert(assets.libraries && Object.keys(assets.libraries).length > 0, 'Actual restored library closure required');
  const projects = Object.entries(assets.libraries).filter(([, value]) => value.type === 'project');
  assert(projects.length > 0, 'Actual transitive project references required');
  const projectDirectory = path.dirname(actualProject);
  const scopedInput = value => path.relative(projectDirectory, path.resolve(projectDirectory, value));
  const paths = new Set();
  for (const [identity, item] of projects) {
    assert(typeof item.path === 'string' && typeof item.msbuildProject === 'string', `Project paths required: ${identity}`);
    assert(!forbiddenPath(scopedInput(item.path)) && !forbiddenPath(scopedInput(item.msbuildProject)) && !forbiddenPath(identity) && !/\.(?:Tests?)(?:\/|$)/i.test(identity),
      `Production project closure includes test/visual project: ${identity}`);
    for (const value of [item.path, item.msbuildProject]) {
      const resolved = path.resolve(path.dirname(actualProject), value);
      readRegular(resolved); paths.add(resolved);
    }
  }
  for (const framework of Object.values(assets.project.restore.frameworks ?? {}))
    for (const [key, item] of Object.entries(framework.projectReferences ?? {})) {
      const reference = path.resolve(item.projectPath ?? key);
      assert(!forbiddenPath(scopedInput(reference)), `Production root references test/visual project: ${reference}`);
      assert(paths.has(reference), `Direct project reference missing from restored closure: ${reference}`);
    }
  let evaluatedInputs = 0;
  if (evaluatedItemsPath) {
    const items = JSON.parse(readRegular(evaluatedItemsPath)).Items;
    for (const name of ['Compile', 'ProjectReference', 'ReferencePath']) {
      assert(Array.isArray(items?.[name]) && items[name].length > 0, `Actual SDK ${name} items required`);
      for (const item of items[name]) {
        const identity = item.FullPath ?? item.Identity;
        assert(typeof identity === 'string' && identity.length > 0, `Actual SDK ${name} identity required`);
        assert(!forbiddenPath(scopedInput(identity)), `Production ${name} includes test/visual input: ${identity}`);
        evaluatedInputs++;
      }
    }
  }
  return { projectPath: actualProject, projectAssetsSha256: sha256(source), projectReferences: projects.length,
    rootCompileAndReferenceItemsChecked: !!evaluatedItemsPath, evaluatedInputs,
    evaluatedItemsSha256: evaluatedItemsPath ? sha256(readRegular(evaluatedItemsPath)) : null,
    projectClosureIsolationVerified: true };
}
module.exports = { verifyProductionAssets, verifyProductionProjectClosure };
if (require.main === module) {
  try {
    assert([3, 5, 6].includes(process.argv.length),
      'Usage: node verify-production-isolation.cjs <wwwroot> [<project.assets.json> <entry.csproj> [<evaluated-build-items.json>]]');
    const result = { assets: verifyProductionAssets(process.argv[2]) };
    if (process.argv[3]) result.projectClosure = verifyProductionProjectClosure(process.argv[3], process.argv[4], process.argv[5]);
    console.log(JSON.stringify(result, null, 2));
  } catch (error) { console.error(error.stack); process.exitCode = 1; }
}
