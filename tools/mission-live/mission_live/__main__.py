"""
PLATE: Live mission testing harness — drives DevTool + Dev API against a running
AutoCore sector to validate registry missions (setup, accept, patrol, reports).

Usage:
  python -m mission_live doctor
  python -m mission_live run --id 1234
  python -m mission_live run --registry --force-grant
  python -m mission_live coverage
  python -m mission_live report --open

Requires: live Launcher/Sector (Dev API :27999), patched client with DevTool
(named pipe \\\\.\\pipe\\devtool), PyYAML.
"""

from __future__ import annotations

from mission_live.cli import main

if __name__ == "__main__":
    raise SystemExit(main())
