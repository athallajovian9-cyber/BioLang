# BioLang — handoff brief for stage 2: the parser, written in BioLang

Paste everything below the line into the model.

---

## The job

BioLang is a small interpreted language where the keywords are biology. It is
being made **self-hosting**: the interpreter is being rewritten in BioLang itself,
the way Rust's compiler is written in Rust.

**Stage 1 is done and passing.** The lexer now exists in both languages:

- `selfhost/lexer.bio` — the lexer, written in BioLang
- `src/Lexer.cs` — the C# reference implementation
- `selfhost/check_lexer.sh` — the test, and it passes

**Your task is stage 2: write the parser in BioLang**, producing the same syntax
tree the C# parser produces.

Root: `C:\Users\RDC\Desktop\BioLang`

## Read these first, in this order

1. **`selfhost/lexer.bio`** — a working, tested 266-line BioLang program. This is
   your best reference for how to write the language. It already reads a file,
   builds a `colony` of `membrane` values, walks strings character by character,
   uses `replicate`/`sever`/`skip`, and defines helper `organ`s. Copy its style.
2. **`src/Parser.cs`** — the C# parser. Your output must match what this produces.
   It contains the grammar, the AST node shapes, and comments explaining three
   defects in the original published spec.
3. **`selfhost/check_lexer.sh`** — the verification pattern. Read how it compares
   against the C# side rather than against hand-written expectations.

## The verification standard, and why it is the whole task

Stage 1 landed **three real bugs**, and **none of them were findable by reading
the code**:

- an escape the table did not recognise was rewritten to its own letter, so a
  carriage-return escape produced the string `r`, and — because the whitespace
  test was written as that literal — **every identifier starting with `r` was
  skipped as whitespace**. `replicate` lexed as `eplicate`. It had been wrong,
  silently, for three released versions.
- the same bug again, independently, in the self-hosted lexer's escape table.
- `void` missing from the self-hosted keyword list.

A hand-written expectation would have passed all three. **Byte-equality against
the C# implementation caught all three immediately.**

So: your parser must be checked the same way. Run both parsers on the same source
and compare the tree they produce, node for node. Do not write tests that assert
what you believe your parser does — assert equality against the reference.

**Prove your check can fail.** Break something on purpose, watch it go red,
restore, watch it pass. A comparison that has never failed is not a comparison.

## Grammar, as implemented in `src/Parser.cs`

```
Program        ::= "organism" Identifier "{" ( FunctionDecl | TraitDecl
                                              | MembraneDecl | MainDecl | Statement )* "}"
TraitDecl      ::= "trait" Identifier "{" FunctionSig* "}"
FunctionSig    ::= "organ" Identifier "(" ParamList? ")" "->" Type
MembraneDecl   ::= "membrane" Identifier ( "witnesses" Identifier ("," Identifier)* )?
                   "{" ( FieldDecl | FunctionDecl )* "}"
FieldDecl      ::= Type Identifier
FunctionDecl   ::= "organ" Identifier "(" ParamList? ")" "->" Type Block
MainDecl       ::= "nucleus" "(" ")" Block
Block          ::= "{" Statement* "}"

Statement      ::= VarDecl | Assign | FieldAssign | IfStmt | WhileStmt | ForStmt
                 | PrintStmt | ReturnStmt | BreakStmt | ContinueStmt
                 | GraftStmt | ExprStmt
VarDecl        ::= ("cell" | "fossil" | Type) Type Identifier "=" Expression
FieldAssign    ::= Identifier "." Identifier "=" Expression
IfStmt         ::= "mutate" "(" Expression ")" Block ( "adapt" ( IfStmt | Block ) )?
WhileStmt      ::= "replicate" "(" Expression ")" Block
ForStmt        ::= "traverse" "(" SimpleStmt? ";" Expression? ";" SimpleStmt? ")" Block
PrintStmt      ::= "secrete" "(" Expression ")"
GraftStmt      ::= "graft" String

Expression     ::= Comparison
Comparison     ::= Term ( ("<"|">"|"<="|">="|"=="|"!=") Term )*
Term           ::= Factor ( ("+"|"-") Factor )*
Factor         ::= Unary ( ("*"|"/") Unary )*
Unary          ::= ("-"|"+")? Postfix
Postfix        ::= Primary ( "." Identifier ( "(" Args? ")" )?
                           | "[" Expression "]"
                           | "(" Args? ")" )*
Primary        ::= Number | String | "active" | "dormant"
                 | "spore" "(" ParamList? ")" "->" Type Block
                 | Identifier ( "(" Args? ")" | "{" FieldInitList "}" )?
                 | "(" Expression ")" | ArrayLiteral
ArrayLiteral   ::= "[" ( Expression ( "," Expression )* )? "]"
FieldInitList  ::= ( Identifier "=" Expression ( "," Identifier "=" Expression )* )?
```

