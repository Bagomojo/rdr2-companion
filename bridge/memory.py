"""What the companion remembers between sessions (milestone 5).

Every turn is already in the daily logs. Once a session is over (no turns for SESSION_GAP_S),
Opus reads it back and rewrites his journal, nudges trust and respect, and the bridge keeps
the lines he has used so he doesn't recycle them next time. Sessions the bridge never got to
recap (it was closed first) are caught up when it starts.
"""

import json
import threading
import time
from datetime import datetime, timezone
from pathlib import Path

import anthropic

from decide import MODELS, _cost
from prompt import _persona_text

HERE = Path(__file__).parent
LOG_DIR = HERE / "logs"
RECAP_RULES = HERE / "prompts" / "recap.md"
SESSION_GAP_S = 20 * 60
JOURNAL_MAX = 20
USED_LINES_MAX = 60
_lock = threading.Lock()

RECAP_SCHEMA = {
    "type": "object",
    "properties": {
        "session_summary": {"type": "string", "description": "Two sentences at most, in his voice."},
        "journal": {"type": "array", "items": {"type": "string"}},
        "trust_delta": {"type": "integer", "enum": [-3, -2, -1, 0, 1, 2, 3]},
        "respect_delta": {"type": "integer", "enum": [-3, -2, -1, 0, 1, 2, 3]},
    },
    "required": ["session_summary", "journal", "trust_delta", "respect_delta"],
    "additionalProperties": False,
}


def _at(entry: dict) -> float:
    return datetime.fromisoformat(entry["at"]).timestamp()


def turns_after(stamp: str) -> list[dict]:
    turns = []
    for path in sorted(LOG_DIR.glob("20*.jsonl")):
        for line in path.read_text(encoding="utf-8").splitlines():
            entry = json.loads(line)
            if entry["at"] > stamp:
                turns.append(entry)
    return turns


def split_sessions(turns: list[dict]) -> list[list[dict]]:
    sessions = []
    for t in turns:
        if sessions and _at(t) - _at(sessions[-1][-1]) < SESSION_GAP_S:
            sessions[-1].append(t)
        else:
            sessions.append([t])
    return sessions


def _transcript(turns: list[dict]) -> str:
    lines = []
    for t in turns[-200:]:
        s, d = t["snapshot"], t["decision"]
        when = datetime.fromisoformat(t["at"]).astimezone().strftime("%H:%M")
        what = s.get("event") or ""
        if s.get("hotkey"):
            what += f" ({s['hotkey']})"
        latest = (s.get("recent_events") or [""])[-1]
        world = s.get("world", {})
        place = world.get("town") or world.get("region") or ""
        row = f"{when} [{what}] {place}: {latest}"
        if d.get("say"):
            row += f' | he said: "{d["say"]}"'
        lines.append(row)
    return "\n".join(lines)


def recap(client: anthropic.Anthropic, state: dict, turns: list[dict]) -> dict:
    """One Opus call that turns a finished session into memory. Returns the updated state."""
    cfg = MODELS["opus"]
    system = RECAP_RULES.read_text(encoding="utf-8").rstrip() + "\n\n# His persona\n\n" + _persona_text()
    known = {k: state.get(k) for k in ("trust", "respect", "journal", "last_session", "sessions")}
    body = "WHAT HE REMEMBERS SO FAR\n" + json.dumps(known, indent=1) + "\n\nTHE SESSION JUST ENDED\n" + _transcript(turns)
    response = client.with_options(timeout=60, max_retries=2).messages.create(
        model=cfg["id"],
        max_tokens=4096,
        system=system,
        messages=[{"role": "user", "content": body}],
        output_config={"effort": "medium", "format": {"type": "json_schema", "schema": RECAP_SCHEMA}},
    )
    text = next(b.text for b in response.content if b.type == "text")
    r = json.loads(text)
    u = response.usage
    usage = {
        "input_tokens": u.input_tokens or 0,
        "output_tokens": u.output_tokens or 0,
        "cache_read_input_tokens": u.cache_read_input_tokens or 0,
        "cache_creation_input_tokens": u.cache_creation_input_tokens or 0,
    }

    said = [t["decision"]["say"] for t in turns if t["decision"].get("say")]
    ended = datetime.fromisoformat(turns[-1]["at"]).astimezone()
    state["trust"] = max(0, min(100, state["trust"] + r["trust_delta"]))
    state["respect"] = max(0, min(100, state["respect"] + r["respect_delta"]))
    state["journal"] = [j for j in r["journal"] if j.strip()][-JOURNAL_MAX:]
    state["used_lines"] = (state.get("used_lines", []) + said)[-USED_LINES_MAX:]
    state["sessions"] = state.get("sessions", 0) + 1
    state["last_session"] = {"ended": ended.strftime("%Y-%m-%d %H:%M"), "summary": r["session_summary"]}
    state["summarized_through"] = turns[-1]["at"]

    LOG_DIR.mkdir(exist_ok=True)
    with (LOG_DIR / "recaps.jsonl").open("a", encoding="utf-8") as f:
        f.write(json.dumps({"at": datetime.now(timezone.utc).isoformat(), "turns": len(turns),
                            "recap": r, "usage": usage, "cost_usd": _cost(cfg, usage)}) + "\n")
    return state


def catch_up(client: anthropic.Anthropic, load_state, save_state, include_open: bool = False) -> int:
    """Recap every finished session not yet in memory. Returns how many were recapped.

    The newest session counts as finished once it has been quiet for SESSION_GAP_S, or always
    when include_open is set (bridge startup: the game isn't talking to us yet).
    """
    with _lock:
        state = load_state()
        sessions = split_sessions(turns_after(state.get("summarized_through", "")))
        if sessions and not include_open and time.time() - _at(sessions[-1][-1]) < SESSION_GAP_S:
            sessions.pop()
        for s in sessions:
            state = recap(client, state, s)
            save_state(state)
        return len(sessions)


def since_last_session(state: dict) -> str:
    """'3 days', '5 hours', '10 minutes', or '' if there is no previous session."""
    last = state.get("last_session")
    if not last:
        return ""
    gap = datetime.now() - datetime.strptime(last["ended"], "%Y-%m-%d %H:%M")
    if gap.days >= 1:
        return f"{gap.days} day{'s' if gap.days > 1 else ''}"
    hours = gap.seconds // 3600
    if hours:
        return f"{hours} hour{'s' if hours > 1 else ''}"
    minutes = max(1, gap.seconds // 60)
    return f"{minutes} minute{'s' if minutes > 1 else ''}"
