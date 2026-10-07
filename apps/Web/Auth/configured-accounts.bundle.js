// ../node_modules/jose/dist/webapi/lib/buffer_utils.js
var encoder = new TextEncoder();
var decoder = new TextDecoder();
var strictDecoder = new TextDecoder("utf-8", { fatal: true });
var MAX_INT32 = 2 ** 32;
function concat(...buffers) {
  const size = buffers.reduce((acc, { length }) => acc + length, 0), buf = new Uint8Array(size);
  let i = 0;
  for (const buffer of buffers)
    buf.set(buffer, i), i += buffer.length;
  return buf;
}
var NON_ASCII = /[^\x00-\x7f]/;
function encode(string) {
  if (typeof string == "string" && string.length >= 128) {
    if (NON_ASCII.test(string))
      throw new TypeError("non-ASCII string encountered in encode()");
    return encoder.encode(string);
  }
  const bytes = new Uint8Array(string.length);
  for (let i = 0; i < string.length; i++) {
    const code = string.charCodeAt(i);
    if (code > 127)
      throw new TypeError("non-ASCII string encountered in encode()");
    bytes[i] = code;
  }
  return bytes;
}
function decodeBase64(encoded, url = false) {
  if (Uint8Array.fromBase64)
    return Uint8Array.fromBase64(encoded, { alphabet: url ? "base64url" : "base64" });
  if (url) {
    if (encoded.includes("+") || encoded.includes("/"))
      throw new TypeError("Invalid base64url");
    encoded = encoded.replace(/-/g, "+").replace(/_/g, "/");
  }
  const binary = atob(encoded), bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++)
    bytes[i] = binary.charCodeAt(i);
  return bytes;
}

// ../node_modules/jose/dist/webapi/util/errors.js
var JOSEError = class extends Error {
  static code = "ERR_JOSE_GENERIC";
  code = "ERR_JOSE_GENERIC";
  constructor(message2, options) {
    super(message2, options), this.name = this.constructor.name, Error.captureStackTrace?.(this, this.constructor);
  }
};
var JWTClaimValidationFailed = class extends JOSEError {
  static code = "ERR_JWT_CLAIM_VALIDATION_FAILED";
  code = "ERR_JWT_CLAIM_VALIDATION_FAILED";
  claim;
  reason;
  payload;
  constructor(message2, payload, claim = "unspecified", reason = "unspecified") {
    super(message2, { cause: { claim, reason, payload } }), this.claim = claim, this.reason = reason, this.payload = payload;
  }
};
var JWTExpired = class extends JOSEError {
  static code = "ERR_JWT_EXPIRED";
  code = "ERR_JWT_EXPIRED";
  claim;
  reason;
  payload;
  constructor(message2, payload, claim = "unspecified", reason = "unspecified") {
    super(message2, { cause: { claim, reason, payload } }), this.claim = claim, this.reason = reason, this.payload = payload;
  }
};
var JOSEAlgNotAllowed = class extends JOSEError {
  static code = "ERR_JOSE_ALG_NOT_ALLOWED";
  code = "ERR_JOSE_ALG_NOT_ALLOWED";
};
var JOSENotSupported = class extends JOSEError {
  static code = "ERR_JOSE_NOT_SUPPORTED";
  code = "ERR_JOSE_NOT_SUPPORTED";
};
var JWSInvalid = class extends JOSEError {
  static code = "ERR_JWS_INVALID";
  code = "ERR_JWS_INVALID";
};
var JWTInvalid = class extends JOSEError {
  static code = "ERR_JWT_INVALID";
  code = "ERR_JWT_INVALID";
};
var JWKSInvalid = class extends JOSEError {
  static code = "ERR_JWKS_INVALID";
  code = "ERR_JWKS_INVALID";
};
var JWKSNoMatchingKey = class extends JOSEError {
  static code = "ERR_JWKS_NO_MATCHING_KEY";
  code = "ERR_JWKS_NO_MATCHING_KEY";
  constructor(message2 = "no applicable key found in the JSON Web Key Set", options) {
    super(message2, options);
  }
};
var JWKSMultipleMatchingKeys = class extends JOSEError {
  [Symbol.asyncIterator] = async function* () {
  };
  static code = "ERR_JWKS_MULTIPLE_MATCHING_KEYS";
  code = "ERR_JWKS_MULTIPLE_MATCHING_KEYS";
  constructor(message2 = "multiple matching keys found in the JSON Web Key Set", options) {
    super(message2, options);
  }
};
var JWKSTimeout = class extends JOSEError {
  static code = "ERR_JWKS_TIMEOUT";
  code = "ERR_JWKS_TIMEOUT";
  constructor(message2 = "request timed out", options) {
    super(message2, options);
  }
};
var JWSSignatureVerificationFailed = class extends JOSEError {
  static code = "ERR_JWS_SIGNATURE_VERIFICATION_FAILED";
  code = "ERR_JWS_SIGNATURE_VERIFICATION_FAILED";
  constructor(message2 = "signature verification failed", options) {
    super(message2, options);
  }
};

// ../node_modules/jose/dist/webapi/util/base64url.js
var invalid = "The input to be decoded is not correctly encoded.";
function decode(input) {
  try {
    return decodeBase64(typeof input == "string" ? input : decoder.decode(input), true);
  } catch (cause) {
    throw new TypeError(invalid, { cause });
  }
}

// ../node_modules/jose/dist/webapi/lib/validate.js
function isObject(input) {
  if (typeof input != "object" || input === null || Object.prototype.toString.call(input) !== "[object Object]")
    return false;
  const prototype = Object.getPrototypeOf(input);
  return prototype === null || Object.getPrototypeOf(prototype) === null;
}
function isJwkSet(input) {
  return isObject(input) && Array.isArray(input.keys) && Array.from(input.keys).every(isObject);
}
function isDisjoint(...headers) {
  const parameters = /* @__PURE__ */ new Set();
  for (const header of headers)
    if (header)
      for (const parameter of Object.keys(header)) {
        if (parameters.has(parameter))
          return false;
        parameters.add(parameter);
      }
  return true;
}
function decodeBase64url(value, label, ErrorClass) {
  try {
    return decode(value);
  } catch {
    throw new ErrorClass(`Failed to base64url decode the ${label}`);
  }
}
function encodeBase64url(value, label, ErrorClass) {
  try {
    return encode(value);
  } catch {
    throw new ErrorClass(`The ${label} is not a valid base64url string`);
  }
}
function parseJoseHeader(b64, ErrorClass, message2) {
  let parsed;
  try {
    parsed = JSON.parse(strictDecoder.decode(decode(b64)));
  } catch {
    throw new ErrorClass(message2);
  }
  if (!isObject(parsed))
    throw new ErrorClass(message2);
  return parsed;
}
var JWS_RECOGNIZED = { __proto__: null, b64: true };
function validateAlgorithms(option, algorithms) {
  if (algorithms !== void 0 && (!Array.isArray(algorithms) || algorithms.some((s) => typeof s != "string")))
    throw new TypeError(`"${option}" option must be an array of strings`);
  return algorithms === void 0 ? void 0 : new Set(algorithms);
}
function validateCrit(Err, recognizedDefault, recognizedOption, protectedHeader, joseHeader) {
  if (joseHeader.crit !== void 0 && protectedHeader?.crit === void 0)
    throw new Err('"crit" (Critical) Header Parameter MUST be integrity protected');
  if (!protectedHeader || protectedHeader.crit === void 0)
    return [];
  if (!Array.isArray(protectedHeader.crit) || protectedHeader.crit.length === 0 || protectedHeader.crit.some((input) => typeof input != "string" || input.length === 0))
    throw new Err('"crit" (Critical) Header Parameter MUST be an array of non-empty strings when present');
  const recognized = recognizedOption === void 0 ? recognizedDefault : { __proto__: null, ...recognizedOption, ...recognizedDefault };
  for (const parameter of protectedHeader.crit) {
    if (!(parameter in recognized))
      throw new JOSENotSupported(`Extension Header Parameter "${parameter}" is not recognized`);
    if (!Object.hasOwn(joseHeader, parameter) || joseHeader[parameter] === void 0)
      throw new Err(`Extension Header Parameter "${parameter}" is missing`);
    if (recognized[parameter] && (!Object.hasOwn(protectedHeader, parameter) || protectedHeader[parameter] === void 0))
      throw new Err(`Extension Header Parameter "${parameter}" MUST be integrity protected`);
  }
  return protectedHeader.crit;
}
function validateB64(protectedHeader, extensions) {
  if (extensions.includes("b64")) {
    const b64 = protectedHeader.b64;
    if (typeof b64 != "boolean")
      throw new JWSInvalid('The "b64" (base64url-encode payload) Header Parameter must be a boolean');
    return b64;
  }
  return true;
}

