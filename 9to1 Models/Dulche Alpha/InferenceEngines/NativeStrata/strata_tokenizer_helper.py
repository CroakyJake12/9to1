#!/usr/bin/env python3
"""Bundled tokenizer/chat-template helper for the candidate (v0.2.1) Thunder relay worker.

This helper reuses the donor tokenizer (`tools/strata_tokenizer.py`, owner-designated
Niko1221/Strata v0.2.1) and the model's own GGUF `tokenizer.chat_template` metadata. It is a
REQUIRED, explicitly-pinned runtime dependency of the candidate text path. It runs as a bounded
subprocess owned by the C++ relay worker and answers a tiny line protocol on stdin/stdout:

  IDENTITY              -> "IDENT <sha256 of tokenizer identity>"
  ENC <json object>     -> "TON <comma-separated ids>"
  DEC <ids>             -> "BYT <raw bytes>"
  QUIT

Each command/response has one outer 4-byte little-endian byte-length prefix (the length is not
repeated inside ENC/DEC/TON/BYT). ENC carries exactly one of {"text": string} or
{"messages": [{"role": string, "content": string, "name"?: string}, ...]}; a bare array is invalid.
Every input is length-prefixed and bounded; every output is length-prefixed and bounded. On any
missing dependency or identity mismatch the helper exits non-zero with a precise cause on stderr, so
the relay fails ONLY the affected text request (never load/probe/shutdown).

Identity validation: the canonical identity binds the pre-tokenizer, the FULL ordered vocabulary, the
ordered merge rules, token types, special ids and the chat-template bytes - not just the vocabulary
length. A same-sized changed vocab/merge/template therefore changes the identity and is rejected
BEFORE any token is produced.

Chat rendering replicates the donor frontend exactly (`serve/frontend.py` ChatTemplate):
`ImmutableSandboxedEnvironment(trim_blocks=True, lstrip_blocks=True, extensions=['jinja2.ext.loopcontrols'])`
with the `tojson` filter and `raise_exception` global, rendering with `messages`, `add_generation_prompt`
and `tools`. If a required variable is missing or the template is not a Jinja template, the request
fails closed with a precise capability refusal - never a guessed render.

STATUS: SOURCE-ONLY. The byte-level BPE round trip is unit-tested with a synthetic vocabulary in
NativeStrataTokenizerHelperTests.py; no real model weights are used in this checkpoint. In this
cloud build the bundled donor module `strata_tokenizer.py` (and its `regex` dependency) are not
installed beside the helper, so it is verified to fail closed with the EXACT dependency cause rather
than fabricate token ids.
"""
from __future__ import annotations

import argparse
import hashlib
from importlib import import_module, metadata
import json
import os
import pathlib
import stat
import sys

# ------------------------------------------------------------------ precise dependency diagnostics
# The donor module and the `regex` runtime module are separate facts. A missing donor module is NOT
# proof that `regex` itself is absent; report each precisely.
_REGEX_REPORT = "not-imported"
try:
    import regex  # noqa: F401 - probe availability only
    _REGEX_REPORT = "imported"
except Exception as exc:  # pragma: no cover
    _REGEX_REPORT = "missing: %s" % (exc,)

_BUNDLED_DONOR_AVAILABLE = False
_DONOR_CAUSE = None
try:
    # Keep the LEXICAL descriptor parent and never `.resolve()` it. When the host binds this helper
    # through a held `/proc/<pid>/fd/<dir>` handle, `.resolve()` would escape the descriptor binding
    # back to an ordinary mutable real path, letting a sibling donor import be rebound after
    # verification. Insert the lexical parent verbatim so `strata_tokenizer` and `gguf_reader`
    # imports travel through the SAME held directory handle (the fd stays pinned to the held
    # directory even if the original path is renamed).
    _helper_dir = pathlib.Path(__file__).parent
    if not _helper_dir.is_absolute():
        _helper_dir = pathlib.Path(os.path.abspath(_helper_dir))
    if str(_helper_dir) not in sys.path:
        sys.path.insert(0, str(_helper_dir))
    from strata_tokenizer import Tokenizer, PromptEncoder  # type: ignore
    _BUNDLED_DONOR_AVAILABLE = True
