"""Builds each request: frozen rules and persona first (cached), live state last."""

import json
import re
from pathlib import Path

import personas

HERE = Path(__file__).parent
RULES = HERE / "prompts" / "rules.md"


def _persona_text() -> str:
    text = (HERE / "persona" / personas.current()["file"]).read_text(encoding="utf-8")
    # An "Open for Dave" section holds editing notes, not character: drop just that section.
    return re.sub(r"## Open for Dave.*?(?=\n## |\Z)", "", text, flags=re.S).rstrip()


def system_blocks() -> list[dict]:
    """The frozen prefix. It must not change between calls or the cache misses."""
    text = RULES.read_text(encoding="utf-8").rstrip() + "\n\n# The companion's persona\n\n" + _persona_text()
    return [{"type": "text", "text": text, "cache_control": {"type": "ephemeral"}}]


def user_message(state: dict, snapshot: dict) -> dict:
    # sort_keys keeps the serialization stable, which keeps anything cacheable stable.
    body = (
        "STATE\n"
        + json.dumps({k: v for k, v in state.items() if k != "summarized_through"}, indent=1, sort_keys=True)
        + "\n\nSNAPSHOT\n"
        + json.dumps(snapshot, indent=1, sort_keys=True)
        + "\n\nDecide his next move."
    )
    return {"role": "user", "content": body}
