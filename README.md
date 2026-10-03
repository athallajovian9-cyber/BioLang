# BioLang

A small interpreted language where the keywords are biology.

An **organism** holds **organs**, the **nucleus** is where it starts, and a
**fossil** can never be changed.

```
organism FibonacciSystem {
    organ computeGen(dna generations) -> colony {
        cell colony sequence = [0, 1]
        replicate (sequence.length < generations) {
            dna nextGen = sequence[-1] + sequence[-2]
            sequence.inject(nextGen)
        }
        return sequence
    }

    nucleus() {
        fossil dna target = 10
        fossil enzyme isReady = active
        mutate (isReady) {
            cell colony result = computeGen(target)
            secrete("Generation Complete:")
            secrete(result)
        } adapt {
            secrete("System is dormant.")
        }
    }
}
```

```
$ biolang fibonacci.bio
Generation Complete:
[0, 1, 1, 2, 3, 5, 8, 13, 21, 34]
```

## Keywords

| Keyword | Means |
|---|---|
| `organism` | the module. one per file. |
| `nucleus` | entry point. runs first. |
| `organ` | a function |
| `cell` | a variable you can change |
| `fossil` | a constant. enforced, not suggested. |
| `mutate` | if |
| `adapt` | else — and `adapt mutate (...)` is an else-if chain |
| `replicate` | while |
| `traverse` | counted for-loop: `traverse (init; cond; step)` |
| `sever` | break |
| `skip` | continue |
| `graft` | pull in another `.bio` file |
| `membrane` | a struct: named fields, optionally methods |
| `trait` | a promise about behaviour |
| `witnesses` | a membrane keeping that promise |
| `spore` | an anonymous function |

## Membranes: structs with behaviour

```
membrane Circle {
    dna r
    organ area() -> dna { return 3 * r * r }
}

cell Circle c = Circle { r = 2 }
c.area()          // 12
c.r = 5           // fields assign
```

Records are **references**: passing one to an organ and mutating a field there is visible to the caller. Every declared field must be supplied at construction — a partially built record is a bug, not a feature.

## Traits: one function, many types

```
trait Shape {
    organ area() -> dna
    organ name() -> rna
}

membrane Circle witnesses Shape {
    dna r
    organ area() -> dna { return 3 * r * r }
    organ name() -> rna { return "circle" }
}

membrane Square witnesses Shape {
    dna side
    organ area() -> dna { return side * side }
    organ name() -> rna { return "square" }
}

organ total(Shape a, Shape b) -> dna { return a.area() + b.area() }
```

`total(circle, square)` works without a type switch. Witnessing is checked when the program loads: promising `Shape` without implementing `name()` is a load-time error, not a surprise on whichever path happens to call it.

Satisfaction is **nominal, not structural**: a `Blob` that happens to have an `area()` is still refused where a `Shape` is expected. Matching by shape accepts coincidences.

## Closures

```
organ makeAdder(dna n) -> organ {
    return spore (dna x) -> dna { return x + n }
}

cell organ add5 = makeAdder(5)
add5(10)          // 15
```

Organs are values: passable, returnable, callable through a variable. A named organ closes over the **global** scope only — capturing the caller's locals would be dynamic scoping.

## The colony library

Seventeen methods, all reachable from a `.bio` program:

```
inject(x)      length          insert(at, x)    remove(at)     clear()
contains(x)    indexOf(x)      reverse()        sort([desc])   sum()
min()          max()           first()          last()         slice(a[,b])
copy()         join([sep])
```

Indexing accepts negatives, so `-1` is the last element. `slice` is
end-exclusive: `c.slice(1, 3)` gives two elements.

## Multiple files

```
organism Main {
    graft "mathlib.bio"
    nucleus() { secrete(square(7)) }
}
```

Paths are relative to the file that writes the graft. Cycles are detected and
refused, and a missing target is an error rather than a silent no-op.

## Build and run

Needs the .NET SDK (built against .NET 10).

```bash
git clone https://github.com/athallajovian9-cyber/BioLang
cd BioLang/src
dotnet build
./bin/Debug/net10.0/biolang ../tests/fibonacci.bio
```

