"""DevTool named-pipe actuator (\\\\.\\pipe\\devtool)."""

from __future__ import annotations

import json
import time
from typing import Any


PIPE_NAME = r"\\.\pipe\devtool"


class ActuatorError(RuntimeError):
    pass


class DevToolActuator:
    def __init__(self, pipe_name: str = PIPE_NAME, timeout_sec: float = 30.0):
        self.pipe_name = pipe_name
        self.timeout_sec = timeout_sec

    def cmd(self, command: str) -> dict[str, Any]:
        payload = json.dumps({"cmd": command}, separators=(",", ":")).encode("utf-8")
        deadline = time.time() + self.timeout_sec
        last_err: Exception | None = None
        while time.time() < deadline:
            try:
                return self._once(payload, command)
            except OSError as ex:
                last_err = ex
                time.sleep(0.25)
        raise ActuatorError(f"DevTool pipe unavailable ({self.pipe_name}): {last_err}")

    def _once(self, payload: bytes, command: str) -> dict[str, Any]:
        # Windows named pipe client via win32 CreateFile semantics through open().
        with open(self.pipe_name, "r+b", buffering=0) as pipe:
            pipe.write(payload)
            pipe.write(b"\n")
            chunks: list[bytes] = []
            while True:
                chunk = pipe.read(4096)
                if not chunk:
                    break
                chunks.append(chunk)
                if b"\n" in chunk or b"}" in chunk:
                    # Response is one JSON line; stop once we have a closing brace.
                    data = b"".join(chunks)
                    if b"}" in data:
                        break
            raw = b"".join(chunks).decode("utf-8", errors="replace").strip()
        if not raw:
            raise ActuatorError(f"Empty response for command: {command}")
        # Take first JSON object line.
        line = raw.splitlines()[0]
        try:
            doc = json.loads(line)
        except json.JSONDecodeError as ex:
            raise ActuatorError(f"Bad JSON from DevTool: {line[:200]}") from ex
        return doc

    def ping(self) -> bool:
        try:
            doc = self.cmd("player position")
            return bool(doc.get("ok"))
        except Exception:
            return False
