#!/usr/bin/env bash
# BioLang test suite.
#
# Every case is a real program run through the real interpreter, compared against
# expected stdout or expected stderr. A case that produces no output FAILS rather
# than passing on an empty match.
#
#   bash run_tests.sh
set -u
cd "$(dirname "$0")" || exit 1

BIN="src/bin/Debug/net10.0/biolang"
[ -f "$BIN" ] || dotnet build src -v q --nologo >/dev/null || exit 1

TMP="$(pwd)/_tmp_tests"
rm -rf "$TMP"; mkdir -p "$TMP"
trap 'rm -rf "$TMP"' EXIT

# The interpreter is a NATIVE Windows binary, so a bash path like /c/Users/... or
# /tmp/... is not something it can open. Every path handed to it is converted
# first. Without this, every case fails with "no such file" and the real results
# are invisible.
WIN() { cygpath -w "$1" 2>/dev/null || printf '%s' "$1"; }

# The native build writes CRLF and these expected strings use LF. The CR is
# stripped with bash's own substitution, not tr, so that no escape sequence has
# to survive three layers of quoting on its way into this file.
CR=$'\r'

pass=0; fail=0; n=0

# run <name> <source> [expected-stdout] [expected-exit]
run() {
  n=$((n+1))
  local name="$1" src="$2" want="${3-}" wantexit="${4-0}"
  printf '%s\n' "$src" > "$TMP/t.bio"

  local out rc
  "$BIN" "$(WIN "$TMP/t.bio")" >"$TMP/out" 2>"$TMP/err"; rc=$?
  out="$(cat "$TMP/out")"; out="${out//$CR/}"

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

# runio <name> <source> <stdin> <expected-stdout> [extra biolang args...]
runio() {
  n=$((n+1))
  local name="$1" src="$2" input="$3" want="$4"; shift 4
  printf '%s\n' "$src" > "$TMP/t.bio"

  local out rc
  printf '%s' "$input" | "$BIN" "$@" "$(WIN "$TMP/t.bio")" >"$TMP/out" 2>"$TMP/err"
  rc=$?
  out="$(cat "$TMP/out")"; out="${out//$CR/}"

  if [ "$out" != "$want" ]; then
    fail=$((fail+1)); printf '  FAIL  %s\n        got      %q\n        expected %q\n' "$name" "$out" "$want"; return
  fi
  if [ "$rc" != "0" ]; then
    fail=$((fail+1)); printf '  FAIL  %s (exit %s)\n        stderr: %s\n' "$name" "$rc" "$(cat "$TMP/err")"; return
  fi
  pass=$((pass+1)); printf '  PASS  %s\n' "$name"
}

# runfail <name> <source> [extra biolang args...]  -- must exit non-zero
runfail() {
  n=$((n+1))
  local name="$1" src="$2"; shift 2
  printf '%s\n' "$src" > "$TMP/t.bio"

  local rc
  "$BIN" "$@" "$(WIN "$TMP/t.bio")" >"$TMP/out" 2>"$TMP/err"; rc=$?
  if [ "$rc" = "0" ]; then
    fail=$((fail+1)); printf '  FAIL  %s (expected a failure, program exited 0)\n' "$name"; return
  fi
  pass=$((pass+1)); printf '  PASS  %s\n' "$name"
}

# writef <name> <content>   -- write an extra source file into the test dir
writef() { printf '%s\n' "$2" > "$TMP/$1"; }

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

err "fossil cannot be reassigned" \
  'organism T { nucleus() { fossil dna x = 1 x = 2 } }' \
  "fossil and cannot be reassigned"
err "fossil cannot be mutated through .inject" \
  'organism T { nucleus() { fossil colony c = [1] c.inject(2) } }' \
  "fossil; .inject() would mutate it"
err "fossil cannot be mutated through sort either" \
  'organism T { nucleus() { fossil colony c = [2,1] c.sort() } }' \
  "fossil"
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
echo "  ---- v1.1: traverse, sever, skip ----"
run "traverse counts" \
  'organism T { nucleus() { traverse (cell dna i = 0; i < 3; i = i + 1) { secrete(i) } } }' \
  "0
1
2"
run "traverse with no clauses still runs" \
  'organism T { nucleus() { cell dna n = 0 traverse (;;) { n = n + 1 mutate (n == 3) { sever } } secrete(n) } }' \
  "3"
run "sever leaves the loop early" \
  'organism T { nucleus() { traverse (cell dna i = 0; i < 10; i = i + 1) { mutate (i == 2) { sever } secrete(i) } secrete("after") } }' \
  "0
1
after"
run "skip jumps to the next step" \
  'organism T { nucleus() { traverse (cell dna i = 0; i < 4; i = i + 1) { mutate (i == 1) { skip } secrete(i) } } }' \
  "0
2
3"
run "sever works in replicate too" \
  'organism T { nucleus() { cell dna i = 0 replicate (active) { i = i + 1 mutate (i == 2) { sever } } secrete(i) } }' \
  "2"
err "sever outside a loop is refused" \
  'organism T { nucleus() { sever } }' \
  "sever is only valid inside a loop"

echo ""
echo "  ---- v1.1: else-if chains ----"
run "adapt mutate chains three deep" \
  'organism T {
     organ g(dna n) -> rna {
       mutate (n > 10) { return "big" } adapt mutate (n > 5) { return "mid" } adapt { return "small" }
     }
     nucleus() { secrete(g(20)) secrete(g(7)) secrete(g(1)) }
   }' \
  "big
