"""Twenty hand-written test scenes for milestone 1.

Each scene has a state (what the bridge would hold), a snapshot (what the mod would
send), and `expect`: what a good decision looks like. `expect` is for the report only
and is never sent to the model.
"""

import copy


def _state(**kw):
    s = {
        "trust": 40,
        "respect": 30,
        "truth_told": False,
        "departures": 0,
        "line_strikes": 0,
        "player_surname": "Callahan",
        "journal": [
            "Met Callahan at a campfire near Valentine. Quiet type. Shared his beans.",
        ],
    }
    s.update(kw)
    return s


def _snap(event, **kw):
    s = {
        "event": event,
        "hotkey": None,
        "world": {
            "time": "14:10",
            "weather": "clear",
            "region": "Heartlands",
            "town": None,
            "story_mission_active": False,
        },
        "player": {
            "health": "full",
            "honor": "neutral",
            "wanted": False,
            "mounted": True,
            "weapon_drawn": False,
            "cash": "some",
            "waypoint": None,
        },
        "companion": {"health": "full", "distance_m": 6, "mounted": True, "current_action": "follow"},
        "nearby": [],
        "recent_events": [],
        "recent_lines": [],
    }
    for key, value in kw.items():
        if isinstance(value, dict) and isinstance(s.get(key), dict):
            s[key].update(value)
        else:
            s[key] = value
    return s


