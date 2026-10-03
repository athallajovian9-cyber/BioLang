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
| `adapt` | else |
| `replicate` | while |
| `secrete` | print |
| `absorb` | read input — **declared, not yet wired** |
| `active` / `dormant` | true / false |
| `rna` / `dna` / `enzyme` / `colony` | string / number / boolean / array |

`colony` carries two methods: `.length` and `.inject(element)`. Indexing accepts
negatives, so `-1` is the last element.

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

Test suite — **41 checks, each one a real program through the real interpreter**:

```bash
cd BioLang && bash run_tests.sh
```

## What it is

A tree-walking interpreter. Lexer, recursive-descent parser, AST, evaluator.
Written in C#.

## What it is not

Stated plainly, because overselling an interpreter is the fastest way to lose a
reader's trust:

- **Not a compiler.** No bytecode, no JIT, no optimiser. The original
  specification said "compiler"; this is the honest first target instead.
- **`absorb` does nothing.** It lexes and parses; there is no implementation
  behind it. Reading input needs a stream design that does not exist yet.
- **No for-loop, no else-if chain, no `break`.** `replicate` and nested `mutate`
  cover the same ground less conveniently.
- **One file per organism.** No imports.
- **The standard library is two array methods.** Mutating arrays is done by
  hand — `c.inject(x)` appends; removing needs a rebuild.
- **No closures, structs, or generics.**
- **Loops are capped** at 1,000,000 iterations so a runaway `replicate` fails
  loudly instead of hanging the machine.

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