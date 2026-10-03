// BioLang v1.0 - AST and recursive-descent parser.
//
// GRAMMAR, as fixed against the published spec. Two corrections are marked FIX,
// because the spec as written cannot parse its own example program.
//
//   Program        ::= "organism" Identifier "{" ( FunctionDecl | MainDecl | Statement )* "}"
//                                                                         ^^^^^^^^ FIX
//     The spec defined  MainDecl ::= "nucleus" "(" ")" Block  but never
//     referenced it from Program, so `nucleus()` - which the fibonacci example
//     uses - had no production to parse through.
//
//   Expression     ::= Comparison           <- FIX: the spec used Expression in
//     Comparison   ::= Term ( ("<"|">"|"<="|">="|"=="|"!=") Term )*      seven
//     Term         ::= Factor ( ("+"|"-") Factor )*                      places
//     Factor       ::= Unary ( ("*"|"/") Unary )*                        but never
//     Unary        ::= ("-"|"!")? Postfix                                defined it
//     Postfix      ::= Primary ( "." Identifier ( "(" Args? ")" )? | "[" Expression "]" )*
//     Primary      ::= Number | String | "active" | "dormant"
//                    | Identifier ( "(" Args? ")" )? | "(" Expression ")" | ArrayLiteral
//     ArrayLiteral ::= "[" ( Expression ( "," Expression )* )? "]"
using System.Globalization;

namespace BioLang;

public enum BioType { Rna, Dna, Enzyme, Colony, Membrane, Organ, Void }

// A declared type. The old flat enum could not name anything the program
// defined, which is exactly why `membrane` had nowhere to live: a struct's type
// IS its name. Kind says what class of value it is; Name is set only for
// membranes, so `dna` and `Point` are distinguishable without a second enum.
public sealed record TypeRef(BioType Kind, string? Name = null)
{
    public static readonly TypeRef Rna = new(BioType.Rna);
    public static readonly TypeRef Dna = new(BioType.Dna);
    public static readonly TypeRef Enzyme = new(BioType.Enzyme);
    public static readonly TypeRef Colony = new(BioType.Colony);

    public static TypeRef Membrane(string name) => new(BioType.Membrane, name);

    // How the type reads in an error message.
    public override string ToString() => Name ?? Kind switch
    {
        BioType.Rna => "rna",
        BioType.Dna => "dna",
        BioType.Enzyme => "enzyme",
        BioType.Colony => "colony",
        _ => "void",
    };

    public bool SameAs(TypeRef other) =>
        Kind == other.Kind && string.Equals(Name, other.Name, StringComparison.Ordinal);
}

// A field on a membrane: a name and its declared type.
public sealed record FieldDecl(TypeRef Type, string Name, Token Tok);

public abstract record Node;
public abstract record Expr : Node;
public abstract record Stmt : Node;

// ---------------------------------------------------------------- expressions
public sealed record NumLit(double Value) : Expr;
public sealed record StrLit(string Value) : Expr;
public sealed record BoolLit(bool Value) : Expr;
public sealed record ArrayLit(List<Expr> Items) : Expr;
public sealed record Ident(string Name, Token Tok) : Expr;
public sealed record Binary(string Op, Expr L, Expr R, Token Tok) : Expr;
public sealed record Unary(string Op, Expr E, Token Tok) : Expr;
public sealed record Call(string Name, List<Expr> Args, Token Tok) : Expr;
// A call through a value rather than a name: `f(1)` where f holds an organ, or
// the result of any expression that yields one.
public sealed record Invoke(Expr Callee, List<Expr> Args, Token Tok) : Expr;
// `spore (dna n) -> dna { return n * 2 }` - an anonymous function.
public sealed record LambdaLit(List<Param> Params, TypeRef ReturnType, Block Body, Token Tok) : Expr;
public sealed record Member(Expr Target, string Name, Token Tok) : Expr;
public sealed record MethodCall(Expr Target, string Name, List<Expr> Args, Token Tok) : Expr;
public sealed record Index(Expr Target, Expr Key, Token Tok) : Expr;

// ---------------------------------------------------------------- statements
public sealed record Param(TypeRef Type, string Name, Token Tok);
public sealed record Block(List<Stmt> Body) : Node;

