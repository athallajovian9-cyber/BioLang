// BioLang v1.2 - AST to JSON.
//
// Exists for one reason: so the self-hosted parser can be checked by
// BYTE-EQUALITY against this one, rather than against hand-written
// expectations. That distinction is the whole test - it is what caught three
// bugs in the lexer that reading the code would never have shown.
//
// The shape is deliberately flat and stable:
//   every node is {"type": "...", ...fields...}
//   tokens become {"line": n, "col": n}
//   lists stay lists, nulls stay null
//
// Anything omitted would be a thing the self-hosted parser could get wrong
// without the comparison noticing.
using System.Globalization;
using System.Text;

namespace BioLang;

public static class AstJson
{
    public static string Dump(Program p)
    {
        var sb = new StringBuilder();
        W(sb, "{");
        K(sb, "type"); S(sb, "Program"); C(sb);
        K(sb, "name"); S(sb, p.Name); C(sb);
        K(sb, "functions"); Fns(sb, p.Functions); C(sb);
        K(sb, "membranes"); Mems(sb, p.Membranes); C(sb);
        K(sb, "traits"); Traits(sb, p.Traits); C(sb);
        K(sb, "main");
        if (p.Main is null) N(sb); else Main(sb, p.Main);
        C(sb);
        K(sb, "topLevel"); Stmts(sb, p.TopLevel);
        W(sb, "}");
        return sb.ToString();
    }

    // ------------------------------------------------------------- writing

    private static void W(StringBuilder sb, string s) => sb.Append(s);

    private static void K(StringBuilder sb, string key)
    {
        Q(sb, key);
        sb.Append(':');
    }

    private static void C(StringBuilder sb) => sb.Append(',');

    private static void N(StringBuilder sb) => sb.Append("null");

