"""Gathers what's needed for a bug report into one zip on the desktop, with keys and your
Windows user name taken out.

Usage: collect-logs.cmd [game folder]   (the game folder is found on its own for Steam, Rockstar
and Epic installs; pass it only if it isn't)
"""

import json
import os
import platform
import re
import sys
import winreg
import zipfile
from datetime import datetime
from pathlib import Path

HERE = Path(__file__).parent
LOGS = HERE / "logs"
TURNS = 50          # the last this many turns of the bridge's own log
MOD_LINES = 600     # the last this many lines of the mod's log

SECRETS = [
    re.compile(r"sk-ant-[A-Za-z0-9_\-]{8,}"),       # Anthropic
    re.compile(r"\bsk_[0-9a-f]{20,}\b"),            # ElevenLabs
    re.compile(r"\b[0-9a-f]{32}\b"),                # older ElevenLabs keys
]
SECRET_SETTING = re.compile(r"^\s*([A-Z0-9_]*(KEY|TOKEN|SECRET|PASSWORD)[A-Z0-9_]*)\s*=.*$", re.M | re.I)


def scrub(text: str) -> str:
    for pattern in SECRETS:
        text = pattern.sub("[key removed]", text)
    text = SECRET_SETTING.sub(r"\1=[removed]", text)
    home = os.path.expanduser("~")
    user = os.environ.get("USERNAME", "")
    text = text.replace(home, "%USERPROFILE%").replace(home.replace("\\", "/"), "%USERPROFILE%")
    if len(user) > 2:
        text = re.sub(re.escape(user), "<user>", text, flags=re.I)
    return text


def _reg(root, path: str, name: str) -> str:
    try:
        with winreg.OpenKey(root, path) as key:
            return str(winreg.QueryValueEx(key, name)[0])
    except OSError:
        return ""


def game_folder() -> Path | None:
    candidates = []
    candidates += [Path(a.strip('"')) for a in sys.argv[1:] if not a.startswith("--")]
    candidates.append(Path(_reg(winreg.HKEY_LOCAL_MACHINE,
                                r"SOFTWARE\WOW6432Node\Rockstar Games\Red Dead Redemption 2", "InstallFolder")))
    steam = _reg(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam", "SteamPath")
    if steam:
        libraries = [Path(steam)]
        vdf = Path(steam) / "steamapps" / "libraryfolders.vdf"
        if vdf.exists():
            libraries += [Path(p.replace("\\\\", "\\")) for p in re.findall(r'"path"\s+"([^"]+)"', vdf.read_text(errors="ignore"))]
        candidates += [lib / "steamapps" / "common" / "Red Dead Redemption 2" for lib in libraries]
    candidates += [Path(r"C:\Program Files\Epic Games\RedDeadRedemption2"),
                   Path(r"C:\Program Files\Rockstar Games\Red Dead Redemption 2")]
    for c in candidates:
        if str(c) not in ("", ".") and (c / "RDR2.exe").exists():
            return c
    return None


def tail(path: Path, lines: int) -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="replace").splitlines()[-lines:])


def settings_summary() -> str:
    """Which settings are set in .env, never their values."""
    env = HERE / ".env"
    if not env.exists():
        return ".env: missing (run setup.cmd)"
    names = []
    for line in env.read_text(encoding="utf-8", errors="replace").splitlines():
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            name, value = line.split("=", 1)
            names.append(f"{name.strip()}: {'set' if value.strip() else 'blank'}")
    return ".env settings:\n  " + "\n  ".join(names or ["(none)"])


def main() -> None:
    sys.path.insert(0, str(HERE))
    from version import VERSION

    game = game_folder()
    stamp = datetime.now().strftime("%Y%m%d-%H%M")
    desktop = Path(os.path.expanduser("~")) / "Desktop"
    out = (desktop if desktop.exists() else HERE) / f"dave-logs-{stamp}.zip"

    info = [f"Bridge v{VERSION}", f"Collected {datetime.now():%Y-%m-%d %H:%M}",
            f"Windows {platform.version()}, Python {platform.python_version()}",
            f"Companions: {', '.join(sorted(p.name for p in (HERE / 'companions').iterdir() if p.is_dir()))}",
            settings_summary()]
    files: dict[str, str] = {}

    if game:
        scripts = game / "scripts"
        info.append(f"Game folder: {game}")
        info.append("In the game folder: " + ", ".join(
            f"{n} ({(game / n).stat().st_size:,} bytes)" for n in
            ("ScriptHookRDR2.dll", "dinput8.dll", "ScriptHookRDRDotNet.asi", "ScriptHookRDRNetAPI.dll")
            if (game / n).exists()) or "In the game folder: no ScriptHook files found")
        if scripts.exists():
            info.append("In scripts: " + ", ".join(sorted(p.name for p in scripts.iterdir())))
            for name in ("DaveCompanion.log", "KitCompanion.log"):
                if (scripts / name).exists():
                    files[name] = tail(scripts / name, MOD_LINES)
            for name in ("DaveCompanion.ini", "KitCompanion.ini"):
                if (scripts / name).exists():
                    files[name] = (scripts / name).read_text(encoding="utf-8", errors="replace")
        else:
            info.append("The game folder has no scripts folder.")
    else:
        info.append("Game folder: not found. Run collect-logs.cmd with the game folder after it to include the mod's log.")

    if (LOGS / "console.txt").exists():
        files["console.txt"] = tail(LOGS / "console.txt", 400)
    days = sorted(LOGS.glob("20*.jsonl"))
    turns: list[str] = []
    for day in reversed(days):
        turns = day.read_text(encoding="utf-8", errors="replace").splitlines()[-(TURNS - len(turns)):] + turns
        if len(turns) >= TURNS:
            break
    if turns:
        files["last-turns.jsonl"] = "\n".join(turns)
        bad = 0
        for t in turns:
            try:
                turn = json.loads(t)
                bad += bool(turn.get("error") or turn.get("problems"))
            except ValueError:
                pass
        info.append(f"Last {len(turns)} turns: {bad} with an error or problem")

    files["info.txt"] = "\n".join(info)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for name, text in files.items():
            z.writestr(name, scrub(text))
    print(f"Saved {out}")
    print("Attach that file to your issue on GitHub. Keys and your Windows user name have been taken out,")
    print("but it does include what Dave said and what was happening in your game, so look it over first.")
    if "--no-open" not in sys.argv:
        os.startfile(out.parent)


if __name__ == "__main__":
    main()
