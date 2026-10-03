#!/usr/bin/env bash
# BioLang test suite.
#
# Every case is a real program run through the real interpreter. `expect` calls
# are compared against stdout, and `expect_err` against stderr - so a case that
# silently produces no output FAILS rather than passing on an empty match.
#
#   ./run_tests.sh
set -u
cd "$(dirname "$0")" || exit 1

BIN="src/bin/Debug/net10.0/biolang"
[ -f "$BIN" ] || dotnet build src -v q --nologo >/dev/null || exit 1

TMP="$(pwd)/_tmp_tests"
rm -rf "$TMP"; mkdir -p "$TMP"
trap 'rm -rf "$TMP"' EXIT

# The interpreter is a NATIVE Windows binary, so a bash-style path like
# /c/Users/... or /tmp/... is not something it can open. Every path handed to it
# is converted first. Without this every case fails with "no such file" and the
# real test results are invisible.
WIN() { cygpath -w "$1" 2>/dev/null || printf '%s' "$1"; }

pass=0; fail=0; n=0

# run <name> <source> [expected-stdout] [expected-exit]
run() {
  n=$((n+1))
  local name="$1" src="$2" want="${3-}" wantexit="${4-0}"
  printf '%s\n' "$src" > "$TMP/t.bio"

  local out rc
  # Strip CR: the native Windows build writes \r\n, the expected strings here use
  # \n. Normalising in the harness keeps the comparison about behaviour rather
  # than about the host line ending.
  out="$("$BIN" "$(WIN "$TMP/t.bio")" 2>"$TMP/err" | tr -d '\r')"; rc=${PIPESTATUS[0]}

  if [ -n "$want" ] && [ "$out" != "$want" ]; then
    fail=$((fail+1)); printf '  FAIL  %s\n        got      %q\n        expected %q\n' "$name" "$out" "$want"; return
  fi
  if [ "$rc" != "$wantexit" ]; then
    fail=$((fail+1)); printf '  FAIL  %s (exit %s, wanted %s)\n        stderr: %s\n' "$name" "$rc" "$wantexit" "$(cat "$TMP/err")"; return
  fi
  pass=$((pass+1)); printf '  PASS  %s\n' "$name"
}

# err <name> <source> <substring that must appear on stderr>
err() {
  n=$((n+1))
  local name="$1" src="$2" needle="$3"
  printf '%s\n' "$src" > "$TMP/t.bio"

  local rc; "$BIN" "$(WIN "$TMP/t.bio")" >"$TMP/out" 2>"$TMP/err"; rc=$?
  if [ "$rc" = "0" ]; then
    fail=$((fail+1)); printf '  FAIL  %s (expected a failure, program exited 0)\n' "$name"; return
  fi
  if ! grep -qF -- "$needle" "$TMP/err"; then
    fail=$((fail+1)); printf '  FAIL  %s\n        stderr: %s\n        wanted it to contain: %s\n' "$name" "$(cat "$TMP/err")" "$needle"; return
  fi
  pass=$((pass+1)); printf '  PASS  %s\n' "$name"
}

echo ""
echo "  ---- printing and literals ----"
run "secrete an rna" 'organism T { nucleus() { secrete("hello") } }' "hello"
run "dna prints without a trailing .0" 'organism T { nucleus() { secrete(42) } }' "42"
run "dna keeps a real fraction" 'organism T { nucleus() { secrete(1.5) } }' "1.5"
run "enzyme prints as a word" 'organism T { nucleus() { secrete(active) secrete(dormant) } }' "active
dormant"
run "colony prints as a list" 'organism T { nucleus() { secrete([1, 2, 3]) } }' "[1, 2, 3]"

echo ""
echo "  ---- arithmetic ----"
run "integer addition" 'organism T { nucleus() { secrete(2 + 3) } }' "5"
run "precedence: * binds tighter than +" 'organism T { nucleus() { secrete(2 + 3 * 4) } }' "14"
run "parentheses override precedence" 'organism T { nucleus() { secrete((2 + 3) * 4) } }' "20"
run "unary minus" 'organism T { nucleus() { secrete(-5 + 8) } }' "3"
run "string concatenation" 'organism T { nucleus() { secrete("a" + "b") } }' "ab"

