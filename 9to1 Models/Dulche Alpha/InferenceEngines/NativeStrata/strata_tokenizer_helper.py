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

Identity validation: the GGUF tokenizer metadata (vocabulary size, pre-type, special tokens) is
hashed and compared with the registered model identity supplied at spawn time. A mismatch is
rejected BEFORE any token is produced.

STATUS: SOURCE-ONLY. The byte-level BPE round trip is unit-tested with a synthetic vocabulary in
NativeStrataTokenizerHelperTests.py; no real model weights are used in this checkpoint. In this
cloud build the donor `regex`/`jinja2` dependencies are not installed, so the helper is verified to
fail closed with the exact dependency cause rather than fabricate token ids.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import sys

_DEPENDENCY_CAUSE = None
try:
    sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
    from strata_tokenizer import Tokenizer, PromptEncoder  # type: ignore
except Exception as exc:  # pragma: no cover - dependency gate
    _DEPENDENCY_CAUSE = "missing donor tokenizer dependency (tools/strata_tokenizer.py requires 'regex'): %s" % (exc,)
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


def tokenizer_identity(tok) -> str:
    """Deterministic identity hash of the tokenizer's verified metadata (not weight shards)."""
    h = hashlib.sha256()
    h.update(b"strata-tokenizer-v1")
    h.update(tok.pre.encode("utf-8"))
    h.update(len(tok.tokens).to_bytes(8, "little"))
    for t in sorted(tok.special_tokens, key=lambda s: tok.special_tokens[s]):
        h.update(t.encode("utf-8"))
        h.update(tok.special_tokens[t].to_bytes(8, "little"))
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
                    ids = render_messages(tok, encoder, data["messages"])
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


def render_messages(tok, encoder, messages) -> list[int]:
    """Render chat messages through the model's own chat template when available.

    The donor server renders the chat template in Python (Jinja). Reimplementing a general Jinja
    renderer here would be a guessed template, which is forbidden. Therefore the helper renders the
    model's stored `tokenizer.chat_template` only with a supported Jinja renderer; otherwise it fails
    closed for THIS request with the exact dependency cause.
    """
    try:
        import jinja2  # type: ignore
    except Exception as exc:
        raise ValueError("chat template renderer dependency (jinja2) is unavailable: %s" % (exc,))
    template_text = getattr(tok, "chat_template", None)
    if template_text is None:
        raise ValueError("model tokenizer carries no chat_template; cannot render a chat prompt")
    if "{%" not in template_text:
        raise ValueError("chat template is not a Jinja template; refusing to guess role semantics")
    template = jinja2.Environment().from_string(template_text)
    rendered = template.render(messages=messages, add_generation_prompt=True, **{"tools": None})
    return tok.encode(rendered, parse_special=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--gguf", help="GGUF model file carrying the tokenizer metadata")
    parser.add_argument("--tokenizer-dir", help="extracted tokenizer/ directory (pack layout)")
    parser.add_argument("--expected-identity", help="registered model tokenizer identity to compare")
    args = parser.parse_args()

    if Tokenizer is None or PromptEncoder is None:
        sys.stderr.write("dependency: %s\n" % _DEPENDENCY_CAUSE)
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
            tok.chat_template = (d / "chat_template.jinja").read_text(encoding="utf-8") \
                if (d / "chat_template.jinja").exists() else None
        elif args.gguf:
            tok = Tokenizer.from_gguf(args.gguf)
        else:
            sys.stderr.write("dependency: one of --gguf or --tokenizer-dir is required\n")
            return 3
    except Exception as exc:
        sys.stderr.write("dependency: tokenizer load failed: %s\n" % (exc,))
        return 3

    return serve(tok, PromptEncoder(tok), args.expected_identity)


if __name__ == "__main__":
    raise SystemExit(main())
