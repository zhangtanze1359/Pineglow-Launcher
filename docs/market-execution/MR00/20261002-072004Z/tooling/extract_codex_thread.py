"""Extract user/assistant messages from a Codex rollout jsonl.

Reads the file with shared-read access (Codex may still hold a write lock)
and writes a readable transcript to a .txt file for inspection.
"""
import json
import sys
import os


def read_lines_shared(path):
    import msvcrt
    with open(path, "rb") as fh:
        # try plain read first, then share-deny-none style fallback
        try:
            data = fh.read()
            return data.decode("utf-8", "replace").splitlines()
        except OSError:
            pass
    # fallback: open via os.open and read in chunks
    fd = os.open(path, os.O_RDONLY)
    try:
        chunks = []
        while True:
            buf = os.read(fd, 1 << 20)
            if not buf:
                break
            chunks.append(buf)
    finally:
        os.close(fd)
    return b"".join(chunks).decode("utf-8", "replace").splitlines()


def text_of(item):
    """Best-effort extraction of human-readable text from a response_item."""
    payload = item.get("payload") or item
    role = payload.get("role")
    ptype = payload.get("type")
    out = []

    if ptype == "message":
        for c in payload.get("content") or []:
            ct = c.get("type")
            if ct in ("input_text", "output_text", "text", "summary_text"):
                out.append(c.get("text") or "")
            elif ct == "input_image":
                out.append("[image omitted]")
    elif ptype == "reasoning":
        summ = payload.get("summary") or []
        for s in summ:
            if isinstance(s, dict):
                out.append("[reasoning] " + (s.get("text") or ""))
    elif ptype == "function_call":
        out.append("[call %s] %s" % (payload.get("name"), (payload.get("arguments") or "")[:1200]))
    elif ptype == "function_call_output":
        o = payload.get("output")
        if not isinstance(o, str):
            o = json.dumps(o, ensure_ascii=False)
        out.append("[result] " + o[:1500])
    elif ptype == "local_shell_call":
        out.append("[shell] " + json.dumps(payload.get("action") or {}, ensure_ascii=False)[:1200])
    return role, ptype, "\n".join(x for x in out if x)


def main():
    src = sys.argv[1]
    dst = sys.argv[2]
    lines = read_lines_shared(src)
    buf = []
    for i, ln in enumerate(lines):
        if not ln.strip():
            continue
        try:
            o = json.loads(ln)
        except Exception as e:
            buf.append("\n===== line %d (UNPARSEABLE: %s) len=%d =====\n%s\n" % (i, e, len(ln), ln[:600]))
            continue
        t = o.get("type")
        if t == "response_item":
            role, ptype, txt = text_of(o)
            if not txt:
                continue
            if ptype == "message":
                buf.append("\n===== line %d  MESSAGE role=%s =====\n%s\n" % (i, role, txt))
            elif ptype == "reasoning":
                buf.append("\n----- line %d  REASONING -----\n%s\n" % (i, txt))
            elif ptype in ("function_call", "function_call_output", "local_shell_call"):
                buf.append("\n----- line %d  %s -----\n%s\n" % (i, ptype, txt))
        elif t == "event_msg":
            ep = o.get("payload") or {}
            et = ep.get("type")
            if et in ("user_message", "agent_message", "agent_reasoning"):
                buf.append("\n***** line %d  EVENT %s *****\n%s\n" % (i, et, ep.get("message") or ep.get("text") or ""))
    with open(dst, "w", encoding="utf-8") as f:
        f.write("SOURCE: %s\nlines: %d\n" % (src, len(lines)))
        f.write("\n".join(buf))
    print("wrote %s (%d bytes) from %d lines" % (dst, os.path.getsize(dst), len(lines)))


if __name__ == "__main__":
    main()