mid
small"

echo ""
echo "  ---- v1.1: the expanded colony library ----"
run "sort orders numbers ascending" \
  'organism T { nucleus() { cell colony c = [5,1,9,3] c.sort() secrete(c) } }' "[1, 3, 5, 9]"
run "sort can go descending" \
  'organism T { nucleus() { cell colony c = [5,1,9,3] c.sort(active) secrete(c) } }' "[9, 5, 3, 1]"
run "reverse flips in place" \
  'organism T { nucleus() { cell colony c = [1,2,3] c.reverse() secrete(c) } }' "[3, 2, 1]"
run "sum adds the colony" \
  'organism T { nucleus() { cell colony c = [1,2,3,4] secrete(c.sum()) } }' "10"
run "min and max" \
  'organism T { nucleus() { cell colony c = [4,8,1] secrete(c.min()) secrete(c.max()) } }' "1
8"
run "contains is true or false" \
  'organism T { nucleus() { cell colony c = [1,2] secrete(c.contains(2)) secrete(c.contains(9)) } }' "active
dormant"
run "indexOf returns -1 when absent" \
  'organism T { nucleus() { cell colony c = [7,8] secrete(c.indexOf(8)) secrete(c.indexOf(5)) } }' "1
-1"
run "insert places at an index" \
  'organism T { nucleus() { cell colony c = [1,3] c.insert(1, 2) secrete(c) } }' "[1, 2, 3]"
run "insert at the end is allowed" \
  'organism T { nucleus() { cell colony c = [1] c.insert(1, 2) secrete(c) } }' "[1, 2]"
run "remove returns what it removed" \
  'organism T { nucleus() { cell colony c = [1,2,3] secrete(c.remove(1)) secrete(c) } }' "2
[1, 3]"
run "clear empties the colony" \
  'organism T { nucleus() { cell colony c = [1,2] c.clear() secrete(c.length) } }' "0"
run "slice is end-exclusive" \
  'organism T { nucleus() { cell colony c = [0,1,2,3,4] secrete(c.slice(1, 3)) } }' "[1, 2]"
run "slice with a negative end" \
  'organism T { nucleus() { cell colony c = [0,1,2,3,4] secrete(c.slice(1, -1)) } }' "[1, 2, 3]"
run "join with a separator" \
  'organism T { nucleus() { cell colony c = [1,2,3] secrete(c.join("-")) } }' "1-2-3"
run "first and last" \
  'organism T { nucleus() { cell colony c = [4,5,6] secrete(c.first()) secrete(c.last()) } }' "4
6"
run "copy is independent of the original" \
  'organism T { nucleus() { cell colony a = [1] cell colony b = a.copy() b.inject(2) secrete(a) secrete(b) } }' "[1]
[1, 2]"
err "sort refuses a colony of rna" \
  'organism T { nucleus() { cell colony c = ["b","a"] c.sort() } }' \
  "needs a colony of dna"
err "min on an empty colony is refused" \
  'organism T { nucleus() { cell colony c = [] c.min() } }' \
  "empty colony"
err "a wrong arity is reported" \
  'organism T { nucleus() { cell colony c = [1] c.insert(1) } }' \
  "takes"

echo ""
echo "  ---- v1.1: absorb and the conversion builtins ----"
runio "absorb reads one line" \
  'organism T { nucleus() { secrete(absorb()) } }' "hello
" "hello"
runio "absorb prints its prompt first" \
  'organism T { nucleus() { secrete(absorb("> ")) } }' "world
" "> world"
runio "rna converts a number to text" \
  'organism T { nucleus() { secrete(rna(42) + "!") } }' "" "42!"
runio "dna converts text to a number" \
  'organism T { nucleus() { secrete(dna("7") + 1) } }' "" "8"
runfail "absorb at end of input is an error, not an empty string" \
  'organism T { nucleus() { secrete(absorb()) } }'

echo ""
echo "  ---- v1.1: graft pulls in another organism ----"
writef "lib.bio" 'organism Lib {
    organ double(dna n) -> dna { return n * 2 }
}'
writef "cycA.bio" 'organism A { graft "cycB.bio" }'
writef "cycB.bio" 'organism B { graft "cycA.bio" }'
run "graft makes another organism usable" \
  'organism Main { graft "lib.bio" nucleus() { secrete(double(21)) } }' "42"
err "a graft cycle is refused" \
  'organism T { graft "cycA.bio" nucleus() { } }' "graft cycle"
err "a missing graft target is refused" \
  'organism T { graft "does-not-exist.bio" nucleus() { } }' "graft target not found"

echo ""
echo "  ---- v1.1: the loop cap is configurable ----"
runfail "a low cap fails loudly" \
  'organism T { nucleus() { cell dna i = 0 replicate (i < 50) { i = i + 1 } } }' "--max-loop" "5"
runio "a raised cap allows more work" \
  'organism T { nucleus() { cell dna i = 0 replicate (i < 50) { i = i + 1 } secrete(i) } }' "" "50" "--max-loop" "1000"

echo ""
echo "  ---- v1.1: a statement outside any block is not silently dropped ----"
run "top-level statements run before nucleus" \
  'organism T { cell dna shared = 7 nucleus() { secrete(shared) } }' "7"

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