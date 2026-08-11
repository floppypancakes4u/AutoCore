from __future__ import annotations

import re
import time
from dataclasses import dataclass, field
from typing import Any


def chat_was_accepted(response: dict[str, Any] | None) -> bool:
    """
    Detect whether DevTool actually submitted the chat command.

    - UI ``chat``: requires ``accepted=1`` / ``queued length=`` (busy phase → accepted=0).
    - ``chat-direct``: the client dispatcher often returns 0 even on success, so
      ``accepted=0`` is unreliable. Treat ``dispatcher result=`` as entered unless
      the command was rejected / player missing.
    """
    if not response:
        return False
    out = str(response.get("output") or "")
    if "chat-direct:" in out:
        if "rejected" in out or "player state not available" in out:
            return False
        if "dispatcher result=" in out:
            return True
        if "accepted=1" in out:
            return True
        if "accepted=0" in out:
            return False
        return bool(response.get("ok"))

    if "accepted=0" in out:
        return False
    if "accepted=1" in out or "queued length=" in out:
        return True
    return bool(response.get("ok"))


def action_clicked(response: dict[str, Any] | None) -> bool:
    """True when DevTool logged a successful UI click (``clicked=1``)."""
    if not response:
        return False
    out = str(response.get("output") or "")
    return bool(re.search(r"clicked=1\b", out))


def dialog_found(response: dict[str, Any] | None, *, kind: str = "any") -> bool:
    """True when a mission dialog probe reported ``found=1``."""
    if not response:
        return False
    out = str(response.get("output") or "")
    patterns = {
        "new": r"mission: new-dialog found=1\b",
        "completion": r"mission-completion: found=1\b",
        "current": r"mission-current: found=1\b",
        "any": r"found=1\b",
    }
    return bool(re.search(patterns.get(kind, patterns["any"]), out))


@dataclass
class StepResult:
    key: str
    status: str  # PASS | FAIL | SKIP | ERROR
    detail: str = ""
    before: dict[str, Any] = field(default_factory=dict)
    after: dict[str, Any] = field(default_factory=dict)
    output: str = ""

    def to_dict(self) -> dict[str, Any]:
        return {
            "key": self.key,
            "status": self.status,
            "detail": self.detail,
            "before": self.before,
            "after": self.after,
            "output": self.output,
        }


@dataclass
class MissionResult:
    mission_id: int
    status: str  # PASS | PARTIAL | FAIL | SKIP | ERROR
    title: str = ""
    force_grant: bool = False
    seeded_prereqs: list[int] = field(default_factory=list)
    steps: list[StepResult] = field(default_factory=list)
    fail_locus: str = ""
    policy: str = "partial"
    duration_sec: float = 0.0

    def to_dict(self) -> dict[str, Any]:
        return {
            "missionId": self.mission_id,
            "status": self.status,
            "title": self.title,
            "forceGrant": self.force_grant,
            "seededPrereqs": self.seeded_prereqs,
            "failLocus": self.fail_locus,
            "policy": self.policy,
            "durationSec": self.duration_sec,
            "steps": [s.to_dict() for s in self.steps],
        }


@dataclass
class RunContext:
    actuator: Any
    oracle: Any
    force_grant: bool = False
    settle_sec: float = 1.5
    # Default pause after chat-direct. Override per-call for map transfers (2–3s)
    # vs same-map snaps like /tptowaypoint (~1s).
    chat_settle_sec: float = 1.0
    chat_retry_sec: float = 8.0
    # Settle after interact / mission UI clicks so dialogs appear and MSXML
    # teardown can finish before the next MemScan+click RPC.
    ui_settle_sec: float = 1.2
    step_timeout_sec: float = 45.0
    prep_continent: int = 789
    mission_result: MissionResult | None = None
    plan: dict[str, Any] = field(default_factory=dict)
    progress: Any = None  # LiveStepPrinter | None

    def chat(self, command: str, *, settle_sec: float | None = None) -> dict[str, Any]:
        """
        Submit via DevTool ``chat-direct`` (low-level client dispatcher).

        ``settle_sec`` overrides the post-command wait (default ``chat_settle_sec``).
        Use a longer settle for /warp and /tptonpc; short settle for /tptowaypoint.
        """
        raw = command
        if raw.startswith("chat-direct "):
            text = raw
        elif raw.startswith("chat "):
            text = "chat-direct " + raw[len("chat ") :]
        else:
            text = f"chat-direct {raw}"
        deadline = time.time() + self.chat_retry_sec
        last: dict[str, Any] = {"ok": False, "output": ""}
        wait = self.chat_settle_sec if settle_sec is None else settle_sec
        while True:
            last = self.actuator.cmd(text)
            if chat_was_accepted(last):
                time.sleep(max(0.0, wait))
                last = dict(last)
                last["ok"] = True
                return last
            if time.time() >= deadline:
                last = dict(last)
                last["ok"] = False
                out = str(last.get("output") or "")
                if "dispatcher result=" not in out and "accepted=1" not in out:
                    last["output"] = out + "\n[mission_live] chat-direct not confirmed entered"
                return last
            time.sleep(0.35)

    def cmd(self, command: str) -> dict[str, Any]:
        return self.actuator.cmd(command)

    def sleep(self, seconds: float | None = None) -> None:
        time.sleep(self.settle_sec if seconds is None else seconds)

    def state(self) -> dict[str, Any]:
        return self.oracle.mission_state()

    def wait_until(self, predicate, timeout: float | None = None, interval: float = 0.5) -> bool:
        deadline = time.time() + (timeout if timeout is not None else self.step_timeout_sec)
        while time.time() < deadline:
            if predicate(self.state()):
                return True
            time.sleep(interval)
        return False

    def step_begin(self, key: str, detail: str = "") -> None:
        if self.progress is not None:
            self.progress.begin(key, detail)

    def step_end(self, step: StepResult) -> StepResult:
        if self.progress is not None:
            self.progress.end(step.status, step.detail)
        return step

    def record_step(
        self,
        steps: list[StepResult],
        key: str,
        status: str,
        detail: str = "",
        output: str = "",
        *,
        began: bool = True,
    ) -> StepResult:
        """Append a step and (if not already begun) flash it on the console."""
        if not began:
            self.step_begin(key, detail)
        step = StepResult(key=key, status=status, detail=detail, output=output)
        steps.append(step)
        return self.step_end(step)
