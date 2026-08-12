"""Mission dialog click helpers (complete / OK / accept)."""

from __future__ import annotations

import re
import time

from mission_live.strategies.base import (
    RunContext,
    StepResult,
    action_clicked,
    dialog_kind,
)


def parse_active_mission_id(output: str) -> int:
    m = re.search(r"mission-active:\s*id=(\d+)", output or "")
    return int(m.group(1)) if m else 0


def mission_is_active(ctx: RunContext, mission_id: int) -> tuple[bool, str]:
    """Cheap check: DevTool journal pointers + Dev API state (no widget scan)."""
    active = ctx.cmd("mission active")
    out = str(active.get("output") or "")
    aid = parse_active_mission_id(out)
    if aid == mission_id:
        return True, out
    try:
        state = ctx.state()
        for q in state.get("activeQuests") or []:
            if int(q.get("missionId") or 0) == mission_id:
                return True, out
    except Exception as ex:
        out = f"{out}\n[oracle] {ex}"
    return False, out


def wait_for_dialog(
    ctx: RunContext,
    *,
    timeout: float = 6.0,
    kinds: tuple[str, ...] = ("new", "complete", "ok"),
) -> tuple[str, str]:
    """
    Poll ``mission dialog`` until a classified box appears or timeout.

    Returns (kind, output). kind is new|complete|ok|none.
    """
    last = ""
    deadline = time.time() + timeout
    while True:
        resp = ctx.cmd("mission dialog")
        out = str(resp.get("output") or "")
        last = out
        kind = dialog_kind(resp)
        if kind in kinds or (kind != "none" and "any" in kinds):
            return kind, out
        if time.time() >= deadline:
            return "none", last
        ctx.sleep(0.35)


def _click_complete_once(ctx: RunContext, results: list[StepResult], post_click: float) -> bool:
    """Returns True if a Complete dialog was found and clicked."""
    complete = ctx.cmd("mission complete")
    cout = str(complete.get("output") or "")
    if "found=1" not in cout:
        return False
    ok = action_clicked(complete)
    ctx.step_begin("dialog/complete", "mission complete")
    step = StepResult(
        key="dialog/complete",
        status="PASS" if ok else "FAIL",
        detail="mission complete",
        output=cout,
    )
    results.append(step)
    ctx.step_end(step)
    ctx.sleep(post_click)
    return ok


def _drain_ok(ctx: RunContext, results: list[StepResult], post_click: float, rounds: int = 2) -> None:
    for _ in range(rounds):
        okay = ctx.cmd("mission ok")
        oout = str(okay.get("output") or "")
        if "found=1" not in oout:
            break
        ook = action_clicked(okay)
        ctx.step_begin("dialog/ok", "mission ok")
        ostep = StepResult(
            key="dialog/ok",
            status="PASS" if ook else "FAIL",
            detail="mission ok",
            output=oout,
        )
        results.append(ostep)
        ctx.step_end(ostep)
        ctx.sleep(post_click)
        if not ook:
            break


def handle_dialogs(ctx: RunContext, *, max_rounds: int = 2) -> list[StepResult]:
    """
    Drain Complete then OK after an NPC interact.

    First waits briefly for any mission dialog (single MemScan classify), then
    clicks Complete/OK. First Complete click often only posts mouse-down; after
    a settle, one spaced retry clears a sticky turn-in without a full re-interact.
    """
    results: list[StepResult] = []
    post_click = max(getattr(ctx, "ui_settle_sec", 1.2), 2.0)

    kind, classify_out = wait_for_dialog(ctx, timeout=4.0, kinds=("complete", "ok", "new"))
    if kind != "none":
        ctx.step_begin("dialog/classify", kind)
        results.append(
            ctx.step_end(
                StepResult(
                    key="dialog/classify",
                    status="PASS",
                    detail=kind,
                    output=classify_out,
                )
            )
        )

    clicked = _click_complete_once(ctx, results, post_click)
    if clicked:
        # Sticky turn-in: one spaced second Complete if still present.
        _click_complete_once(ctx, results, post_click)
        _drain_ok(ctx, results, post_click, rounds=max_rounds)
    else:
        _drain_ok(ctx, results, post_click, rounds=max_rounds)
    return results


