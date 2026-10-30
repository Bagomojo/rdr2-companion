"""One decision: one API call, one validated action back."""

import json
import time
from dataclasses import dataclass, field

import anthropic

import flags
import router
from prompt import system_blocks, user_message
from schema import DECISION_SCHEMA, validate

FALLBACK_ACTION = {
    "reason": "bridge fallback",
    "action": "follow",
    "target": "",
    "say": "",
    "mood": "neutral",
    "trust_delta": 0,
    "respect_delta": 0,
    "memory_note": "",
    "reveals_truth": False,
}

# Per million tokens. Cache reads/writes are priced as multiples of input.
# Haiku 5.5 cache-read rate is assumed at 0.1x input; Opus 5.5's is published at $0.20.
MODELS = {
    "haiku": {
        "id": "claude-haiku-5-5",
        "input": 0.10,
        "output": 0.50,
        "cache_read": 0.01,
        "cache_write": 0.125,
        "fallbacks": False,
        "timeout_s": 4.0,
    },
    "opus": {
        "id": "claude-opus-5-5",
        "input": 4.00,
        "output": 20.00,
        "cache_read": 0.20,
        "cache_write": 5.00,
        "fallbacks": True,
        # Opus only gets calm moments, so a slower answer is acceptable there.
        "timeout_s": 8.0,
    },
}


@dataclass
class Result:
    decision: dict
    model: str
    latency_s: float
    usage: dict = field(default_factory=dict)
    cost_usd: float = 0.0
    stop_reason: str = ""
    problems: list = field(default_factory=list)
    fell_back: bool = False
    error: str = ""


def _cost(cfg: dict, u: dict) -> float:
    return (
        u["input_tokens"] * cfg["input"]
        + u["output_tokens"] * cfg["output"]
        + u["cache_read_input_tokens"] * cfg["cache_read"]
        + u["cache_creation_input_tokens"] * cfg["cache_write"]
    ) / 1_000_000


def decide(client: anthropic.Anthropic, model: str, state: dict, snapshot: dict, timeout_s: float | None = None) -> Result:
    """model is "haiku", "opus", or "mix" (the router picks per turn)."""
    snapshot = {**snapshot, "bridge_flags": flags.compute(state, snapshot)}
    if model == "mix":
        model = router.pick(snapshot, snapshot["bridge_flags"])
    cfg = MODELS[model]
    timeout_s = timeout_s or cfg["timeout_s"]
    params = dict(
        model=cfg["id"],
        max_tokens=2048,
        system=system_blocks(),
        messages=[user_message(state, snapshot)],
        output_config={
            "effort": "low",
            "format": {"type": "json_schema", "schema": DECISION_SCHEMA},
        },
    )
    started = time.perf_counter()
    try:
        api = client.with_options(timeout=timeout_s, max_retries=1)
        if cfg["fallbacks"]:
            response = api.beta.messages.create(
                betas=["server-side-fallback-2026-07-01"], fallbacks="default", **params
            )
        else:
            response = api.messages.create(**params)
    except anthropic.APIError as e:
        return Result(FALLBACK_ACTION, model, time.perf_counter() - started, error=f"{type(e).__name__}: {e}")
    latency = time.perf_counter() - started

    u = response.usage
    usage = {
        "input_tokens": u.input_tokens or 0,
        "output_tokens": u.output_tokens or 0,
        "cache_read_input_tokens": u.cache_read_input_tokens or 0,
        "cache_creation_input_tokens": u.cache_creation_input_tokens or 0,
    }
    fell_back = any(getattr(it, "type", "") == "fallback_message" for it in (getattr(u, "iterations", None) or []))
    result = Result(FALLBACK_ACTION, model, latency, usage, _cost(cfg, usage), response.stop_reason or "", fell_back=fell_back)

    if response.stop_reason != "end_turn":
        result.error = f"stop_reason {response.stop_reason}"
        return result
    text = next((b.text for b in response.content if b.type == "text"), "")
    try:
        decision = json.loads(text)
    except json.JSONDecodeError as e:
        result.error = f"bad JSON: {e}"
        return result
    # A campfire story is the one place he's allowed to run long.
    result.problems = validate(decision, 700 if snapshot.get("event") == "campfire" else 240)
    if not result.problems:
        result.decision = decision
    return result
