# Dave: a wisecracking, time-traveling, AI-controlled companion

An AI companion for Red Dead Redemption 2 story mode on PC. He rides with Arthur, fights beside
him, fishes badly, gets drunk when you do, tells stories at the campfire, and remembers your
last session the next time you summon him. Every line is written live by Claude from what is
actually happening in your game, and can be spoken aloud with an ElevenLabs voice.

[![Watch the trailer](https://img.youtube.com/vi/0922MrEbpeo/maxresdefault.jpg)](https://youtu.be/0922MrEbpeo)

**[Watch the trailer on YouTube](https://youtu.be/0922MrEbpeo)**

The default companion is **Dave**: a guy from the present day who woke up in 1899, has made his
peace with it, and has seen every movie ever made. He quotes them, mangles them, ribs Arthur,
compares everything to modern life, and now and then goes by "Arkansas" Dave Rudabaugh. You
can also write your own companions and pick which one rides with you.

### Why Dave?
Because he's me, or at least that was the original idea. I'm a 50-something IT nerd, and if I
got transported back to 1899, I'd be useless. I'd just end up quoting movies and making
smart-ass comments. RDR2 wouldn't let me make him look like me, but I could make him sound like
me: I cloned my voice with ElevenLabs using audio from some of my past talks, so the voice you
hear in the trailer is mine. I hope you enjoy it!

**New here? The [user guide](docs/USER-GUIDE.md) walks you through installing and playing, step by step.**

**Story mode only. Never run ScriptHook in Red Dead Online.**

## How it works
- `mod/` is a ScriptHookRDR2DotNet V2 script (C#). It spawns him and his horse, steers him
  (follow, fight, fish, sit at the fire, follow you into buildings), and every so often sends a
  snapshot of the game to the bridge: where you are, who's around, what just happened.
- `bridge/` is a small Python server on your own PC (127.0.0.1:7777, nothing else can reach it).
  It asks Claude what he says and does, plays the line through ElevenLabs, and keeps his
  memory: a journal of things you've done together, how much he trusts and respects you, and
  the lines he's already used so he doesn't repeat himself.
- `bridge/companions/` holds the characters, one folder each (see Companions below).

## What you need
- Red Dead Redemption 2 on PC (Steam, Rockstar or Epic).
- [ScriptHookRDR2](https://www.dev-c.com/rdr2/scripthookrdr2/) by Alexander Blade (tested with 1.0.1491.17).
- [ScriptHookRDR2DotNet V2](https://github.com/Halen84/ScriptHookRDR2DotNet-V2/releases) (tested
  with 2.2.0). The ASI loader it needs, `dinput8.dll`, comes with ScriptHookRDR2.
- Python 3.10 or newer from [python.org](https://www.python.org/downloads/) (tested with 3.12).
- An Anthropic API key with credit on it, from [console.anthropic.com](https://console.anthropic.com).
  This is the pay-as-you-go API, billed separately from Claude.ai: a Claude Pro or Max
  subscription won't work. He only runs on Claude; other AI models aren't supported. In testing
  he cost about two thirds of a cent per line on the default settings, roughly 40 to 50 cents
  for an hour of play.
- Optional: an ElevenLabs account and API key to hear him. Every spoken line uses your plan's
  character credits (a cloned voice needs a paid plan). Without it his lines are subtitles only.

## Install
1. Install ScriptHookRDR2 and ScriptHookRDR2DotNet V2 (the [user guide](docs/USER-GUIDE.md#2-install-scripthook)
   lists which files go where).
2. Copy everything in the release zip's `scripts` folder into the game's `scripts` folder
   (`DaveCompanion.dll`, `DaveCompanion.ini` and the `looks` folder).
3. Put the `bridge` folder anywhere you like and double-click `bridge\setup.cmd`.
4. Open `bridge\.env` in Notepad and paste your Anthropic key after `ANTHROPIC_API_KEY=`.
   Add `ELEVENLABS_API_KEY` and `ELEVENLABS_VOICE_ID` too if you want him to talk out loud.

## Play
1. Double-click `bridge\start-bridge.cmd` and leave its window open.
2. Start story mode, then press **Numpad 1** to summon him.

| Key | Does |
|---|---|
| Numpad 1 | Summon or dismiss him and his horse |
| Numpad 2 | Ask "What do you think?" |
| Numpad 3 | "Wait here" |
| Numpad 4 | "Follow me" |
| Numpad 5 | "Scout ahead" |
| Numpad 6 | Side by side on or off: he rides and walks right next to you (great for screenshots) |
| Numpad 7 | Ambush: four poorly aiming outlaws appear about 40 m ahead and come for you (use it out in the wilds) |
| End | Look editor: change his face, hair and clothes in game |

Change the keys in `DaveCompanion.ini` in the game's `scripts` folder.

To have the bridge start with the game, set `Bridge=` in `DaveCompanion.ini` to the bridge folder
(for example `Bridge=C:\rdr2-companion\bridge`). It then runs hidden, writes its output to
`bridge\logs\console.txt`, and stops when you quit the game.

He never fires first: in a fistfight he keeps his gun holstered and backs away. Once you shoot,
or anyone shoots at you, he draws and joins in.

## Companions
Each companion is a folder in `bridge\companions`: `companion.ini` holds his name, his
ElevenLabs voice and how he behaves, and `persona.md` describes who he is. These come with it:
- `dave` (the default): the movie-quoting guy from the present day.
- `kip`: a 2015 attorney hiding where he's from, with a hard moral line.
- `doc`: a dying Southern gentleman gunfighter in the mold of Doc Holliday in Tombstone.
- `hollis`: a gentle mountain man, twenty years alone in the high country, lethal when provoked.
- `kid`: a young outlaw in the mold of Billy the Kid, charming and a ruthless killer.

To make your own, copy the `dave` folder, rename it (say `wade`), rewrite `persona.md` in plain
English, and set the name and `voice_id` in `companion.ini`. Then pick him in
`DaveCompanion.ini`:
```
Companion=wade
```
Each companion keeps his own memory (`bridge\state\wade.json`) and his own look
(`scripts\looks\wade.ini`, written when you save in the look editor). A new companion starts
with a default look until you make one. If you start the bridge by hand instead of with `Bridge=`,
set `COMPANION=wade` in `bridge\.env` too, and restart the bridge whenever you switch.

## Upgrading
From v0.2: copy the new `DaveCompanion.dll` into `scripts` and the new `bridge` files over yours
(your `.env`, `state` and `logs` are kept), then run `setup.cmd` again. Update both: the v0.2.1
mod and bridge won't talk to an older copy of the other.

From v0.1: the mod was called KitCompanion. Delete `KitCompanion.dll` from the game's `scripts` folder
(otherwise both run), then copy in the new files. Your old `KitCompanion.ini` and `KitLook.ini`
keep working until you replace them with `DaveCompanion.ini` and `looks\dave.ini`.

Things to try: ride somewhere and let him talk, start a gunfight, take out your fishing rod,
drink at a saloon, make camp and sit by the fire, or quit for the night and summon him again
tomorrow.

## Known issues
- His riding is still a bit buggy. On horseback he can fall behind, take odd lines, bump into
  you or get stuck on scenery. If he's lost or stuck, press Numpad 4 (follow me), or Numpad 1
  twice to send him off and call him back.

## Reporting a problem
The bridge window (and `bridge\logs\console.txt`) shows what the bridge is doing, the game's
`scripts\DaveCompanion.log` shows what the mod is doing, and `bridge\logs\` has every line he's
said. If the bridge isn't running he still follows you, quietly.

To report a bug, run `collect-logs.cmd` in the `bridge` folder. It puts those logs in one zip on
your desktop, with keys and your Windows user name taken out. Look it over, then attach it to a
[new issue](https://github.com/Bagomojo/rdr2-companion/issues/new/choose).

## Build from source
```
dotnet build mod\DaveCompanion.csproj -c Release
```
The project looks for the game in the default Steam folder; for another install add
`-p:RDR2Dir="D:\path\to\Red Dead Redemption 2"`. Copy `mod\bin\Release\net48\DaveCompanion.dll`,
`mod\DaveCompanion.ini` and the `mod\looks` folder into the game's `scripts` folder.

`bridge\compare.py` replays the scenes in `bridge\scenarios.py` against each model with the game
closed, for tuning prompts.

## License
MIT. See `LICENSE`.

Not affiliated with Rockstar Games or Take-Two Interactive.
