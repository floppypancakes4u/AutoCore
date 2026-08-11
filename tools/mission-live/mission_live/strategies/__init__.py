from __future__ import annotations

from mission_live.strategies.deliver import DeliverStrategy
from mission_live.strategies.dialogs import accept_mission_from_npc, handle_dialogs
from mission_live.strategies.mission_req import MissionReqStrategy
from mission_live.strategies.patrol import PatrolStrategy
from mission_live.strategies.unsupported import UnsupportedStrategy

STRATEGIES = {
    "Patrol": PatrolStrategy(),
    "Mission": MissionReqStrategy(),
    "Deliver": DeliverStrategy(),
}


def strategy_for(req_type: str):
    return STRATEGIES.get(req_type, UnsupportedStrategy(req_type))


__all__ = [
    "STRATEGIES",
    "strategy_for",
    "handle_dialogs",
    "accept_mission_from_npc",
    "PatrolStrategy",
    "MissionReqStrategy",
    "DeliverStrategy",
    "UnsupportedStrategy",
]
