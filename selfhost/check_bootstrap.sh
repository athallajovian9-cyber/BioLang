#!/usr/bin/env bash
# BioLang Stage 4 Bootstrap Self-Hosting Check.
set -u
cd "$(dirname "$0")/.." || exit 1

BIN="${BIN:-src/bin/Debug/net10.0/biolang}"
[ -f "$BIN" ] || dotnet build src -v q --nologo >/dev/null || exit 1

echo "Testing selfhost/bootstrap.bio..."
"$BIN" selfhost/bootstrap.bio
rc=$?
if [ $rc -eq 0 ]; then
    echo "  PASS  selfhost/bootstrap.bio execution verified."
else
    echo "  FAIL  selfhost/bootstrap.bio execution failed with code $rc."
    exit 1
fi
