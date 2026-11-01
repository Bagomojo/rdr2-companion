"""The decision contract between Claude and the mod.

The mod only ever runs actions from ACTIONS. Claude's reply is constrained to
DECISION_SCHEMA through structured output, so it is always one valid object.
"""

ACTIONS = [
    "follow",
    "wait_here",
    "scout_ahead",
    "go_to",
    "mount_up",
    "dismount",
    "guard",
    "attack",
    "stay_out",
    "flee",
    "idle",
    "leave",
]

DELTAS = [-2, -1, 0, 1, 2]

DECISION_SCHEMA = {
    "type": "object",
    "properties": {
        "reason": {
            "type": "string",
            "description": "One short sentence, the companion's private reason. Never shown in game.",
        },
        "action": {"type": "string", "enum": ACTIONS},
        "target": {
            "type": "string",
            "description": "A nearby person id or landmark name, or empty string.",
        },
        "say": {
            "type": "string",
            "description": "Subtitle line, one or two sentences, or empty string for silence.",
        },
        "mood": {"type": "string", "description": "One lowercase word."},
        "trust_delta": {"type": "integer", "enum": DELTAS},
        "respect_delta": {"type": "integer", "enum": DELTAS},
        "memory_note": {
            "type": "string",
            "description": "One line worth remembering long-term, or empty string.",
        },
        "reveals_truth": {
            "type": "boolean",
            "description": "True only on the one turn the companion tells the player he is from 2015.",
        },
    },
    "required": [
        "reason",
        "action",
        "target",
        "say",
        "mood",
        "trust_delta",
        "respect_delta",
        "memory_note",
        "reveals_truth",
    ],
    "additionalProperties": False,
}


def validate(decision: dict, max_say: int = 240) -> list[str]:
    """Belt-and-braces check on top of structured output. Returns problems found."""
    problems = []
    if decision.get("action") not in ACTIONS:
        problems.append(f"unknown action {decision.get('action')!r}")
    for key in ("trust_delta", "respect_delta"):
        if decision.get(key) not in DELTAS:
            problems.append(f"{key} out of range")
    if len(decision.get("say", "")) > max_say:
        problems.append("say line too long")
    return problems
