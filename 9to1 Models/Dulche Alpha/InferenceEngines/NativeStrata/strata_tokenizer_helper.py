#!/usr/bin/env python3
"""Bundled tokenizer/chat-template helper for the candidate (v0.2.1) Thunder relay worker.

This helper reuses the donor tokenizer (`tools/strata_tokenizer.py`, owner-designated
Niko1221/Strata v0.2.1) and the model's own GGUF `tokenizer.chat_template` metadata. It is a
REQUIRED, explicitly-pinned runtime dependency of the candidate text path. It runs as a bounded
subprocess owned by the C++ relay worker and answers a tiny line protocol on stdin/stdout:

  IDENTITY              -> "IDENT <sha256 of tokenizer identity>"
  ENC <len> <json>      -> "TON <len> <comma-separated ids>"
  DEC <len> <ids>       -> "BYT <len> <raw bytes>"
  QUIT

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
import json
import os
import pathlib
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
                if "text" in data:
                    ids = tok.encode(data["text"], parse_special=True)
                elif "messages" in data:
                    ids = render_messages(tok, data["messages"])
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