// ../node_modules/jose/dist/webapi/lib/key.js
var tag = (key) => key[Symbol.toStringTag];
var jwkMatchesOp = (entry, key, usage) => {
  const { alg } = entry;
  if (key.use !== void 0) {
    const expected = usage === "sign" || usage === "verify" ? "sig" : "enc";
    if (key.use !== expected)
      throw new TypeError(`Invalid key for this operation, its "use" must be "${expected}" when present`);
  }
  if (key.alg !== void 0 && key.alg !== alg)
    throw new TypeError(`Invalid key for this operation, its "alg" must be "${alg}" when present`);
  if (Array.isArray(key.key_ops)) {
    const expectedKeyOp = usage === "encrypt" || usage === "decrypt" ? entry.ops?.[usage === "encrypt" ? 0 : 1] : usage;
    if (expectedKeyOp && !key.key_ops.includes(expectedKeyOp))
      throw new TypeError(`Invalid key for this operation, its "key_ops" must include "${expectedKeyOp}" when present`);
  }
};
async function prepareKey(entry, key, usage) {
  const { alg, secret } = entry, privateKey = usage === "decrypt" || usage === "sign";
  if (secret && key instanceof Uint8Array)
    return key;
  let normalized, keyObject;
  if (isObject(key)) {
    if (normalized = normalizeJwk(key), typeof normalized.kty != "string")
      throw invalidKeyType(alg, key, secret);
    if (!(secret ? normalized.kty === "oct" && typeof normalized.k == "string" : normalized.kty !== "oct" && (privateKey ? normalized.kty === "AKP" && typeof normalized.priv == "string" || typeof normalized.d == "string" : normalized.d === void 0 && normalized.priv === void 0)))
      throw new TypeError(secret ? 'JSON Web Key for symmetric algorithms must have JWK "kty" (Key Type) equal to "oct" and the JWK "k" (Key Value) present' : `JSON Web Key for this operation must be a ${privateKey ? "private" : "public"} JWK`);
    if (jwkMatchesOp(entry, normalized, usage), normalized.kty === "oct")
      return decode(normalized.k);
    if (!Object.isFrozen(key)) {
      const { key_ops } = key;
      Array.isArray(key_ops) && Object.freeze(key_ops), Object.freeze(key);
    }
  } else {
    if (!isKeyLike(key))
      throw invalidKeyType(alg, key, secret);
    const expectedType = secret ? "secret" : privateKey ? "private" : "public";
    if (key.type !== expectedType && (secret || ["secret", "public", "private"].includes(key.type)))
      throw new TypeError(`${tag(key)} instances must be of type "${expectedType}" for the ${alg} algorithm`);
    if (isCryptoKey(key))
      return key;
    if (keyObject = key, keyObject.type === "secret")
      return keyObject.export();
  }
  cache ||= /* @__PURE__ */ new WeakMap();
  const cacheKey = key;
  let cached = cache.get(cacheKey);
  if (cached?.[alg])
    return cached[alg];
  if (cached || cache.set(cacheKey, cached = {}), keyObject && typeof keyObject.toCryptoKey == "function") {
    const isPublic = keyObject.type === "public", crv = nist[keyObject.asymmetricKeyDetails?.namedCurve], params = entry.resolve?.({ crv, asymmetricKeyType: keyObject.asymmetricKeyType }) ?? entry.subtle;
    return cached[alg] = keyObject.toCryptoKey(params, isPublic, entry.usages[isPublic ? 0 : 1]);
  }
  return normalized ??= keyObject.export({ format: "jwk" }), normalized.alg = alg, cached[alg] = await jwkToKey(entry, normalized);
}
var cache;
var nist = {
  __proto__: null,
  prime256v1: "P-256",
  secp384r1: "P-384",
  secp521r1: "P-521"
};
var isCryptoKey = (key) => {
  if (key?.[Symbol.toStringTag] === "CryptoKey")
    return true;
  try {
    return key instanceof CryptoKey;
  } catch {
    return false;
  }
};
var isKeyObject = (key) => key?.[Symbol.toStringTag] === "KeyObject";
var isKeyLike = (key) => isCryptoKey(key) || isKeyObject(key);
function message(msg, actual, ...types) {
  if (types.length > 2) {
    const last = types.pop();
    msg += `one of type ${types.join(", ")}, or ${last}.`;
  } else types.length === 2 ? msg += `one of type ${types[0]} or ${types[1]}.` : msg += `of type ${types[0]}.`;
  return actual == null ? msg += ` Received ${actual}` : typeof actual == "function" && actual.name ? msg += ` Received function ${actual.name}` : typeof actual == "object" && actual != null && actual.constructor?.name && (msg += ` Received an instance of ${actual.constructor.name}`), msg;
}
function invalidKeyType(alg, actual, secret) {
  const types = ["CryptoKey", "KeyObject", "JSON Web Key"];
  return secret && types.push("Uint8Array"), new TypeError(message(`Key for the ${alg} algorithm must be `, actual, ...types));
}
var unusable = (name, prop = "algorithm.name") => new TypeError(`CryptoKey does not support this operation, its ${prop} must be ${name}`);
function checkUsage(key, usage) {
  if (usage && !key.usages.includes(usage))
    throw new TypeError(`CryptoKey does not support this operation, its usages must include ${usage}.`);
}
function checkModulusLength(alg, key) {
  const { modulusLength } = key.algorithm;
  if (typeof modulusLength != "number" || modulusLength < 2048)
    throw new TypeError(`${alg} requires key modulusLength to be 2048 bits or larger`);
}
function checkCryptoKey(key, expected, usage) {
  const algorithm = key.algorithm;
  if (algorithm.name !== expected.name)
    throw unusable(expected.name);
  if (expected.hash && algorithm.hash?.name !== expected.hash)
    throw unusable(expected.hash, "algorithm.hash");
  if (expected.namedCurve && algorithm.namedCurve !== expected.namedCurve)
    throw unusable(expected.namedCurve, "algorithm.namedCurve");
  if (expected.length !== void 0 && algorithm.length !== expected.length)
    throw unusable(expected.length, "algorithm.length");
  checkUsage(key, usage);
}
function snapshotJwk(jwk) {
  return { __proto__: null, ...jwk };
}
function normalizeJwk(jwk) {
  const normalized = snapshotJwk(jwk);
  if (normalized.ext !== void 0 && typeof normalized.ext != "boolean")
    throw new TypeError('"ext" (Extractable) Parameter must be a boolean');
  if (normalized.key_ops !== void 0) {
    const value = normalized.key_ops, keyOps = Array.isArray(value) ? [...value] : void 0;
    if (!keyOps || keyOps.some((operation) => typeof operation != "string") || new Set(keyOps).size !== keyOps.length)
      throw new TypeError('"key_ops" (Key Operations) Parameter must be an array of unique strings');
    normalized.key_ops = keyOps;
  }
  return normalized;
}
async function jwkToKey(entry, jwk, extractable) {
  if (!entry.kty.includes(jwk.kty))
    throw new JOSENotSupported('Invalid or unsupported JWK "alg" (Algorithm) Parameter value');
  const algorithm = entry.resolve?.({ kty: jwk.kty, crv: jwk.crv }) ?? entry.subtle, isPrivate = !!(jwk.d || jwk.priv), keyData = { ...jwk, ext: extractable ?? jwk.ext };
  return keyData.kty !== "AKP" && delete keyData.alg, delete keyData.use, crypto.subtle.importKey("jwk", keyData, algorithm, keyData.ext ?? !isPrivate, jwk.key_ops ?? entry.usages[isPrivate ? 1 : 0]);
}
async function rawKey(key, expected, usage, extractable = false) {
  return key instanceof Uint8Array && (key = await crypto.subtle.importKey("raw", key, expected, extractable, [usage])), checkCryptoKey(key, expected, usage), key;
}

// ../node_modules/jose/dist/webapi/lib/key_descriptor.js
function table(entries) {
  const out = { __proto__: null };
  for (const alg in entries)
    out[alg] = { ...entries[alg], alg };
  return out;
}

// ../node_modules/jose/dist/webapi/lib/jws_algorithms.js
var sig = [["verify"], ["sign"]];
function hmac(bits) {
  const subtle = { name: "HMAC", hash: `SHA-${bits}` };
  return { kty: ["oct"], secret: true, subtle, signing: subtle, usages: sig };
}
function rsa(bits, saltLength) {
  const subtle = { name: saltLength ? "RSA-PSS" : "RSASSA-PKCS1-v1_5", hash: `SHA-${bits}` };
  return {
    kty: ["RSA"],
    subtle,
    signing: saltLength ? { ...subtle, saltLength } : subtle,
    usages: sig,
    minRsaBits: 2048
  };
}
function ecdsa(crv, bits) {
  return {
    kty: ["EC"],
    crv,
    subtle: { name: "ECDSA", namedCurve: crv },
    signing: { name: "ECDSA", hash: `SHA-${bits}` },
    usages: sig
  };
}
function eddsa() {
  const subtle = { name: "Ed25519" };
  return {
    kty: ["OKP"],
    crv: "Ed25519",
    subtle,
    signing: subtle,
    usages: sig
  };
}
function mldsa(bits) {
  const subtle = { name: `ML-DSA-${bits}` };
  return {
    kty: ["AKP"],
    subtle,
    signing: subtle,
    usages: sig
  };
}
var JWS = table({
  HS256: hmac(256),
  HS384: hmac(384),
  HS512: hmac(512),
  RS256: rsa(256),
  RS384: rsa(384),
  RS512: rsa(512),
  PS256: rsa(256, 32),
  PS384: rsa(384, 48),
  PS512: rsa(512, 64),
  ES256: ecdsa("P-256", 256),
  ES384: ecdsa("P-384", 384),
  ES512: ecdsa("P-521", 512),
  EdDSA: eddsa(),
  Ed25519: eddsa(),
  "ML-DSA-44": mldsa(44),
  "ML-DSA-65": mldsa(65),
  "ML-DSA-87": mldsa(87)
});
function jwsAlgorithm(alg) {
  const entry = typeof alg == "string" ? JWS[alg] : void 0;
  if (!entry)
    throw new JOSENotSupported(`alg ${alg} is not supported either by JOSE or your javascript runtime`);
  return entry;
}

