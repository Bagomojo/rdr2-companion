"""Which companion the bridge plays, from the folders in bridge/companions.

Each folder holds companion.ini (name, voice, behavior) and persona.md (who he is). The mod picks
one with Companion= in DaveCompanion.ini and starts the bridge with it; when the bridge is started
by hand, COMPANION in bridge/.env picks it. Default: dave.
"""

import configparser
import os
from pathlib import Path

HERE = Path(__file__).parent / "companions"
DEFAULT = "dave"


def available() -> list[str]:
    return sorted(p.name for p in HERE.iterdir() if (p / "persona.md").exists()) if HERE.exists() else []


def _load(name: str) -> dict:
    folder = HERE / name
    ini = configparser.ConfigParser(inline_comment_prefixes=(";",))
    ini.read(folder / "companion.ini", encoding="utf-8")
    c = ini["companion"] if ini.has_section("companion") else {}
    get = lambda key, default="": (c.get(key) or "").strip() or default
    yes = lambda key, default: get(key, "yes" if default else "no").lower() in ("yes", "true", "1", "on")
    return {
        "id": name,
        "dir": folder,
        "name": get("name", name.title()),
        "voice_id": get("voice_id"),
        "voice_volume": get("voice_volume"),
        "comedy": yes("comedy", False),
        "comment_cooldown_s": float(get("comment_cooldown_s", "0")),
        "keeps_secret": yes("keeps_secret", False),
        "can_leave": yes("can_leave", False),
    }


NAME = (os.environ.get("RDR2_COMPANION") or os.environ.get("COMPANION")
        or os.environ.get("COMPANION_PERSONA") or DEFAULT).strip().lower()
if NAME not in available():
    raise SystemExit(f"No companion '{NAME}' in bridge/companions. Choose from: {', '.join(available())}")
_current = _load(NAME)


def current() -> dict:
    return _current