    private static void Q(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (char ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
    }

    private static void S(StringBuilder sb, string value) => Q(sb, value);

    // Numbers are written invariantly and without a trailing .0, so the two
    // implementations cannot disagree about formatting a whole number.
    private static void Num(StringBuilder sb, double v)
    {
        if (Math.Abs(v - Math.Round(v)) < 1e-9 && Math.Abs(v) < 1e15)
            sb.Append(((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture));
        else
            sb.Append(v.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void B(StringBuilder sb, bool v) => sb.Append(v ? "true" : "false");

    private static void Tok(StringBuilder sb, Token t)
    {
        W(sb, "{"); K(sb, "line"); sb.Append(t.Line); C(sb);
        K(sb, "col"); sb.Append(t.Col); W(sb, "}");
    }

    private static void TypeRefOut(StringBuilder sb, TypeRef t)
    {
        W(sb, "{"); K(sb, "kind"); S(sb, t.Kind.ToString()); C(sb);
        K(sb, "name");
        if (t.Name is null) N(sb); else S(sb, t.Name);
        W(sb, "}");
    }

    // -------------------------------------------------------------- nodes

    private static void Fns(StringBuilder sb, List<FunctionDecl> fns)
    {
        W(sb, "[");
        for (int i = 0; i < fns.Count; i++)
        {
            if (i > 0) C(sb);
            Fn(sb, fns[i]);
        }
        W(sb, "]");
    }

    private static void Fn(StringBuilder sb, FunctionDecl f)
    {
        W(sb, "{"); K(sb, "type"); S(sb, "FunctionDecl"); C(sb);
        K(sb, "name"); S(sb, f.Name); C(sb);
        K(sb, "returnType"); TypeRefOut(sb, f.ReturnType); C(sb);
        K(sb, "params");
        W(sb, "[");
        for (int i = 0; i < f.Params.Count; i++)
        {
            if (i > 0) C(sb);
            W(sb, "{"); K(sb, "type"); S(sb, "Param"); C(sb);
            K(sb, "name"); S(sb, f.Params[i].Name); C(sb);
            K(sb, "paramType"); TypeRefOut(sb, f.Params[i].Type); C(sb);
            K(sb, "tok"); Tok(sb, f.Params[i].Tok);
            W(sb, "}");
        }
        W(sb, "]"); C(sb);
        K(sb, "body"); Block(sb, f.Body); C(sb);
        K(sb, "tok"); Tok(sb, f.Tok);
        W(sb, "}");
    }

    private static void Mems(StringBuilder sb, List<MembraneDecl> mems)
    {
        W(sb, "[");
        for (int i = 0; i < mems.Count; i++)
        {
            if (i > 0) C(sb);
            var m = mems[i];
            W(sb, "{"); K(sb, "type"); S(sb, "MembraneDecl"); C(sb);
            K(sb, "name"); S(sb, m.Name); C(sb);
            K(sb, "fields");
            W(sb, "[");
            for (int j = 0; j < m.Fields.Count; j++)
            {
                if (j > 0) C(sb);
                W(sb, "{"); K(sb, "type"); S(sb, "FieldDecl"); C(sb);
                K(sb, "name"); S(sb, m.Fields[j].Name); C(sb);
                K(sb, "fieldType"); TypeRefOut(sb, m.Fields[j].Type); C(sb);
                K(sb, "tok"); Tok(sb, m.Fields[j].Tok);
                W(sb, "}");
            }
            W(sb, "]"); C(sb);
            K(sb, "methods"); Fns(sb, m.Methods); C(sb);
            K(sb, "traits");
            W(sb, "[");
            for (int j = 0; j < m.Traits.Count; j++)
            {
                if (j > 0) C(sb);
                W(sb, "{"); K(sb, "trait"); S(sb, m.Traits[j].Trait); C(sb);
                K(sb, "tok"); Tok(sb, m.Traits[j].Tok);
                W(sb, "}");
            }
            W(sb, "]"); C(sb);
            K(sb, "tok"); Tok(sb, m.Tok);
            W(sb, "}");
        }
        W(sb, "]");
    }

    private static void Traits(StringBuilder sb, List<TraitDecl> ts)
    {
        W(sb, "[");
        for (int i = 0; i < ts.Count; i++)
        {
            if (i > 0) C(sb);
            W(sb, "{"); K(sb, "type"); S(sb, "TraitDecl"); C(sb);
            K(sb, "name"); S(sb, ts[i].Name); C(sb);
            K(sb, "signatures"); Fns(sb, ts[i].Signatures); C(sb);
            K(sb, "tok"); Tok(sb, ts[i].Tok);
            W(sb, "}");
        }
        W(sb, "]");
    }

    private static void Main(StringBuilder sb, MainDecl m)
    {
        W(sb, "{"); K(sb, "type"); S(sb, "MainDecl"); C(sb);
        K(sb, "body"); Block(sb, m.Body); C(sb);
        K(sb, "tok"); Tok(sb, m.Tok);
        W(sb, "}");
    }

    private static void Block(StringBuilder sb, Block b)
    {
        W(sb, "{"); K(sb, "type"); S(sb, "Block"); C(sb);
        K(sb, "body"); Stmts(sb, b.Body);
        W(sb, "}");
    }

    private static void Stmts(StringBuilder sb, List<Stmt> list)
    {
        W(sb, "[");
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) C(sb);
            Stmt(sb, list[i]);
        }
        W(sb, "]");
    }

    private static void Stmt(StringBuilder sb, Stmt s)
    {
        switch (s)
        {
            case VarDecl d:
                W(sb, "{"); K(sb, "type"); S(sb, "VarDecl"); C(sb);
                K(sb, "mutable"); B(sb, d.Mutable); C(sb);
                K(sb, "declType"); TypeRefOut(sb, d.Type); C(sb);
                K(sb, "name"); S(sb, d.Name); C(sb);
                K(sb, "init"); Expr(sb, d.Init); C(sb);
                K(sb, "tok"); Tok(sb, d.Tok);
                W(sb, "}");
                break;

            case Assign a:
                W(sb, "{"); K(sb, "type"); S(sb, "Assign"); C(sb);
                K(sb, "name"); S(sb, a.Name); C(sb);
                K(sb, "value"); Expr(sb, a.Value); C(sb);
                K(sb, "tok"); Tok(sb, a.Tok);
                W(sb, "}");
                break;

            case FieldAssign fa:
                W(sb, "{"); K(sb, "type"); S(sb, "FieldAssign"); C(sb);
                K(sb, "target"); Expr(sb, fa.Target); C(sb);
                K(sb, "field"); S(sb, fa.Field); C(sb);
                K(sb, "value"); Expr(sb, fa.Value); C(sb);
                K(sb, "tok"); Tok(sb, fa.Tok);
                W(sb, "}");
                break;

            case IfStmt i:
                W(sb, "{"); K(sb, "type"); S(sb, "IfStmt"); C(sb);
                K(sb, "cond"); Expr(sb, i.Cond); C(sb);
                K(sb, "then"); Block(sb, i.Then); C(sb);
                K(sb, "else");
                if (i.Else is null) N(sb); else Block(sb, i.Else);
                W(sb, "}");
                break;

            case WhileStmt w:
                W(sb, "{"); K(sb, "type"); S(sb, "WhileStmt"); C(sb);
                K(sb, "cond"); Expr(sb, w.Cond); C(sb);
                K(sb, "body"); Block(sb, w.Body); C(sb);
                K(sb, "tok"); Tok(sb, w.Tok);
                W(sb, "}");
                break;

            case ForStmt f:
                W(sb, "{"); K(sb, "type"); S(sb, "ForStmt"); C(sb);
                K(sb, "init");
                if (f.Init is null) N(sb); else Stmt(sb, f.Init);
                C(sb);
                K(sb, "cond");
                if (f.Cond is null) N(sb); else Expr(sb, f.Cond);
                C(sb);
                K(sb, "step");
                if (f.Step is null) N(sb); else Stmt(sb, f.Step);
                C(sb);
                K(sb, "body"); Block(sb, f.Body); C(sb);
                K(sb, "tok"); Tok(sb, f.Tok);
                W(sb, "}");
                break;

            case PrintStmt p:
                W(sb, "{"); K(sb, "type"); S(sb, "PrintStmt"); C(sb);
                K(sb, "value"); Expr(sb, p.Value); C(sb);
                K(sb, "tok"); Tok(sb, p.Tok);
                W(sb, "}");
                break;

            case ReturnStmt r:
                W(sb, "{"); K(sb, "type"); S(sb, "ReturnStmt"); C(sb);
                K(sb, "value"); Expr(sb, r.Value); C(sb);
                K(sb, "tok"); Tok(sb, r.Tok);
                W(sb, "}");
                break;

            case BreakStmt b:
                W(sb, "{"); K(sb, "type"); S(sb, "BreakStmt"); C(sb);
                K(sb, "tok"); Tok(sb, b.Tok);
                W(sb, "}");
                break;

            case ContinueStmt co:
                W(sb, "{"); K(sb, "type"); S(sb, "ContinueStmt"); C(sb);
                K(sb, "tok"); Tok(sb, co.Tok);
                W(sb, "}");
                break;

            case GraftStmt g:
                W(sb, "{"); K(sb, "type"); S(sb, "GraftStmt"); C(sb);
                K(sb, "path"); S(sb, g.Path); C(sb);
                K(sb, "tok"); Tok(sb, g.Tok);
                W(sb, "}");
                break;

            case ExprStmt e:
                W(sb, "{"); K(sb, "type"); S(sb, "ExprStmt"); C(sb);
                K(sb, "value"); Expr(sb, e.Value);
                W(sb, "}");
                break;

            default:
                W(sb, "{"); K(sb, "type"); S(sb, "UnknownStmt:" + s.GetType().Name);
                W(sb, "}");
                break;
        }
    }

    private static void Expr(StringBuilder sb, Expr e)
    {
        switch (e)
        {
            case NumLit n:
                W(sb, "{"); K(sb, "type"); S(sb, "NumLit"); C(sb);
                K(sb, "value"); Num(sb, n.Value);
                W(sb, "}");
                break;

            case StrLit s:
                W(sb, "{"); K(sb, "type"); S(sb, "StrLit"); C(sb);
                K(sb, "value"); S(sb, s.Value);
                W(sb, "}");
                break;

            case BoolLit b:
                W(sb, "{"); K(sb, "type"); S(sb, "BoolLit"); C(sb);
                K(sb, "value"); B(sb, b.Value);
                W(sb, "}");
                break;

            case ArrayLit a:
                W(sb, "{"); K(sb, "type"); S(sb, "ArrayLit"); C(sb);
                K(sb, "items");
                W(sb, "[");
                for (int i = 0; i < a.Items.Count; i++)
                {
                    if (i > 0) C(sb);
                    Expr(sb, a.Items[i]);
                }
                W(sb, "]");
                W(sb, "}");
                break;

            case Ident id:
                W(sb, "{"); K(sb, "type"); S(sb, "Ident"); C(sb);
                K(sb, "name"); S(sb, id.Name); C(sb);
                K(sb, "tok"); Tok(sb, id.Tok);
                W(sb, "}");
                break;

            case Binary bi:
                W(sb, "{"); K(sb, "type"); S(sb, "Binary"); C(sb);
                K(sb, "op"); S(sb, bi.Op); C(sb);
                K(sb, "left"); Expr(sb, bi.L); C(sb);
                K(sb, "right"); Expr(sb, bi.R); C(sb);
                K(sb, "tok"); Tok(sb, bi.Tok);
                W(sb, "}");
                break;

            case Unary u:
                W(sb, "{"); K(sb, "type"); S(sb, "Unary"); C(sb);
                K(sb, "op"); S(sb, u.Op); C(sb);
                K(sb, "expr"); Expr(sb, u.E); C(sb);
                K(sb, "tok"); Tok(sb, u.Tok);
                W(sb, "}");
                break;

            case Call c:
                W(sb, "{"); K(sb, "type"); S(sb, "Call"); C(sb);
                K(sb, "name"); S(sb, c.Name); C(sb);
                K(sb, "args"); Exprs(sb, c.Args); C(sb);
                K(sb, "tok"); Tok(sb, c.Tok);
                W(sb, "}");
                break;

            case Invoke iv:
                W(sb, "{"); K(sb, "type"); S(sb, "Invoke"); C(sb);
                K(sb, "callee"); Expr(sb, iv.Callee); C(sb);
                K(sb, "args"); Exprs(sb, iv.Args); C(sb);
                K(sb, "tok"); Tok(sb, iv.Tok);
                W(sb, "}");
                break;

            case LambdaLit la:
                W(sb, "{"); K(sb, "type"); S(sb, "LambdaLit"); C(sb);
                K(sb, "returnType"); TypeRefOut(sb, la.ReturnType); C(sb);
                K(sb, "params");
                W(sb, "[");
                for (int i = 0; i < la.Params.Count; i++)
                {
                    if (i > 0) C(sb);
                    W(sb, "{"); K(sb, "type"); S(sb, "Param"); C(sb);
                    K(sb, "name"); S(sb, la.Params[i].Name); C(sb);
                    K(sb, "paramType"); TypeRefOut(sb, la.Params[i].Type); C(sb);
                    K(sb, "tok"); Tok(sb, la.Params[i].Tok);
                    W(sb, "}");
                }
                W(sb, "]"); C(sb);
                K(sb, "body"); Block(sb, la.Body); C(sb);
                K(sb, "tok"); Tok(sb, la.Tok);
                W(sb, "}");
                break;

            case Member m:
                W(sb, "{"); K(sb, "type"); S(sb, "Member"); C(sb);
                K(sb, "target"); Expr(sb, m.Target); C(sb);
                K(sb, "name"); S(sb, m.Name); C(sb);
                K(sb, "tok"); Tok(sb, m.Tok);
                W(sb, "}");
                break;

            case MethodCall mc:
                W(sb, "{"); K(sb, "type"); S(sb, "MethodCall"); C(sb);
                K(sb, "target"); Expr(sb, mc.Target); C(sb);
                K(sb, "name"); S(sb, mc.Name); C(sb);
                K(sb, "args"); Exprs(sb, mc.Args); C(sb);
                K(sb, "tok"); Tok(sb, mc.Tok);
                W(sb, "}");
                break;

            case Index ix:
                W(sb, "{"); K(sb, "type"); S(sb, "Index"); C(sb);
                K(sb, "target"); Expr(sb, ix.Target); C(sb);
                K(sb, "key"); Expr(sb, ix.Key); C(sb);
                K(sb, "tok"); Tok(sb, ix.Tok);
                W(sb, "}");
                break;

            case StructLit sl:
                W(sb, "{"); K(sb, "type"); S(sb, "StructLit"); C(sb);
                K(sb, "name"); S(sb, sl.Name); C(sb);
                K(sb, "fields");
                W(sb, "[");
                for (int i = 0; i < sl.Fields.Count; i++)
                {
                    if (i > 0) C(sb);
                    W(sb, "{"); K(sb, "field"); S(sb, sl.Fields[i].Field); C(sb);
                    K(sb, "value"); Expr(sb, sl.Fields[i].Value);
                    W(sb, "}");
                }
                W(sb, "]"); C(sb);
                K(sb, "tok"); Tok(sb, sl.Tok);
                W(sb, "}");
                break;

            default:
                W(sb, "{"); K(sb, "type"); S(sb, "UnknownExpr:" + e.GetType().Name);
                W(sb, "}");
                break;
        }
    }

    private static void Exprs(StringBuilder sb, List<Expr> list)
    {
        W(sb, "[");
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) C(sb);
            Expr(sb, list[i]);
        }
        W(sb, "]");
    }
}