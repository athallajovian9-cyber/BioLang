#!/usr/bin/env bash
# Self-hosting check: the BioLang lexer written in BioLang must produce output
# byte-identical to the C# reference lexer.
#
# This is the only meaningful test of a lexer. "It looks right" cannot catch a
# character being dropped, an escape being misread, or a column drifting by one -
# and all three happened while this was being written.
#
#   bash selfhost/check_lexer.sh
set -u
cd "$(dirname "$0")/.." || exit 1

BIN="${BIN:-src/bin/Debug/net10.0/biolang}"
[ -f "$BIN" ] || dotnet build src -v q --nologo >/dev/null || exit 1

WORK="$(pwd)/_tmp_lexcheck"
rm -rf "$WORK"; mkdir -p "$WORK"
trap 'rm -rf "$WORK"' EXIT
WIN() { cygpath -w "$1" 2>/dev/null || printf '%s' "$1"; }

# Every .bio file in the repo that is legal source. Each is lexed both ways and
# compared. A single file would test one path through the lexer; the whole set
# exercises comments, strings, escapes, numbers and nested blocks.
FILES=(
    tests/fibonacci.bio
    selfhost/lexer.bio
)

pass=0; fail=0; total=0
echo ""
for f in "${FILES[@]}"; do
    [ -f "$f" ] || continue
    total=$((total+1))
    name="$(basename "$f")"

    # reference: the C# lexer, raw
    "$BIN" --tokens "$(WIN "$PWD/$f")" 2>/dev/null | tr -d '\r' > "$WORK/cs.txt"

    # self-hosted: the .bio lexer, also raw. The ONLY difference is the input
    # prompt, which absorb() prints without a trailing newline - so the prefix is
    # stripped from line 1 and nothing else is touched.
    #
    # An earlier version ran this side through grep and left the other raw. That
    # is not a comparison: tokens whose text contains a newline (a string holding
    # an escape, for instance) span two lines in the reference output and were
    # filtered out of the other side, producing a difference that was entirely the
    # harness's.
    printf '%s\n' "$(WIN "$PWD/$f")" \
        | timeout 240 "$BIN" "$(WIN "$PWD/selfhost/lexer.bio")" 2>&1 \
        | tr -d '\r' | sed '1s/^source? //' > "$WORK/bio.txt"

    ncs=$(wc -l < "$WORK/cs.txt")
    nbio=$(wc -l < "$WORK/bio.txt")

    if [ "$ncs" -eq 0 ]; then
        fail=$((fail+1)); printf '  FAIL  %-22s reference produced no tokens\n' "$name"; continue
    fi

    if diff -q "$WORK/cs.txt" "$WORK/bio.txt" >/dev/null 2>&1; then
        pass=$((pass+1)); printf '  PASS  %-22s %s tokens identical\n' "$name" "$ncs"
    else
        fail=$((fail+1))
        printf '  FAIL  %-22s C#=%s  BioLang=%s\n' "$name" "$ncs" "$nbio"
        diff "$WORK/cs.txt" "$WORK/bio.txt" | head -6 | sed 's/^/          /'
    fi
done

echo ""
echo "  ============================================"
printf '  files=%d  identical=%d  differing=%d\n' "$total" "$pass" "$fail"
echo "  ============================================"
[ "$fail" -eq 0 ] || exit 1