public sealed record VarDecl(bool Mutable, TypeRef Type, string Name, Expr Init, Token Tok) : Stmt;
public sealed record Assign(string Name, Expr Value, Token Tok) : Stmt;
// Assigning to a field: `p.x = 5`. A separate node rather than a general
// lvalue, because a field is the only mutable location besides a variable.
public sealed record FieldAssign(Expr Target, string Field, Expr Value, Token Tok) : Stmt;
public sealed record IfStmt(Expr Cond, Block Then, Block? Else) : Stmt;
public sealed record WhileStmt(Expr Cond, Block Body, Token Tok) : Stmt;
public sealed record PrintStmt(Expr Value, Token Tok) : Stmt;
public sealed record ReturnStmt(Expr Value, Token Tok) : Stmt;
public sealed record ExprStmt(Expr Value) : Stmt;

// v1.1 additions
public sealed record BreakStmt(Token Tok) : Stmt;
public sealed record ContinueStmt(Token Tok) : Stmt;
public sealed record ForStmt(Stmt? Init, Expr? Cond, Stmt? Step, Block Body, Token Tok) : Stmt;
public sealed record GraftStmt(string Path, Token Tok) : Stmt;

public sealed record FunctionDecl(TypeRef ReturnType, string Name, List<Param> Params,
                                  Block Body, Token Tok) : Node;
public sealed record MainDecl(Block Body, Token Tok) : Node;

// `membrane Point { dna x  dna y }` - a named record with typed fields, and
// optionally the organs that make its behaviour, and the traits it promises.
public sealed record MembraneDecl(string Name, List<FieldDecl> Fields,
                                  List<FunctionDecl> Methods, List<(string Trait, Token Tok)> Traits,
                                  Token Tok) : Node;

// `trait Shape { organ area() -> dna }` - a promise about behaviour.
// The signatures carry no body: a trait says what must exist, not what it does.
public sealed record TraitDecl(string Name, List<FunctionDecl> Signatures, Token Tok) : Node;

// `Point { x = 1, y = 2 }` - construction. Every field must be given; a partial
// record with undefined fields is a source of bugs, not a feature.
public sealed record StructLit(string Name, List<(string Field, Expr Value)> Fields, Token Tok) : Expr;

public sealed record Program(string Name, List<FunctionDecl> Functions, List<MembraneDecl> Membranes,
                             List<TraitDecl> Traits,
                             MainDecl? Main, List<Stmt> TopLevel, Token Tok) : Node;

// -------------------------------------------------------------------- parser
public sealed class Parser
{
    private readonly List<Token> _t;
    private int _p;

    public Parser(List<Token> tokens) => _t = tokens;

    private Token Cur => _t[_p];
    private Token Peek(int n = 1) => _t[Math.Min(_p + n, _t.Count - 1)];
    private bool At(TokKind k) => Cur.Kind == k;

    private Token Expect(TokKind k, string what)
    {
        if (!At(k)) throw new BioSyntaxError($"expected {what}, found '{Cur.Text}'", Cur);
        return _t[_p++];
    }

    private bool Match(TokKind k)
    {
        if (!At(k)) return false;
        _p++;
        return true;
    }

    // ------------------------------------------------------------ entry point
    public Program ParseProgram()
    {
        var tok = Expect(TokKind.Organism, "'organism'");
        string name = Expect(TokKind.Identifier, "organism name").Text;
        Expect(TokKind.LBrace, "'{'");

        var fns = new List<FunctionDecl>();
        var mems = new List<MembraneDecl>();
        var traits = new List<TraitDecl>();
        MainDecl? main = null;
        var top = new List<Stmt>();

        while (!At(TokKind.RBrace) && !At(TokKind.EOF))
        {
            if (At(TokKind.Organ))
                fns.Add(ParseFunction());
            else if (At(TokKind.Trait))
                traits.Add(ParseTrait());
            else if (At(TokKind.Membrane))
                mems.Add(ParseMembrane());
            else if (At(TokKind.Nucleus))
            {
                if (main is not null)
                    throw new BioSyntaxError("only one nucleus() is allowed per organism", Cur);
                main = ParseMain();
            }
            else
                // Top-level statements are collected, not discarded. Dropping them
                // silently was the old behaviour: a statement written outside any
                // block simply did nothing, with no error to explain why.
                top.Add(ParseStatement());
        }

        Expect(TokKind.RBrace, "'}'");
        Expect(TokKind.EOF, "end of file");
        return new Program(name, fns, mems, traits, main, top, tok);
    }

