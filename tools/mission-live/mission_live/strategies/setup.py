"""Setup prelude: clear, hop map, level, prereq tree, tptonpc, accept."""

from __future__ import annotations

from typing import Any

from mission_live.plan import race_class_eligible
from mission_live.strategies.base import RunContext, StepResult
from mission_live.strategies.dialogs import accept_mission_from_npc


def run_setup(ctx: RunContext, mission_id: int, plan: dict[str, Any]) -> list[StepResult]:
    steps: list[StepResult] = []
    mr = ctx.mission_result

    def add(key: str, status: str, detail: str = "", output: str = "", *, began: bool = True) -> StepResult:
        return ctx.record_step(steps, key, status, detail=detail, output=output, began=began)

    # 1. Clear missions
    ctx.step_begin("setup/clearAllMissions")
    r = ctx.chat("/clearAllMissions", settle_sec=2.0)
    add("setup/clearAllMissions", "PASS" if r.get("ok") else "FAIL", output=str(r.get("output") or ""))
    ctx.sleep(0.3)

    # 2. Prep hop (stable town for level/prereq GM work)
    continent = int(plan.get("continent") or 0)
    prep = ctx.prep_continent
    state = ctx.state()
    if int(state.get("continentId") or 0) != prep:
        ctx.step_begin("setup/warpPrep", str(prep))
        r = ctx.chat(f"/warp {prep}", settle_sec=3.0)
        add("setup/warpPrep", "PASS" if r.get("ok") else "FAIL", detail=str(prep), output=str(r.get("output") or ""))
        ctx.sleep(0.5)
    else:
        add("setup/warpPrep", "PASS", detail="already on prep map", began=False)

    # 3. Level
    req_min = int(plan.get("reqLevelMin") or 0)
    if req_min > 0:
        ctx.step_begin("setup/level", str(req_min))
        r = ctx.chat(f"/level {req_min}", settle_sec=2.0)
        add("setup/level", "PASS" if r.get("ok") else "FAIL", detail=str(req_min), output=str(r.get("output") or ""))
        ctx.sleep(0.3)

    # 4. Prerequisite tree
    ctx.step_begin("setup/completemissiontree", str(mission_id))
    r = ctx.chat(f"/completemissiontree {mission_id}", settle_sec=2.0)
    out = str(r.get("output") or "")
    add("setup/completemissiontree", "PASS" if r.get("ok") else "FAIL", output=out)
    if mr is not None:
        # Best-effort parse "Seeded N completed: a, b, c"
        seeded: list[int] = []
        if "Seeded" in out:
            after = out.split(":", 1)[-1]
            for tok in after.replace("\n", " ").split(","):
                tok = tok.strip()
                if tok.isdigit():
                    seeded.append(int(tok))
        mr.seeded_prereqs = seeded
    ctx.sleep(0.3)

    # Race/class gate
    ctx.step_begin("setup/raceClass")
    state = ctx.state()
    eligible, reason = race_class_eligible(plan, state)
    if not eligible and not ctx.force_grant:
        add("setup/raceClass", "SKIP", detail=reason)
        return steps

    if not eligible and ctx.force_grant:
        add("setup/raceClass", "PASS", detail=f"force_grant bypass: {reason}")
        if mr is not None:
            mr.force_grant = True
        # World may still matter for objectives — hop continent when known.
        if continent > 0:
            ctx.step_begin("setup/warpContinent", str(continent))
            r = ctx.chat(f"/warp {continent}", settle_sec=3.0)
            add(
                "setup/warpContinent",
                "PASS" if r.get("ok") else "FAIL",
                detail=str(continent),
                output=str(r.get("output") or ""),
            )
            ctx.sleep(0.5)
        ctx.step_begin("setup/giveMission", str(mission_id))
        r = ctx.chat(f"/giveMission {mission_id}", settle_sec=2.0)
        add("setup/giveMission", "PASS" if r.get("ok") else "FAIL", output=str(r.get("output") or ""))
        ctx.sleep(0.5)
        return steps

    add("setup/raceClass", "PASS", detail="eligible")

    # 5. Travel to giver via /tptonpc (cross-map transfer).
    npc = int(plan.get("npc") or 0)
    if npc > 0:
        ctx.step_begin("setup/tptonpc", str(npc))
        r = ctx.chat(f"/tptonpc {npc}", settle_sec=3.0)
        add("setup/tptonpc", "PASS" if r.get("ok") else "FAIL", detail=str(npc), output=str(r.get("output") or ""))
        ctx.sleep(0.5)

        # 6–7. Interact → drain turn-in dialogs → accept (may take several rounds
        # when the same NPC must complete a prior mission before offering this one).
        accepted = accept_mission_from_npc(ctx, steps, mission_id=mission_id)
        if not accepted:
            add(
                "setup/missionAccept",
                "FAIL",
                detail="no accept after interact/dialog rounds",
                began=False,
            )
    else:
        # No giver NPC — grant directly (optional continent hop for world objs).
        if continent > 0:
            ctx.step_begin("setup/warpContinent", str(continent))
            r = ctx.chat(f"/warp {continent}", settle_sec=3.0)
            add(
                "setup/warpContinent",
                "PASS" if r.get("ok") else "FAIL",
                detail=str(continent),
                output=str(r.get("output") or ""),
            )
            ctx.sleep(0.5)
        ctx.step_begin("setup/giveMissionNoNpc", str(mission_id))
        r = ctx.chat(f"/giveMission {mission_id}", settle_sec=2.0)
        add("setup/giveMissionNoNpc", "PASS" if r.get("ok") else "FAIL", output=str(r.get("output") or ""))
        ctx.sleep(0.5)

    # 8. Assert active
    def is_active(s: dict) -> bool:
        for q in s.get("activeQuests") or []:
            if int(q.get("missionId") or 0) == mission_id:
                return True
        return False

    ctx.step_begin("setup/assertActive", f"mission {mission_id}")
    ok = ctx.wait_until(is_active, timeout=15)
    add("setup/assertActive", "PASS" if ok else "FAIL", detail=f"mission {mission_id}")
    return steps