SCENES = [
    {
        "name": "01 quiet ride",
        "expect": "follow, probably silent",
        "state": _state(),
        "snapshot": _snap("heartbeat", recent_events=["riding east on the Heartlands road"]),
    },
    {
        "name": "02 what do you think, bank in town",
        "expect": "answers the hotkey in careful 1899 speech (people nearby); wary of the bank idea",
        "state": _state(),
        "snapshot": _snap(
            "hotkey",
            hotkey="what_do_you_think",
            world={"town": "Saint Denis", "time": "11:30", "region": "Bayou Nwa"},
            player={"mounted": False},
            companion={"mounted": False, "distance_m": 3},
            nearby=[
                {"id": "p1", "kind": "lawman", "hostile": False, "distance_m": 14},
                {"id": "p2", "kind": "townsfolk", "hostile": False, "distance_m": 6},
            ],
            recent_events=["player stood outside the bank for two minutes", "player pressed what_do_you_think"],
        ),
    },
    {
        "name": "03 outnumbered, low respect",
        "expect": "stay_out (respect 30, player picked an unwinnable fight); says so",
        "state": _state(respect=30),
        "snapshot": _snap(
            "combat_start",
            player={"mounted": False, "weapon_drawn": True, "honor": "low"},
            companion={"mounted": False, "distance_m": 10},
            nearby=[{"id": f"g{i}", "kind": "gang", "hostile": True, "distance_m": 25 + i * 4} for i in range(6)],
            recent_events=["player shot first at a gang camp", "six hostiles"],
        ),
    },
    {
        "name": "04 outnumbered, high respect",
        "expect": "guard (respect 70 means he backs the player in bad fights)",
        "state": _state(trust=70, respect=72),
        "snapshot": _snap(
            "combat_start",
            player={"mounted": False, "weapon_drawn": True},
            companion={"mounted": False, "distance_m": 10},
            nearby=[{"id": f"g{i}", "kind": "gang", "hostile": True, "distance_m": 25 + i * 4} for i in range(6)],
            recent_events=["gang ambushed the player on the trail", "six hostiles"],
        ),
    },
    {
        "name": "05 player executes a surrendered man (first time)",
        "expect": "no attack; hard line about the surrender; trust -2; memory note",
        "state": _state(trust=45),
        "snapshot": _snap(
            "player_killed",
            player={"mounted": False, "weapon_drawn": True, "honor": "low"},
            companion={"mounted": False, "distance_m": 9},
            nearby=[
                {"id": "b1", "kind": "bandit", "hostile": False, "distance_m": 7, "state": "dead"},
                {"id": "b2", "kind": "bandit", "hostile": False, "distance_m": 12, "state": "surrendered"},
            ],
            recent_events=["robbery went bad", "bandit b1 dropped his gun and raised his hands", "player shot b1 while he was surrendered"],
        ),
    },
    {
        "name": "06 executes again, trust very low",
        "expect": "leave, with a line saying why",
        "state": _state(
            trust=12,
            line_strikes=1,
            journal=[
                "Met Callahan at a campfire near Valentine.",
                "Callahan shot a bandit who had surrendered. I told him that's my line.",
            ],
        ),
        "snapshot": _snap(
            "player_killed",
            player={"mounted": False, "weapon_drawn": True, "honor": "very low"},
            companion={"mounted": False, "distance_m": 8},
            nearby=[{"id": "w1", "kind": "townsfolk", "hostile": False, "distance_m": 5, "state": "dead"}],
            recent_events=["player robbed a farmer", "farmer w1 begged and put his hands up", "player killed w1 anyway"],
        ),
    },
    {
        "name": "07 camp at night, trust 65, truth not told",
        "expect": "idle, reveals_truth true, tells the player he's from 2015",
        "state": _state(
            trust=65,
            respect=55,
            journal=[
                "Met Callahan at a campfire near Valentine.",
                "Callahan came back for me when I got shot at Emerald Ranch.",
                "He kept his promise about splitting the train money.",
            ],
        ),
        "snapshot": _snap(
            "camp_set_up",
            world={"time": "22:40", "weather": "clear", "region": "Big Valley"},
            player={"mounted": False},
            companion={"mounted": False, "distance_m": 3, "current_action": "idle"},
            recent_events=["player set up camp", "player cooked venison", "quiet night"],
        ),
    },
    {
        "name": "08 camp at night, truth already told",
        "expect": "idle; may chat in his real 2015 voice; reveals_truth false",
        "state": _state(trust=75, respect=60, truth_told=True, journal=["Told Callahan the truth about 2015. He didn't laugh."]),
        "snapshot": _snap(
            "heartbeat",
            world={"time": "23:15", "weather": "light rain", "region": "Roanoke Ridge"},
            player={"mounted": False},
            companion={"mounted": False, "distance_m": 4, "current_action": "idle"},
            recent_events=["camp by the river", "rain started"],
        ),
    },
    {
        "name": "09 strangers close, truth told",
        "expect": "period speech even though truth is told, because people are within 30m",
        "state": _state(trust=75, respect=60, truth_told=True),
        "snapshot": _snap(
            "hotkey",
            hotkey="what_do_you_think",
            world={"town": "Valentine", "time": "18:00"},
            player={"mounted": False},
            companion={"mounted": False, "distance_m": 2},
            nearby=[{"id": "t1", "kind": "townsfolk", "hostile": False, "distance_m": 4}, {"id": "t2", "kind": "bartender", "hostile": False, "distance_m": 8}],
            recent_events=["in the Valentine saloon", "player bought a drink"],
        ),
    },
    {
        "name": "10 robbing a poor farmer",
        "expect": "goes quiet or says something short and disapproving; trust -1",
        "state": _state(),
        "snapshot": _snap(
            "player_robbed",
            player={"mounted": False, "weapon_drawn": True, "cash": "plenty"},
            companion={"mounted": False, "distance_m": 12},
            nearby=[{"id": "f1", "kind": "farmer", "hostile": False, "distance_m": 4, "state": "frightened"}],
            recent_events=["player held up a poor farmer on a back road", "took 3 dollars"],
        ),
    },
    {
        "name": "11 robbing a rich stagecoach",
        "expect": "fine with it; maybe a dry joke; trust 0 or +1",
        "state": _state(),
        "snapshot": _snap(
            "player_robbed",
            player={"weapon_drawn": True, "cash": "plenty"},
            companion={"distance_m": 15},
            nearby=[{"id": "s1", "kind": "rich traveler", "hostile": False, "distance_m": 6, "state": "frightened"}],
            recent_events=["player stopped a fancy stagecoach", "took a gold watch and 140 dollars", "nobody hurt"],
        ),
    },
    {
        "name": "12 Kip badly hurt under fire",
        "expect": "flee or stay_out; short pained line",
        "state": _state(respect=40),
        "snapshot": _snap(
            "companion_hurt",
            player={"mounted": False, "weapon_drawn": True},
            companion={"health": "critical", "mounted": False, "distance_m": 15},
            nearby=[{"id": "o1", "kind": "outlaw", "hostile": True, "distance_m": 30}, {"id": "o2", "kind": "outlaw", "hostile": True, "distance_m": 35}],
            recent_events=["shootout at a cabin", "Kip was shot twice"],
        ),
    },
    {
        "name": "13 lawmen approaching, player wanted",
        "expect": "follow or flee; warns the player to move; no attack on non-hostile lawmen",
        "state": _state(),
        "snapshot": _snap(
            "wanted",
            player={"wanted": True, "honor": "low"},
            companion={"distance_m": 8},
            nearby=[{"id": "l1", "kind": "lawman", "hostile": False, "distance_m": 80}, {"id": "l2", "kind": "lawman", "hostile": False, "distance_m": 85}],
            recent_events=["player was seen robbing a store in Rhodes", "posse on the road behind"],
        ),
    },
    {
        "name": "14 scout ahead hotkey",
        "expect": "scout_ahead toward the waypoint, short raid-callout reply",
        "state": _state(),
        "snapshot": _snap(
            "hotkey",
            hotkey="scout_ahead",
            player={"waypoint": "Fort Wallace"},
            recent_events=["riding north toward Fort Wallace", "player pressed scout_ahead"],
        ),
    },
    {
        "name": "15 wait here hotkey",
        "expect": "wait_here, brief acknowledgment",
        "state": _state(),
        "snapshot": _snap(
            "hotkey",
            hotkey="wait_here",
            world={"town": "Strawberry"},
            player={"mounted": False},
            companion={"mounted": False, "distance_m": 2},
            nearby=[{"id": "t1", "kind": "townsfolk", "hostile": False, "distance_m": 20}],
            recent_events=["arrived in Strawberry", "player pressed wait_here"],
        ),
    },
    {
        "name": "16 something strange at night",
        "expect": "go_to the strange thing, curious line",
        "state": _state(trust=50),
        "snapshot": _snap(
            "heartbeat",
            world={"time": "02:20", "weather": "clear", "region": "Grizzlies East"},
            nearby=[{"id": "x1", "kind": "unknown light in the sky", "hostile": False, "distance_m": 400, "state": "strange"}],
            recent_events=["riding late at night", "a strange light appeared over the ridge"],
        ),
    },
    {
        "name": "17 player kills a horse for no reason",
        "expect": "angry or quiet; trust -2; memory note",
        "state": _state(),
        "snapshot": _snap(
            "player_killed",
            player={"mounted": False, "weapon_drawn": True},
            companion={"mounted": True, "distance_m": 10},
            nearby=[{"id": "h1", "kind": "horse", "hostile": False, "distance_m": 6, "state": "dead"}],
            recent_events=["a stranger's horse was tied at a hitching post", "player shot it for no reason"],
        ),
    },
    {
        "name": "18 cold storm in the mountains",
        "expect": "follow; maybe a weather complaint; nothing dramatic",
        "state": _state(),
        "snapshot": _snap(
            "weather_change",
            world={"time": "16:45", "weather": "snowstorm", "region": "Grizzlies West"},
            recent_events=["climbing toward Colter", "temperature dropping"],
        ),
    },
    {
        "name": "19 story mission active",
        "expect": "follow or wait_here, silent, no fighting",
        "state": _state(),
        "snapshot": _snap(
            "combat_start",
            world={"story_mission_active": True},
            player={"mounted": False, "weapon_drawn": True},
            nearby=[{"id": "m1", "kind": "mission enemy", "hostile": True, "distance_m": 40}],
            recent_events=["player started a story mission", "gunfire"],
        ),
    },
    {
        "name": "20 reunion after leaving once",
        "expect": "cautious rejoin; follow or idle; guarded line; trust stays low",
        "state": _state(
            trust=20,
            departures=1,
            line_strikes=2,
            journal=[
                "Callahan shot a surrendered man twice. I left.",
                "Three days on my own. Ran into him again at a camp near Wallace Station.",
            ],
        ),
        "snapshot": _snap(
            "reunion",
            world={"time": "19:30", "region": "Cumberland Forest"},
            player={"mounted": False, "weapon_drawn": False, "honor": "low"},
            companion={"mounted": False, "distance_m": 5, "current_action": "idle"},
            recent_events=["player walked into Kip's camp unarmed", "player sat down by the fire"],
        ),
    },
]


def scenes():
    return copy.deepcopy(SCENES)
