"""Rules the bridge works out itself, so the model doesn't have to infer them.

The results go into the snapshot as `bridge_flags`. The model treats them as fact.
"""

import personas

EARSHOT_M = 30
# Nearby things that can't overhear Kit.
NOT_PEOPLE = ("horse", "animal", "dog", "wolf", "deer", "bear", "cougar", "bird", "cow", "light")
CALM_EVENTS = {"camp_set_up", "campfire", "heartbeat", "hotkey"}


def _is_person(p: dict) -> bool:
    kind = p.get("kind", "").lower()
    return not any(word in kind for word in NOT_PEOPLE)


def compute(state: dict, snapshot: dict) -> dict:
    nearby = snapshot.get("nearby", [])
    companion = snapshot.get("companion", {})
    world = snapshot.get("world", {})

    strangers_in_earshot = any(
        _is_person(p) and p.get("state") != "dead" and p.get("distance_m", 999) <= EARSHOT_M
        for p in nearby
    )
    hostiles_near = any(p.get("hostile") for p in nearby)
    at_camp = companion.get("current_action") == "idle" or snapshot.get("event") == "camp_set_up"
    calm = not hostiles_near and companion.get("health") != "critical" and snapshot.get("event") in CALM_EVENTS

    persona = personas.current()
    truth_scene_allowed = (
        persona["keeps_secret"]
        and not state.get("truth_told")
        and state.get("trust", 0) >= 60
        and not strangers_in_earshot
        and at_camp
        and calm
        and not world.get("story_mission_active")
    )

    if snapshot.get("companion", {}).get("drunk") in ("drunk", "hammered"):
        voice = "drunk"
    elif not persona["keeps_secret"]:
        voice = "own"
    elif strangers_in_earshot or not state.get("truth_told"):
        voice = "period"
    else:
        voice = "own"

    return {
        "strangers_in_earshot": strangers_in_earshot,
        "truth_scene_allowed": truth_scene_allowed,
        "voice": voice,
        "may_leave": persona["can_leave"] and state.get("trust", 0) < 15,
    }
