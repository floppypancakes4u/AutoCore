"""
PLATE: List files inside an Auto Assault .glm archive (maps*.glm, misc.glm, missions.glm, …).

What it does:
  Parses the CHNK trailer (string table + file entries) and prints name/offset/size.
  Optionally filter by substring and extract a matching member to a file.

Examples:
  python scripts/list_glm_contents.py maps1.glm
  python scripts/list_glm_contents.py maps1.glm --filter arkbay
  python scripts/list_glm_contents.py maps1.glm --filter arkbaytutorial.fam --extract tmp-map/arkbay.fam

Requires: game install (AA_INSTALL) or an absolute path to a .glm file.
"""

from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

# Allow `python scripts/list_glm_contents.py` from repo root
sys.path.insert(0, str(Path(__file__).resolve().parent))
from aa_paths import default_install  # noqa: E402


def parse_glm(path: Path) -> tuple[bytes, dict[str, tuple[int, int, int, int]]]:
    """Return (raw_bytes, {name: (offset, size, realsize, scheme)})."""
    data = path.read_bytes()
    header_off = struct.unpack_from("<i", data, len(data) - 4)[0]
    if header_off < 0 or header_off + 20 > len(data):
        raise ValueError(f"bad header offset {header_off}")
    if data[header_off : header_off + 4] != b"CHNK":
        raise ValueError(f"no CHNK at header_off (got {data[header_off:header_off+4]!r})")

    str_table_off = struct.unpack_from("<i", data, header_off + 8)[0]
    str_table_size = struct.unpack_from("<i", data, header_off + 12)[0]
    entry_count = struct.unpack_from("<i", data, header_off + 16)[0]
    string_table = data[str_table_off : str_table_off + str_table_size]

    names: list[str] = []
    i = 0
    while i < len(string_table):
        if string_table[i] == 0:
            i += 1
            continue
        start = i
        while i < len(string_table) and string_table[i] != 0:
            i += 1
        names.append(string_table[start:i].decode("latin-1", "replace"))
        i += 1

    if len(names) != entry_count:
        # Some GLMs may pad the table; keep what we got
        pass

    pos = header_off + 20
    files: dict[str, tuple[int, int, int, int]] = {}
    for name in names:
        if pos + 22 > len(data):
            break
        off = struct.unpack_from("<i", data, pos)[0]
        size = struct.unpack_from("<i", data, pos + 4)[0]
        real = struct.unpack_from("<i", data, pos + 8)[0]
        # mtime at +12, scheme i16 at +16, then pad to 22
        scheme = struct.unpack_from("<h", data, pos + 16)[0]
        pos += 22
        files[name] = (off, size, real, scheme)
    return data, files


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("Examples:")[0].strip())
    ap.add_argument(
        "glm",
        help="Path to .glm, or basename under AA_INSTALL (e.g. maps1.glm)",
    )
    ap.add_argument("--filter", "-f", help="Only show names containing this substring (case-insensitive)")
    ap.add_argument("--extract", "-e", help="Write first matching member to this path")
    args = ap.parse_args()

    glm_path = Path(args.glm)
    if not glm_path.is_file():
        candidate = default_install() / args.glm
        if candidate.is_file():
            glm_path = candidate
        else:
            print(f"not found: {args.glm}", file=sys.stderr)
            return 1

    data, files = parse_glm(glm_path)
    filt = (args.filter or "").lower()
    matches = [(n, files[n]) for n in sorted(files) if not filt or filt in n.lower()]

    print(f"{glm_path}: {len(files)} entries, {len(matches)} shown")
    for name, (off, size, real, scheme) in matches:
        print(f"  {name}  off={off} size={size} real={real} scheme={scheme}")

    if args.extract:
        if not matches:
            print("no match to extract", file=sys.stderr)
            return 1
        name, (off, size, _, _) = matches[0]
        out = Path(args.extract)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_bytes(data[off : off + size])
        print(f"extracted {name!r} -> {out} ({size} bytes)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
