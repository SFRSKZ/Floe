"""Repairs mojibake in a UTF-8 file, one line at a time.

Mixed files (some lines clean, some run through cp1252 once or twice) defeat a whole-file re-decode, so each
line is repaired on its own: try cp1252 -> utf-8 re-decode, fall back to the explicit Turkish mapping, repeat
while markers shrink. Clean lines are left untouched.
"""
import sys
from pathlib import Path

MARKERS = "\u00c3\u00c5\u00c4"
PAIRS = {
    "\u00e2\u20ac\u201d": "\u2014", "\u00e2\u20ac\u0153": "\u201c", "\u00e2\u20ac\u009d": "\u201d",
    "\u00e2\u20ac\u2122": "\u2019", "\u00e2\u20ac\u00a6": "\u2026", "\u00e2\u201a\u00ac": "\u20ac",
    "\u00e2\u20ac\u2013": "\u2013", "\u00c3\u00a7": "\u00e7", "\u00c3\u0087": "\u00c7",
    "\u00c4\u0178": "\u011f", "\u00c4\u017e": "\u011e", "\u00c4\u00b1": "\u0131", "\u00c4\u00b0": "\u0130",
    "\u00c3\u00b6": "\u00f6", "\u00c3\u2013": "\u00d6", "\u00c5\u0178": "\u015f", "\u00c5\u017e": "\u015e",
    "\u00c3\u00bc": "\u00fc", "\u00c3\u0153": "\u00dc", "\u00c3\u00a2": "\u00e2", "\u00c3\u00ae": "\u00ee",
    "\u00c3\u00bb": "\u00fb", "\u00c3\u00a0": "\u00e0",
}


def fix_line(line: str) -> str:
    for _ in range(4):
        before = sum(line.count(m) for m in MARKERS)
        if before == 0:
            break
        try:
            candidate = line.encode("cp1252").decode("utf-8")
            if candidate != line and sum(candidate.count(m) for m in MARKERS) <= before:
                line = candidate
                continue
        except (UnicodeEncodeError, UnicodeDecodeError):
            pass
        for broken, fixed in PAIRS.items():
            line = line.replace(broken, fixed)
        if sum(line.count(m) for m in MARKERS) == before:
            break
    return line


def main(paths: list[str]) -> int:
    for raw in paths:
        path = Path(raw)
        text = path.read_text(encoding="utf-8", errors="replace")
        before = sum(text.count(m) for m in MARKERS)
        fixed = "\n".join(fix_line(line) for line in text.split("\n"))
        after = sum(fixed.count(m) for m in MARKERS)
        path.write_text(fixed, encoding="utf-8", newline="\n")
        print(f"{path.name}: markers {before} -> {after}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