// ../node_modules/jose/dist/webapi/lib/jws_verify.js
function prepareVerify(options) {
  return [options && validateAlgorithms("algorithms", options.algorithms), options?.crit];
}
function parseProtectedHeader(encodedProtected) {
  return encodedProtected === void 0 ? {} : parseJoseHeader(encodedProtected, JWSInvalid, "JWS Protected Header is invalid");
}
function encodeCompactUnencodedPayload(payload) {
  try {
    return encode(payload);
  } catch {
    throw new JWSInvalid("JWS Compact Serialization payload must use only ASCII characters");
  }
}
async function verifySignature(jws, shared, key, encodeUnencodedPayload, parsedProtected) {
  const { protected: encodedProtected, header, payload: inputPayload } = jws, parsedProt = parsedProtected ?? parseProtectedHeader(encodedProtected);
  if (!isDisjoint(parsedProt, header))
    throw new JWSInvalid("JWS Protected and JWS Unprotected Header Parameter names must be disjoint");
  const joseHeader = { ...parsedProt, ...header }, b64 = validateB64(parsedProt, validateCrit(JWSInvalid, JWS_RECOGNIZED, shared[1], parsedProt, joseHeader)), { alg } = joseHeader;
  if (typeof alg != "string" || !alg)
    throw new JWSInvalid('JWS "alg" (Algorithm) Header Parameter missing or invalid');
  if (shared[0] && !shared[0].has(alg))
    throw new JOSEAlgNotAllowed('"alg" (Algorithm) Header Parameter value not allowed');
  if (b64) {
    if (typeof inputPayload != "string")
      throw new JWSInvalid("JWS Payload must be a string");
  } else if (typeof inputPayload != "string" && !(inputPayload instanceof Uint8Array))
    throw new JWSInvalid("JWS Payload must be a string or an Uint8Array instance");
  const signingPayload = b64 || typeof inputPayload != "string" ? inputPayload : encodeUnencodedPayload(inputPayload);
  let resolvedKey = false;
  typeof key == "function" && (key = await key(parsedProt, jws), resolvedKey = true);
  const entry = jwsAlgorithm(alg), data = concat(encodedProtected !== void 0 ? encode(encodedProtected) : new Uint8Array(), encode("."), typeof signingPayload == "string" ? shared[2] ??= encodeBase64url(signingPayload, "payload", JWSInvalid) : signingPayload), signature = decodeBase64url(jws.signature, "signature", JWSInvalid), k = await prepareKey(entry, key, "verify"), cryptoKey = await rawKey(k, entry.subtle, "verify");
  entry.minRsaBits && checkModulusLength(entry.alg, cryptoKey);
  let verified = false;
  try {
    verified = await crypto.subtle.verify(entry.signing, cryptoKey, signature, data);
  } catch {
  }
  if (!verified)
    throw new JWSSignatureVerificationFailed();
  const result = { payload: typeof signingPayload == "string" ? decodeBase64url(signingPayload, "payload", JWSInvalid) : signingPayload };
  return encodedProtected !== void 0 && (result.protectedHeader = parsedProt), header !== void 0 && (result.unprotectedHeader = header), resolvedKey ? [{ ...result, key: k }, b64] : [result, b64];
}
async function verifyCompact(jws, shared, key) {
  if (jws instanceof Uint8Array && (jws = decoder.decode(jws)), typeof jws != "string")
    throw new JWSInvalid("Compact JWS must be a string or Uint8Array");
  const { 0: protectedHeader, 1: payload, 2: signature, length } = jws.split(".");
  if (length !== 3)
    throw new JWSInvalid("Invalid Compact JWS");
  return verifySignature({ payload, protected: protectedHeader, signature }, shared, key, encodeCompactUnencodedPayload);
}

// ../node_modules/jose/dist/webapi/lib/jwt_claims_set.js
var epoch = (date2) => Math.floor(date2.getTime() / 1e3);
var multipliers = {
  s: 1,
  m: 60,
  h: 3600,
  d: 86400,
  w: 604800,
  y: 31557600
};
var REGEX = /^(\+|\-)? ?(\d+|\d+\.\d+) ?(seconds?|secs?|s|minutes?|mins?|m|hours?|hrs?|h|days?|d|weeks?|w|years?|yrs?|y)(?: (ago|from now))?$/i;
var checkFailed = "check_failed";
function invalidDuration() {
  throw new TypeError("Invalid time period format");
}
function secs(str) {
  typeof str != "string" && invalidDuration();
  const matched = REGEX.exec(str);
  (!matched || matched[4] && matched[1]) && invalidDuration();
  const value = parseFloat(matched[2]), numericDate2 = Math.round(value * multipliers[matched[3][0].toLowerCase()]);
  return Number.isFinite(numericDate2) || invalidDuration(), matched[1] === "-" || matched[4] === "ago" ? -numericDate2 : numericDate2;
}
function validateInput(label, input) {
  if (!Number.isFinite(input))
    throw new TypeError(`Invalid ${label} input`);
  return input;
}
var normalizeTyp = (value) => {
  const normalized = value.toLowerCase();
  return value.includes("/") ? normalized : `application/${normalized}`;
};
var checkAudiencePresence = (audPayload, audOption) => typeof audPayload == "string" ? audOption.includes(audPayload) : Array.isArray(audPayload) ? audOption.some((aud) => audPayload.includes(aud)) : false;
function validateNumericDate(payload, claim, required = false) {
  const value = payload[claim];
  if (!(value === void 0 && !required)) {
    if (typeof value != "number")
      throw new JWTClaimValidationFailed(`"${claim}" claim must be a number`, payload, claim, "invalid");
    return value;
  }
}
function unexpectedClaim(payload, claim) {
  throw new JWTClaimValidationFailed(`unexpected "${claim}" claim value`, payload, claim, checkFailed);
}
function validateClaimsSet(protectedHeader, encodedPayload, options = {}) {
  let payload;
  try {
    payload = JSON.parse(strictDecoder.decode(encodedPayload));
  } catch {
  }
  if (!isObject(payload))
    throw new JWTInvalid("JWT Claims Set must be a top-level JSON object");
  const { typ } = options;
  if (typ !== void 0 && (typeof protectedHeader.typ != "string" || normalizeTyp(protectedHeader.typ) !== normalizeTyp(typ)))
    throw new JWTClaimValidationFailed('unexpected "typ" JWT header value', payload, "typ", checkFailed);
  const { requiredClaims = [], issuer, subject, audience, maxTokenAge } = options, presenceCheck = [...requiredClaims];
  maxTokenAge !== void 0 && presenceCheck.push("iat"), audience !== void 0 && presenceCheck.push("aud"), subject !== void 0 && presenceCheck.push("sub"), issuer !== void 0 && presenceCheck.push("iss");
  for (const claim of new Set(presenceCheck.reverse()))
    if (!Object.hasOwn(payload, claim))
      throw new JWTClaimValidationFailed(`missing required "${claim}" claim`, payload, claim, "missing");
  issuer !== void 0 && !(Array.isArray(issuer) ? issuer : [issuer]).includes(payload.iss) && unexpectedClaim(payload, "iss"), subject !== void 0 && payload.sub !== subject && unexpectedClaim(payload, "sub"), audience !== void 0 && !checkAudiencePresence(payload.aud, typeof audience == "string" ? [audience] : audience) && unexpectedClaim(payload, "aud");
  const { clockTolerance } = options;
  let tolerance = 0;
  if (typeof clockTolerance == "string")
    tolerance = secs(clockTolerance);
  else if (clockTolerance !== void 0) {
    if (typeof clockTolerance != "number")
      throw new TypeError("Invalid clockTolerance option type");
    tolerance = clockTolerance;
  }
  validateInput("clockTolerance option", tolerance);
  const { currentDate } = options, now = validateInput("currentDate option", epoch(currentDate === void 0 ? /* @__PURE__ */ new Date() : currentDate)), iat = validateNumericDate(payload, "iat", maxTokenAge !== void 0), nbf = validateNumericDate(payload, "nbf");
  if (nbf !== void 0 && nbf > now + tolerance)
    throw new JWTClaimValidationFailed('"nbf" claim timestamp check failed', payload, "nbf", checkFailed);
  const exp = validateNumericDate(payload, "exp");
  if (exp !== void 0 && exp <= now - tolerance)
    throw new JWTExpired('"exp" claim timestamp check failed', payload, "exp", checkFailed);
  if (maxTokenAge !== void 0) {
    const age = now - iat, max = validateInput("maxTokenAge option", typeof maxTokenAge == "number" ? maxTokenAge : secs(maxTokenAge));
    if (age - tolerance > max)
      throw new JWTExpired('"iat" claim timestamp check failed (too far in the past)', payload, "iat", checkFailed);
    if (age < -tolerance)
      throw new JWTClaimValidationFailed('"iat" claim timestamp check failed (it should be in the past)', payload, "iat", checkFailed);
  }
  return payload;
}

// ../node_modules/jose/dist/webapi/jwt/verify.js
async function jwtVerify(jwt, key, options) {
  const [verified, b64] = await verifyCompact(jwt, prepareVerify(options), key);
  if (!b64)
    throw new JWTInvalid("JWTs MUST NOT use unencoded payload");
  const payload = validateClaimsSet(verified.protectedHeader, verified.payload, options);
  return { ...verified, payload };
}

// ../node_modules/jose/dist/webapi/jwks/local.js
function isUsableJWK(jwk, entry, alg, kid) {
  const { kty, key_ops: keyOps, ext, kid: jwkKid, alg: jwkAlg, use, crv } = jwk;
  return (ext === void 0 || typeof ext == "boolean") && (keyOps === void 0 || Array.isArray(keyOps) && keyOps.every((operation, index) => typeof operation == "string" && keyOps.indexOf(operation) === index) && keyOps.includes("verify")) && entry.kty.includes(kty) && (kid === void 0 || typeof kid == "string" && kid === jwkKid) && (jwkAlg === void 0 ? kty !== "AKP" : alg === jwkAlg) && (use === void 0 || use === "sig") && (!entry.crv || crv === entry.crv);
}
async function importWithAlgCache(cache2, jwk, entry) {
  const cached = cache2.get(jwk) || cache2.set(jwk, {}).get(jwk), { alg } = entry;
  if (cached[alg] === void 0) {
    const pending = jwkToKey(entry, jwk, true).then((key) => {
      if (key.type !== "public")
        throw new JWKSInvalid("JSON Web Key Set members must be public keys");
      return cached[alg] = key, key;
    }).catch((error) => {
      throw cached[alg] === pending && delete cached[alg], error;
    });
    cached[alg] = pending;
  }
  return cached[alg];
}
function createLocalJWKSet(jwks) {
  let snapshot;
  try {
    snapshot = structuredClone(jwks);
  } catch {
  }
  if (!isJwkSet(snapshot))
    throw new JWKSInvalid("JSON Web Key Set malformed");
  const metadata = snapshot.keys.map((jwk) => {
    const normalized = snapshotJwk(jwk);
    return Array.isArray(normalized.key_ops) && (normalized.key_ops = [...normalized.key_ops]), normalized;
  }), cached = /* @__PURE__ */ new WeakMap();
  return Object.defineProperty(async (protectedHeader, token) => {
    const { alg, kid } = { ...protectedHeader, ...token?.header }, entry = typeof alg == "string" ? JWS[alg] : void 0;
    if (!entry || entry.secret)
      throw new JOSENotSupported('Unsupported "alg" value for a JSON Web Key Set');
    const candidates = snapshot.keys.filter((_, index) => isUsableJWK(metadata[index], entry, alg, kid)), { 0: jwk, length } = candidates;
    if (!length)
      throw new JWKSNoMatchingKey();
    if (length !== 1) {
      const error = new JWKSMultipleMatchingKeys();
      throw error[Symbol.asyncIterator] = async function* () {
        for (const jwk2 of candidates)
          try {
            yield await importWithAlgCache(cached, jwk2, entry);
          } catch {
          }
      }, error;
    }
    return importWithAlgCache(cached, jwk, entry);
  }, "jwks", {
    value: () => structuredClone(snapshot)
  });
}

