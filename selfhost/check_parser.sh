#!/usr/bin/env bash
# Self-hosting check: the BioLang parser written in BioLang must produce output
# byte-identical to the C# reference parser.
#
# This is the only meaningful test of a parser. "It looks right" cannot catch a
# column drifting by one, a node in the wrong field, or a precedence level
# collapsing - and all three happened while this was being written.
#
#   bash selfhost/check_parser.sh
set -u
cd "$(dirname "$0")/.." || exit 1

BIN="${BIN:-src/bin/Debug/net10.0/biolang}"
[ -f "$BIN" ] || dotnet build src -v q --nologo >/dev/null || exit 1

WORK="$(pwd)/_tmp_parsecheck"
rm -rf "$WORK"; mkdir -p "$WORK"
trap 'rm -rf "$WORK"' EXIT
WIN() { cygpath -w "$1" 2>/dev/null || printf '%s' "$1"; }

FILES=(
    tests/fibonacci.bio
    selfhost/lexlib.bio
    selfhost/parser.bio
)

pass=0; fail=0; total=0
echo ""
for f in "${FILES[@]}"; do
    [ -f "$f" ] || continue
    total=$((total+1))
    name="$(basename "$f")"

    # reference: the C# parser on the SAME single file, with no graft
    # resolution - parser against parser, which is what stage 2 is.
    # (`--ast-json` would run Loader.Load first, and comparing a parser
    # against a loader is not the same test.)
    "$BIN" --ast-json-raw "$(WIN "$PWD/$f")" 2>/dev/null | tr -d '\r' > "$WORK/cs.txt"

    # self-hosted: read the file, lex it with lexlib, parse it, print the tree.
    # The ONLY difference from the reference is the input prompt, which absorb()
    # prints without a trailing newline - so the prefix comes off line 1 and
    # nothing else is touched.
    printf '%s\n' "$(WIN "$PWD/$f")" \
        | timeout 300 "$BIN" "$(WIN "$PWD/selfhost/parser.bio")" 2>"$WORK/err.txt" \
        | tr -d '\r' | sed '1s/^source? //' > "$WORK/bio.txt"

    ncs=$(wc -c < "$WORK/cs.txt")
    nbio=$(wc -c < "$WORK/bio.txt")

    if [ "$ncs" -eq 0 ]; then
        fail=$((fail+1)); printf '  FAIL  %-18s the reference produced nothing\n' "$name"; continue
    fi

    if diff -q "$WORK/cs.txt" "$WORK/bio.txt" >/dev/null 2>&1; then
        pass=$((pass+1)); printf '  PASS  %-18s %s bytes identical\n' "$name" "$ncs"
    else
        fail=$((fail+1))
        printf '  FAIL  %-18s C#=%s  BioLang=%s bytes\n' "$name" "$ncs" "$nbio"
        # The first byte where they diverge, plus a window around it. A whole
        # diff of two 40 KB JSON lines is unreadable; the first divergence is
        # what actually names the bug.
        #
        # Paths go through WIN(): Python here is a native binary and cannot
        # resolve an MSYS path like /c/Users/..., which is how this failed with
        # FileNotFoundError and hid the actual difference.
        python3 - "$(WIN "$WORK/cs.txt")" "$(WIN "$WORK/bio.txt")" "$(WIN "$WORK/err.txt")" <<'PY' | sed 's/^/          /'
import sys, pathlib
cs, bio, errp = (pathlib.Path(p) for p in sys.argv[1:4])
a = cs.read_text(encoding="utf-8", errors="replace")
b = bio.read_text(encoding="utf-8", errors="replace")
if not b.strip():
    print("the BioLang parser produced no output.")
    if errp.exists():
        print("stderr: " + errp.read_text(encoding="utf-8", errors="replace").strip()[:200])
else:
    n = min(len(a), len(b))
    i = next((k for k in range(n) if a[k] != b[k]), n)
    print("first difference at byte %d of %d" % (i, len(a)))
    print("  C#      : ..." + a[max(0, i - 45):i + 45].replace("\n", " "))
    print("  BioLang : ..." + b[max(0, i - 45):i + 45].replace("\n", " "))
PY
    fi
done

echo ""
echo "  ============================================"
printf '  files=%d  identical=%d  differing=%d\n' "$total" "$pass" "$fail"
echo "  ============================================"
[ "$fail" -eq 0 ] || exit 1