echo ""
echo "  ---- colony methods and indexing ----"
run ".length returns the count" 'organism T { nucleus() { cell colony c = [1,2,3] secrete(c.length) } }' "3"
run ".inject appends" 'organism T { nucleus() { cell colony c = [1] c.inject(2) secrete(c) } }' "[1, 2]"
run "index from the front" 'organism T { nucleus() { cell colony c = [10,20,30] secrete(c[0]) } }' "10"
run "negative index takes the last" 'organism T { nucleus() { cell colony c = [10,20,30] secrete(c[-1]) } }' "30"
run "negative index takes the second last" 'organism T { nucleus() { cell colony c = [10,20,30] secrete(c[-2]) } }' "20"

echo ""
echo "  ---- control flow ----"
run "mutate runs the true branch" 'organism T { nucleus() { mutate (active) { secrete("yes") } adapt { secrete("no") } } }' "yes"
run "adapt runs the false branch" 'organism T { nucleus() { mutate (dormant) { secrete("yes") } adapt { secrete("no") } } }' "no"
run "adapt is optional" 'organism T { nucleus() { mutate (dormant) { secrete("yes") } secrete("after") } }' "after"
run "replicate loops" 'organism T { nucleus() { cell dna i = 0 replicate (i < 3) { secrete(i) i = i + 1 } } }' "0
1
2"
run "comparison operators" 'organism T { nucleus() { secrete(3 < 5) secrete(5 <= 5) secrete(3 == 4) } }' "active
active
dormant"

echo ""
echo "  ---- functions ----"
run "an organ returns a value" 'organism T { organ twice(dna n) -> dna { return n * 2 } nucleus() { secrete(twice(21)) } }' "42"
run "parameters are scoped to the call" 'organism T { organ add(dna a, dna b) -> dna { return a + b } nucleus() { secrete(add(2,3)) } }' "5"
run "a function may build a colony" 'organism T { organ mk() -> colony { cell colony c = [1] c.inject(2) return c } nucleus() { secrete(mk()) } }' "[1, 2]"

echo ""
echo "  ---- the rules that make it a language, not a toy ----"

# fossil immutability - the spec's headline safety rule
err "fossil cannot be reassigned" \
  'organism T { nucleus() { fossil dna x = 1 x = 2 } }' \
  "fossil and cannot be reassigned"
err "fossil cannot be mutated through .inject" \
  'organism T { nucleus() { fossil colony c = [1] c.inject(2) } }' \
  "fossil; .inject() would mutate it"

# type checking
err "storing an rna in a dna is refused" \
  'organism T { nucleus() { cell dna x = "text" } }' \
  "cannot store rna in dna"
err "a wrong return type is refused" \
  'organism T { organ f() -> dna { return "text" } nucleus() { f() } }' \
  "declared -> dna but returned rna"
err "a wrong argument type is refused" \
  'organism T { organ f(dna n) -> dna { return n } nucleus() { f("x") } }' \
  "expects dna, got rna"
err "too few arguments is refused" \
  'organism T { organ f(dna a, dna b) -> dna { return a } nucleus() { f(1) } }' \
  "takes 2 argument(s), got 1"
err "falling off the end without returning is refused" \
  'organism T { organ f() -> dna { cell dna x = 1 } nucleus() { f() } }' \
  "finished without returning"

# runtime guards
err "division by zero is refused" \
  'organism T { nucleus() { secrete(1 / 0) } }' \
  "division by zero"
err "an index outside the colony is refused" \
  'organism T { nucleus() { cell colony c = [1] secrete(c[5]) } }' \
  "outside the colony"
err "calling an unknown organ is refused" \
  'organism T { nucleus() { nope() } }' \
  "no organ named"
err "an unknown method is refused" \
  'organism T { nucleus() { cell colony c = [1] c.push(2) } }' \
  "no method"
err "an undefined variable is refused" \
  'organism T { nucleus() { secrete(missing) } }' \
  "is not defined"
err "redefining a name in one scope is refused" \
  'organism T { nucleus() { cell dna x = 1 cell dna x = 2 } }' \
  "already defined"
err "a missing nucleus is refused" \
  'organism T { organ f() -> dna { return 1 } }' \
  "no nucleus() found"

echo ""
echo "  ---- syntax errors are reported with a position ----"
err "a missing closing brace" 'organism T { nucleus() { secrete(1)' "SYNTAX ERROR"
err "an unterminated string" 'organism T { nucleus() { secrete("oops) } }' "unterminated string"
err "a bad keyword" 'organism T { nucleus() { loop (1) { } } }' "SYNTAX ERROR"
err "a stray character" 'organism T { nucleus() { secrete(1) @ } }' "unexpected character"

echo ""
echo "  ============================================"
printf '  checks=%d  passes=%d  fails=%d\n' "$n" "$pass" "$fail"
echo "  ============================================"
[ "$fail" -eq 0 ] || exit 1