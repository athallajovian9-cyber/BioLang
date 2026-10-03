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

public enum BioType { Rna, Dna, Enzyme, Colony, Void }

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
public sealed record Member(Expr Target, string Name, Token Tok) : Expr;
public sealed record MethodCall(Expr Target, string Name, List<Expr> Args, Token Tok) : Expr;
public sealed record Index(Expr Target, Expr Key, Token Tok) : Expr;

// ---------------------------------------------------------------- statements
public sealed record Param(BioType Type, string Name, Token Tok);
public sealed record Block(List<Stmt> Body) : Node;

public sealed record VarDecl(bool Mutable, BioType Type, string Name, Expr Init, Token Tok) : Stmt;
public sealed record Assign(string Name, Expr Value, Token Tok) : Stmt;
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

public sealed record FunctionDecl(BioType ReturnType, string Name, List<Param> Params,
                                  Block Body, Token Tok) : Node;
public sealed record MainDecl(Block Body, Token Tok) : Node;
public sealed record Program(string Name, List<FunctionDecl> Functions, MainDecl? Main,
                                 List<Stmt> TopLevel, Token Tok) : Node;

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
        MainDecl? main = null;
        var top = new List<Stmt>();

        while (!At(TokKind.RBrace) && !At(TokKind.EOF))
        {
            if (At(TokKind.Organ))
                fns.Add(ParseFunction());
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
        return new Program(name, fns, main, top, tok);
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

    private BioType ParseType()
    {
        var t = Cur.Kind switch
        {
            TokKind.Rna => BioType.Rna,
            TokKind.Dna => BioType.Dna,
            TokKind.Enzyme => BioType.Enzyme,
            TokKind.Colony => BioType.Colony,
            _ => throw new BioSyntaxError($"expected a type (rna, dna, enzyme, colony), found '{Cur.Text}'", Cur),
        };
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
                if (IsTypeToken(Cur.Kind) && Peek().Kind == TokKind.Identifier
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

    private static bool IsTypeToken(TokKind k) =>
        k is TokKind.Rna or TokKind.Dna or TokKind.Enzyme or TokKind.Colony;

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

        if (IsTypeToken(Cur.Kind) && Peek().Kind == TokKind.Identifier
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