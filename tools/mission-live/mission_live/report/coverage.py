"""Coverage merge: retail catalog × registry × last results."""

from __future__ import annotations

from typing import Any

from mission_live.catalog import load_catalog, requirement_types_in_catalog_entry
from mission_live.plan import SUPPORTED_TYPES
from mission_live.registry import RegistryEntry, load_registry
from mission_live.report.model import load_results


def build_coverage(
    registry: list[RegistryEntry] | None = None,
    results_doc: dict[str, Any] | None = None,
) -> dict[str, Any]:
    registry = registry if registry is not None else load_registry()
    results_doc = results_doc if results_doc is not None else load_results()
    catalog = load_catalog()

    last_by_id = {
        int(m["missionId"]): m
        for m in (results_doc.get("missions") or [])
        if m.get("missionId") is not None
    }
    reg_ids = {e.mission_id for e in registry}

    fully_auto = 0
    partial_auto = 0
    for mid, entry in catalog.items():
        types = requirement_types_in_catalog_entry(entry)
        if not types:
            continue
        if all(t in SUPPORTED_TYPES for t in types):
            fully_auto += 1
        elif any(t in SUPPORTED_TYPES for t in types):
            partial_auto += 1

    rows = []
    all_ids = sorted(set(catalog.keys()) | reg_ids | set(last_by_id.keys()))
    for mid in all_ids:
        last = last_by_id.get(mid)
        rows.append(
            {
                "missionId": mid,
                "inCatalog": mid in catalog,
                "inRegistry": mid in reg_ids,
                "lastStatus": (last or {}).get("status") or "NEVER_RUN",
                "title": (last or {}).get("title")
                or (catalog.get(mid) or {}).get("title")
                or (catalog.get(mid) or {}).get("name")
                or "",
            }
        )

    return {
        "retailCount": len(catalog),
        "registryCount": len(reg_ids),
        "registryInCatalog": len(reg_ids & set(catalog.keys())),
        "catalogFullyAutoSupported": fully_auto,
        "catalogPartialAutoSupported": partial_auto,
        "lastSummary": results_doc.get("summary") or {},
        "rows": rows,
    }
