"""Bounded exact tool receipts; working context can shrink without erasing history."""
import json

MAX_RECEIPTS = 12
MAX_CACHE_CHARS = 90000
MAX_RECEIPT_CHARS = 24000

def is_source_read(operation):
    if not isinstance(operation, dict):
        return False
    if operation.get("action") in ("read", "search", "list"):
        return True
    children = operation.get("actions")
    return operation.get("action") == "batch" and isinstance(children, list) and bool(children) and all(is_source_read(child) for child in children)

def cached_receipt(receipts, signature):
    return next((item for item in reversed(receipts) if item.get("signature") == signature), None)

def remember_receipt(receipts, signature, operation, result):
    if not is_source_read(operation) or not isinstance(result, str):
        return list(receipts)
    item = {"signature": signature, "operation": operation, "result": result if len(result) <= MAX_RECEIPT_CHARS else result[:MAX_RECEIPT_CHARS-100] + "\n[Observed response truncated to cache bound; request a narrower read for omitted content.]"}
    result = [x for x in receipts if x.get("signature") != signature] + [item]
    while len(result) > MAX_RECEIPTS or sum(len(x["result"]) for x in result) > MAX_CACHE_CHARS:
        result.pop(0)
    return result

def receipt_is_visible(receipt, messages):
    if not receipt or not receipt.get("result"):
        return False
    return any(receipt["result"] in message.get("content", "") for message in messages)

def read_recovery(receipts, signature, messages, repeat_counts):
    """Return exact cached source even after eviction from the model context.

    Repeatedly requesting a response already fully visible remains bounded.
    An old action count alone cannot prove that its source is still visible.
    """
    receipt = cached_receipt(receipts, signature)
    visible = receipt_is_visible(receipt, messages)
    count = repeat_counts.get(signature, 0) + 1 if visible else 0
    repeat_counts[signature] = count
    if count >= 2:
        raise RuntimeError("repeated source read whose exact receipt is already visible; context retained")
    # Directory discovery has no single file revision: retain it as observed
    # evidence, but rerun rather than replay a potentially changed directory.
    operation = receipt.get("operation", {}) if receipt else {}
    children = operation.get("actions", []) if operation.get("action") == "batch" else [operation]
    replayable = bool(children) and all(child.get("action") == "read" for child in children)
    return receipt["result"] if receipt and replayable else None

def receipt_excerpt(text, limit=3000):
    if len(text) <= limit:
        return text
    lines = text.splitlines()
    declarations = [line for line in lines if any(word in line for word in (
        " class ", " record ", " interface ", "public ", "internal ", "private ", "protected ", "[Fact", "[Theory", "[AvaloniaFact"))]
    body = "\n".join(declarations)[:max(0, limit - 1400)]
    return text[:600] + "\n[Receipt excerpt; exact full response can be restored by the same read.]\n" + body + "\n" + text[-600:]

def compact_history(history, receipts, notes, diff, budget=22000):
    """Construct a working view; never mutate canonical history or receipts."""
    rendered = []
    used = 0
    for item in reversed(receipts):
        block = json.dumps(item["operation"], ensure_ascii=False) + "\n" + receipt_excerpt(item["result"])
        if used + len(block) > budget:
            continue
        rendered.append(block)
        used += len(block)
    text = ("PERSISTENT FINDINGS\n" + notes[:4000] + "\nCURRENT OWNED DIFF\n" + diff[:12000] +
            "\nRETAINED DEPENDENCY READ EVIDENCE\n" + "\n\n".join(reversed(rendered)) +
            "\nEarlier source reads are cached. A repeated exact read restores its full observed response; "
            "do not broaden discovery. Implement the concrete scoped change using observed APIs.")
    return [{"role": "user", "content": text}, *[dict(message) for message in history[-4:]]]

def focused_source(text, patterns, radius=12, maximum_lines=300):
    lines = text.splitlines()
    keep = set(range(min(32, len(lines))))
    for index, line in enumerate(lines):
        if any(pattern.casefold() in line.casefold() for pattern in patterns):
            keep.update(range(max(0, index-radius), min(len(lines), index+radius+1)))
    selected = sorted(keep)[:maximum_lines]
    result = []
    previous = -1
    for index in selected:
        if index != previous + 1:
            result.append(f"[Omitted lines {previous+2}-{index}; use a targeted read if needed.]")
        result.append(f"{index+1}: {lines[index]}")
        previous = index
    if previous < len(lines)-1:
        result.append(f"[Omitted lines {previous+2}-{len(lines)}; use a targeted read if needed.]")
    return "\n".join(result) + f"\nTotal lines: {len(lines)}"
