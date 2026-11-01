"""API keys and local settings for the bridge.

Settings come from bridge/.env (copy .env.example to start) or the environment. Each key can be
given directly (ANTHROPIC_API_KEY, ELEVENLABS_API_KEY) or fetched at startup by a password-manager
script named in RDR2_VAULT_SCRIPT, which is called as a child process and prints the secret:

    powershell -File <RDR2_VAULT_SCRIPT> -Item <item name> -Field password

Keys are only held in memory: never written to disk, printed or logged.
"""

import os
import subprocess
from pathlib import Path

ENV_FILE = Path(__file__).parent / ".env"
VAULT_TIMEOUT_S = 60  # each lookup normally takes about 20 s


def load_env() -> None:
    """Read bridge/.env into the environment. Variables already set win."""
    if not ENV_FILE.exists():
        return
    for line in ENV_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        name, value = line.split("=", 1)
        os.environ.setdefault(name.strip(), value.strip().strip('"').strip("'"))


load_env()


def secret(env_name: str, vault_item_env: str, default_item: str) -> str:
    """The key in env_name, or else the vault item named by vault_item_env. '' if neither is set."""
    key = os.environ.get(env_name, "").strip()
    if key:
        return key
    script = os.environ.get("RDR2_VAULT_SCRIPT", "").strip()
    if not script:
        return ""
    item = os.environ.get(vault_item_env, default_item)
    proc = subprocess.Popen(
        ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
         "-Item", item, "-Field", "password"],
        stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
    )
    try:
        out, _ = proc.communicate(timeout=VAULT_TIMEOUT_S)
    except subprocess.TimeoutExpired:
        # A locked vault can sit at a password prompt nobody sees. Killing only PowerShell
        # leaves its children holding the pipe open, so take down the whole tree.
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)], capture_output=True)
        raise RuntimeError(f"Reading '{item}' with {script} took over {VAULT_TIMEOUT_S} s. "
                           "Is the password manager locked?") from None
    key = out.strip()
    if proc.returncode != 0 or not key:
        raise RuntimeError(f"Could not read '{item}' with {script} (exit {proc.returncode}).")
    return key


def anthropic_key() -> str:
    key = secret("ANTHROPIC_API_KEY", "RDR2_VAULT_ANTHROPIC_ITEM", "Anthropic rdr2-companion")
    if not key.startswith("sk-ant-"):
        raise SystemExit("No Anthropic API key. Put ANTHROPIC_API_KEY=sk-ant-... in bridge\\.env "
                         "(copy .env.example), then start the bridge again.")
    return key


def elevenlabs_key() -> str:
    key = secret("ELEVENLABS_API_KEY", "RDR2_VAULT_ELEVENLABS_ITEM", "ElevenLabs rdr2-companion")
    if not key:
        raise RuntimeError("no ELEVENLABS_API_KEY in bridge\\.env")
    return key