def accept_mission_from_npc(
    ctx: RunContext,
    steps: list[StepResult],
    *,
    mission_id: int,
    rounds: int = 5,
) -> bool:
    """
    Interact → drain turn-in → accept until the target mission is active.

    Intermediate verify misses are SKIP (retry), not FAIL.
    """
    settle = max(getattr(ctx, "ui_settle_sec", 1.2), 1.2)
    post_click = max(settle, 2.0)

    def add(key: str, status: str, detail: str = "", output: str = "") -> StepResult:
        return ctx.record_step(steps, key, status, detail=detail, output=output, began=True)

    def wait_active(timeout: float) -> tuple[bool, str]:
        last = ""
        deadline = time.time() + timeout
        while time.time() < deadline:
            ok, last = mission_is_active(ctx, mission_id)
            if ok:
                return True, last
            ctx.sleep(0.5)
        return mission_is_active(ctx, mission_id)

    def try_accept(suffix: str) -> bool:
        """Click Accept up to twice; return True if a click landed."""
        accepted_click = False
        for click_i in range(2):
            key = f"setup/missionAccept{suffix}" if click_i == 0 else f"setup/missionAccept{suffix}b"
            ctx.step_begin(key)
            kind, classify_out = wait_for_dialog(ctx, timeout=3.0, kinds=("new",))
            if kind != "new":
                ctx.step_end(
                    StepResult(
                        key=key,
                        status="PASS",
                        detail="no offer dialog yet",
                        output=classify_out,
                    )
                )
                break
            accept = ctx.cmd("mission accept")
            out = str(accept.get("output") or "")
            ok = action_clicked(accept)
            found = "found=1" in out
            if not found and not ok:
                ctx.step_end(StepResult(key=key, status="PASS", detail="no dialog yet", output=out))
                break
            add(
                key,
                "PASS" if ok else "FAIL",
                detail="clicked" if ok else "found but click failed",
                output=out,
            )
            if ok:
                accepted_click = True
                ctx.sleep(post_click)
                active, _ = mission_is_active(ctx, mission_id)
                if active:
                    break
            else:
                ctx.sleep(settle)
        return accepted_click

    for i in range(rounds):
        suffix = "" if i == 0 else f"#{i + 1}"

        active, active_out = mission_is_active(ctx, mission_id)
        if active:
            add("setup/assertActiveEarly", "PASS", detail=f"mission {mission_id}", output=active_out)
            return True

        ctx.step_begin(f"setup/interact{suffix}")
        interact = ctx.cmd("action activate-or-interact")
        add(
            f"setup/interact{suffix}",
            "PASS" if interact.get("ok") else "FAIL",
            output=str(interact.get("output") or ""),
        )
        ctx.sleep(post_click)

        steps.extend(handle_dialogs(ctx))
        # Offer often replaces turn-in after a beat — try Accept before re-interacting.
        ctx.sleep(1.0)
        accepted_click = try_accept(suffix)

        if not accepted_click:
            continue

        steps.extend(handle_dialogs(ctx, max_rounds=1))

        ctx.step_begin(f"setup/verifyActive{suffix}", str(mission_id))
        active, active_out = wait_active(timeout=6.0)
        if active:
            add(
                f"setup/verifyActive{suffix}",
                "PASS",
                detail=f"want={mission_id}",
                output=active_out,
            )
            return True

        add(
            f"setup/verifyActive{suffix}",
            "SKIP",
            detail=f"want={mission_id}; accept clicked, not active yet — retry",
            output=active_out,
        )

    return False