// ../node_modules/jose/dist/webapi/jwks/remote.js
function isCloudflareWorkers() {
  return typeof WebSocketPair < "u" || typeof navigator < "u" && navigator.userAgent === "Cloudflare-Workers" || typeof EdgeRuntime < "u" && EdgeRuntime === "vercel";
}
var USER_AGENT;
(typeof navigator > "u" || !navigator.userAgent?.startsWith?.("Mozilla/5.0 ")) && (USER_AGENT = "jose/v6.2.12");
var customFetch = /* @__PURE__ */ Symbol();
async function fetchJwks(url, headers, signal, fetchImpl = fetch) {
  const response = await fetchImpl(url, {
    method: "GET",
    signal,
    redirect: "manual",
    headers
  }).catch((err) => {
    throw err.name === "TimeoutError" ? new JWKSTimeout() : err;
  });
  if (response.status !== 200)
    throw new JOSEError("Expected 200 OK from the JSON Web Key Set HTTP response");
  try {
    return await response.json();
  } catch {
    throw new JOSEError("Failed to parse the JSON Web Key Set HTTP response as JSON");
  }
}
var jwksCache = /* @__PURE__ */ Symbol();
function isFreshFor(timestamp, duration) {
  return Number.isFinite(timestamp) && Date.now() < timestamp + duration;
}
function validateDuration(value, fallback, option) {
  if (Number.isNaN(value))
    throw new TypeError(`"${option}" option must not be NaN`);
  return typeof value == "number" ? value : fallback;
}
function createRemoteJWKSet(url, options) {
  if (!(url instanceof URL))
    throw new TypeError("url must be an instance of URL");
  const href = new URL(url.href).href, opts = options ?? {}, timeoutOption = opts.timeoutDuration;
  if (typeof timeoutOption == "number" && (!Number.isInteger(timeoutOption) || timeoutOption < 0))
    throw new TypeError('"timeoutDuration" option must be a non-negative integer');
  const timeoutDuration = typeof timeoutOption == "number" ? timeoutOption : 5e3, cooldownDuration = validateDuration(opts.cooldownDuration, 3e4, "cooldownDuration"), cacheMaxAge = validateDuration(opts.cacheMaxAge, 6e5, "cacheMaxAge"), headers = new Headers(opts.headers);
  USER_AGENT && !headers.has("User-Agent") && headers.set("User-Agent", USER_AGENT), headers.has("accept") || headers.set("accept", "application/json, application/jwk-set+json");
  const fetchImpl = opts[customFetch], cache2 = opts[jwksCache];
  let jwksTimestamp, pendingFetch, reloadSequence = 0, appliedSequence = 0, local;
  if (cache2 && typeof cache2 == "object") {
    const { uat, jwks } = cache2;
    isFreshFor(uat, cacheMaxAge) && isJwkSet(jwks) && (jwksTimestamp = uat, local = createLocalJWKSet(jwks));
  }
  const reload = async () => {
    if (pendingFetch && isCloudflareWorkers() && (pendingFetch = void 0), !pendingFetch) {
      const sequence = ++reloadSequence, current = pendingFetch = fetchJwks(href, headers, AbortSignal.timeout(timeoutDuration), fetchImpl).then((json) => {
        const next = createLocalJWKSet(json);
        if (sequence <= appliedSequence)
          return;
        local = next;
        const updatedAt = Date.now();
        cache2 && (cache2.uat = updatedAt, cache2.jwks = json), jwksTimestamp = updatedAt, appliedSequence = sequence;
      }).finally(() => {
        pendingFetch === current && (pendingFetch = void 0);
      });
    }
    await pendingFetch;
  };
  return Object.defineProperties(async (protectedHeader, token) => {
    (!local || !isFreshFor(jwksTimestamp, cacheMaxAge)) && await reload();
    try {
      return await local(protectedHeader, token);
    } catch (err) {
      if (err instanceof JWKSNoMatchingKey && !isFreshFor(jwksTimestamp, cooldownDuration))
        return await reload(), local(protectedHeader, token);
      throw err;
    }
  }, {
    coolingDown: {
      get: () => isFreshFor(jwksTimestamp, cooldownDuration),
      enumerable: true
    },
    fresh: {
      get: () => isFreshFor(jwksTimestamp, cacheMaxAge),
      enumerable: true
    },
    reload: {
      value: reload,
      enumerable: true
    },
    reloading: {
      get: () => !!pendingFetch,
      enumerable: true
    },
    jwks: {
      value: () => local?.jwks(),
      enumerable: true
    }
  });
}

