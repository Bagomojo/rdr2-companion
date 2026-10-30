"""Speaks Kit's lines through the PC's speakers with his ElevenLabs voice.

Each line is generated as 16-bit PCM, wrapped in a WAV header, scaled for how far
Kit is from the player, and played with winsound, so there are no extra packages.
Lines are cached by text and delivery, so repeats cost nothing. Any failure just
leaves the line as a subtitle.
"""

import hashlib
import json
import os
import queue
import re
import struct
import threading
import urllib.error
import urllib.request
import winsound
from array import array
from pathlib import Path

# Any voice in your ElevenLabs library: a cloned one, or one from their Voice Library.
VOICE_ID = os.environ.get("ELEVENLABS_VOICE_ID") or os.environ.get("KIT_VOICE_ID", "")
# Most expressive model first (Flash sounded flat in testing); about 0.5 s slower per line.
# If the service rejects a model, fall back to the next one.
MODELS = ["eleven_v3", "eleven_multilingual_v2"]
SAMPLE_RATE = 22050
# Kit played far louder than the game's own voices at 1.0. Override with KIT_VOICE_VOLUME.
VOLUME = float(os.environ.get("KIT_VOICE_VOLUME", "0.4"))
SILENT_BEYOND_M = 40
FULL_VOLUME_WITHIN_M = 8

# "period": careful 1899 speech around strangers. "own": his real, quicker 2015 voice.
# eleven_v3 takes stability 0.0, 0.5 or 1.0 only; 0.5 is its natural setting.
DELIVERY = {
    "period": {"stability": 0.5, "similarity_boost": 0.8, "style": 0.3, "speed": 0.93},
    "own": {"stability": 0.5, "similarity_boost": 0.8, "style": 0.45, "speed": 1.0},
    # Loosest stability v3 takes, slower, and a [drunk] cue in front of the line.
    "drunk": {"stability": 0.0, "similarity_boost": 0.8, "style": 0.6, "speed": 0.9},
}
AUDIO_TAG = re.compile(r"\s*\[[a-z ]{2,24}\]\s*")

CACHE_DIR = Path(__file__).parent / "state" / "voice-cache"


def _volume_for(distance_m: float) -> float:
    if distance_m <= FULL_VOLUME_WITHIN_M:
        return 1.0
    span = SILENT_BEYOND_M - FULL_VOLUME_WITHIN_M
    return max(0.25, 1.0 - 0.75 * (distance_m - FULL_VOLUME_WITHIN_M) / span)


def _wav(pcm: bytes) -> bytes:
    header = b"RIFF" + struct.pack("<I", 36 + len(pcm)) + b"WAVE"
    header += b"fmt " + struct.pack("<IHHIIHH", 16, 1, 1, SAMPLE_RATE, SAMPLE_RATE * 2, 2, 16)
    header += b"data" + struct.pack("<I", len(pcm))
    return header + pcm


def _scaled(pcm: bytes, gain: float) -> bytes:
    if gain >= 0.999:
        return pcm
    samples = array("h", pcm)
    for i, s in enumerate(samples):
        samples[i] = int(s * gain)
    return samples.tobytes()


class Voice:
    def __init__(self, key: str):
        if not VOICE_ID:
            raise RuntimeError("no ELEVENLABS_VOICE_ID in bridge\\.env")
        self.key = key
        self.model_index = 0
        self.lines: queue.Queue = queue.Queue(maxsize=2)
        CACHE_DIR.mkdir(parents=True, exist_ok=True)
        threading.Thread(target=self._player, daemon=True).start()

    def speak(self, text: str, delivery: str, distance_m: float) -> None:
        """Queue a line. Never blocks; drops the line if two are already waiting."""
        if not text or distance_m > SILENT_BEYOND_M:
            return
        try:
            self.lines.put_nowait((text, delivery if delivery in DELIVERY else "period", distance_m))
        except queue.Full:
            pass

    def _player(self) -> None:
        while True:
            text, delivery, distance_m = self.lines.get()
            try:
                pcm = self._pcm(text, delivery)
                gain = _volume_for(distance_m) * VOLUME
                winsound.PlaySound(_wav(_scaled(pcm, gain)), winsound.SND_MEMORY | winsound.SND_NODEFAULT)
            except Exception as e:  # a voice failure must never take the bridge down
                print(f"voice: {type(e).__name__}: {e}", flush=True)

    def _pcm(self, text: str, delivery: str) -> bytes:
        settings = json.dumps(DELIVERY[delivery], sort_keys=True)
        key = f"{VOICE_ID}|{MODELS[self.model_index]}|{settings}|{text}"
        name = hashlib.sha1(key.encode()).hexdigest() + ".pcm"
        cached = CACHE_DIR / name
        if cached.exists():
            return cached.read_bytes()
        pcm = self._generate(text, delivery)
        cached.write_bytes(pcm)
        return pcm

    def _generate(self, text: str, delivery: str) -> bytes:
        while True:
            model = MODELS[self.model_index]
            if model != "eleven_v3":  # only v3 understands audio tags; others would read them out
                text = AUDIO_TAG.sub(" ", text).strip()
            elif delivery == "drunk":
                text = "[drunk] " + text
            body = {"text": text, "voice_settings": DELIVERY[delivery]}
            if model:
                body["model_id"] = model
            req = urllib.request.Request(
                f"https://api.elevenlabs.io/v1/text-to-speech/{VOICE_ID}/stream?output_format=pcm_{SAMPLE_RATE}",
                data=json.dumps(body).encode(),
                headers={"xi-api-key": self.key, "Content-Type": "application/json"},
                method="POST",
            )
            try:
                with urllib.request.urlopen(req, timeout=15) as resp:
                    return resp.read()
            except urllib.error.HTTPError as e:
                detail = e.read().decode(errors="replace")[:200]
                if e.code in (400, 422) and self.model_index + 1 < len(MODELS):
                    self.model_index += 1
                    print(f"voice: {model} rejected ({detail[:80]}), trying {MODELS[self.model_index]}", flush=True)
                    continue
                raise RuntimeError(f"HTTP {e.code}: {detail}") from None