Note `Primary`'s identifier case: `f(...)` is a call, `Point { x = 1 }` is
construction, and a bare identifier is a variable. A brace after a bare
identifier is unambiguous — every other block opener is preceded by a keyword or
a closing paren.

## Three spec defects the C# parser fixes. Do not reintroduce them.

1. **`nucleus()` was unreachable.** The published grammar defined `MainDecl` but
   never referenced it from `Program`, so the entry point had no production. The
   spec could not parse its own example program.
2. **No `Expression` production existed**, though the grammar used `Expression`
   in seven places. Precedence is as written above.
3. **`VarDeclaration` required `cell` or `fossil`**, but the spec's own example
   writes `dna nextGen = ...` bare. A bare typed declaration is legal and means
   `cell`.

## BioLang syntax reference

```bio
organism Name {
    membrane Tok { rna kind  rna text  dna line  dna col }
    membrane Circle witnesses Shape { dna r  organ area() -> dna { return 3 * r * r } }
    trait Shape { organ area() -> dna }
    organ f(dna n, rna s) -> dna { return n }
    organ proc(colony c) -> void { c.inject(1) }   // void may fall off the end
    nucleus() { }                                   // entry point
}
```

Statements: `cell Type x = e` · `fossil Type x = e` · `Type x = e` (implies cell) ·
`x = e` · `obj.field = e` · `mutate (e) { } adapt { }` · `adapt mutate (e) { }`
chains · `replicate (e) { }` · `traverse (init; cond; step) { }` · `sever` ·
`skip` · `secrete(e)` · `return e` · `graft "file.bio"`

Types: `rna` (string) · `dna` (number) · `enzyme` (bool) · `colony` (array) ·
any membrane name · `organ` (a function value) · `void`

Values: `[1, 2, 3]` · `Point { x = 1, y = 2 }` · `spore (dna x) -> dna { ... }` ·
`active` / `dormant`

`colony` methods: `length` `inject` `insert` `remove` `clear` `contains`
`indexOf` `reverse` `sort` `sum` `min` `max` `first` `last` `slice` `copy` `join`

`rna` methods: `.length` `s[i]` `slice` `indexOf` `contains` `startsWith`
`endsWith` `upper` `lower` `trim` `chars` `split`

Builtins: `ord(c)` `chr(n)` `readFile(path)` `writeFile(path, text)`
`appendFile(path, text)` `fileExists(path)` `absorb([prompt])` `rna(x)` `dna(x)`

Numbers print without a trailing `.0` when whole. Indexing accepts negatives.
`fossil` refuses both reassignment and mutation.

## The one real constraint you will hit

**BioLang has no map type.** A symbol table has to be a `colony` of membranes
scanned linearly, or two parallel `colony`s (names + values). That is O(n) per
lookup. It works and is fine for a prototype — `keywordKind` in `lexer.bio`
already does exactly this. Do not invent a map; use what the language has and say
so in a comment.

## Hard rules

- **Do not change `src/`.** The C# implementation is the reference. If you think
  it is wrong, say so in your report instead of editing it.
- **Do not change `selfhost/lexer.bio`** unless you find a genuine bug — and if
  you do, re-run `bash selfhost/check_lexer.sh` before and after.
- **Keep `bash run_tests.sh` at 117 passes.** It runs the lexer check too.
- **No new dependencies.** BioLang only, one file if you can manage it.
- **Comments explain why, not what.** Match `lexer.bio`: every non-obvious line
  says what breaks without it.
- **Never claim something works without running it.** If you could not verify a
  claim, say so plainly instead of asserting it.

## What to hand back

1. `selfhost/parser.bio` — the parser, in BioLang.
2. `selfhost/check_parser.sh` — the byte-equality check against `src/Parser.cs`,
   following the shape of `check_lexer.sh`.
3. The output of that check, on at least `tests/fibonacci.bio` and
   `selfhost/lexer.bio`.
4. A one-line statement of what the check does NOT cover. Every check has a blind
   spot; name yours rather than implying completeness.
5. If you could not get equality on some node, **say which node and why** rather
   than loosening the comparison until it passes. A comparison weakened to pass
   is worse than an honest failure, because it hides the next bug too.