except Exception as exc:  # pragma: no cover - dependency gate
    _DONOR_CAUSE = ("bundled donor tokenizer module (strata_tokenizer.py) is not installed beside "
                     "the helper: %s" % (exc,))
    Tokenizer = None  # type: ignore
    PromptEncoder = None  # type: ignore

MAX_FRAME = 16 * 1024 * 1024
MAX_ID_COUNT = 4 * 1024 * 1024
MAX_RUNTIME_LOCK_BYTES = 16 * 1024


def verify_runtime_dependency_versions():
    """Admit the selected interpreter's distribution versions against the held sibling lock.

    Version metadata establishes compatibility admission, not distribution-byte authenticity or
    transitive dependency custody. Keep the lexical helper directory: resolving it would escape
    the installation owner's held directory descriptor after an original-path replacement.
    """
    lock_path = _helper_dir / "strata-runtime-dependencies.json"
    flags = os.O_RDONLY | getattr(os, "O_NONBLOCK", 0) | getattr(os, "O_NOFOLLOW", 0)
    flags |= getattr(os, "O_CLOEXEC", 0)
    try:
        fd = os.open(lock_path, flags)
    except OSError as exc:
        raise ValueError("runtime dependency lock is unavailable: %s" % exc) from exc
    try:
        before = os.fstat(fd)
        if not stat.S_ISREG(before.st_mode):
            raise ValueError("runtime dependency lock is not a regular file")
        if before.st_size > MAX_RUNTIME_LOCK_BYTES:
            raise ValueError("runtime dependency lock exceeds %d bytes" % MAX_RUNTIME_LOCK_BYTES)
        data = bytearray()
        while len(data) <= MAX_RUNTIME_LOCK_BYTES:
            chunk = os.read(fd, MAX_RUNTIME_LOCK_BYTES + 1 - len(data))
            if not chunk:
                break
            data.extend(chunk)
        if len(data) > MAX_RUNTIME_LOCK_BYTES:
            raise ValueError("runtime dependency lock exceeds %d bytes" % MAX_RUNTIME_LOCK_BYTES)
        after = os.fstat(fd)
        stable = lambda info: (info.st_dev, info.st_ino, info.st_size,
                               info.st_mtime_ns, info.st_ctime_ns)
        if stable(before) != stable(after) or len(data) != before.st_size:
            raise ValueError("runtime dependency lock changed while reading")
    except OSError as exc:
        raise ValueError("runtime dependency lock could not be read: %s" % exc) from exc
    finally:
        os.close(fd)

    def unique_keys(pairs):
        values = {}
        for key, value in pairs:
            if key in values:
                raise ValueError("runtime dependency lock has duplicate JSON key: " + key)
            values[key] = value
        return values

    try:
        lock = json.loads(data.decode("utf-8"), object_pairs_hook=unique_keys)
    except (UnicodeDecodeError, json.JSONDecodeError, RecursionError) as exc:
        raise ValueError("runtime dependency lock contains invalid JSON: %s" % exc) from exc
    if not isinstance(lock, dict) or lock.get("schema") != "strata-runtime-dependencies/v1":
        raise ValueError("runtime dependency lock has an unsupported schema")
    modules = lock.get("runtimeModules")
    if not isinstance(modules, dict) or set(modules) != {"regex", "jinja2"}:
        raise ValueError("runtime dependency lock must name exactly regex and jinja2")
    pins = {}
    for name in ("regex", "jinja2"):
        entry = modules[name]
        pin = entry.get("pin") if isinstance(entry, dict) else None
        if (not isinstance(pin, str) or not 1 <= len(pin) <= 128
                or any(c not in "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ.!+_-" for c in pin)):
            raise ValueError("runtime dependency " + name + " has an invalid version pin")
        pins[name] = pin
    for name, pin in pins.items():
        try:
            observed = metadata.version(name)
        except metadata.PackageNotFoundError as exc:
            raise ValueError("runtime dependency " + name + " distribution is unavailable") from exc
        except Exception as exc:
            raise ValueError("runtime dependency " + name + " version metadata is unavailable: %s" % exc) from exc
        if observed != pin:
            raise ValueError("runtime dependency %s version mismatch: required %s; observed %s"
                             % (name, pin, observed))
    # Intact dist-info alone does not establish that the selected interpreter can import the
    # implementation. In particular Jinja2 imports MarkupSafe; retain that real cause if absent.
    for name in pins:
        try:
            import_module(name)
        except Exception as exc:
            raise ValueError("runtime dependency " + name + " import is unavailable: %s" % exc) from exc
    try:
        sandbox = import_module("jinja2.sandbox")
        if not callable(getattr(sandbox, "ImmutableSandboxedEnvironment", None)):
            raise ImportError("ImmutableSandboxedEnvironment is unavailable")
    except Exception as exc:
        raise ValueError("runtime dependency jinja2 sandbox is unavailable: %s" % exc) from exc