Debug modes:

```bash
biolang --tokens file.bio   # dump the token stream
biolang --ast    file.bio   # parse, report the tree, do not run
```

Test suite — **79 checks, each one a real program through the real interpreter**:

```bash
cd BioLang && bash run_tests.sh
```

## What it is

A tree-walking interpreter. Lexer, recursive-descent parser, AST, evaluator.
Written in C#.

## Self-hosting

The lexer is also written **in BioLang** — `selfhost/lexer.bio`. It reads a `.bio`
source file and emits the same token stream the C# lexer does.

```
bash selfhost/check_lexer.sh

PASS  fibonacci.bio          100 tokens identical
PASS  lexer.bio             1943 tokens identical
```

It is checked by **byte-equality against the C# reference**, not against
hand-written expectations. That distinction matters: byte-equality is the only
test that catches a dropped character, a misread escape, or a column drifting by
one — and all three happened while this was being written. `lexer.bio` lexing its
own 266-line source is the interesting case, because it exercises strings with
escapes, both comment forms and every token type through the self-hosted path.

Three bugs came out of that comparison, none findable by reading:

1. `\r` in a string literal produced the letter `r`, because the C# escape table
   handled `\n`, `\t`, `\"` and `\\` and **silently dropped the backslash** on
   anything else. A whitespace check written as a CR literal therefore matched the
   letter `r`, and every identifier starting with `r` was skipped as whitespace.
   Unknown escapes are now an error rather than being guessed at.
2. The same bug again, independently, in the self-hosted lexer's escape table.
3. `void` was missing from the self-hosted keyword list.

Status: **stage one of four.**

```
1  lexer in BioLang       done, byte-identical
2  parser in BioLang      not started
3  evaluator in BioLang   not started
4  bootstrap              not started
```

Stage 4 is the one that means "self-hosted" in the Rust sense: the `.bio`
interpreter running `.bio` programs under the C# one. It still needs a C# host to
start, which is the same stage0 problem every self-hosted language has.

## What it is not

Stated plainly, because overselling an interpreter is the fastest way to lose a
reader's trust:

- **Not a compiler.** No bytecode, no JIT, no optimiser. The original
  specification said "compiler"; this is the honest first target instead.
- **No generics, and I would argue against them.** `colony<T>` would be
  decoration: the runtime already tags every value, so it adds syntax and no
  safety. Traits are the polymorphism that was actually missing.
- **No user-defined operators, no namespaces, no method overloading.**
- **Traits cannot be composed** — a trait may not require another trait.
- **Loops are capped** at 1,000,000 iterations so a runaway loop fails loudly
  instead of hanging the machine. Raise it with `--max-loop N`.

## Specification defects found while implementing

The published spec could not parse its own example program. Three fixes, each
documented at the code that implements it:

**1. `nucleus()` was unreachable.** The grammar defined
`MainDecl ::= "nucleus" "(" ")" Block` but never referenced it from `Program`,
which allowed only `FunctionDecl | Statement`. The fibonacci example uses
`nucleus()`, so nothing could parse it.

**2. No `Expression` production existed.** The grammar used `Expression` in seven
places without defining it anywhere. Precedence is now
`comparison > term > factor > unary > postfix > primary`.

**3. The grammar rejected the spec's own example.** `VarDeclaration` requires
`cell` or `fossil`, but the example writes `dna nextGen = ...` bare. A bare typed
declaration is now legal and means the same as `cell`; the extension is marked in
the parser.

## Semantic decisions the spec left open

Each of these is a real choice, so each is written down in `Interpreter.cs` with
its reasoning rather than left to chance:

- `dna` prints as an integer when the value is whole — no trailing `.0`.
- `fossil` refuses reassignment **and** mutation. The spec only required the
  first, but `fossil colony c` followed by `c.inject(1)` mutates it just as
  surely as `c = ...` would, and a rule that permits that is not a rule.
- Negative indexing counts from the end.
- Conditions coerce: `enzyme` is itself, `dna` is non-zero, `rna` is non-empty.
- Types are enforced on declaration, assignment, argument and return.

## Licence

MIT.