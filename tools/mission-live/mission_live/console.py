"""Colored same-line step progress for mission_live CLI runs."""

from __future__ import annotations

import sys
import time
from datetime import datetime
from typing import TextIO

try:
    from colorama import Fore, Style, init as colorama_init
except ImportError:  # pragma: no cover
    Fore = Style = None  # type: ignore[misc, assignment]
    colorama_init = None  # type: ignore[assignment]

_STATUS_STYLE = {
    "PASS": "green",
    "FAIL": "red",
    "ERROR": "red",
    "SKIP": "yellow",
    "PARTIAL": "yellow",
    "RUN": "cyan",
}


def enable_coloring() -> None:
    """Initialize ANSI coloring (Windows-safe via colorama)."""
    if colorama_init is not None:
        colorama_init(autoreset=False)


def paint(text: str, style: str) -> str:
    if Fore is None or Style is None:
        return text
    color = {
        "green": Fore.GREEN,
        "red": Fore.RED,
        "yellow": Fore.YELLOW,
        "cyan": Fore.CYAN,
        "dim": Fore.LIGHTBLACK_EX,
    }.get(style)
    if not color:
        return text
    return f"{color}{text}{Style.RESET_ALL}"


def _format_stamp(wall: str, elapsed_sec: float | None = None) -> str:
    if elapsed_sec is None:
        return f"[{wall}]"
    return f"[{wall} +{elapsed_sec:.1f}s]"


class LiveStepPrinter:
    """
    Print each harness step as it starts, then rewrite the same line with the
    outcome before advancing.

    TTY:  ``  [21:15:03] … setup/warp`` → ``  [21:15:03 +1.2s] PASS setup/warp``
    Pipe: one finished line per step (no carriage-return tricks)
    """

    def __init__(self, stream: TextIO[str] | None = None, *, enabled: bool | None = None) -> None:
        self.stream = stream or sys.stdout
        if enabled is None:
            enabled = bool(getattr(self.stream, "isatty", lambda: False)())
        self.enabled = enabled
        self._key = ""
        self._detail = ""
        self._open = False
        self._t0 = 0.0
        self._wall = ""

    def mission_header(self, mission_id: int, title: str = "") -> None:
        wall = datetime.now().strftime("%H:%M:%S")
        label = f"mission {mission_id}"
        if title:
            label = f"{label} — {title}"
        self._writeln(f"{paint(_format_stamp(wall), 'dim')} {paint(label, 'cyan')}")

    def begin(self, key: str, detail: str = "") -> None:
        if self._open:
            # Previous step never finished — close as ERROR so lines stay clean.
            self.end("ERROR", "interrupted")
        self._key = key
        self._detail = detail
        self._open = True
        self._t0 = time.perf_counter()
        self._wall = datetime.now().strftime("%H:%M:%S")
        if not self.enabled:
            return
        stamp = paint(_format_stamp(self._wall), "dim")
        line = f"  {stamp} {paint('…', 'cyan')} {key}"
        if detail:
            line += f"  {paint(detail, 'dim')}"
        self._write_in_place(line)

    def end(self, status: str, detail: str = "") -> None:
        key = self._key or "?"
        shown_detail = detail or self._detail
        elapsed = time.perf_counter() - self._t0 if self._t0 else 0.0
        wall = self._wall or datetime.now().strftime("%H:%M:%S")
        style = _STATUS_STYLE.get(status.upper(), "dim")
        badge = paint(f"{status:<5}", style)
        stamp = paint(_format_stamp(wall, elapsed), "dim")
        line = f"  {stamp} {badge} {key}"
        if shown_detail:
            line += f"  {paint(shown_detail, 'dim')}"
        if self.enabled and self._open:
            self._write_in_place(line)
            self.stream.write("\n")
            self.stream.flush()
        else:
            self._writeln(line)
        self._open = False
        self._key = ""
        self._detail = ""
        self._t0 = 0.0
        self._wall = ""

    def note(self, message: str) -> None:
        if self._open and self.enabled:
            self.stream.write("\n")
            self.stream.flush()
            self._open = False
        wall = datetime.now().strftime("%H:%M:%S")
        self._writeln(f"  {paint(_format_stamp(wall), 'dim')} {paint('·', 'dim')} {message}")

    def _write_in_place(self, text: str) -> None:
        # Erase current line, then write (works in Windows Terminal / colorama).
        self.stream.write("\r\033[2K")
        self.stream.write(text)
        self.stream.flush()

    def _writeln(self, text: str) -> None:
        self.stream.write(text + "\n")
        self.stream.flush()
