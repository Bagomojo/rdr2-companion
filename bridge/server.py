"""Local HTTP bridge for the mod. Listens on 127.0.0.1 only.

POST /decide with {"snapshot": {...}} returns one decision object.
The bridge owns state (trust, respect, flags, journal) and logs every turn.

Usage: .venv\\Scripts\\python.exe server.py [mix|haiku|opus]   (default: mix)
"""

import json
import os
import random
import re
import sys
import threading
import time
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


class _Tee:
    """Started by hand, the bridge prints to its window; keep a copy in logs/console.txt as well,
    so there's always a log to attach to a bug report. (Started by the mod, it's already there.)"""

    def __init__(self, stream, copy):
        self.stream, self.copy = stream, copy

    def write(self, text):
        self.stream.write(text)
        self.copy.write(text)
        self.copy.flush()

    def flush(self):
        self.stream.flush()


if sys.stdout.isatty():
    (Path(__file__).parent / "logs").mkdir(exist_ok=True)
    _console = open(Path(__file__).parent / "logs" / "console.txt", "w", encoding="utf-8")
    sys.stdout, sys.stderr = _Tee(sys.stdout, _console), _Tee(sys.stderr, _console)

from version import VERSION

print(f"Companion bridge v{VERSION}, {datetime.now():%Y-%m-%d %H:%M}", flush=True)

import anthropic

from keys import anthropic_key, elevenlabs_key  # first: it loads bridge/.env for the rest
import flags
import memory
import personas
from decide import FALLBACK_ACTION, decide
from voice import Voice

HOST, PORT = "127.0.0.1", 7777
HERE = Path(__file__).parent
STATE_FILE = HERE / "state" / f"{personas.NAME}.json"
LOG_DIR = HERE / "logs"

DEFAULT_STATE = {
    "trust": 40,
    "respect": 30,
    "truth_told": False,
    "departures": 0,
    "line_strikes": 0,
    "player_name": "Arthur Morgan",
    "journal": [],
    "sessions": 0,
}

MODEL = sys.argv[1] if len(sys.argv) > 1 else "mix"
last_comment_at = float("-inf")
warned_profile = False  # told the player once that the mod asked for another companion
greeted = False  # has this bridge run already said hello? A fresh bridge means a fresh session.
said_lately: list[str] = []  # survives re-summons, unlike the mod's recent_lines
print("Reading the API keys...", flush=True)
client = anthropic.Anthropic(api_key=anthropic_key())
try:
    voice = Voice(elevenlabs_key())
    print(f"{personas.current()['name']}'s voice is on.", flush=True)
except Exception as e:  # no voice key means subtitles only, not a dead bridge
    voice = None
    print(f"{personas.current()['name']}'s voice is off (subtitles only): {e}", flush=True)


def load_state() -> dict:
    if STATE_FILE.exists():
        return {**DEFAULT_STATE, **json.loads(STATE_FILE.read_text(encoding="utf-8"))}
    return dict(DEFAULT_STATE)


def save_state(state: dict) -> None:
    STATE_FILE.parent.mkdir(exist_ok=True)
    STATE_FILE.write_text(json.dumps(state, indent=1), encoding="utf-8")


def apply(state: dict, d: dict) -> dict:
    state["trust"] = max(0, min(100, state["trust"] + d["trust_delta"]))
    state["respect"] = max(0, min(100, state["respect"] + d["respect_delta"]))
    if d["reveals_truth"]:
        state["truth_told"] = True
    if d["action"] == "leave":
        state["departures"] += 1
    if d["memory_note"]:
        state["journal"].append(d["memory_note"])
    return state


AUDIO_TAGS = {"laughs", "chuckles", "laughs harder", "starts laughing", "wheezing", "sighs",
              "exhales", "snorts", "sarcastic", "excited", "tired", "groans", "gasps",
              "mischievously", "hiccups", "burps", "drunk", "curious", "annoyed", "happy"}
TAG = re.compile(r"\[([a-z ]{2,24})\]")


def keep_known_tags(line: str) -> str:
    return re.sub(r"\s{2,}", " ", TAG.sub(lambda m: m.group(0) if m.group(1) in AUDIO_TAGS else "", line)).strip()


def strip_tags(line: str) -> str:
    return re.sub(r"\s{2,}", " ", TAG.sub("", line)).strip()