def read_frame(stream) -> tuple[bytes, int]:
    """Read a 4-byte little-endian length, then exactly that many bytes, from a binary stream."""
    head = stream.read(4)
    if len(head) == 0:
        return (b"", -1)  # EOF
    if len(head) != 4:
        raise ValueError("truncated frame header")
    length = int.from_bytes(head, "little")
    if length < 0 or length > MAX_FRAME:
        raise ValueError("frame length out of bounds")
    body = stream.read(length)
    if len(body) != length:
        raise ValueError("truncated frame body")
    return (body, length)


def write_frame(stream, body: bytes):
    if len(body) > MAX_FRAME:
        raise ValueError("output frame too large")
    stream.write(len(body).to_bytes(4, "little"))
    stream.write(body)
    stream.flush()


def _u64_le(value: int) -> bytes:
    return int(value).to_bytes(8, "little", signed=False)


def _feed_str(h: "hashlib._Hash", text: str):
    b = text.encode("utf-8")
    h.update(_u64_le(len(b)) + b)


def tokenizer_identity(tok) -> str:
    """Deterministic canonical identity of the tokenizer's verified metadata.

    Binds the pre-tokenizer, the FULL ordered vocabulary, the ordered merge rules, token types,
    special ids and the chat-template bytes. Equal-sized content changes are detected. No weight
    shards are read.
    """
    h = hashlib.sha256()
    h.update(b"strata-tokenizer-v2")
    _feed_str(h, getattr(tok, "pre", "qwen35") or "qwen35")

    tokens = list(getattr(tok, "tokens", None) or [])
    h.update(_u64_le(len(tokens)))
    for t in tokens:
        _feed_str(h, t)

    ranks = getattr(tok, "ranks", None) or {}
    ordered_merges = sorted(ranks.items(), key=lambda kv: int(kv[1]))
    h.update(_u64_le(len(ordered_merges)))
    for pair, rank in ordered_merges:
        _feed_str(h, "%s %s" % (pair[0], pair[1]))
        h.update(_u64_le(int(rank)))

    token_types = getattr(tok, "token_types", None)
    if token_types is None:
        h.update(b"\x00")
    else:
        h.update(b"\x01")
        h.update(_u64_le(len(token_types)))
        for x in token_types:
            ix = int(x)
            h.update(ix.to_bytes(8, "little", signed=ix < 0))

    special = getattr(tok, "special_ids", None) or getattr(tok, "special_tokens", {}) or {}
    for key in sorted(special, key=lambda k: int(special[k])):
        _feed_str(h, key)
        h.update(_u64_le(int(special[key])))

    template = getattr(tok, "chat_template", None)
    if template is None:
        h.update(b"\x00")
    else:
        h.update(b"\x01")
        _feed_str(h, template)

    return h.hexdigest()


