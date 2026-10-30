# rdr2-companion

An AI companion for Red Dead Redemption 2 story mode on PC. He rides with Arthur, fights beside
him, fishes badly, gets drunk when you do, tells stories at the campfire, and remembers your
last session the next time you summon him. Every line is written live by Claude from what is
actually happening in your game, and can be spoken aloud with an ElevenLabs voice.

The default companion is **Dave**: a guy from the present day who woke up in 1899, has made his
peace with it, and talks almost entirely in 80s and 90s movie quotes.

**Story mode only. Never run ScriptHook in Red Dead Online.**

## How it works
- `mod/` is a ScriptHookRDR2DotNet V2 script (C#). It spawns him and his horse, steers him
  (follow, fight, fish, sit at the fire, follow you into buildings), and every so often sends a
  snapshot of the game to the bridge: where you are, who's around, what just happened.
- `bridge/` is a small Python server on your own PC (127.0.0.1:7777, nothing else can reach it).
  It asks Claude what he says and does, plays the line through ElevenLabs, and keeps his
  memory: a journal of things you've done together, how much he trusts and respects you, and
  the lines he's already used so he doesn't repeat himself.
- `bridge/persona/` holds the characters. Write your own: copy `dave.md`, change it, and add it
  to `bridge/personas.py`.

## What you need
- Red Dead Redemption 2 on PC (Steam, Rockstar or Epic).
- [ScriptHookRDR2](https://www.dev-c.com/rdr2/scripthookrdr2/) by Alexander Blade (tested with 1.0.1491.17).
- ScriptHookRDR2DotNet V2 (tested with 2.2.0) and an ASI loader, from Nexus Mods.
- Python 3.10 or newer from [python.org](https://www.python.org/downloads/) (tested with 3.12).
- An Anthropic API key from [console.anthropic.com](https://console.anthropic.com). In testing he
  cost about two thirds of a cent per line on the default settings, roughly 40 to 50 cents for
  an hour of play.
- Optional: an ElevenLabs account (a paid plan for a cloned voice) to hear him. Without it his
  lines are subtitles only.

## Install
1. Install ScriptHookRDR2, the ASI loader and ScriptHookRDR2DotNet V2 as their pages describe.
2. Copy everything in the release zip's `scripts` folder into the game's `scripts` folder
   (`KitCompanion.dll`, `KitCompanion.ini`, `KitLook.ini`).
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
| End | Look editor: change his face, hair and clothes in game |

Change the keys or his name in `KitCompanion.ini` in the game's `scripts` folder.

Things to try: ride somewhere and let him talk, start a gunfight, take out your fishing rod,
drink at a saloon, make camp and sit by the fire, or quit for the night and summon him again
tomorrow.

Problems: the bridge window shows what it's doing, the game's `scripts\KitCompanion.log` shows
what the mod is doing, and `bridge\logs\` has every line he's said. If the bridge isn't
running he still follows you, quietly.

## Build from source
```
dotnet build mod\KitCompanion.csproj -c Release
```
The project looks for the game in the default Steam folder; for another install add
`-p:RDR2Dir="D:\path\to\Red Dead Redemption 2"`. Copy `mod\bin\Release\net48\KitCompanion.dll`,
`mod\KitCompanion.ini` and `mod\looks\dave.ini` (renamed `KitLook.ini`) into the game's
`scripts` folder.

`bridge\compare.py` replays the scenes in `bridge\scenarios.py` against each model with the game
closed, for tuning prompts.

## License
MIT. See `LICENSE`.

Not affiliated with Rockstar Games or Take-Two Interactive.