# Left alone, every line came out as one of his two classics. They stay as two kinds among many:
# the bridge picks what kind of line he says this turn, weighted, never the same kind as the last two.
LINE_STYLES = {
    "quote": (3, "a movie quote that fits, said straight: no setup, no movie name"),
    "plain": (3, "a dry comment in his own words, no movie and no fact"),
    "modern": (2, "a comparison to something specific from modern life"),
    "roast": (2, "ribbing Arthur about what he's doing right now"),
    "short": (2, "a tiny reaction, three to six words"),
    "wright": (2, "a new deadpan, surreal one-liner in the style of Steven Wright, about something here"),
    "fact": (1, "one useless, true, oddly specific fact, no movie"),
    "body": (1, "a complaint about his own body or comfort"),
    "question": (1, "a short absurd question, no movie"),
    "misquote": (1, "a famous quote mangled, or credited to the wrong movie"),
    "callback": (1, "a callback to something in his journal"),
    "classic": (3, 'his classic bit: an observation, then "Very <movie title>.", then a quote from that movie'),
    "classic_fact": (2, "his other classic: an observation, then a random fact, then an unrelated movie quote"),
}
STYLE_EXEMPT = {"campfire", "reunion", "hotkey"}
recent_styles: list[str] = []


def pick_line_style(state: dict) -> str:
    names = [n for n in LINE_STYLES if n not in recent_styles[-2:]]
    if not state.get("journal"):
        names = [n for n in names if n != "callback"]
    style = random.choices(names, weights=[LINE_STYLES[n][0] for n in names])[0]
    recent_styles.append(style)
    del recent_styles[:-4]
    return style


def _ngrams(text: str, n: int = 4) -> set:
    words = re.findall(r"[a-z']+", strip_tags(text).lower())
    return {tuple(words[i:i + n]) for i in range(len(words) - n + 1)}


def repeats(line: str, earlier: list[str]) -> bool:
    """True if the line reuses four words in a row from anything he said before (a quote, a bit)."""
    grams = _ngrams(line)
    return bool(grams) and any(grams & _ngrams(e) for e in earlier)


def is_new_session() -> bool:
    turns = memory.turns_after(datetime.fromtimestamp(time.time() - memory.SESSION_GAP_S, timezone.utc).isoformat())
    return not turns


def recap_when_quiet() -> None:
    """Background: once the game has gone quiet for a session gap, write the session to memory."""
    while True:
        time.sleep(60)
        try:
            if memory.catch_up(client, load_state, save_state):
                print("Wrote the last session to memory.", flush=True)
        except Exception as e:  # memory is a nice-to-have; never take the bridge down for it
            print(f"Memory recap failed, will retry: {e}", flush=True)


def log(entry: dict) -> None:
    LOG_DIR.mkdir(exist_ok=True)
    path = LOG_DIR / f"{datetime.now():%Y-%m-%d}.jsonl"
    with path.open("a", encoding="utf-8") as f:
        f.write(json.dumps(entry) + "\n")


