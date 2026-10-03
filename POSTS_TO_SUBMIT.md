# Posts you submit yourself

I cannot post these. No Reddit or Hacker News account exists on this machine, and
creating one to post your project is astroturfing — it gets accounts and domains
banned, and it is not something I will do on your behalf.

Copy, paste, submit. Adjust the voice to yours first; both communities can smell
a template.

---

## r/ProgrammingLanguages

**Title:** BioLang — a small interpreted language where the keywords are biology. I implemented the spec and found it couldn't parse its own example.

**Body:**

I was handed a spec for a toy language called BioLang and asked to implement it.
The keyword set is biological — `organism`, `nucleus`, `organ`, `cell`, `fossil`,
`mutate`, `adapt`, `replicate`, `secrete` — and the types are `rna` (string),
`dna` (number), `enzyme` (bool), `colony` (array).

The interesting part wasn't the implementation. It was that the published
grammar could not parse the example program that shipped with it.

**Defect 1 — the entry point was unreachable.** The grammar defined

```
MainDecl ::= "nucleus" "(" ")" Block
```

but `Program` was `"organism" Identifier "{" ( FunctionDecl | Statement )* "}"`.
`MainDecl` is never referenced, so `nucleus()` had no production to parse
through. The example uses it.

**Defect 2 — `Expression` was never defined.** The grammar referenced `Expression`
in seven productions. There is no `Expression ::=` line anywhere. I chose
`comparison > term > factor > unary > postfix > primary` with the usual
precedence, but that was my call, not the spec's.

**Defect 3 — `VarDeclaration` rejected the spec's own syntax.**

```
VarDeclaration ::= ("cell" | "fossil") Type Identifier "=" Expression
```

A type may only follow `cell` or `fossil`. But the example writes

```
dna nextGen = sequence[-1] + sequence[-2]
```

bare, no keyword. I made a bare typed declaration legal and equivalent to `cell`,
and marked it in the parser as an extension rather than pretending it was always
intended.

There's also no `Expression` production for `sequence[-1]`, which the example uses
twice, and no `ReturnStmt` in `Statement`'s alternatives despite `ReturnStmt`
being a production — so `return` inside a block wasn't reachable either.

**A semantics decision worth arguing about.** The spec says `fossil` is immutable
and requires a hard error on reassignment. It says nothing about mutation. So:

```
fossil colony c = [1]
c.inject(2)          # legal? it's not reassignment
```

I made that an error. My reasoning: a rule you can walk around by calling a method
is not a rule, and the value changed either way. But it is a real design choice —
some languages treat binding immutability and value immutability as separate
things on purpose (see `final` in Java vs `const` in C++), and someone will
disagree with me. I'd like to hear the argument.

**What it is:** a tree-walking interpreter in C#, ~1,100 lines across lexer,
recursive-descent parser, AST and evaluator. 41 tests, each one a real program
executed end to end. Repo: https://github.com/athallajovian9-cyber/BioLang

**What it isn't:** not a compiler, no bytecode, no JIT, no optimiser. `absorb`
lexes and parses but has no implementation behind it — I left the keyword in
rather than silently dropping it, and it's documented as non-functional rather
than quietly broken. No for-loop, no closures, no imports.

Happy to hear where the semantics are wrong.

---

## Show HN

**Title:** Show HN: BioLang – a small language whose keywords are biology

**URL:** https://athallajovian9-cyber.github.io/BioLang/

**Comment to post immediately after:**

Author here. A spec for this landed in my lap and I implemented it in C# over a
couple of days.

The reason it might interest this crowd: the published grammar could not parse its
own example program, in three separate ways. The entry-point production was
defined and never referenced from the top level. `Expression` was used in seven
productions and never defined. And the variable-declaration rule required a
`cell`/`fossil` keyword that the example does not use. I fixed all three and
documented each one at the code that implements it rather than quietly patching
the spec.

The design question I'd defend in a comment thread is `fossil`. The spec requires
a hard error on reassignment but says nothing about mutation through a method. I
made both illegal, because `fossil colony c = [1]; c.inject(2)` mutates the value
whether or not it reassigns the binding. I think that's right, but it's arguable
and several languages deliberately draw that line elsewhere.

It is an interpreter, not a compiler — tree-walking, no bytecode, no optimiser.
`absorb` (read) is a keyword with no implementation behind it yet. Both limits are
stated in the README rather than glossed.

41 tests, each a real program through the real interpreter. MIT.

---

## What I am NOT posting, and why

**r/DataIsBeautiful — do not post there.** That subreddit is for visualisations of
real datasets. A programming language is neither data nor a visualisation; the
post gets removed and you collect a rule-breaking strike. Gemini suggested it on
the "if you add cool outputs" condition, and no output this project produces would
qualify.

**r/webdev — I would skip it.** The site is a hand-written static HTML page. It is
not a web development story, and a post whose real content is "here is my language
project" reads as self-promotion. Heavy self-promo gets removed there too.

**r/ProgrammingLanguages is the right target**, but read their rules first — they
are strict about low-effort posts and will say so bluntly. The post above leads
with a concrete finding rather than an announcement, which is what gives it a
chance.

**Show HN needs a real account** with some history. A brand-new account posting a
project link is the classic pattern their spam filter is built to catch. If you
have an account, post it Tuesday–Thursday morning US Eastern.