// apps/Web/Auth/browser-public-client.js
var problem = (code) => Object.assign(new Error(code), { code });
var base64url = (bytes) => btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
var uuid = (value) => typeof value === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(value);
var object = (value) => value !== null && typeof value === "object" && !Array.isArray(value);
function endpoint(value, allowLoopback) {
  let url;
  try {
    url = new URL(value);
  } catch {
    throw problem("InvalidConfiguration");
  }
  const isolated = allowLoopback === true && ["127.0.0.1", "localhost", "[::1]"].includes(url.hostname) && url.protocol === "http:";
  if (url.username || url.password || url.search || url.hash || url.protocol !== "https:" && !isolated) throw problem("InvalidConfiguration");
  return url;
}
function validatePublicClientConfiguration(input, origin) {
  if (!object(input)) throw problem("InvalidConfiguration");
  const allowed = /* @__PURE__ */ new Set(["issuer", "apiResource", "clientId", "redirectUri", "scopes", "allowLoopbackForIsolatedTests"]);
  if (Object.keys(input).some((key) => !allowed.has(key))) throw problem("InvalidConfiguration");
  const snapshot = Object.fromEntries(Object.entries(input));
  const issuer = endpoint(snapshot.issuer, snapshot.allowLoopbackForIsolatedTests);
  const api = endpoint(snapshot.apiResource, snapshot.allowLoopbackForIsolatedTests);
  const redirect = endpoint(snapshot.redirectUri, false);
  if (api.pathname !== "/" || redirect.origin !== origin || typeof snapshot.clientId !== "string" || !snapshot.clientId || typeof snapshot.scopes !== "string" || !snapshot.scopes.split(/\s+/).includes("openid") || snapshot.allowLoopbackForIsolatedTests !== void 0 && typeof snapshot.allowLoopbackForIsolatedTests !== "boolean") throw problem("InvalidConfiguration");
  return Object.freeze({
    issuer: issuer.href,
    apiResource: api.origin,
    clientId: snapshot.clientId,
    redirectUri: redirect.href,
    scopes: snapshot.scopes,
    allowLoopbackForIsolatedTests: snapshot.allowLoopbackForIsolatedTests === true
  });
}
function handleOAuthPopupCallback(configuration, win = globalThis.window) {
  if (!configuration || !win) return false;
  const config = validatePublicClientConfiguration(configuration, win.location.origin);
  const expected = new URL(config.redirectUri);
  if (win.location.origin !== expected.origin || win.location.pathname !== expected.pathname) return false;
  let parent;
  try {
    parent = win.opener;
    if (!parent || parent.closed || parent.location.origin !== expected.origin) throw problem("CallbackParentUnavailable");
  } catch {
    throw problem("CallbackParentUnavailable");
  }
  const callbackUri = win.location.href;
  win.history.replaceState(null, "", expected.pathname);
  parent.postMessage({ type: "nineToOne.oauth.callback", callbackUri }, expected.origin);
  return true;
}
var BrowserPublicClient = class {
  #config;
  #window;
  #fetch;
  #crypto;
  #before;
  #verifyAccount;
  #verified;
  #signInFailed;
  #expired;
  #failure;
  #issued = /* @__PURE__ */ new Set();
  #cleanupErrors = [];
  #cleanupTask;
  #beginExpiry;
  #taskIdentity = null;
  #taskProfile;
  #ownerEpoch;
  #issuedTaskIdentities = /* @__PURE__ */ new WeakMap();
  #token = null;
  #expires = 0;
  #timer;
  #pending = null;
  #generation = 0;
  #disposed = false;
  #discovery;
  #jwks;
  constructor({
    configuration,
    window: win = globalThis.window,
    fetch: transport = globalThis.fetch,
    crypto: crypto2 = globalThis.crypto,
    onBeforeSignIn,
    verifyCurrentAccount,
    onVerifiedIdentity,
    onSignInFailed,
    onTokenExpired,
    onFailure,
    beginTokenExpiryInvalidation = void 0,
    readCurrentTaskProfile = void 0
  }) {
    if (!win || typeof transport !== "function" || !crypto2?.subtle || typeof onBeforeSignIn !== "function" || typeof verifyCurrentAccount !== "function" || typeof onVerifiedIdentity !== "function" || typeof onSignInFailed !== "function" || typeof onTokenExpired !== "function" || typeof onFailure !== "function") throw problem("InvalidConfiguration");
    if (beginTokenExpiryInvalidation !== void 0 && typeof beginTokenExpiryInvalidation !== "function") throw problem("InvalidConfiguration");
    if (readCurrentTaskProfile !== void 0 && typeof readCurrentTaskProfile !== "function") throw problem("InvalidConfiguration");
    this.#taskProfile = readCurrentTaskProfile;
    this.#ownerEpoch = base64url(crypto2.getRandomValues(new Uint8Array(32)));
    this.#beginExpiry = beginTokenExpiryInvalidation;
    this.#config = validatePublicClientConfiguration(configuration, win.location.origin);
    this.#window = win;
    this.#fetch = transport === globalThis.fetch ? transport.bind(globalThis) : transport;
    this.#crypto = crypto2;
    this.#before = onBeforeSignIn;
    this.#verifyAccount = verifyCurrentAccount;
    this.#verified = onVerifiedIdentity;
    this.#signInFailed = onSignInFailed;
    this.#expired = onTokenExpired;
    this.#failure = onFailure;
  }
  get configuration() {
    return this.#config;
  }
  // Public configuration only.
  getAccessToken() {
    return !this.#disposed && this.#token && Date.now() < this.#expires ? this.#token : null;
  }
  clearToken({ cancelPending = true } = {}) {
    this.#generation++;
    this.#taskIdentity = null;
    this.#token = null;
    this.#expires = 0;
    clearTimeout(this.#timer);
    if (cancelPending) this.#pending?.controller.abort();
  }
  readCurrentTaskIdentity({ signal } = {}) {
    return this.#issue("task-identity", async () => {
      const original = this.#taskIdentity;
      if (!original || !this.#taskProfile || !this.#taskIdentityCurrent(original)) return null;
      if (signal?.aborted) throw problem("Cancelled");
      const profile2 = await this.#taskProfile(original.accountId, signal);
      if (signal?.aborted) throw problem("Cancelled");
      if (!this.#taskIdentityCurrent(original)) throw problem("SessionContextChanged");
      if (!object(profile2) || profile2.accountId !== original.accountId || !Number.isSafeInteger(profile2.revision) || profile2.revision <= 0) throw problem("TaskProfileVerificationFailed");
      const observed = Object.freeze({ ...original, profileAccountId: profile2.accountId, profileRevision: profile2.revision });
      this.#issuedTaskIdentities.set(observed, original);
      return observed;
    });
  }
  isOriginalTaskIdentityCurrent(observed) {
    const original = object(observed) ? this.#issuedTaskIdentities.get(observed) : null;
    return original !== void 0 && original !== null && this.#taskIdentityCurrent(original);
  }
  #taskIdentityCurrent(original) {
    return !this.#disposed && original === this.#taskIdentity && original.generation === this.#generation && this.#token !== null && Date.now() < this.#expires && Date.now() < original.expiresAt * 1e3;
  }
  async #json(url, options, signal) {
    const response = await this.#fetch(url, { ...options, credentials: "omit", cache: "no-store", redirect: "error", referrerPolicy: "no-referrer", signal });
    if (signal.aborted) throw problem("Cancelled");
    if (!response.ok || response.redirected || response.url && response.url !== url) throw problem("ProviderRequestFailed");
    let body;
    try {
      body = await response.json();
    } catch {
      throw problem(signal.aborted ? "Cancelled" : "MalformedProviderResponse");
    }
    if (signal.aborted) throw problem("Cancelled");
    if (!object(body)) throw problem("MalformedProviderResponse");
    return body;
  }
  async #metadata(signal) {
    if (this.#discovery) return this.#discovery;
    const issuer = new URL(this.#config.issuer);
    const discovery = await this.#json(`${issuer.href.replace(/\/$/, "")}/.well-known/openid-configuration`, {}, signal);
    if (discovery.issuer !== this.#config.issuer) throw problem("IssuerMismatch");
    for (const field of ["authorization_endpoint", "token_endpoint", "jwks_uri"]) {
      const url = endpoint(discovery[field], this.#config.allowLoopbackForIsolatedTests);
      if (url.origin !== issuer.origin) throw problem("UnexpectedProviderOrigin");
    }
    this.#discovery = Object.freeze({
      issuer: discovery.issuer,
      authorization_endpoint: discovery.authorization_endpoint,
      token_endpoint: discovery.token_endpoint,
      jwks_uri: discovery.jwks_uri
    });
    this.#jwks = createRemoteJWKSet(new URL(discovery.jwks_uri), { [customFetch]: (url, options) => this.#fetch(url, {
      ...options,
      credentials: "omit",
      cache: "no-store",
      redirect: "error",
      referrerPolicy: "no-referrer"
    }) });
    return this.#discovery;
  }
  async #exchange(pending, callbackUri, discovery) {
    const callback = new URL(callbackUri);
    const expected = new URL(this.#config.redirectUri);
    if (callback.origin !== expected.origin || callback.pathname !== expected.pathname || callback.username || callback.password || callback.hash) throw problem("CallbackTargetMismatch");
    if (callback.searchParams.getAll("state").length !== 1 || callback.searchParams.get("state") !== pending.state) throw problem("StateMismatch");
    if (callback.searchParams.has("iss") && (callback.searchParams.getAll("iss").length !== 1 || callback.searchParams.get("iss") !== this.#config.issuer)) throw problem("IssuerMismatch");
    if (callback.searchParams.has("error")) throw problem("AuthorizationDenied");
    if (callback.searchParams.getAll("code").length !== 1 || !callback.searchParams.get("code")) throw problem("AuthorizationCodeMissing");
    const tokens = await this.#json(discovery.token_endpoint, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        grant_type: "authorization_code",
        client_id: this.#config.clientId,
        redirect_uri: this.#config.redirectUri,
        code: callback.searchParams.get("code"),
        code_verifier: pending.verifier,
        resource: this.#config.apiResource
      })
    }, pending.controller.signal);
    if (tokens.token_type?.toLowerCase() !== "bearer" || typeof tokens.access_token !== "string" || !tokens.access_token || typeof tokens.id_token !== "string") throw problem("MalformedProviderResponse");
    let id, access;
    try {
      ({ payload: id } = await jwtVerify(tokens.id_token, this.#jwks, { issuer: this.#config.issuer, audience: this.#config.clientId, requiredClaims: ["exp", "iat", "sub", "nonce"] }));
      ({ payload: access } = await jwtVerify(tokens.access_token, this.#jwks, { issuer: this.#config.issuer, audience: this.#config.apiResource, requiredClaims: ["exp", "iat", "sub", "sid"] }));
    } catch {
      throw problem("TokenVerificationFailed");
    }
    if (id.nonce !== pending.nonce || Array.isArray(id.aud) && id.aud.length > 1 && id.azp !== this.#config.clientId || id.azp !== void 0 && id.azp !== this.#config.clientId || !uuid(id.sub) || access.sub !== id.sub || !uuid(access.sid) || !Number.isSafeInteger(access.exp) || access.exp * 1e3 <= Date.now()) throw problem("TokenBindingMismatch");
    if (pending.controller.signal.aborted || pending.generation !== this.#generation || this.#disposed) throw problem("SessionContextChanged");
    this.#token = tokens.access_token;
    this.#expires = access.exp * 1e3;
    try {
      await pending.callbacks.verifyAccount(id.sub, pending.controller.signal);
    } catch {
      if (pending.generation === this.#generation) this.clearToken({ cancelPending: false });
      throw problem("CurrentAccountVerificationFailed");
    }
    if (pending.controller.signal.aborted || pending.generation !== this.#generation || this.#disposed) {
      if (pending.generation === this.#generation) this.clearToken({ cancelPending: false });
      throw problem("SessionContextChanged");
    }
    try {
      await pending.callbacks.verified();
    } catch {
      if (pending.generation === this.#generation) this.clearToken({ cancelPending: false });
      throw problem("PrivateContextCleanupFailed");
    }
    if (pending.controller.signal.aborted || pending.generation !== this.#generation || this.#disposed) throw problem("SessionContextChanged");
    if (Number.isSafeInteger(access.iat) && access.iat >= 0 && access.iat <= access.exp && Number.isSafeInteger(id.exp) && id.exp * 1e3 > Date.now()) {
      this.#taskIdentity = Object.freeze({
        version: 1,
        issuer: this.#config.issuer,
        apiResource: this.#config.apiResource,
        clientId: this.#config.clientId,
        accountId: id.sub,
        sessionId: access.sid,
        issuedAt: access.iat,
        expiresAt: Math.min(access.exp, id.exp),
        ownerEpoch: this.#ownerEpoch,
        generation: this.#generation
      });
    }
    const tokenGeneration = this.#generation;
    this.#timer = setTimeout(() => {
      if (this.#disposed || tokenGeneration !== this.#generation) return;
      const expired = this.#expired, failure = this.#failure, begin = this.#beginExpiry;
      this.#issue("expiry", async () => {
        try {
          this.clearToken({ cancelPending: false });
          if (begin !== void 0) {
            try {
              const acknowledgement = begin("token_expired");
              if (acknowledgement !== void 0) {
                if (acknowledgement && typeof acknowledgement.then === "function") Promise.resolve(acknowledgement).catch(() => {
                });
                throw problem("PrivateContextCleanupFailed");
              }
            } finally {
              this.#pending?.controller.abort();
            }
          } else this.clearToken();
          await expired();
        } catch (error) {
          this.#cleanupErrors.push(error);
          try {
            failure("PrivateContextCleanupFailed");
          } catch (observerError) {
            this.#cleanupErrors.push(observerError);
          }
          throw error;
        }
      }).catch(() => {
      });
    }, Math.min(this.#expires - Date.now(), 2147483647));
  }
  #issue(action, factory) {
    if (this.#disposed) return Promise.reject(problem("ServiceUnavailable"));
    let settle;
    const completion = new Promise((resolve) => {
      settle = resolve;
    });
    const entry = { action, completion };
    this.#issued.add(entry);
    let actual;
    try {
      actual = Promise.resolve(factory());
    } catch (error) {
      actual = Promise.reject(error);
    }
    actual.then(() => {
      this.#issued.delete(entry);
      settle();
    }, (error) => {
      if (error?.code === "PrivateContextCleanupFailed") this.#cleanupErrors.push(error);
      this.#issued.delete(entry);
      settle();
    });
    return actual;
  }
  async joinIssuedWork() {
    while (this.#issued.size) await Promise.all([...this.#issued].map((entry) => entry.completion));
    if (this.#cleanupErrors.length) throw new AggregateError([...this.#cleanupErrors], "Broker lifecycle cleanup failed.");
  }
  hasOutstandingWork() {
    return this.#issued.size !== 0;
  }
  signIn(options = {}) {
    if (this.#disposed) return Promise.reject(problem("ServiceUnavailable"));
    if (this.#issued.size) return Promise.reject(problem("SignInInProgress"));
    return this.#issue("sign-in", () => this.#signInCore(options));
  }
  async #signInCore({ signal } = {}) {
    if (this.#disposed) throw problem("ServiceUnavailable");
    if (this.#pending) throw problem("SignInInProgress");
    if (signal?.aborted) throw problem("Cancelled");
    const callbacks = { before: this.#before, verifyAccount: this.#verifyAccount, verified: this.#verified, signInFailed: this.#signInFailed, failure: this.#failure };
    const popup = this.#window.open("about:blank", `nine-to-one-${base64url(this.#crypto.getRandomValues(new Uint8Array(16)))}`, "popup,width=520,height=720");
    if (!popup) throw problem("PopupUnavailable");
    if (this.#disposed || signal?.aborted) {
      try {
        popup.close();
      } catch {
      }
      throw problem("Cancelled");
    }
    const controller = new AbortController();
    const abort = () => controller.abort();
    signal?.addEventListener("abort", abort, { once: true });
    const pending = { popup, controller, generation: null, state: null, nonce: null, verifier: null, claimed: false, callbacks };
    this.#pending = pending;
    let listener, poll;
    try {
      await callbacks.before(controller.signal);
      if (controller.signal.aborted || this.#disposed) throw problem("Cancelled");
      pending.generation = this.#generation;
      pending.state = base64url(this.#crypto.getRandomValues(new Uint8Array(32)));
      pending.nonce = base64url(this.#crypto.getRandomValues(new Uint8Array(32)));
      pending.verifier = base64url(this.#crypto.getRandomValues(new Uint8Array(48)));
      const challenge = base64url(new Uint8Array(await this.#crypto.subtle.digest("SHA-256", new TextEncoder().encode(pending.verifier))));
      const discovery = await this.#metadata(controller.signal);
      if (controller.signal.aborted || pending.generation !== this.#generation) throw problem("SessionContextChanged");
      const completion = new Promise((resolve, reject) => {
        listener = (event) => {
          if (event.origin !== new URL(this.#config.redirectUri).origin || event.source !== popup || event.data?.type !== "nineToOne.oauth.callback" || pending.claimed) return;
          pending.claimed = true;
          this.#issue("exchange", () => this.#exchange(pending, event.data.callbackUri, discovery)).then(resolve, reject);
        };
        this.#window.addEventListener("message", listener);
        controller.signal.addEventListener("abort", () => reject(problem("Cancelled")), { once: true });
        poll = setInterval(() => {
          if (popup.closed && !pending.claimed) reject(problem("PopupClosed"));
        }, 100);
      });
      const authorization = new URL(discovery.authorization_endpoint);
      authorization.search = new URLSearchParams({
        response_type: "code",
        client_id: this.#config.clientId,
        redirect_uri: this.#config.redirectUri,
        scope: this.#config.scopes,
        state: pending.state,
        nonce: pending.nonce,
        code_challenge: challenge,
        code_challenge_method: "S256",
        resource: this.#config.apiResource
      });
      popup.location.replace(authorization.href);
      await completion;
    } catch (error) {
      this.clearToken({ cancelPending: false });
      let code = controller.signal.aborted ? "Cancelled" : typeof error?.code === "string" ? error.code : "ProviderUnavailable";
      try {
        await callbacks.signInFailed();
      } catch {
        code = "PrivateContextCleanupFailed";
      }
      try {
        callbacks.failure(code);
      } catch (error2) {
        this.#cleanupErrors.push(error2);
        throw error2;
      }
      throw problem(code);
    } finally {
      signal?.removeEventListener("abort", abort);
      this.#window.removeEventListener("message", listener);
      clearInterval(poll);
      try {
        popup.close();
      } catch {
      }
      if (this.#pending === pending) this.#pending = null;
      pending.verifier = null;
      pending.nonce = null;
      pending.state = null;
      pending.callbacks = null;
    }
  }
  revokePrivateContext() {
    this.#disposed = true;
    this.clearToken({ cancelPending: false });
    if (this.#pending) {
      this.#pending.verifier = null;
      this.#pending.nonce = null;
      this.#pending.state = null;
    }
    this.#before = null;
    this.#verifyAccount = null;
    this.#verified = null;
    this.#signInFailed = null;
    this.#expired = null;
    this.#failure = null;
    this.#beginExpiry = void 0;
    this.#taskProfile = void 0;
    this.#discovery = void 0;
    this.#jwks = void 0;
  }
  disposeAsync() {
    if (this.#cleanupTask) return this.#cleanupTask;
    this.revokePrivateContext();
    let resolve, reject;
    this.#cleanupTask = new Promise((a, b) => {
      resolve = a;
      reject = b;
    });
    try {
      this.#pending?.controller.abort();
    } catch (error) {
      this.#cleanupErrors.push(error);
    }
    this.joinIssuedWork().then(resolve, reject);
    return this.#cleanupTask;
  }
  dispose() {
    this.disposeAsync().catch(() => {
    });
  }
  // Legacy caller gets no full-drain receipt.
};

// apps/Web/Auth/account-api-client.js
var uuid2 = (value) => typeof value === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(value);
var object2 = (value) => value !== null && typeof value === "object" && !Array.isArray(value);
var nullableString = (value) => value === null || typeof value === "string";
var date = (value) => typeof value === "string" && Number.isFinite(Date.parse(value));
var positiveRevision = (value) => Number.isSafeInteger(value) && value > 0;
var profile = (value) => object2(value) && uuid2(value.accountId) && typeof value.name === "string" && typeof value.username === "string" && nullableString(value.icon) && nullableString(value.pronouns) && nullableString(value.job) && positiveRevision(value.revision);
var session = (value) => object2(value) && uuid2(value.sessionId) && uuid2(value.accountId) && typeof value.deviceName === "string" && date(value.createdAt) && date(value.expiresAt) && (value.revokedAt === null || date(value.revokedAt)) && nullableString(value.registeredClientId);
function failed(code, action, status = null, body = null) {
  return { ok: false, status, error: { code, action, body } };
}
var AccountApiClient = class {
  #origin;
  #token;
  #fetch;
  #clear;
  #beginPrivate;
  #generation = 0;
  #pending = /* @__PURE__ */ new Set();
  constructor({
    apiResource,
    getAccessToken,
    onPrivateContextInvalidated,
    beginPrivateContextInvalidation = void 0,
    fetch: transport = globalThis.fetch,
    allowLoopbackForIsolatedTests = false
  }) {
    const resource = new URL(apiResource);
    const loopback = ["localhost", "127.0.0.1", "[::1]"].includes(resource.hostname);
    if (resource.username || resource.password || resource.search || resource.hash || resource.pathname !== "/" || resource.protocol !== "https:" && !(allowLoopbackForIsolatedTests && loopback && resource.protocol === "http:")) {
      throw new TypeError("A credential-free HTTPS API origin is required.");
    }
    if (typeof getAccessToken !== "function" || typeof onPrivateContextInvalidated !== "function" || typeof transport !== "function") {
      throw new TypeError("A token supplier, private-context invalidator and fetch transport are required.");
    }
    if (beginPrivateContextInvalidation !== void 0 && typeof beginPrivateContextInvalidation !== "function") throw new TypeError("A synchronous private fence is required when supplied.");
    this.#beginPrivate = beginPrivateContextInvalidation;
    this.#origin = resource.origin;
    this.#token = getAccessToken;
    this.#clear = onPrivateContextInvalidated;
    this.#fetch = transport === globalThis.fetch ? transport.bind(globalThis) : transport;
  }
  /** Call before switching account/session/organisation; await cleanup before opening new private surfaces. */
  // Explicit sync begin receipt only; caller MUST separately await its owning
  // full cleanup join before admitting new private surfaces. No wire authority.
  beginPrivateContextInvalidation(reason = "context_changed") {
    this.#fenceAndAbort(reason, null);
  }
  async invalidatePrivateContext(reason = "context_changed") {
    await this.#invalidate(reason);
  }
  async #invalidate(reason, settledTransport = null) {
    this.#fenceAndAbort(reason, settledTransport);
    await this.#clear(reason);
  }
  #fenceAndAbort(reason, settledTransport) {
    this.#generation++;
    try {
      if (this.#beginPrivate !== void 0) {
        const acknowledgement = this.#beginPrivate(reason);
        if (acknowledgement !== void 0) {
          if (acknowledgement && typeof acknowledgement.then === "function") Promise.resolve(acknowledgement).catch(() => {
          });
          throw new TypeError("Private fencing must synchronously return void.");
        }
      }
    } finally {
      for (const controller of this.#pending) if (controller !== settledTransport) controller.abort();
    }
  }
  // Only INTERNAL request authentication failures can acknowledge the fence
  // without awaiting their own owner drain. They remain owned until they settle.
  async #invalidateFromRequest(reason, settledTransport = null) {
    if (this.#beginPrivate === void 0) return this.#invalidate(reason, settledTransport);
    this.#fenceAndAbort(reason, settledTransport);
  }
  getCurrent({ signal } = {}) {
    return this.#request(
      "GetCurrent",
      "/api/account/current",
      "GET",
      void 0,
      (value) => object2(value) && uuid2(value.accountId) && typeof value.displayName === "string",
      signal
    );
  }
  getProfile({ signal } = {}) {
    return this.#request(
      "GetProfile",
      "/api/account/profile",
      "GET",
      void 0,
      (value) => object2(value) && profile(value.profile),
      signal
    );
  }
  updateProfile(expectedRevision, fields, { signal } = {}) {
    const allowed = /* @__PURE__ */ new Set(["name", "username", "icon", "pronouns", "job"]);
    if (!positiveRevision(expectedRevision) || expectedRevision >= Number.MAX_SAFE_INTEGER || !object2(fields)) {
      return Promise.resolve(failed("InvalidArgument", "UpdateProfile"));
    }
    let entries;
    try {
      entries = Object.entries(fields);
    } catch {
      return Promise.resolve(failed("InvalidArgument", "UpdateProfile"));
    }
    if (!entries.length || entries.some(([key, value]) => !allowed.has(key) || typeof value !== "string" && !(value === null && !["name", "username"].includes(key)))) {
      return Promise.resolve(failed("InvalidArgument", "UpdateProfile"));
    }
    const snapshot = Object.fromEntries(entries);
    const nextRevision = expectedRevision + 1;
    return this.#request(
      "UpdateProfile",
      "/api/account/profile",
      "PATCH",
      { expectedRevision, fields: snapshot },
      (value) => object2(value) && profile(value.profile) && value.profile.revision === nextRevision,
      signal
    );
  }
  listSessions({ signal } = {}) {
    return this.#request(
      "ListSessions",
      "/api/account/sessions",
      "GET",
      void 0,
      (value) => object2(value) && Array.isArray(value.sessions) && value.sessions.every(session) && new Set(value.sessions.map((item) => item.sessionId)).size === value.sessions.length && new Set(value.sessions.map((item) => item.accountId)).size <= 1,
      signal
    );
  }
  signOut(options = {}) {
    return this.#sessionMutation("SignOut", "/api/account/signout", "POST", options.signal);
  }
  revokeSession(sessionId, options = {}) {
    if (!uuid2(sessionId)) return Promise.resolve(failed("InvalidArgument", "RevokeSession"));
    return this.#sessionMutation("RevokeSession", `/api/account/sessions/${sessionId}`, "DELETE", options.signal);
  }
  revokeAllOtherSessions(options = {}) {
    return this.#sessionMutation("RevokeAllOtherSessions", "/api/account/revoke-other-sessions", "POST", options.signal);
  }
  async #sessionMutation(action, path, method, signal) {
    const startingGeneration = this.#generation;
    const controller = new AbortController();
    const abort = () => controller.abort();
    signal?.addEventListener("abort", abort, { once: true });
    if (signal?.aborted) abort();
    this.#pending.add(controller);
    try {
      if (controller.signal.aborted) return failed("Cancelled", action);
      const token = await this.#token({ signal: controller.signal });
      if (startingGeneration !== this.#generation) return failed("SessionContextChanged", action);
      if (controller.signal.aborted) return failed("Cancelled", action);
      if (typeof token !== "string" || !token || /\s/.test(token)) {
        try {
          await this.invalidatePrivateContext("authentication_required");
        } catch {
          return failed("PrivateContextCleanupFailed", action);
        }
        return failed("AuthenticationRequired", action);
      }
      this.#pending.delete(controller);
      const expectedGeneration = this.#generation + 1;
      try {
        await this.invalidatePrivateContext("session_mutation");
      } catch {
        return failed("PrivateContextCleanupFailed", action);
      }
      if (this.#generation !== expectedGeneration) return failed("SessionContextChanged", action);
      if (controller.signal.aborted) return failed("Cancelled", action);
      return await this.#request(action, path, method, void 0, null, signal, token);
    } catch {
      if (startingGeneration !== this.#generation) return failed("SessionContextChanged", action);
      return failed(controller.signal.aborted ? "Cancelled" : "TransportUnavailable", action);
    } finally {
      signal?.removeEventListener("abort", abort);
      this.#pending.delete(controller);
    }
  }
  async #request(action, path, method, body, validate, signal, capturedToken = void 0) {
    const generation = this.#generation;
    let expectedGeneration = generation;
    const controller = new AbortController();
    const abort = () => controller.abort();
    signal?.addEventListener("abort", abort, { once: true });
    if (signal?.aborted) abort();
    this.#pending.add(controller);
    try {
      if (controller.signal.aborted) return failed("Cancelled", action);
      const token = capturedToken === void 0 ? await this.#token({ signal: controller.signal }) : capturedToken;
      if (generation !== this.#generation) return failed("SessionContextChanged", action);
      if (controller.signal.aborted) return failed("Cancelled", action);
      if (typeof token !== "string" || !token || /\s/.test(token)) {
        try {
          await this.#invalidateFromRequest("authentication_required");
        } catch {
          return failed("PrivateContextCleanupFailed", action);
        }
        return failed("AuthenticationRequired", action);
      }
      const url = new URL(path, this.#origin).href;
      const response = await this.#fetch(url, {
        method,
        headers: { Authorization: `Bearer ${token}`, Accept: "application/json", ...body === void 0 ? {} : { "Content-Type": "application/json" } },
        body: body === void 0 ? void 0 : JSON.stringify(body),
        credentials: "omit",
        cache: "no-store",
        redirect: "error",
        referrerPolicy: "no-referrer",
        signal: controller.signal
      });
      if (generation !== this.#generation) return failed("SessionContextChanged", action);
      if (controller.signal.aborted) return failed("Cancelled", action);
      if (response.redirected || response.url && response.url !== url || response.status >= 300 && response.status < 400) {
        return failed("UnexpectedRedirect", action, response.status);
      }
      if (response.status === 401) {
        expectedGeneration = generation + 1;
        try {
          await this.#invalidateFromRequest("session_invalidated", controller);
        } catch {
          return failed("PrivateContextCleanupFailed", action, response.status);
        }
      }
      if (expectedGeneration !== this.#generation) return failed("SessionContextChanged", action);
      if (!response.ok) {
        let wire2;
        try {
          wire2 = await response.json();
        } catch {
          if (expectedGeneration !== this.#generation) return failed("SessionContextChanged", action);
          if (controller.signal.aborted) return failed("Cancelled", action);
          return failed("MalformedResponse", action, response.status);
        }
        if (expectedGeneration !== this.#generation) return failed("SessionContextChanged", action);
        if (controller.signal.aborted) return failed("Cancelled", action);
        if (!object2(wire2) || typeof wire2.error !== "string" || !wire2.error || wire2.revision !== void 0 && !positiveRevision(wire2.revision)) {
          return failed("MalformedResponse", action, response.status);
        }
        return failed(wire2.error, action, response.status, wire2);
      }
      if (validate === null) {
        return response.status === 204 ? { ok: true, status: 204, body: null } : failed("MalformedResponse", action, response.status);
      }
      if (response.status !== 200 || !response.headers.get("content-type")?.toLowerCase().includes("application/json")) {
        return failed("MalformedResponse", action, response.status);
      }
      let wire;
      try {
        wire = await response.json();
      } catch {
        if (expectedGeneration !== this.#generation) return failed("SessionContextChanged", action);
        if (controller.signal.aborted) return failed("Cancelled", action);
        return failed("MalformedResponse", action, response.status);
      }
      if (generation !== this.#generation) return failed("SessionContextChanged", action);
      if (controller.signal.aborted) return failed("Cancelled", action);
      if (!validate(wire)) return failed("MalformedResponse", action, response.status);
      return { ok: true, status: response.status, body: wire };
    } catch {
      if (generation !== this.#generation) return failed("SessionContextChanged", action);
      if (controller.signal.aborted) return failed("Cancelled", action);
      return failed("TransportUnavailable", action);
    } finally {
      signal?.removeEventListener("abort", abort);
      this.#pending.delete(controller);
    }
  }
};

// apps/Web/Services/account-service.js
function createAccountModule(client = null) {
  const pending = /* @__PURE__ */ new Map();
  const issued = /* @__PURE__ */ new Set();
  let disposed = false, cleanupTask;
  const failure = (code) => JSON.stringify({ ok: false, status: null, error: { code, action: "AccountSurface", body: null } });
  const revokePrivateContext = () => {
    disposed = true;
    client = null;
  };
  const drain = async () => {
    while (issued.size) await Promise.all([...issued].map((entry) => entry.completion));
  };
  const disposeAsync = () => {
    if (cleanupTask) return cleanupTask;
    revokePrivateContext();
    let resolve, reject;
    cleanupTask = new Promise((a, b) => {
      resolve = a;
      reject = b;
    });
    const errors = [];
    for (const entry of pending.values()) try {
      entry.controller.abort();
    } catch (error) {
      errors.push(error);
    }
    drain().then(() => {
      if (errors.length) reject(new AggregateError(errors, "Account module cleanup failed."));
      else resolve();
    }, reject);
    return cleanupTask;
  };
  return {
    invoke(requestId, action, argumentsJson) {
      if (!client || disposed) return Promise.resolve(failure("ServiceUnavailable"));
      if (typeof requestId !== "string" || !requestId || pending.has(requestId)) return Promise.resolve(failure("InvalidArgument"));
      let args;
      try {
        args = JSON.parse(argumentsJson);
      } catch {
        return Promise.resolve(failure("InvalidArgument"));
      }
      const controller = new AbortController();
      const options = { signal: controller.signal };
      const operations = {
        GetCurrent: () => client.getCurrent(options),
        GetProfile: () => client.getProfile(options),
        ListSessions: () => client.listSessions(options),
        UpdateProfile: () => client.updateProfile(args?.expectedRevision, args?.fields, options),
        SignOut: () => client.signOut(options),
        RevokeSession: () => client.revokeSession(args?.sessionId, options),
        RevokeAllOtherSessions: () => client.revokeAllOtherSessions(options)
      };
      if (!Object.hasOwn(operations, action)) return Promise.resolve(failure("InvalidArgument"));
      let settle;
      const completion = new Promise((resolve) => {
        settle = resolve;
      });
      const entry = { controller, completion };
      pending.set(requestId, entry);
      issued.add(entry);
      const actual = (async () => {
        try {
          const reply = await operations[action]();
          return controller.signal.aborted ? failure("Cancelled") : JSON.stringify(reply);
        } catch {
          return failure(controller.signal.aborted ? "Cancelled" : "TransportUnavailable");
        }
      })();
      actual.then(() => {
        pending.delete(requestId);
        issued.delete(entry);
        settle();
      }, () => {
        pending.delete(requestId);
        issued.delete(entry);
        settle();
      });
      return actual;
    },
    cancel(requestId) {
      pending.get(requestId)?.controller.abort();
    },
    revokePrivateContext,
    joinIssuedWork: drain,
    hasOutstandingWork() {
      return issued.size !== 0;
    },
    dispose() {
      disposeAsync().catch(() => {
      });
    },
    // Legacy immediate return is NOT a drained receipt.
    disposeAsync
  };
}

// apps/Web/Auth/configured-accounts.js
function createConfiguredAccounts({
  configuration = null,
  window = globalThis.window,
  fetch: fetch2 = globalThis.fetch,
  crypto: crypto2 = globalThis.crypto,
  onPrivateContextInvalidated,
  onVerifiedIdentity,
  onFailure,
  beginPrivateContextInvalidation = void 0
}) {
  if (typeof onPrivateContextInvalidated !== "function" || typeof onVerifiedIdentity !== "function" || typeof onFailure !== "function") throw new TypeError("Private context callbacks are required.");
  if (beginPrivateContextInvalidation !== void 0 && typeof beginPrivateContextInvalidation !== "function") throw new TypeError("A synchronous root private fence is required when supplied.");
  let broker, api, disposed = false, prepared = false, cleanupTask, module;
  const signIns = /* @__PURE__ */ new Map(), taskReads = /* @__PURE__ */ new Map(), taskIdentityReceipts = /* @__PURE__ */ new Map(), issued = /* @__PURE__ */ new Set(), cleanupErrors = [];
  const issue = (action, factory) => {
    if (disposed) return Promise.reject(new Error("ServiceUnavailable"));
    let settle;
    const completion = new Promise((resolve) => {
      settle = resolve;
    });
    const entry = { action, completion };
    issued.add(entry);
    let actual;
    try {
      actual = Promise.resolve(factory());
    } catch (error) {
      actual = Promise.reject(error);
    }
    actual.then(() => {
      issued.delete(entry);
      settle();
    }, (error) => {
      if (action === "prepare" || action === "invalidate") cleanupErrors.push(error);
      issued.delete(entry);
      settle();
    });
    return actual;
  };
  const joinIssued = async () => {
    while (issued.size) await Promise.all([...issued].map((e) => e.completion));
    if (cleanupErrors.length) throw new AggregateError([...cleanupErrors], "Configured lifecycle cleanup failed.");
  };
  const base = configuration === null ? createAccountModule() : (() => {
    const begin = (reason) => {
      taskIdentityReceipts.clear();
      broker?.clearToken({ cancelPending: false });
      try {
        const acknowledgement = beginPrivateContextInvalidation(reason);
        if (acknowledgement !== void 0) {
          if (acknowledgement && typeof acknowledgement.then === "function") Promise.resolve(acknowledgement).catch(() => {
          });
          throw new TypeError("Root private fencing must synchronously return void.");
        }
      } finally {
        if (reason !== "sign_in_started") broker?.clearToken();
      }
    };
    const clear = async (reason) => {
      taskIdentityReceipts.clear();
      if (beginPrivateContextInvalidation === void 0)
        broker?.clearToken({ cancelPending: reason !== "sign_in_started" });
      await onPrivateContextInvalidated(reason);
    };
    broker = new BrowserPublicClient({
      configuration,
      window,
      fetch: fetch2,
      crypto: crypto2,
      onBeforeSignIn: () => api.invalidatePrivateContext("sign_in_started"),
      verifyCurrentAccount: async (subject, signal) => {
        const current = await api.getCurrent({ signal });
        if (!current.ok || current.body.accountId !== subject || signal.aborted) throw new Error("CurrentAccountVerificationFailed");
      },
      readCurrentTaskProfile: async (subject, signal) => {
        const current = await api.getCurrent({ signal });
        if (!current.ok) throw Object.assign(new Error("TaskIdentityUnavailable"), { code: "TaskIdentityUnavailable" });
        if (current.body.accountId !== subject || signal?.aborted) throw Object.assign(new Error("SessionContextChanged"), { code: "SessionContextChanged" });
        const response = await api.getProfile({ signal });
        if (!response.ok) throw Object.assign(new Error("TaskIdentityUnavailable"), { code: "TaskIdentityUnavailable" });
        if (response.body.profile.accountId !== subject || signal?.aborted) throw Object.assign(new Error("TaskProfileVerificationFailed"), { code: "TaskProfileVerificationFailed" });
        return { accountId: response.body.profile.accountId, revision: response.body.profile.revision };
      },
      onVerifiedIdentity,
      onSignInFailed: () => api.invalidatePrivateContext("sign_in_failed"),
      onTokenExpired: () => beginPrivateContextInvalidation === void 0 ? api.invalidatePrivateContext("token_expired") : onPrivateContextInvalidated("token_expired"),
      beginTokenExpiryInvalidation: beginPrivateContextInvalidation === void 0 ? void 0 : () => api.beginPrivateContextInvalidation("token_expired"),
      onFailure
    });
    api = new AccountApiClient({
      apiResource: broker.configuration.apiResource,
      getAccessToken: () => broker.getAccessToken(),
      onPrivateContextInvalidated: clear,
      beginPrivateContextInvalidation: beginPrivateContextInvalidation === void 0 ? void 0 : begin,
      fetch: fetch2,
      allowLoopbackForIsolatedTests: broker.configuration.allowLoopbackForIsolatedTests
    });
    return createAccountModule(api);
  })();
  return module = {
    invalidatePrivateContext(reason) {
      return issue("invalidate", async () => {
        if (broker) await api.invalidatePrivateContext(reason);
        else {
          if (beginPrivateContextInvalidation !== void 0) {
            const acknowledgement = beginPrivateContextInvalidation(reason);
            if (acknowledgement !== void 0) {
              if (acknowledgement && typeof acknowledgement.then === "function") Promise.resolve(acknowledgement).catch(() => {
              });
              throw new TypeError("Private fencing must synchronously return void.");
            }
          }
          await onPrivateContextInvalidated(reason);
        }
      });
    },
    prepare() {
      return issue("prepare", async () => {
        if (disposed) throw new Error("ServiceUnavailable");
        if (broker && !prepared) {
          await api.invalidatePrivateContext("configuration_prepared");
          prepared = true;
        }
      });
    },
    invoke(id, action, args) {
      if (disposed) return Promise.resolve(JSON.stringify({ ok: false, status: null, error: { code: "ServiceUnavailable", action, body: null } }));
      return issue("invoke", async () => {
        if (broker && !disposed && !broker.getAccessToken()) {
          if (!prepared) return JSON.stringify({ ok: false, status: null, error: { code: "PrivateContextNotPrepared", action, body: null } });
          return JSON.stringify({ ok: false, status: null, error: { code: "AuthenticationRequired", action, body: null } });
        }
        return base.invoke(id, action, args);
      });
    },
    readTaskIdentity(id) {
      return issue("task-identity", async () => {
        if (!broker || !prepared || disposed) return JSON.stringify({ ok: false, code: "AuthenticationRequired" });
        if (typeof id !== "string" || !id || taskReads.has(id) || taskIdentityReceipts.has(id)) throw new TypeError("Invalid Task identity request.");
        if (taskReads.size + taskIdentityReceipts.size >= 128) throw new Error("Task identity request custody is full.");
        const controller = new AbortController();
        taskReads.set(id, controller);
        try {
          const identity = await broker.readCurrentTaskIdentity({ signal: controller.signal });
          if (disposed || controller.signal.aborted) return JSON.stringify({ ok: false, code: "Cancelled" });
          if (identity !== null && !broker.isOriginalTaskIdentityCurrent(identity)) throw Object.assign(new Error("SessionContextChanged"), { code: "SessionContextChanged" });
          if (identity !== null) taskIdentityReceipts.set(id, identity);
          return identity === null ? JSON.stringify({ ok: false, code: "AuthenticationRequired" }) : JSON.stringify({ ok: true, identity });
        } finally {
          taskReads.delete(id);
        }
      });
    },
    confirmTaskIdentity(id) {
      const original = taskIdentityReceipts.get(id);
      taskIdentityReceipts.delete(id);
      return !disposed && broker !== void 0 && original !== void 0 && broker.isOriginalTaskIdentityCurrent(original);
    },
    releaseTaskIdentity(id) {
      taskIdentityReceipts.delete(id);
    },
    cancelTaskIdentity(id) {
      taskIdentityReceipts.delete(id);
      taskReads.get(id)?.abort();
    },
    signInAvailable() {
      return Boolean(broker && prepared && !disposed);
    },
    requestSignIn(id) {
      if (disposed) return Promise.resolve(JSON.stringify({ ok: false, error: { code: "ServiceUnavailable" } }));
      return issue("sign-in", async () => {
        if (!broker || !prepared || disposed) return JSON.stringify({ ok: false, error: { code: "ServiceUnavailable" } });
        if (typeof id !== "string" || !id || signIns.has(id)) return JSON.stringify({ ok: false, error: { code: "InvalidArgument" } });
        const controller = new AbortController();
        signIns.set(id, controller);
        try {
          await broker.signIn({ signal: controller.signal });
          return JSON.stringify({ ok: true });
        } catch (error) {
          return JSON.stringify({ ok: false, error: { code: typeof error?.code === "string" ? error.code : "ProviderUnavailable" } });
        } finally {
          signIns.delete(id);
        }
      });
    },
    cancel(id) {
      base.cancel(id);
      signIns.get(id)?.abort();
    },
    revokePrivateContext() {
      disposed = true;
      taskIdentityReceipts.clear();
      broker?.revokePrivateContext();
      base.revokePrivateContext();
    },
    disposeAsync() {
      if (cleanupTask) return cleanupTask;
      disposed = true;
      taskIdentityReceipts.clear();
      broker?.revokePrivateContext();
      base.revokePrivateContext();
      let resolve, reject;
      cleanupTask = new Promise((a, b) => {
        resolve = a;
        reject = b;
      });
      const errors = [];
      for (const controller of [...signIns.values(), ...taskReads.values()]) try {
        controller.abort();
      } catch (error) {
        errors.push(error);
      }
      Promise.allSettled([base.disposeAsync(), broker?.disposeAsync() ?? Promise.resolve(), joinIssued()]).then((results) => {
        errors.push(...results.filter((r) => r.status === "rejected").map((r) => r.reason));
        if (errors.length) reject(new AggregateError(errors, "Configured account cleanup failed."));
        else resolve();
      }, reject);
      return cleanupTask;
    },
    dispose() {
      module.disposeAsync().catch(() => {
      });
    }
  };
}
export {
  BrowserPublicClient,
  createConfiguredAccounts,
  handleOAuthPopupCallback
};