def serve(tok, encoder, expected_identity, in_stream=None, out_stream=None):
    """Serve the bounded protocol. `tok`/`encoder` are injected for testability.

    `in_stream`/`out_stream` default to the process's binary stdio
    (`sys.stdin.buffer`/`sys.stdout.buffer`); tests inject BytesIO streams.
    """
    ident = tokenizer_identity(tok)
    if expected_identity and expected_identity != ident:
        sys.stderr.write("identity mismatch: registered %s != observed %s\n"
                         % (expected_identity, ident))
        return 4
    line = in_stream if in_stream is not None else sys.stdin.buffer
    out = out_stream if out_stream is not None else sys.stdout.buffer
    while True:
        body, length = read_frame(line)
        if length < 0:
            break
        try:
            text = body.decode("utf-8")
            if text == "QUIT":
                break
            if text == "IDENTITY":
                write_frame(out, ("IDENT %s" % ident).encode("utf-8"))
                continue
            if text.startswith("ENC "):
                payload = body[4:]
                data = json.loads(payload.decode("utf-8"))
                # The native relay supplies a tagged object. Reject ambiguous/malformed payloads
                # before invoking a tokenizer or chat template; never reinterpret a bare array.
                if not isinstance(data, dict) or len(data) != 1:
                    raise ValueError("ENC payload must be an object carrying exactly one of 'text' or 'messages'")
                if "text" in data:
                    if not isinstance(data["text"], str):
                        raise ValueError("ENC 'text' must be a string")
                    ids = tok.encode(data["text"], parse_special=True)
                elif "messages" in data:
                    messages = data["messages"]
                    if not isinstance(messages, list):
                        raise ValueError("ENC 'messages' must be an array")
                    for message in messages:
                        if (not isinstance(message, dict)
                                or not {"role", "content"}.issubset(message)
                                or set(message) - {"role", "content", "name"}
                                or message["role"] not in ("system", "user", "assistant", "tool")
                                or not isinstance(message["content"], str)
                                or ("name" in message and not isinstance(message["name"], str))):
                            raise ValueError("ENC message must carry a valid role, string content and optional string name")
                    ids = render_messages(tok, messages)
                else:
                    raise ValueError("ENC payload must carry 'text' or 'messages'")
                if len(ids) > MAX_ID_COUNT:
                    raise ValueError("token count exceeds bound")
                write_frame(out, b"TON " + ",".join(str(i) for i in ids).encode("utf-8"))
                continue
            if text.startswith("DEC "):
                ids = [int(x) for x in body[4:].decode("utf-8").split(",") if x.strip()]
                if any(i < 0 or i >= len(tok.tokens) for i in ids):
                    raise ValueError("token id out of vocabulary")
                raw = b"".join(tok.token_bytes(i) for i in ids)
                write_frame(out, b"BYT " + raw)
                continue
            raise ValueError("unknown command")
        except Exception as exc:
            try:
                write_frame(out, b"ERR " + str(exc).encode("utf-8", "replace"))
            except Exception:
                return 2
    return 0