class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        if self.path != "/decide":
            self.send_error(404)
            return
        # Only the mod may talk to us. A web page can't send JSON here without a preflight we never
        # answer, and browsers always add Origin, so this keeps drive-by pages from spending credit.
        if self.headers.get("Origin") or not (self.headers.get("Content-Type") or "").startswith("application/json"):
            self.send_error(403)
            return
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        state = load_state()
        snapshot = body["snapshot"]
        global last_comment_at
        global greeted
        global warned_profile
        if snapshot.get("event") == "summoned" and state.get("sessions") and (not greeted or is_new_session()):
            # First summon after a break: a reunion, with what he remembers of last time.
            snapshot = {**snapshot, "event": "reunion",
                        "last_together": f"{memory.since_last_session(state)} ago"}
        cooldown = personas.current().get("comment_cooldown_s", 0)
        if snapshot.get("event") == "heartbeat" and time.monotonic() - last_comment_at < cooldown:
            # Too soon after his last comment: stay quiet without calling Claude at all.
            self._reply(dict(FALLBACK_ACTION, reason="comment cooldown"))
            return
        if snapshot.get("event") in ("summoned", "reunion"):
            greeted = True
        # The mod forgets its lines when he's re-summoned; the bridge doesn't.
        lines = snapshot.get("recent_lines") or []
        snapshot = {**snapshot, "recent_lines": (said_lately + [l for l in lines if l not in said_lately])[-12:]}
        if snapshot.get("profile") and snapshot["profile"] != personas.NAME and not warned_profile:
            warned_profile = True
            print(f"The mod wants companion '{snapshot['profile']}' but this bridge is playing "
                  f"'{personas.NAME}'. Restart the bridge (or the game) to switch.", flush=True)
        # The joke kinds are a comedy companion's; a straight one writes in his own voice.
        if personas.current()["comedy"] and snapshot.get("event") not in STYLE_EXEMPT:
            style = pick_line_style(state)
            snapshot = {**snapshot, "line_style": LINE_STYLES[style][1],
                        "address_arthur": random.random() < 0.33}
        # A story takes Opus a few seconds longer to write than a one-liner, and is long enough to
        # share four words with something by chance, so it skips the repeat guard (and its retry,
        # which would outlast the mod's 20 s wait).
        story = snapshot.get("event") == "campfire"
        result = decide(client, MODEL, state, snapshot, 15.0 if story else None)
        if not story and repeats(result.decision.get("say", ""), snapshot["recent_lines"] + state.get("used_lines", [])):
            retry = {**snapshot, "recent_events": snapshot.get("recent_events", []) + [
                f'his first try, "{result.decision["say"]}", repeated a line he already used; he says something new']}
            result = decide(client, MODEL, state, retry)
            if repeats(result.decision.get("say", ""), snapshot["recent_lines"] + state.get("used_lines", [])):
                result.decision = {**result.decision, "say": ""}
        # Audio tags like [laughs] are for the voice only; subtitles and memory get the plain line.
        spoken = keep_known_tags(result.decision.get("say", ""))
        result.decision = {**result.decision, "say": strip_tags(spoken)}
        if result.decision.get("say"):
            said_lately.append(result.decision["say"])
            del said_lately[:-12]
        if result.decision.get("say"):
            last_comment_at = time.monotonic()
        if not result.error and not result.problems:
            if voice and result.decision["say"]:
                # Queued before the reply goes back, so the audio starts as soon as possible.
                delivery = flags.compute(state, snapshot)["voice"]
                voice.speak(spoken, delivery, snapshot.get("companion", {}).get("distance_m", 0))
            with memory._lock:  # reload so a recap written meanwhile isn't overwritten
                save_state(apply(load_state(), result.decision))
        log({
            "at": datetime.now(timezone.utc).isoformat(),
            "model": result.model,
            "snapshot": snapshot,
            "decision": result.decision,
            "spoken": spoken,
            "latency_s": round(result.latency_s, 3),
            "usage": result.usage,
            "cost_usd": result.cost_usd,
            "error": result.error,
            "problems": result.problems,
        })
        if result.error:
            print(f"Claude call failed: {result.error[:300]}", flush=True)
            if "credit balance" in result.error:
                print("  Your Anthropic account is out of API credit. Add some at console.anthropic.com under"
                      " Billing; a Claude.ai Pro or Max subscription doesn't cover the API.", flush=True)
            elif "AuthenticationError" in result.error:
                print("  Check ANTHROPIC_API_KEY in bridge\\.env.", flush=True)
        self._reply(result.decision)

    def _reply(self, decision: dict) -> None:
        payload = json.dumps(decision).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def log_message(self, *args):
        pass


def quit_with_game(pid: int) -> None:
    """Background, when the mod started us: wait for the game to close, then stop too."""
    import ctypes
    kernel32 = ctypes.windll.kernel32
    handle = kernel32.OpenProcess(0x00100000, False, pid)  # SYNCHRONIZE: enough to wait on it
    if not handle:
        return
    kernel32.WaitForSingleObject(handle, 0xFFFFFFFF)
    print("The game closed, so the bridge is stopping too.", flush=True)
    os._exit(0)


if __name__ == "__main__":
    if os.environ.get("RDR2_GAME_PID", "").isdigit():
        threading.Thread(target=quit_with_game, args=(int(os.environ["RDR2_GAME_PID"]),), daemon=True).start()
    try:
        n = memory.catch_up(client, load_state, save_state, include_open=True)
        if n:
            print(f"Caught up memory from {n} earlier session(s).", flush=True)
    except Exception as e:
        print(f"Memory catch-up failed, will retry later: {e}", flush=True)
    threading.Thread(target=recap_when_quiet, daemon=True).start()
    print(f"Ready. {personas.current()['name']} on http://{HOST}:{PORT} using {MODEL}", flush=True)
    ThreadingHTTPServer((HOST, PORT), Handler).serve_forever()
