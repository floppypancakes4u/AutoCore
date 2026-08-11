"""DevControl HTTP oracle (:27999)."""

from __future__ import annotations

import json
import urllib.error
import urllib.parse
import urllib.request
from typing import Any


class OracleError(RuntimeError):
    pass


class DevApiOracle:
    def __init__(self, base_url: str = "http://127.0.0.1:27999", timeout_sec: float = 10.0):
        self.base_url = base_url.rstrip("/")
        self.timeout_sec = timeout_sec

    def _get(self, path: str, query: dict[str, str] | None = None) -> dict[str, Any]:
        url = self.base_url + path
        if query:
            url += "?" + urllib.parse.urlencode(query)
        req = urllib.request.Request(url, method="GET")
        try:
            with urllib.request.urlopen(req, timeout=self.timeout_sec) as resp:
                body = resp.read().decode("utf-8")
                return json.loads(body)
        except urllib.error.HTTPError as ex:
            detail = ex.read().decode("utf-8", errors="replace")
            raise OracleError(f"GET {path} -> {ex.code}: {detail}") from ex
        except Exception as ex:
            raise OracleError(f"GET {path} failed: {ex}") from ex

    def health(self) -> dict[str, Any]:
        return self._get("/health")

    def mission_plan(self, mission_id: int) -> dict[str, Any]:
        return self._get("/mission-plan", {"id": str(mission_id)})

    def mission_state(self, character: str | None = None) -> dict[str, Any]:
        q = {"character": character} if character else None
        return self._get("/mission-state", q)

    def ping(self) -> bool:
        try:
            doc = self.health()
            return bool(doc.get("ok"))
        except Exception:
            return False
