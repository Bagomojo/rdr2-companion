# Dave: user guide

Dave is a wisecracking, time-traveling, AI-controlled companion for Red Dead Redemption 2 story
mode on PC. He rides with Arthur, talks about what's actually happening in your game, fights
beside you, drinks with you, tells stories at the campfire, and remembers your last session. This guide takes you from a clean game to
playing with him, then covers everything you can change. To see him in action first, watch the
[trailer](https://youtu.be/0922MrEbpeo).

**Story mode only. Never run ScriptHook mods in Red Dead Online: it can get your account banned.**

## Contents
1. [What you need](#1-what-you-need)
2. [Install ScriptHook](#2-install-scripthook)
3. [Install the mod](#3-install-the-mod)
4. [Set up the bridge](#4-set-up-the-bridge)
5. [Add your keys](#5-add-your-keys)
6. [First ride](#6-first-ride)
7. [Controls](#7-controls)
8. [What he does on his own](#8-what-he-does-on-his-own)
9. [Changing his look](#9-changing-his-look)
10. [Companions: switching and making your own](#10-companions-switching-and-making-your-own)
11. [Settings reference](#11-settings-reference)
12. [Cost and models](#12-cost-and-models)
13. [Troubleshooting](#13-troubleshooting)
14. [Upgrading](#14-upgrading)
15. [Uninstalling](#15-uninstalling)

## 1. What you need
- **Red Dead Redemption 2 on PC** (Steam, Rockstar Games Launcher or Epic).
- **ScriptHookRDR2** by Alexander Blade, from [dev-c.com](https://www.dev-c.com/rdr2/scripthookrdr2/)
  (tested with 1.0.1491.17).
- **ScriptHookRDR2DotNet V2**, from its
  [GitHub releases](https://github.com/Halen84/ScriptHookRDR2DotNet-V2/releases) (tested with 2.2.0).
  It comes as a `.rar`, so you'll need [7-Zip](https://www.7-zip.org/) or similar to open it.
- **Python 3.10 or newer** from [python.org](https://www.python.org/downloads/) (tested with 3.12).
- **An Anthropic API key with credit on it** from [console.anthropic.com](https://console.anthropic.com).
  This pays for his lines: roughly 40 to 50 cents for an hour of play on the default settings (see
  [Cost and models](#12-cost-and-models)). It is the pay-as-you-go API, billed separately from
  Claude.ai, so **a Claude Pro or Max subscription won't work**; you buy API credit in the
  Console. He only runs on Claude: other AI models, such as ChatGPT or Gemini, aren't supported.
- **Optional: an ElevenLabs account and API key** from [elevenlabs.io](https://elevenlabs.io) to
  hear him talk. Every line he speaks uses your ElevenLabs plan's character credits, and a cloned
  voice needs a paid plan. Without it, his lines appear as subtitles only.
- **The release zip** of this mod, `rdr2-companion-v0.2.1.zip`, from the GitHub Releases page.

Throughout this guide, **the game folder** is the folder that holds `RDR2.exe`. On Steam that's
usually `C:\Program Files (x86)\Steam\steamapps\common\Red Dead Redemption 2`. In Steam you can
find it with right-click on the game, Manage, Browse local files.

## 2. Install ScriptHook
Skip this section if you already use other ScriptHook mods.

1. From the ScriptHookRDR2 zip, copy `ScriptHookRDR2.dll` and `dinput8.dll` into the game folder.
   (`dinput8.dll` is the ASI loader that lets the game load mods.)
2. From the ScriptHookRDR2DotNet V2 `.rar`, copy `ScriptHookRDRDotNet.asi`,
   `ScriptHookRDRDotNet.ini` and `ScriptHookRDRNetAPI.dll` into the game folder.
3. Make a folder called `scripts` in the game folder if there isn't one already.

When Rockstar updates the game, ScriptHook can stop working until Alexander Blade releases a
matching version. If the game crashes at start after an update, check dev-c.com for a new one.

## 3. Install the mod
1. Unzip `rdr2-companion-v0.2.1.zip` somewhere handy, like your Downloads folder.
2. Open its `scripts` folder and copy everything in it into the game's `scripts` folder:
   - `DaveCompanion.dll`: the mod itself.
   - `DaveCompanion.ini`: keys and settings.
   - `looks\dave.ini`: Dave's face, hair and clothes.

## 4. Set up the bridge
The bridge is a small program that runs on your PC while you play. The mod sends it what's
happening in the game, and it asks Claude what Dave says and does. It only listens on your own
PC (127.0.0.1), so nothing else on your network or the internet can reach it. Each time it starts
it makes a new secret that only the mod is given, so other programs on your PC can't use it
either. (Update the mod and the bridge together: a mismatched pair won't talk to each other.)

1. Install Python from python.org. On the first screen of the installer, tick
   **Add python.exe to PATH**.
2. Copy the `bridge` folder from the zip somewhere permanent, for example
   `C:\rdr2-companion\bridge`. Avoid a folder that needs admin rights, like Program Files.
3. Double-click `bridge\setup.cmd`. It installs what the bridge needs and creates the file
   `bridge\.env` for your keys. When it says "Setup done", press a key to close it.

## 5. Add your keys
Open `bridge\.env` in Notepad (right-click it, Open with, Notepad).

1. **Anthropic (required).** At [console.anthropic.com](https://console.anthropic.com), buy some
   API credit under Billing (a Claude.ai subscription doesn't count), then create an API key under API keys. Paste it after `ANTHROPIC_API_KEY=` with no
   spaces:
   ```
   ANTHROPIC_API_KEY=sk-ant-...
   ```
2. **ElevenLabs (optional, for his voice).** In ElevenLabs, create an API key in your profile
   settings and paste it after `ELEVENLABS_API_KEY=`. Then choose a voice: clone your own, design
   one, or add one from the Voice Library. In My Voices, open the voice's menu and copy its ID,
   then paste it after `ELEVENLABS_VOICE_ID=`.
3. Save the file.

Keep `.env` private: anyone with these keys can spend your credit. It never leaves your PC and is
never included if you share the mod's code.

## 6. First ride
1. Start the bridge: double-click `bridge\start-bridge.cmd`. When the window says
   `Ready. Dave on http://127.0.0.1:7777`, leave it open and start the game. (To skip this step
   from now on, see "Start the bridge with the game" below.)
2. Load story mode and get somewhere outdoors, out of a mission.
3. Make sure **Num Lock is on**, then press **Numpad 1**. Dave rides up on his horse and says hello.
4. Ride somewhere. He follows you and talks every so often about what's going on.

### Start the bridge with the game
Open `DaveCompanion.ini` in the game's `scripts` folder and set `Bridge=` to your bridge folder:
```
Bridge=C:\rdr2-companion\bridge
```
From then on the mod starts the bridge, hidden, when the game loads, and stops it when you quit.
Its output goes to `bridge\logs\console.txt` instead of a window. The bridge can take a little
while to start, so if you summon him straight away he says "still waking up" and says hello once
it's ready.

## 7. Controls
| Key | What it does |
|---|---|
| Numpad 1 | Summon him, or send him off ("Dave rides off for now"). |
| Numpad 2 | Ask "What do you think?" He answers about whatever's going on. |
| Numpad 3 | "Wait here." He stays put until you come back or call him. |
| Numpad 4 | "Follow me." Back to following after waiting or scouting. |
| Numpad 5 | "Scout ahead." He rides out in front of you. |
| Numpad 6 | Side by side, on or off. He rides or walks level with you instead of trailing behind. See below. |
| Numpad 7 | Ambush: spawns four hostile outlaws about 40 m ahead of you. See below. |
| End | Look editor (see [Changing his look](#9-changing-his-look)). |

All of these can be changed in `DaveCompanion.ini` (see [Settings reference](#11-settings-reference)).

### Side by side (Numpad 6)
Normally Dave follows a little behind you and to your left: a horse length back when riding, a
step back on foot, so he stays in view without getting underfoot. Press Numpad 6 and he moves up
level with you on your left, a few meters away when riding, and speeds up a gait whenever he
drops back, so the two of you ride along together. It's made for screenshots and videos, and for
riding and talking. Press Numpad 6 again to put him back to his usual spot ("Dave gives you some
room"). It stays on until you turn it off. His riding is still a bit buggy, so on a winding trail or
at a full gallop he can still fall behind or drift in close.

### Ambush (Numpad 7)
A way to start a gunfight on demand, made for filming. It spawns four armed outlaws about 40 m
straight ahead of you, in a line facing you, two with repeaters and two with revolvers. They're
hostile to you, fight to the end instead of running, and are poor shots on purpose, so it makes a
fair, watchable fight rather than a massacre. Dave joins in once they start shooting. Once
they're down, the game cleans up the bodies as usual.

Use it out in the wilds, facing open ground. In town, the shooting brings the law and can earn
you a bounty, and pointing it at a building or cliff can spawn them somewhere awkward. Press it
again after the fight for another round.

## 8. What he does on his own
- **Talks.** Every line is written live from what's happening: where you are, the weather, who's
  around, what you just did. He doesn't repeat himself, and every so often he answers to
  "Arkansas" Dave Rudabaugh.
- **Follows.** On horseback or on foot, and into buildings. He steps out of your way and makes way
  for townsfolk instead of blocking doors. When you mount up he goes to his horse, or calls it
  over if it's far off, and when you whistle for your horse, his comes too if it's within about
  100 m.
- **Fights, but never first.** In a fistfight he keeps his gun holstered and backs away. Once you
  fire, or anyone shoots at you or him, he draws and joins in: a carbine at range, his revolver up
  close. If he goes down, he gets back up after a few seconds.
- **Story missions.** He hangs back while you're in a mission and comes back when it's over.
- **Saloons.** Walk up to a bar and he takes a spot beside you and has a drink, bottle in hand.
  Drink yourself and he notices. With the RDR2 Seats mod installed, you can also sit at tables.
- **Fishing.** Get your rod out and he joins in, badly.
- **Campfires.** Make camp and sit by the fire and he tells a story about life back home.
- **Memory.** He keeps a journal of what you've done together and how much he trusts and respects
  you. Quit for the night, summon him tomorrow, and he remembers.

## 9. Changing his look
1. Summon him and get off your horses, somewhere with room to stand.
2. Press **End**. The first time, he rides off and comes back on the character-creator body, ready
   to edit. He then stands in front of the camera.
3. **Up/Down** picks what to change (head, skin tone, body, hair, beard, eyes, face shape,
   clothes and more). **Left/Right** changes it. Hold **Shift** to jump 10 at a time. The bottom
   of the screen shows what you're changing.
4. Press **End** again to save. You'll see "Look saved."

The look is saved to `scripts\looks\dave.ini` (one file per companion), and he keeps it every
time you summon him.

## 10. Companions: switching and making your own
Dave is one companion. Each companion is a folder in `bridge\companions`:
- `companion.ini`: his name, his voice and how he behaves.
- `persona.md`: who he is, in plain English. This is what Claude reads to play him.

These come with the mod:
- **dave** (the default): a guy from the present day, stuck in 1899, who has seen every movie
  ever made.
- **kip**: Kip Dorsey, a 2015 attorney hiding where he's from. More serious, with a
  hard moral line. He can walk away for good if you lose his trust.
- **doc**: a consumptive Southern gentleman gunfighter, played like Doc Holliday in Tombstone. A
  man of 1899, not a time traveler: drinks, gambles, quotes Latin, never misses, and is loyal
  to the end. Give him a slow Southern voice from the ElevenLabs Voice Library (see below).
- **hollis**: Hollis Crane, a mountain man who has lived alone in the high country for twenty
  years. Gentle, quiet and easy going, until someone provokes him; then he's lethal. He can go
  back to the mountains if you lose his trust.
- **kid**: the Kid, a young outlaw in the mold of Billy the Kid. Laughs, sings, dances, and kills
  without a second thought. Loyal only to those loyal to him: he loves it when you're ruthless,
  mocks you when you show mercy, and rides off if you betray him.

### Switch companions
Open `DaveCompanion.ini` in the game's `scripts` folder and set:
```
Companion=kip
```
Then restart the game (or the bridge, if you start it by hand). If you start the bridge by hand,
also set `COMPANION=kip` in `bridge\.env`.

Each companion has his own memory, look and voice. Switching back to Dave later picks up where
you left off with him.

### Make your own
1. Copy the `bridge\companions\dave` folder and rename the copy, for example `wade`. Use
   lowercase letters, with no spaces.
2. Open `wade\companion.ini` in Notepad and set his name and how he behaves (see the table
   below). To give him a different voice from Dave, set `voice_id` to an ElevenLabs voice ID.
3. Rewrite `wade\persona.md`. Describe him the way you'd brief an actor: where he's from, how he
   talks, what he cares about, what he'd never do, a few example lines. Dave's and Kip's files
   are good models. The more specific, the better he plays. A section headed `## Notes` is for
   your own notes: it's left out of what Claude reads.
4. Set `Companion=wade` in `DaveCompanion.ini` and restart the game.
5. He starts with a default look. Summon him and press End to make him one.

| `companion.ini` setting | What it does |
|---|---|
| `name` | Shown on his subtitles and notices. |
| `voice_id` | His ElevenLabs voice. Blank uses `ELEVENLABS_VOICE_ID` from `bridge\.env`. |
| `voice_volume` | How loud he is next to the game's own voices, from 0.0 to 1.0. Blank uses 0.4. |
| `comedy` | `yes` for a comic sidekick: the bridge picks a kind of joke each turn (a movie quote, a dry one-liner, a useless fact...) so he never falls into one bit. `no` lets him speak plainly in his own voice. |
| `comment_cooldown_s` | Seconds he waits between remarks he makes unprompted. 0 means no wait. |
| `keeps_secret` | `yes` and he hides where he's from until he trusts you enough to tell. |
| `can_leave` | `yes` and he can walk away for good if his trust in you falls too low. |

## 11. Settings reference
### `DaveCompanion.ini` (game's `scripts` folder)
| Setting | Default | What it does |
|---|---|---|
| `Summon` | `NumPad1` | Summon or send off. |
| `WhatDoYouThink` | `NumPad2` | Ask what he thinks. |
| `WaitHere` | `NumPad3` | Wait here. |
| `FollowMe` | `NumPad4` | Follow me. |
| `ScoutAhead` | `NumPad5` | Scout ahead. |
| `SideBySide` | `NumPad6` | Side by side on or off. |
| `Ambush` | `NumPad7` | Spawn an ambush. |
| `LookEditor` | `End` | Open or save the look editor. |
| `Companion` | `dave` | Which companion folder to play. |
| `Name` | blank | Overrides the name on his subtitles. Blank uses the companion's own name. |
| `Bridge` | blank | Your bridge folder. When set, the bridge starts and stops with the game. |

Keys use the .NET key names: `F5`, `NumPad8`, `Insert`, `PageUp`, `OemMinus` and so on.

### `bridge\.env`
| Setting | What it does |
|---|---|
| `ANTHROPIC_API_KEY` | Required. Pays for his lines. |
| `ELEVENLABS_API_KEY` | Optional. Turns on his voice. |
| `ELEVENLABS_VOICE_ID` | The voice to use when a companion doesn't set his own `voice_id`. |
| `COMPANION` | Which companion to play when you start the bridge by hand. |
| `COMPANION_VOICE_VOLUME` | Default voice volume for all companions, 0.0 to 1.0 (0.4 if unset). |

## 12. Cost and models
The bridge uses Claude Haiku for most lines and Claude Opus for the moments that matter: a
reunion, a campfire story, fishing, asking what he thinks, and his remarks on quiet rides. Fights
always use Haiku, because speed matters more there. In testing that cost about two
thirds of a cent per line, roughly 40 to 50 cents for an hour of play. ElevenLabs charges
separately for his voice, out of your plan's character credits, and repeated lines are cached on
your PC so they cost nothing the second time. If either account runs out of credit, he goes quiet
(Anthropic) or falls back to subtitles (ElevenLabs) until you top it up.

To change the mix, start the bridge from a Command Prompt in the bridge folder with one of:
```
start-bridge.cmd haiku
start-bridge.cmd opus
```
`haiku` is cheapest, `opus` is the best writer but costs several times more, and `mix` (the
default) is in between.

## 13. Troubleshooting
Three places tell you what's going on:
- **The bridge window**, also saved to `bridge\logs\console.txt` (the only place to look when it
  starts with the game).
- **`scripts\DaveCompanion.log`** in the game folder: what the mod is doing.
- **`bridge\logs\`**: every line he's said, by day.

| Problem | Try this |
|---|---|
| He follows you but never talks | The bridge isn't running. Start `start-bridge.cmd`, or set `Bridge=` so it starts with the game. |
| The bridge window closes straight away, or says "Run setup.cmd first" | Run `setup.cmd`. If it can't find Python, reinstall Python with "Add python.exe to PATH" ticked. |
| The bridge says your Anthropic key is missing or invalid | Check `ANTHROPIC_API_KEY` in `bridge\.env`: no spaces or quotes. |
| The bridge says your credit balance is too low | Buy API credit at console.anthropic.com under Billing. A Claude.ai Pro or Max subscription doesn't cover the API. |
| His voice stops partway through a session | Your ElevenLabs credits ran out. He keeps going in subtitles until they renew or you top up. |
| He talks in subtitles but you can't hear him | Check `ELEVENLABS_API_KEY` and a voice ID. The bridge window says at start whether his voice is on, and why not. |
| Numpad 1 does nothing | Turn Num Lock on. Check that `DaveCompanion.dll` is in `scripts`, and look in `DaveCompanion.log`. |
| "Dave couldn't be spawned" | Move somewhere more open and try again. `DaveCompanion.log` says what went wrong. |
| He rides badly: falls behind, takes odd lines, bumps into you or gets stuck | A known issue: his riding is still a bit buggy. Press Numpad 4 (follow me), or Numpad 1 twice to send him off and call him back. |
| He talks but you can't see him | Press Numpad 1 twice to send him off and call him back. If it keeps happening, open the look editor and save his look again. |
| Two companions appear | An old `KitCompanion.dll` is still in `scripts`. Delete it. |
| The bridge plays the wrong companion | You changed `Companion=` while the bridge was running. Restart the game, or the bridge. |
| The game crashes at start after a Rockstar update | Update ScriptHookRDR2 from dev-c.com. |
| He's too loud or too quiet | Set `voice_volume` in his `companion.ini`, between 0.0 and 1.0. |

### Reporting a problem
If something's still wrong, run `collect-logs.cmd` in the `bridge` folder (double-click it). It
saves `dave-logs-<date>.zip` on your desktop with the three logs above, your settings file from
`scripts`, and the mod and bridge versions. Your keys and Windows user name are taken out, but
it does include what he said and what was happening in your game, so look it over first. Then
open a [new issue](https://github.com/Bagomojo/rdr2-companion/issues/new/choose), say what
happened, and drag the zip in. If it can't find your game, run it from a Command Prompt with the
game folder after it: `collect-logs.cmd "D:\Games\Red Dead Redemption 2"`.

## 14. Upgrading
### From v0.2
1. Copy the new `DaveCompanion.dll` into the game's `scripts` folder. Keep your own
   `DaveCompanion.ini` and `looks` folder.
2. Copy the new files from the zip's `bridge` folder over yours (your `.env`, `state` and `logs`
   folders aren't in the zip, so they're kept), then run `setup.cmd` again.

Update both: the v0.2.1 mod and bridge won't talk to an older copy of the other.

### From v0.1
In v0.1 the mod was called KitCompanion.
1. Delete `KitCompanion.dll` from the game's `scripts` folder (if both are there, both run).
2. Copy in the new `scripts` files from the new zip.
3. Replace your `bridge` folder with the new one, keeping your `.env`, `state` and `logs`
   folders, so his memory and your keys carry over. Run `setup.cmd` again.

Your old `KitCompanion.ini` and `KitLook.ini` keep working until you replace them with
`DaveCompanion.ini` and `looks\dave.ini`.

## 15. Uninstalling
- Delete `DaveCompanion.dll`, `DaveCompanion.ini`, `DaveCompanion.log` and the `looks` folder from
  the game's `scripts` folder.
- Delete the `bridge` folder. That removes your keys and his memory too.
- Leave ScriptHook in place if you use other mods. Otherwise delete the ScriptHook files listed
  in [Install ScriptHook](#2-install-scripthook).
