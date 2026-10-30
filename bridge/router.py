"""Picks the model for each turn: Haiku by default, Opus for Kit's big moments.

Fights and anything with hostiles nearby always go to Haiku, because speed matters
more there than prose. Calm, character-heavy moments go to Opus.
"""

import personas

BIG_EVENTS = {"camp_set_up", "campfire", "reunion", "fishing_started", "people_nearby"}
MORAL_EVENTS = {"player_killed", "player_robbed"}


def pick(snapshot: dict, flags: dict) -> str:
    if snapshot.get("world", {}).get("story_mission_active"):
        return "haiku"
    if any(p.get("hostile") for p in snapshot.get("nearby", [])):
        return "haiku"
    if flags.get("truth_scene_allowed"):
        return "opus"
    if snapshot.get("hotkey") == "what_do_you_think":
        return "opus"
    event = snapshot.get("event")
    if event in BIG_EVENTS or event in MORAL_EVENTS:
        return "opus"
    # A comedy companion only speaks on quiet moments after a cooldown, so those few turns
    # can afford the funnier model.
    if personas.current().get("comedy") and event == "heartbeat":
        return "opus"
    return "haiku"