    // trait Shape { organ area() -> dna  organ name() -> rna }
    private TraitDecl ParseTrait()
    {
        var tok = Expect(TokKind.Trait, "'trait'");
        string name = Expect(TokKind.Identifier, "trait name").Text;
        Expect(TokKind.LBrace, "'{'");

        var sigs = new List<FunctionDecl>();
        while (!At(TokKind.RBrace) && !At(TokKind.EOF))
        {
            if (!At(TokKind.Organ))
                throw new BioSyntaxError(
                    $"a trait may only declare organs, found '{Cur.Text}'", Cur);

            var ftok = Expect(TokKind.Organ, "'organ'");
            string mname = Expect(TokKind.Identifier, "method name").Text;
            Expect(TokKind.LParen, "'('");
            var ps = new List<Param>();
            if (!At(TokKind.RParen))
            {
                do
                {
                    var pt = ParseType();
                    var pn = Expect(TokKind.Identifier, "parameter name");
                    ps.Add(new Param(pt, pn.Text, pn));
                } while (Match(TokKind.Comma));
            }
            Expect(TokKind.RParen, "')'");
            Expect(TokKind.Arrow, "'->'");
            var ret = ParseType();
            // A signature has no body. An empty Block stands in so the shared
            // FunctionDecl shape can be reused rather than a parallel record.
            sigs.Add(new FunctionDecl(ret, mname, ps, new Block(new List<Stmt>()), ftok));
        }
        Expect(TokKind.RBrace, "'}'");

        if (sigs.Count == 0)
            throw new BioSyntaxError($"trait '{name}' declares no organs", tok);
        var dupes = sigs.GroupBy(x => x.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
            throw new BioSyntaxError($"trait '{name}' declares '{dupes[0]}' more than once", tok);

        return new TraitDecl(name, sigs, tok);
    }

    // membrane Circle witnesses Shape { dna r  organ area() -> dna { ... } }
    private MembraneDecl ParseMembrane()
    {
        var tok = Expect(TokKind.Membrane, "'membrane'");
        string name = Expect(TokKind.Identifier, "membrane name").Text;

        var promises = new List<(string, Token)>();
        if (Match(TokKind.Witnesses))
        {
            do
            {
                var tt = Expect(TokKind.Identifier, "trait name");
                promises.Add((tt.Text, tt));
            } while (Match(TokKind.Comma));
        }

        Expect(TokKind.LBrace, "'{'");

        var fields = new List<FieldDecl>();
        var methods = new List<FunctionDecl>();

        while (!At(TokKind.RBrace) && !At(TokKind.EOF))
        {
            Match(TokKind.Comma);
            if (At(TokKind.RBrace)) break;

            if (At(TokKind.Organ))
            {
                methods.Add(ParseFunction());
                continue;
            }

            var type = ParseType();
            var fieldTok = Expect(TokKind.Identifier, "field name");
            fields.Add(new FieldDecl(type, fieldTok.Text, fieldTok));
        }
        Expect(TokKind.RBrace, "'}'");

        if (fields.Count == 0 && methods.Count == 0)
            throw new BioSyntaxError($"membrane '{name}' declares nothing", tok);

        var dupes = fields.GroupBy(f => f.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
            throw new BioSyntaxError(
                $"membrane '{name}' declares field '{dupes[0]}' more than once", tok);

        var mdupes = methods.GroupBy(m => m.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (mdupes.Count > 0)
            throw new BioSyntaxError(
                $"membrane '{name}' declares organ '{mdupes[0]}' more than once", tok);

        return new MembraneDecl(name, fields, methods, promises, tok);
    }

    private FunctionDecl ParseFunction()
    {
        var tok = Expect(TokKind.Organ, "'organ'");
        string name = Expect(TokKind.Identifier, "function name").Text;
        Expect(TokKind.LParen, "'('");

        var ps = new List<Param>();
        if (!At(TokKind.RParen))
        {
            do
            {
                var pt = ParseType();
                var pn = Expect(TokKind.Identifier, "parameter name");
                ps.Add(new Param(pt, pn.Text, pn));
            } while (Match(TokKind.Comma));
        }

        Expect(TokKind.RParen, "')'");
        Expect(TokKind.Arrow, "'->'");
        var ret = ParseType();
        var body = ParseBlock();
        return new FunctionDecl(ret, name, ps, body, tok);
    }

    private MainDecl ParseMain()
    {
        var tok = Expect(TokKind.Nucleus, "'nucleus'");
        Expect(TokKind.LParen, "'('");
        Expect(TokKind.RParen, "')'");
        return new MainDecl(ParseBlock(), tok);
    }

    // Accepts the four builtin types and any name declared by a `membrane`. The name
// is not resolved here: the interpreter owns the registry, and validating in the
// parser would mean two sources of truth for what exists.
    private TypeRef ParseType()
    {
        TypeRef t;
        switch (Cur.Kind)
        {
            case TokKind.Rna:     t = TypeRef.Rna; break;
            case TokKind.Dna:     t = TypeRef.Dna; break;
            case TokKind.Enzyme:  t = TypeRef.Enzyme; break;
            case TokKind.Colony:  t = TypeRef.Colony; break;
            // `organ` as a type: a parameter or field that takes a function.
            case TokKind.Organ:   t = new TypeRef(BioType.Organ); break;
            case TokKind.Void:    t = new TypeRef(BioType.Void); break;
            case TokKind.Identifier: t = TypeRef.Membrane(Cur.Text); break;
            default:
                throw new BioSyntaxError(
                    $"expected a type (rna, dna, enzyme, colony, organ, or a membrane name), found '{Cur.Text}'",
                    Cur);
        }
        _p++;
        return t;
    }

    private Block ParseBlock()
    {
        Expect(TokKind.LBrace, "'{'");
        var body = new List<Stmt>();
        while (!At(TokKind.RBrace) && !At(TokKind.EOF))
            body.Add(ParseStatement());
        Expect(TokKind.RBrace, "'}'");
        return new Block(body);
    }

    private Stmt ParseStatement()
    {
        switch (Cur.Kind)
        {
            case TokKind.Cell:
            case TokKind.Fossil:
                return ParseVarDecl();

            case TokKind.Mutate:
                return ParseIf();

            case TokKind.Replicate:
                return ParseWhile();

            case TokKind.Traverse:
                return ParseFor();

            case TokKind.Sever:
            {
                var tok = Expect(TokKind.Sever, "'sever'");
                Match(TokKind.Semicolon);
                return new BreakStmt(tok);
            }

            case TokKind.Skip:
            {
                var tok = Expect(TokKind.Skip, "'skip'");
                Match(TokKind.Semicolon);
                return new ContinueStmt(tok);
            }

            case TokKind.Graft:
            {
                var tok = Expect(TokKind.Graft, "'graft'");
                var path = Expect(TokKind.String, "a file path in quotes");
                Match(TokKind.Semicolon);
                return new GraftStmt(path.Text, tok);
            }

            case TokKind.Secrete:
            {
                var tok = Expect(TokKind.Secrete, "'secrete'");
                Expect(TokKind.LParen, "'('");
                var e = ParseExpr();
                Expect(TokKind.RParen, "')'");
                Match(TokKind.Semicolon);
                return new PrintStmt(e, tok);
            }

            case TokKind.Return:
            {
                var tok = Expect(TokKind.Return, "'return'");
                var e = ParseExpr();
                Match(TokKind.Semicolon);
                return new ReturnStmt(e, tok);
            }

            default:
            {
                // p.x = 5  -- assignment to a field.
                if (At(TokKind.Identifier) && Peek().Kind == TokKind.Dot
                    && Peek(2).Kind == TokKind.Identifier && Peek(3).Kind == TokKind.Assign)
                {
                    var baseTok = _t[_p++];
                    Expect(TokKind.Dot, "'.'");
                    var fieldTok = Expect(TokKind.Identifier, "field name");
                    Expect(TokKind.Assign, "'='");
                    var val = ParseExpr();
                    Match(TokKind.Semicolon);
                    return new FieldAssign(new Ident(baseTok.Text, baseTok), fieldTok.Text, val, fieldTok);
                }

                // Assignment or a bare expression statement.
                if (At(TokKind.Identifier) && Peek().Kind == TokKind.Assign)
                {
                    var nameTok = _t[_p++];
                    Expect(TokKind.Assign, "'='");
                    var val = ParseExpr();
                    Match(TokKind.Semicolon);
                    return new Assign(nameTok.Text, val, nameTok);
                }

                // EXTENSION: a bare typed declaration, `dna x = ...`, with no
                // cell/fossil keyword. The spec's grammar does not allow this -
                // VarDeclaration requires one of those two words - but the spec's
                // OWN fibonacci example uses it, so rejecting it would mean the
                // language cannot run the program it ships with. A bare
                // declaration is treated as mutable.
                //
                // An identifier in the type position is a membrane name, so this
                // also covers `Point p = Point { ... }`.
                if (TypeStartsHere() && Peek().Kind == TokKind.Identifier
                    && Peek(2).Kind == TokKind.Assign)
                {
                    // Capture the token WITHOUT advancing: ParseType() is what
                    // consumes it. Advancing here as well eats the type and
                    // leaves the parser looking for a type at the variable name.
                    var tok = Cur;
                    var type = ParseType();
                    var nameTok = Expect(TokKind.Identifier, "variable name");
                    Expect(TokKind.Assign, "'='");
                    var init = ParseExpr();
                    Match(TokKind.Semicolon);
                    return new VarDecl(true, type, nameTok.Text, init, tok);
                }

                var e = ParseExpr();
                Match(TokKind.Semicolon);
                return new ExprStmt(e);
            }
        }
    }

    // True when the current token could begin a type: one of the four builtins,
    // or an identifier naming a membrane.
    private bool TypeStartsHere() =>
        Cur.Kind is TokKind.Rna or TokKind.Dna or TokKind.Enzyme or TokKind.Colony
                      or TokKind.Organ or TokKind.Void
        || Cur.Kind == TokKind.Identifier;

    private Stmt ParseVarDecl()
    {
        var tok = _t[_p++];
        bool mutable = tok.Kind == TokKind.Cell;
        var type = ParseType();
        var nameTok = Expect(TokKind.Identifier, "variable name");
        Expect(TokKind.Assign, "'='");
        var init = ParseExpr();
        Match(TokKind.Semicolon);
        return new VarDecl(mutable, type, nameTok.Text, init, tok);
    }

    private Stmt ParseIf()
    {
        var tok = Expect(TokKind.Mutate, "'mutate'");
        Expect(TokKind.LParen, "'('");
        var cond = ParseExpr();
        Expect(TokKind.RParen, "')'");
        var then = ParseBlock();

        Block? els = null;
        if (Match(TokKind.Adapt))
        {
            // `adapt mutate (...)` is the else-if chain. The nested IfStmt is
            // wrapped in a one-statement block so IfStmt.Else stays a Block and
            // nothing downstream has to know about a second shape.
            els = At(TokKind.Mutate)
                ? new Block(new List<Stmt> { ParseIf() })
                : ParseBlock();
        }
        return new IfStmt(cond, then, els);
    }

    // traverse (cell dna i = 0; i < 10; i = i + 1) { ... }
    // A counted for-loop. The three clauses are optional, so `traverse (;;)` is
    // legal and behaves like replicate(active) - the cap still applies.
    private Stmt ParseFor()
    {
        var tok = Expect(TokKind.Traverse, "'traverse'");
        Expect(TokKind.LParen, "'('");

        Stmt? init = null;
        if (!At(TokKind.Semicolon)) init = ParseSimpleStatement();
        Expect(TokKind.Semicolon, "';' after the loop initialiser");

        Expr? cond = null;
        if (!At(TokKind.Semicolon)) cond = ParseExpr();
        Expect(TokKind.Semicolon, "';' after the loop condition");

        Stmt? step = null;
        if (!At(TokKind.RParen)) step = ParseSimpleStatement();
        Expect(TokKind.RParen, "')' after the loop step");

        var body = ParseBlock();
        return new ForStmt(init, cond, step, body, tok);
    }

    // Only a declaration or an assignment - what a for-loop header may contain.
    // Parsed inline rather than through ParseVarDecl, because that variant
    // consumes a trailing ';' as a statement terminator and would eat the
    // separator the loop header still needs.
    private Stmt ParseSimpleStatement()
    {
        if (At(TokKind.Cell) || At(TokKind.Fossil))
        {
            var tok = _t[_p++];
            bool mutable = tok.Kind == TokKind.Cell;
            var type = ParseType();
            var nameTok = Expect(TokKind.Identifier, "variable name");
            Expect(TokKind.Assign, "'='");
            return new VarDecl(mutable, type, nameTok.Text, ParseExpr(), tok);
        }

        if (At(TokKind.Identifier) && Peek().Kind == TokKind.Assign)
        {
            var nameTok = _t[_p++];
            Expect(TokKind.Assign, "'='");
            return new Assign(nameTok.Text, ParseExpr(), nameTok);
        }

        if (TypeStartsHere() && Peek().Kind == TokKind.Identifier
            && Peek(2).Kind == TokKind.Assign)
        {
            var tok = Cur;
            var type = ParseType();
            var nameTok = Expect(TokKind.Identifier, "variable name");
            Expect(TokKind.Assign, "'='");
            return new VarDecl(true, type, nameTok.Text, ParseExpr(), tok);
        }

        throw new BioSyntaxError(
            $"expected a declaration or an assignment here, found '{Cur.Text}'", Cur);
    }

    private Stmt ParseWhile()
    {
        var tok = Expect(TokKind.Replicate, "'replicate'");
        Expect(TokKind.LParen, "'('");
        var cond = ParseExpr();
        Expect(TokKind.RParen, "')'");
        var body = ParseBlock();
        return new WhileStmt(cond, body, tok);
    }

    // ------------------------------------------------------------ expressions
    private Expr ParseExpr() => ParseComparison();

    private Expr ParseComparison()
    {
        var l = ParseTerm();
        while (Cur.Kind is TokKind.Lt or TokKind.Gt or TokKind.Le or TokKind.Ge
               or TokKind.EqEq or TokKind.NotEq)
        {
            var op = _t[_p++];
            var r = ParseTerm();
            l = new Binary(op.Text, l, r, op);
        }
        return l;
    }

    private Expr ParseTerm()
    {
        var l = ParseFactor();
        while (Cur.Kind is TokKind.Plus or TokKind.Minus)
        {
            var op = _t[_p++];
            var r = ParseFactor();
            l = new Binary(op.Text, l, r, op);
        }
        return l;
    }

    private Expr ParseFactor()
    {
        var l = ParseUnary();
        while (Cur.Kind is TokKind.Star or TokKind.Slash)
        {
            var op = _t[_p++];
            var r = ParseUnary();
            l = new Binary(op.Text, l, r, op);
        }
        return l;
    }

    private Expr ParseUnary()
    {
        if (Cur.Kind is TokKind.Minus or TokKind.Plus)
        {
            var op = _t[_p++];
            return new Unary(op.Text, ParseUnary(), op);
        }
        return ParsePostfix();
    }

    private Expr ParsePostfix()
    {
        var e = ParsePrimary();
        for (; ; )
        {
            if (At(TokKind.Dot))
            {
                var dot = _t[_p++];
                var nameTok = Expect(TokKind.Identifier, "member name");
                if (At(TokKind.LParen))
                {
                    _p++;
                    var args = new List<Expr>();
                    if (!At(TokKind.RParen))
                    {
                        do { args.Add(ParseExpr()); } while (Match(TokKind.Comma));
                    }
                    Expect(TokKind.RParen, "')'");
                    e = new MethodCall(e, nameTok.Text, args, dot);
                }
                else
                {
                    // property access: .length
                    e = new Member(e, nameTok.Text, dot);
                }
                continue;
            }
            if (At(TokKind.LBracket))
            {
                var br = _t[_p++];
                var key = ParseExpr();
                Expect(TokKind.RBracket, "']'");
                e = new Index(e, key, br);
                continue;
            }

            // Calling whatever the expression produced: `makeAdder(2)(3)`.
            // A plain name followed by "(" is parsed as Call in ParsePrimary, so
            // this only fires for a callee that is not a bare identifier.
            if (At(TokKind.LParen) && e is not Ident)
            {
                var pt = _t[_p++];
                var args = new List<Expr>();
                if (!At(TokKind.RParen))
                {
                    do { args.Add(ParseExpr()); } while (Match(TokKind.Comma));
                }
                Expect(TokKind.RParen, "')'");
                e = new Invoke(e, args, pt);
                continue;
            }
            return e;
        }
    }

    private Expr ParsePrimary()
    {
        var tok = Cur;

        switch (tok.Kind)
        {
            case TokKind.Number:
                _p++;
                return new NumLit(double.Parse(tok.Text, CultureInfo.InvariantCulture));

            case TokKind.String:
                _p++;
                return new StrLit(tok.Text);

            case TokKind.Active:
                _p++;
                return new BoolLit(true);

            case TokKind.Dormant:
                _p++;
                return new BoolLit(false);

            case TokKind.LBracket:
            {
                _p++;
                var items = new List<Expr>();
                if (!At(TokKind.RBracket))
                {
                    do { items.Add(ParseExpr()); } while (Match(TokKind.Comma));
                }
                Expect(TokKind.RBracket, "']'");
                return new ArrayLit(items);
            }

            case TokKind.LParen:
            {
                _p++;
                var e = ParseExpr();
                Expect(TokKind.RParen, "')'");
                return e;
            }

            // spore (dna n) -> dna { return n * 2 }
            case TokKind.Spore:
            {
                _p++;
                Expect(TokKind.LParen, "'('");
                var ps = new List<Param>();
                if (!At(TokKind.RParen))
                {
                    do
                    {
                        var pt = ParseType();
                        var pn = Expect(TokKind.Identifier, "parameter name");
                        ps.Add(new Param(pt, pn.Text, pn));
                    } while (Match(TokKind.Comma));
                }
                Expect(TokKind.RParen, "')'");
                Expect(TokKind.Arrow, "'->'");
                var ret = ParseType();
                var body = ParseBlock();
                return new LambdaLit(ps, ret, body, tok);
            }

            case TokKind.Identifier:
            {
                _p++;
                if (At(TokKind.LParen))
                {
                    _p++;
                    var args = new List<Expr>();
                    if (!At(TokKind.RParen))
                    {
                        do { args.Add(ParseExpr()); } while (Match(TokKind.Comma));
                    }
                    Expect(TokKind.RParen, "')'");
                    return new Call(tok.Text, args, tok);
                }
                // `Point { x = 1, y = 2 }` - construction. A brace after a bare
                // identifier is unambiguous: every other construct that opens a
                // block is preceded by a keyword or a closing paren.
                if (At(TokKind.LBrace))
                {
                    _p++;
                    var fields = new List<(string, Expr)>();
                    while (!At(TokKind.RBrace) && !At(TokKind.EOF))
                    {
                        Match(TokKind.Comma);
                        if (At(TokKind.RBrace)) break;
                        var fieldTok = Expect(TokKind.Identifier, "field name");
                        Expect(TokKind.Assign, "'='");
                        fields.Add((fieldTok.Text, ParseExpr()));
                    }
                    Expect(TokKind.RBrace, "'}'");
                    return new StructLit(tok.Text, fields, tok);
                }
                return new Ident(tok.Text, tok);
            }

            // Builtins spelled with reserved words. `absorb`, `rna` and `dna`
            // lex as keywords rather than identifiers, so the expression parser
            // has to accept them explicitly or they are unusable as calls.
            case TokKind.Absorb:
            case TokKind.Rna:
            case TokKind.Dna:
            {
                if (Peek().Kind != TokKind.LParen)
                    throw new BioSyntaxError(
                        $"'{tok.Text}' is a type or builtin, not a value; did you mean {tok.Text}(...)?",
                        tok);
                _p++;                        // the keyword
                Expect(TokKind.LParen, "'('");
                var args = new List<Expr>();
                if (!At(TokKind.RParen))
                {
                    do { args.Add(ParseExpr()); } while (Match(TokKind.Comma));
                }
                Expect(TokKind.RParen, "')'");
                return new Call(tok.Text, args, tok);
            }

            default:
                throw new BioSyntaxError($"expected an expression, found '{tok.Text}'", tok);
        }
    }
}