"""Which companion the bridge plays. Pick one with COMPANION_PERSONA (default: dave)."""

import os

PERSONAS = {
    # Comic-relief sidekick, a movie-quoting guy from the present day. No secret past, never leaves.
    "dave": {"file": "dave.md", "name": "Dave", "keeps_secret": False, "can_leave": False,
             # Comedy wants the stronger model on quiet moments, and a cooldown so he doesn't over-talk.
             "comedy": True, "comment_cooldown_s": 75},
    # The original: a 2015 IT guy hiding where he's from, with a hard moral line.
    "kit": {"file": "kit-dorsey.md", "name": "Kit", "keeps_secret": True, "can_leave": True,
            "comedy": False, "comment_cooldown_s": 0},
}

NAME = os.environ.get("COMPANION_PERSONA", "dave").lower()
if NAME not in PERSONAS:
    raise SystemExit(f"Unknown COMPANION_PERSONA '{NAME}'. Choose from: {', '.join(PERSONAS)}")


def current() -> dict:
    return PERSONAS[NAME]
