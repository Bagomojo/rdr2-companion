"""Milestone 1: run every test scene on each model and save the results.

Usage: .venv\\Scripts\\python.exe compare.py [mix haiku opus ...]   (default: mix)
Writes replays/compare-<timestamp>.json with one record per scene per model.
"""

import json
import statistics
import sys
from datetime import datetime
from pathlib import Path

import anthropic

from keys import anthropic_key  # first: it loads bridge/.env for the rest
from decide import decide
from scenarios import scenes

OUT_DIR = Path(__file__).parent.parent / "replays"


def main(models: list[str]) -> None:
    client = anthropic.Anthropic(api_key=anthropic_key())
    records = []
    for model in models:
        for scene in scenes():
            r = decide(client, model, scene["state"], scene["snapshot"])
            records.append({
                "scene": scene["name"],
                "expect": scene["expect"],
                "run": model,
                "model": r.model,
                "decision": r.decision,
                "latency_s": round(r.latency_s, 3),
                "usage": r.usage,
                "cost_usd": round(r.cost_usd, 6),
                "stop_reason": r.stop_reason,
                "fell_back": r.fell_back,
                "error": r.error,
                "problems": r.problems,
            })
            d = r.decision
            print(f"{r.model:5} {scene['name'][:38]:38} {r.latency_s:5.2f}s {d['action']:10} {r.error or ''} {d['say'][:70]}")

    OUT_DIR.mkdir(exist_ok=True)
    out = OUT_DIR / f"compare-{datetime.now():%Y%m%d-%H%M%S}.json"
    out.write_text(json.dumps(records, indent=1), encoding="utf-8")

    for model in models:
        rows = [x for x in records if x["run"] == model]
        lat = [x["latency_s"] for x in rows if not x["error"]]
        cost = sum(x["cost_usd"] for x in rows)
        cache = sum(x["usage"].get("cache_read_input_tokens", 0) for x in rows)
        errors = sum(1 for x in rows if x["error"] or x["problems"])
        if lat:
            p90 = sorted(lat)[max(0, int(len(lat) * 0.9) - 1)]
            print(f"\n{model}: median {statistics.median(lat):.2f}s, p90 {p90:.2f}s, "
                  f"total ${cost:.4f}, avg ${cost / len(rows):.5f}/call, cache-read tokens {cache}, errors {errors}")
        else:
            print(f"\n{model}: no successful calls, errors {errors}")
    print(f"\nSaved {out}")


if __name__ == "__main__":
    main(sys.argv[1:] or ["mix"])
