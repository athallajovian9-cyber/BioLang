#!/usr/bin/env bash
# BioLang Stage 3 Evaluator Self-Hosting Check.
set -u
cd "$(dirname "$0")/.." || exit 1

BIN="${BIN:-src/bin/Debug/net10.0/biolang}"
[ -f "$BIN" ] || dotnet build src -v q --nologo >/dev/null || exit 1

echo "Testing selfhost/eval.bio..."
"$BIN" selfhost/eval.bio
rc=$?
if [ $rc -eq 0 ]; then
    echo "  PASS  selfhost/eval.bio execution verified."
else
    echo "  FAIL  selfhost/eval.bio execution failed with code $rc."
    exit 1
fi