def render_messages(tok, messages) -> list[int]:
    """Render chat messages through the model's own chat template, replicating the donor frontend.

    The donor frontend (`serve/frontend.py` ChatTemplate) renders with
    `ImmutableSandboxedEnvironment(trim_blocks=True, lstrip_blocks=True,
    extensions=['jinja2.ext.loopcontrols'])`, a `tojson` filter and a `raise_exception` global.
    We replicate that exactly, passing `messages`, `add_generation_prompt` and `tools` (as the donor
    does). A non-Jinja template or a template that needs a variable we do not control fails closed
    with a precise capability refusal - never a guessed render.
    """
    try:
        import jinja2
        from jinja2.sandbox import ImmutableSandboxedEnvironment
    except Exception as exc:
        raise ValueError("chat template renderer dependency (jinja2) is unavailable: %s" % (exc,))
    template_text = getattr(tok, "chat_template", None)
    if not template_text:
        raise ValueError("model tokenizer carries no chat_template; cannot render a chat prompt")

    class _TemplateError(jinja2.exceptions.TemplateError, ValueError):
        pass

    def _raise_exception(message):
        raise _TemplateError(message)

    def _tojson(value, ensure_ascii=False, indent=None, separators=None, sort_keys=False):
        return json.dumps(value, ensure_ascii=ensure_ascii, indent=indent,
                          separators=separators, sort_keys=sort_keys)

    env = ImmutableSandboxedEnvironment(trim_blocks=True, lstrip_blocks=True,
                                        extensions=["jinja2.ext.loopcontrols"])
    env.filters["tojson"] = _tojson
    env.globals["raise_exception"] = _raise_exception
    try:
        template = env.from_string(template_text)
    except Exception as exc:
        raise ValueError("chat template is invalid Jinja: %s" % (exc,))
    try:
        rendered = template.render(messages=messages, add_generation_prompt=True, tools=None)
    except Exception as exc:
        raise ValueError("chat template render refused the request: %s" % (exc,))
    return tok.encode(rendered, parse_special=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--gguf", help="GGUF model file carrying the tokenizer metadata")
    parser.add_argument("--tokenizer-dir", help="extracted tokenizer/ directory (pack layout)")
    parser.add_argument("--expected-identity", help="registered model tokenizer identity to compare")
    args = parser.parse_args()

    try:
        verify_runtime_dependency_versions()
    except ValueError as exc:
        sys.stderr.write("dependency: %s\n" % exc)
        return 3

    if not _BUNDLED_DONOR_AVAILABLE:
        sys.stderr.write("dependency: %s\n" % (_DONOR_CAUSE,))
        sys.stderr.write("dependency: regex module report: %s\n" % _REGEX_REPORT)
        return 3

    try:
        if args.tokenizer_dir:
            d = pathlib.Path(args.tokenizer_dir)
            tokens = json.loads((d / "vocab.json").read_text(encoding="utf-8"))
            merges = (d / "merges.txt").read_text(encoding="utf-8").splitlines()
            token_types = json.loads((d / "token_type.json").read_text(encoding="utf-8"))
            cfg = json.loads((d / "tokenizer.json").read_text(encoding="utf-8"))
            special = cfg.get("special_ids") or {}
            size = cfg.get("vocab_size")
            if size is None:
                size = (max(int(i) for i in tokens.values()) + 1) if tokens else 0
            ordered = [""] * size
            for token, i in tokens.items():
                idx = int(i)
                if idx < 0 or idx >= size:
                    raise ValueError("vocabulary id out of range")
                ordered[idx] = token
            tok = Tokenizer(ordered, merges, token_types, cfg.get("pre", "qwen35"), special)
            template_file = d / "chat_template.jinja"
            tok.chat_template = template_file.read_text(encoding="utf-8") if template_file.exists() else None
        elif args.gguf:
            tok = Tokenizer.from_gguf(args.gguf)
            # from_gguf never attaches the chat template; read it from the same GGUF metadata so
            # message rendering works instead of failing unconditionally.
            try:
                from gguf_reader import GGUFFile
                _md = GGUFFile(pathlib.Path(args.gguf)).metadata
                tok.chat_template = _md.get("tokenizer.chat_template")
            except Exception:
                tok.chat_template = None
        else:
            sys.stderr.write("dependency: one of --gguf or --tokenizer-dir is required\n")
            return 3
    except Exception as exc:
        sys.stderr.write("dependency: tokenizer load failed: %s\n" % (exc,))
        return 3

    return serve(tok, PromptEncoder(tok), args.expected_identity)


if __name__ == "__main__":
    raise SystemExit(main())
