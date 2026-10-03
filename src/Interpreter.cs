// BioLang v1.0 - tree-walking interpreter.
//
// SEMANTIC DECISIONS the specification left open. Each is a real choice, so it
// is written down rather than left to chance:
//
//  1. `dna` stores a double but prints as an integer when it is whole. Hex-free
//     64-bit maths was specified as "floating point / integer", which is two
//     types wearing one coat; the observable rule is "no trailing .0".
//  2. `fossil` immutability is enforced on BOTH reassignment and mutation.
//     Spec said only "hard error on reassign" - but `fossil colony x` followed by
//     `x.inject(1)` mutates it just as surely as `x = ...` would, and a rule that
//     permits that is not a rule.
//  3. Indexing accepts negatives: -1 is the last element. The spec defines only
//     .length and .inject, but its own example uses `sequence[-1]`.
//  4. Conditions coerce: enzyme is itself, dna is non-zero, rna is non-empty.
//     The example only passes an enzyme, so strictness here would be untested
//     guesswork.
//  5. Types are checked on decl, assign, argument and return. A language with
//     declared types that ignores them is worse than one with none.
using System.Globalization;

namespace BioLang;

public sealed class BioRuntimeError : Exception
{
    public BioRuntimeError(string msg, Token? tok = null)
        : base(tok is null ? msg : $"{msg} (line {tok.Line}, col {tok.Col})") { }
}

// A returned value plus the type it claims. Keeping the tag beside the value is
// what makes type checking possible without a separate symbol table pass.
//
// TypeName is set only when Type is Membrane: a struct's type is its name, and
// two different membranes must not be interchangeable.
public sealed record Value(BioType Type, object Raw, string? TypeName = null)
{
    public static Value Void() => new(BioType.Void, 0.0);

        // How the type reads in a message. Inlined rather than calling Interpreter's
        // helper: this record sits outside that class and cannot reach it.
        public string TypeLabel => Type switch
        {
            BioType.Membrane => TypeName ?? "membrane",
            BioType.Rna => "rna",
            BioType.Dna => "dna",
            BioType.Enzyme => "enzyme",
            BioType.Colony => "colony",
                        BioType.Organ => "organ",
                        _ => "void",
                    };

                public bool Truthy => Type switch
    {
        BioType.Enzyme => (bool)Raw,
        BioType.Dna => (double)Raw != 0,
        BioType.Rna => ((string)Raw).Length > 0,
        BioType.Colony => ((List<Value>)Raw).Count > 0,
        BioType.Membrane => true,
        _ => false,
    };
}

// A membrane's instance. A reference type on purpose: `p.x = 5` must be visible
// through every binding of p, which is what people expect from a record.
public sealed class BioStruct
{
    public string TypeName { get; }
    public Dictionary<string, Value> Fields { get; }

    public BioStruct(string typeName, Dictionary<string, Value> fields)
    {
        TypeName = typeName;
        Fields = fields;
    }
}

// A variable binding. Top-level rather than nested inside Interpreter, because
// BioClosure carries captured scopes and has to name the type.
//
// `Mutable` records how it was declared, so mutation can be refused.
public sealed record Cell(Value Val, bool Mutable, Token Tok)
{
    public Cell With(Value v) => new(v, Mutable, Tok);
}

// A callable: a named organ or an anonymous spore, plus the scope it was
// created in. Capturing the scope is what makes a closure a closure - without
// it, a spore passed out of a function would lose the variables it closed over.
public sealed class BioClosure
{
    public string Label { get; }
    public List<Param> Params { get; }
    public TypeRef ReturnType { get; }
    public Block Body { get; }
    public List<Dictionary<string, Cell>> Captured { get; }

    public BioClosure(string label, List<Param> parameters, TypeRef returnType,
                      Block body, List<Dictionary<string, Cell>> captured)
    {
        Label = label;
        Params = parameters;
        ReturnType = returnType;
        Body = body;
        Captured = captured;
    }
}

public sealed class Interpreter
{
    private readonly Program _program;
    private readonly Dictionary<string, FunctionDecl> _fns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MembraneDecl> _membranes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TraitDecl> _traits = new(StringComparer.Ordinal);
    private readonly List<Dictionary<string, Cell>> _scopes = new();
        private readonly TextWriter _out;
        private readonly TextReader _in;
    // `Mutable` records how the cell was declared, so mutation can be refused.
        public Interpreter(Program program, TextWriter? output = null, TextReader? input = null)
    {
        _program = program;
        _out = output ?? Console.Out;
        _in = input ?? Console.In;
        foreach (var f in program.Functions)
                {
                    if (!_fns.TryAdd(f.Name, f))
                        throw new BioRuntimeError($"organ '{f.Name}' is declared more than once", f.Tok);
                }
                foreach (var m in program.Membranes)
                        {
                            if (!_membranes.TryAdd(m.Name, m))
                                throw new BioRuntimeError($"membrane '{m.Name}' is declared more than once", m.Tok);
                        }
                        foreach (var t in program.Traits)
                        {
                            if (!_traits.TryAdd(t.Name, t))
                                throw new BioRuntimeError($"trait '{t.Name}' is declared more than once", t.Tok);
                            // A trait sharing a name with a membrane makes every type position
                            // ambiguous, so it is refused rather than resolved by precedence.
                            if (_membranes.ContainsKey(t.Name))
                                throw new BioRuntimeError(
                                    $"'{t.Name}' is declared as both a trait and a membrane", t.Tok);
                        }

                        // Validate in a SECOND pass: a membrane may reference one declared later
                        // in the file, and checking while registering would reject that.
                        foreach (var m in program.Membranes)
                        {
                            foreach (var fld in m.Fields)
                                if (fld.Type.Kind == BioType.Membrane && !_membranes.ContainsKey(fld.Type.Name!)
                                    && !_traits.ContainsKey(fld.Type.Name!))
                                    throw new BioRuntimeError(
                                        $"membrane '{m.Name}' has field '{fld.Name}' of unknown type '{fld.Type}'",
                                        fld.Tok);

                            // Promising a trait means implementing every signature it declares,
                            // with the same arity and the same types. Checked here so a missing
                            // method is a load-time error, not a surprise on the one code path
                            // that happens to call it.
                            foreach (var (traitName, tok) in m.Traits)
                            {
                                if (!_traits.TryGetValue(traitName, out var tr))
                                    throw new BioRuntimeError(
                                        $"membrane '{m.Name}' witnesses unknown trait '{traitName}'", tok);

                                foreach (var sig in tr.Signatures)
                                {
                                    var impl = m.Methods.FirstOrDefault(x => x.Name == sig.Name);
                                    if (impl is null)
                                        throw new BioRuntimeError(
                                            $"membrane '{m.Name}' witnesses {traitName} but does not implement '{sig.Name}'",
                                            tok);
                                    if (impl.Params.Count != sig.Params.Count)
                                        throw new BioRuntimeError(
                                            $"{m.Name}.{sig.Name} takes {impl.Params.Count} argument(s) but {traitName} " +
                                            $"declares {sig.Params.Count}", impl.Tok);
                                    if (!impl.ReturnType.SameAs(sig.ReturnType))
                                        throw new BioRuntimeError(
                                            $"{m.Name}.{sig.Name} returns {impl.ReturnType} but {traitName} " +
                                            $"declares {sig.ReturnType}", impl.Tok);
                                }
                            }
                        }
                    }

    public void Run()
        {
            if (_program.Main is null)
                throw new BioRuntimeError("no nucleus() found - an organism needs an entry point",
                                          _program.Tok);
            // One shared global scope: a cell declared at the top level of the
            // organism is visible inside nucleus(), which is the least surprising
            // reading of "top-level".
            PushScope();
            try
            {
                foreach (var s in _program.TopLevel)
                    if (Exec(s) is ReturnSignal r) throw r;
                ExecBlock(_program.Main.Body);
            }
            catch (BreakSignal)
            {
                throw new BioRuntimeError("sever is only valid inside a loop", _program.Tok);
            }
            catch (ContinueSignal)
            {
                throw new BioRuntimeError("skip is only valid inside a loop", _program.Tok);
            }
            PopScope();
        }

    // ------------------------------------------------------------------ scopes
    private void PushScope() => _scopes.Add(new Dictionary<string, Cell>(StringComparer.Ordinal));
    private void PopScope() => _scopes.RemoveAt(_scopes.Count - 1);

    private Cell Lookup(string name, Token tok)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
            if (_scopes[i].TryGetValue(name, out var c)) return c;
        throw new BioRuntimeError($"'{name}' is not defined", tok);
    }

    private void Define(string name, Cell cell, Token tok)
    {
        var top = _scopes[^1];
        if (top.ContainsKey(name))
            throw new BioRuntimeError($"'{name}' is already defined in this scope", tok);
        top[name] = cell;
    }

    // -------------------------------------------------------------- statements
    private void ExecBlock(Block b)
    {
        foreach (var s in b.Body)
        {
            if (Exec(s) is ReturnSignal r) throw r;
        }
    }

    private sealed class ReturnSignal : Exception
        {
            public Value Value { get; }
            public ReturnSignal(Value v) => Value = v;
        }

        // sever / skip. Carried as exceptions because a break can be several nested
        // blocks deep; unwinding is what the runtime already does for `return`.
        // Unlike return they are caught by the nearest loop, not by the function.
        private sealed class BreakSignal : Exception { }
        private sealed class ContinueSignal : Exception { }

        // v1.1: the loop cap is now a field, so it can be raised from the CLI for
        // long-running work instead of being a hardcoded wall.
        public int MaxLoopIterations { get; set; } = 1_000_000;

    private ReturnSignal? Exec(Stmt s)
    {
        switch (s)
        {
            case VarDecl d:
                        {
                            var v = Eval(d.Init);
                            RequireType(v, d.Type, d.Tok,
                                $"cannot store {v.TypeLabel} in {d.Type} '{d.Name}'");
                            Define(d.Name, new Cell(v, d.Mutable, d.Tok), d.Tok);
                            return null;
                        }

                        case Assign a:
                        {
                            var cell = Lookup(a.Name, a.Tok);
                            if (!cell.Mutable)
                                throw new BioRuntimeError(
                                    $"'{a.Name}' is a fossil and cannot be reassigned", a.Tok);
                            var v = Eval(a.Value);
                            RequireType(v, ToTypeRef(cell.Val), a.Tok,
                                $"cannot store {v.TypeLabel} in {cell.Val.TypeLabel} '{a.Name}'");
                            SetCell(a.Name, cell.With(v));
                            return null;
                        }

                        // p.x = 5. Mutating a field mutates the instance, which every binding
                        // of it sees - that is the point of a record.
                        case FieldAssign fa:
                        {
                            var target = Eval(fa.Target);
                            if (target.Type != BioType.Membrane)
                                throw new BioRuntimeError(
                                    $"'{fa.Field}' is only a field of a membrane, not of {target.TypeLabel}",
                                    fa.Tok);
                            var st = (BioStruct)target.Raw;
                                            if (!st.Fields.TryGetValue(fa.Field, out var old))
                                                throw new BioRuntimeError(
                                                    $"membrane '{st.TypeName}' has no field '{fa.Field}'", fa.Tok);
                                            var v = Eval(fa.Value);
                                            var declared = _membranes[st.TypeName].Fields.First(f => f.Name == fa.Field);
                                            RequireType(v, declared.Type, fa.Tok,
                                                $"cannot store {v.TypeLabel} in {declared.Type} '{st.TypeName}.{fa.Field}'");
                                            st.Fields[fa.Field] = v;
                                            return null;
                        }

            case PrintStmt p:
                _out.WriteLine(Display(Eval(p.Value)));
                return null;

            case ExprStmt e:
                Eval(e.Value);
                return null;

            case ReturnStmt r:
            {
                var v = Eval(r.Value);
                return new ReturnSignal(v);
            }

            case IfStmt i:
            {
                if (Eval(i.Cond).Truthy)
                {
                    PushScope();
                    try { ExecBlock(i.Then); } catch (ReturnSignal) { PopScope(); throw; }
                    PopScope();
                }
                else if (i.Else is not null)
                {
                    PushScope();
                    try { ExecBlock(i.Else); } catch (ReturnSignal) { PopScope(); throw; }
                    PopScope();
                }
                return null;
            }

            case WhileStmt w:
                        {
                            int guard = 0;
                            while (Eval(w.Cond).Truthy)
                            {
                                if (++guard > MaxLoopIterations)
                                    throw new BioRuntimeError(
                                        $"replicate ran {MaxLoopIterations} iterations without ending", w.Tok);
                                // The loop owns this scope, so break/continue can unwind to
                                // exactly here without leaking the scope.
                                PushScope();
                                try { ExecBlock(w.Body); }
                                catch (BreakSignal) { PopScope(); break; }
                                catch (ContinueSignal) { PopScope(); continue; }
                                catch (ReturnSignal) { PopScope(); throw; }
                                PopScope();
                            }
                            return null;
                        }

                        // traverse: initialise, test, body, step. `sever` skips the step,
                        // matching the usual for-loop contract.
                        case ForStmt f:
                        {
                            PushScope();
                            try
                            {
                                if (f.Init is not null) Exec(f.Init);
                                int guard = 0;
                                while (f.Cond is null || Eval(f.Cond).Truthy)
                                {
                                    if (++guard > MaxLoopIterations)
                                        throw new BioRuntimeError(
                                            $"traverse ran {MaxLoopIterations} iterations without ending", f.Tok);
                                    PushScope();
                                    try { ExecBlock(f.Body); }
                                    catch (ContinueSignal) { /* fall through to the step */ }
                                    catch (BreakSignal) { PopScope(); break; }
                                    finally { PopScope(); }
                                    if (f.Step is not null) Exec(f.Step);
                                }
                            }
                            catch (ReturnSignal) { PopScope(); throw; }
                            PopScope();
                            return null;
                        }

                        case BreakStmt:
                            throw new BreakSignal();

                        case ContinueStmt:
                                        throw new ContinueSignal();

                                    case GraftStmt g:
                                        // graft is resolved by the Loader before execution ever starts.
                                        // Reaching here means the loader missed one, which is a bug in
                                        // the loader rather than in the BioLang program.
                                        throw new BioRuntimeError(
                                            $"internal: graft '{g.Path}' was not resolved before execution", g.Tok);

            default:
                throw new BioRuntimeError("unhandled statement " + s.GetType().Name);
        }
    }

    private void SetCell(string name, Cell cell)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
            if (_scopes[i].ContainsKey(name)) { _scopes[i][name] = cell; return; }
        throw new BioRuntimeError($"'{name}' vanished from scope");
    }

    // ------------------------------------------------------------- expressions
    private Value Eval(Expr e)
    {
        switch (e)
        {
            case NumLit n: return new Value(BioType.Dna, n.Value);
            case StrLit s: return new Value(BioType.Rna, s.Value);
            case BoolLit b: return new Value(BioType.Enzyme, b.Value);

            case ArrayLit a:
                            return new Value(BioType.Colony, a.Items.Select(Eval).ToList());

                        // Point { x = 1, y = 2 }. Every declared field must be supplied:
                        // a partially built record is a bug waiting to happen, not a feature.
                        case StructLit sl:
                        {
                            if (!_membranes.TryGetValue(sl.Name, out var decl))
                                throw new BioRuntimeError(
                                    $"'{sl.Name}' is not a membrane. Declare it with: membrane {sl.Name} {{ ... }}",
                                    sl.Tok);

                            var given = new Dictionary<string, Value>(StringComparer.Ordinal);
                            foreach (var (field, expr) in sl.Fields)
                            {
                                if (!given.TryAdd(field, Eval(expr)))
                                    throw new BioRuntimeError(
                                        $"field '{field}' is set twice in this {sl.Name}", sl.Tok);
                            }

                            var fields = new Dictionary<string, Value>(StringComparer.Ordinal);
                            foreach (var f in decl.Fields)
                            {
                                if (!given.TryGetValue(f.Name, out var v))
                                    throw new BioRuntimeError(
                                        $"{sl.Name} is missing field '{f.Name}' ({f.Type})", sl.Tok);
                                RequireType(v, f.Type, sl.Tok,
                                    $"field '{f.Name}' of {sl.Name} is {f.Type}, got {v.TypeLabel}");
                                fields[f.Name] = v;
                            }

                            var unknown = given.Keys.Where(k => !decl.Fields.Any(f => f.Name == k)).ToList();
                            if (unknown.Count > 0)
                                throw new BioRuntimeError(
                                    $"membrane '{sl.Name}' has no field '{unknown[0]}'", sl.Tok);

                            return new Value(BioType.Membrane, new BioStruct(sl.Name, fields), sl.Name);
                        }

            case Ident i:
                            return EvalIdent(i);

                        case LambdaLit lam:
                            return new Value(BioType.Organ,
                                new BioClosure("spore", lam.Params, lam.ReturnType, lam.Body, SnapshotScopes()));

                        case Invoke inv:
                        {
                            var callee = Eval(inv.Callee);
                            if (callee.Type != BioType.Organ)
                                throw new BioRuntimeError(
                                    $"this expression is {callee.TypeLabel}, not something callable", inv.Tok);
                            return CallClosure((BioClosure)callee.Raw, inv.Args, inv.Tok);
                        }

            case Unary u:
            {
                var v = Eval(u.E);
                if (u.Op == "-")
                {
                    if (v.Type != BioType.Dna)
                        throw new BioRuntimeError($"cannot negate {Name(v.Type)}", u.Tok);
                    return new Value(BioType.Dna, -(double)v.Raw);
                }
                throw new BioRuntimeError("unknown unary operator " + u.Op, u.Tok);
            }

            case Binary b: return EvalBinary(b);

            case Call c: return CallFunction(c);

            case Member m:
                        {
                            var t = Eval(m.Target);

                            if (t.Type == BioType.Membrane)
                            {
                                var s = (BioStruct)t.Raw;
                                if (s.Fields.TryGetValue(m.Name, out var fv)) return fv;
                                throw new BioRuntimeError(
                                    $"membrane '{s.TypeName}' has no field '{m.Name}'", m.Tok);
                            }

                            if (t.Type == BioType.Rna)
                                            {
                                                string str = (string)t.Raw;
                                                return m.Name switch
                                                {
                                                    "length" => new Value(BioType.Dna, (double)str.Length),
                                                    _ => throw new BioRuntimeError(
                                                        $"rna has no member '{m.Name}'. It has: length, and the methods " +
                                                        "slice, indexOf, contains, startsWith, endsWith, upper, lower, trim, split, chars",
                                                        m.Tok),
                                                };
                                            }

                                            if (t.Type != BioType.Colony)
                                                throw new BioRuntimeError(
                                                    $"'.{m.Name}' is not defined on {t.TypeLabel}", m.Tok);
                            if (m.Name == "length")
                                return new Value(BioType.Dna, (double)((List<Value>)t.Raw).Count);
                            throw new BioRuntimeError($"colony has no member '{m.Name}'", m.Tok);
                        }

            case MethodCall mc: return CallMethod(mc);

            case Index ix:
                        {
                            var t = Eval(ix.Target);
                            var k = Eval(ix.Key);

                            // s[i] on a string, returning a one-character rna. The lexer is
                            // built on this, so it has to exist before anything can self-host.
                            if (t.Type == BioType.Rna)
                            {
                                if (k.Type != BioType.Dna)
                                    throw new BioRuntimeError("a string index must be dna", ix.Tok);
                                string str = (string)t.Raw;
                                int si = (int)(double)k.Raw;
                                if (si < 0) si += str.Length;
                                if (si < 0 || si >= str.Length)
                                    throw new BioRuntimeError(
                                        $"index {si} is outside the string (length {str.Length})", ix.Tok);
                                return new Value(BioType.Rna, str[si].ToString());
                            }

                            if (t.Type != BioType.Colony)
                                throw new BioRuntimeError(
                                    $"indexing is not defined on {t.TypeLabel}", ix.Tok);
                            if (k.Type != BioType.Dna)
                                throw new BioRuntimeError("an index must be dna", ix.Tok);
                            var list = (List<Value>)t.Raw;
                            int idx = (int)(double)k.Raw;
                            // negative indexes count from the end: -1 is the last element
                            if (idx < 0) idx += list.Count;
                            if (idx < 0 || idx >= list.Count)
                                throw new BioRuntimeError(
                                    $"index {idx} is outside the colony (size {list.Count})", ix.Tok);
                            return list[idx];
                        }

            default:
                throw new BioRuntimeError("unhandled expression " + e.GetType().Name);
        }
    }

    private Value EvalBinary(Binary b)
    {
        var l = Eval(b.L);
        var r = Eval(b.R);

        // string concatenation with +
        if (b.Op == "+" && (l.Type == BioType.Rna || r.Type == BioType.Rna))
            return new Value(BioType.Rna, Display(l) + Display(r));

        if (b.Op is "==" or "!=")
        {
            bool eq = Equals(l, r);
            return new Value(BioType.Enzyme, b.Op == "==" ? eq : !eq);
        }

        if (l.Type != BioType.Dna || r.Type != BioType.Dna)
            throw new BioRuntimeError(
                $"'{b.Op}' needs dna on both sides, got {Name(l.Type)} and {Name(r.Type)}", b.Tok);

        double x = (double)l.Raw, y = (double)r.Raw;
        return b.Op switch
        {
            "+" => new Value(BioType.Dna, x + y),
            "-" => new Value(BioType.Dna, x - y),
            "*" => new Value(BioType.Dna, x * y),
            "/" => y == 0
                    ? throw new BioRuntimeError("division by zero", b.Tok)
                    : new Value(BioType.Dna, x / y),
            "<" => new Value(BioType.Enzyme, x < y),
            ">" => new Value(BioType.Enzyme, x > y),
            "<=" => new Value(BioType.Enzyme, x <= y),
            ">=" => new Value(BioType.Enzyme, x >= y),
            _ => throw new BioRuntimeError("unknown operator " + b.Op, b.Tok),
        };
    }

    private Value CallFunction(Call c)
        {
            // Builtins the runtime provides rather than the program. Checked before
            // user organs so a program cannot accidentally shadow them into
            // something with different arity.
            if (c.Name == "absorb") return BuiltinAbsorb(c);
                    if (c.Name == "rna" || c.Name == "dna") return BuiltinConvert(c);
                    // Needed before anything can read its own source: the self-hosting path
                    // starts with a lexer, and a lexer starts with a file and its characters.
                    if (c.Name is "ord" or "chr" or "readFile" or "writeFile" or "fileExists" or "appendFile")
                        return BuiltinFile(c);

            // A variable holding an organ shadows a declaration of the same name.
                    // Without this, higher-order functions are unusable - passing a function
                    // in and calling it would always reach the top-level one instead.
                    if (TryLookup(c.Name, out var cell))
                    {
                        if (cell.Val.Type == BioType.Organ)
                            return CallClosure((BioClosure)cell.Val.Raw, c.Args, c.Tok);

                        // The name exists but holds something you cannot call. Saying only
                        // "no organ named x" sends the reader looking for a missing function
                        // when the real problem is the value they are holding.
                        throw new BioRuntimeError(
                            $"'{c.Name}' is {cell.Val.TypeLabel}, not something callable", c.Tok);
                    }

            if (!_fns.TryGetValue(c.Name, out var fn))
                throw new BioRuntimeError(
                    $"no organ named '{c.Name}'", c.Tok);

            // A named organ closes over the GLOBAL scope only. Capturing the caller's
            // locals would make it dynamically scoped, which is not what anyone means
            // by lexical scope.
            var globals = new List<Dictionary<string, Cell>> { _scopes[0] };
            return CallClosure(
                new BioClosure(fn.Name, fn.Params, fn.ReturnType, fn.Body, globals),
                c.Args, c.Tok);
        }

        // The single path every call goes through: named organ or anonymous spore.
        private Value CallClosure(BioClosure cl, List<Expr> argExprs, Token tok)
        {
            if (argExprs.Count != cl.Params.Count)
                throw new BioRuntimeError(
                    $"'{cl.Label}' takes {cl.Params.Count} argument(s), got {argExprs.Count}", tok);

            var argv = argExprs.Select(Eval).ToList();
            for (int i = 0; i < argv.Count; i++)
                RequireType(argv[i], cl.Params[i].Type, tok,
                    $"argument {i + 1} of '{cl.Label}' expects {cl.Params[i].Type}, " +
                    $"got {argv[i].TypeLabel}");

            // Swap in the captured scopes, then a frame for the parameters. The
            // caller's scopes are set aside and restored in finally, so a closure
            // cannot accidentally see the locals of whoever called it.
            var callerScopes = _scopes.ToList();
            _scopes.Clear();
            _scopes.AddRange(cl.Captured);
            PushScope();
            for (int i = 0; i < argv.Count; i++)
                Define(cl.Params[i].Name, new Cell(argv[i], true, cl.Params[i].Tok), cl.Params[i].Tok);

            Value result;
                        try
                        {
                            ExecBlock(cl.Body);
                            // A `-> void` organ is a procedure: falling off the end is the
                            // normal exit. Anything else promising a value and not delivering
                            // one is a bug in the program, so it still fails.
                            if (cl.ReturnType.Kind == BioType.Void)
                            {
                                result = Value.Void();
                            }
                            else
                            {
                                throw new BioRuntimeError(
                                    $"'{cl.Label}' finished without returning a {cl.ReturnType}", tok);
                            }
                        }
            catch (ReturnSignal r)
            {
                result = r.Value;
            }
            catch (BreakSignal)
            {
                throw new BioRuntimeError(
                    $"sever is only valid inside a loop, and this one is inside '{cl.Label}'", tok);
            }
            catch (ContinueSignal)
            {
                throw new BioRuntimeError(
                    $"skip is only valid inside a loop, and this one is inside '{cl.Label}'", tok);
            }
            finally
            {
                _scopes.Clear();
                _scopes.AddRange(callerScopes);
            }

            RequireType(result, cl.ReturnType, tok,
                $"'{cl.Label}' declared -> {cl.ReturnType} but returned {result.TypeLabel}");
            return result;
        }

        // A shallow copy of the scope stack. The dictionaries are shared on purpose:
        // a closure should see later changes to a variable it captured, which is what
        // every language with closures does.
        private List<Dictionary<string, Cell>> SnapshotScopes() => _scopes.ToList();

        private bool TryLookup(string name, out Cell cell)
        {
            for (int i = _scopes.Count - 1; i >= 0; i--)
                if (_scopes[i].TryGetValue(name, out var c)) { cell = c; return true; }
            cell = null!;
            return false;
        }

        // Reading an identifier: a variable, or the name of an organ, which becomes a
        // closure over the global scope.
        private Value EvalIdent(Ident i)
        {
            if (TryLookup(i.Name, out var cell)) return cell.Val;

            if (_fns.TryGetValue(i.Name, out var fn))
                return new Value(BioType.Organ,
                    new BioClosure(fn.Name, fn.Params, fn.ReturnType, fn.Body,
                                   new List<Dictionary<string, Cell>> { _scopes[0] }));

            throw new BioRuntimeError($"'{i.Name}' is not defined", i.Tok);
                }

                private Value CallMethod(MethodCall mc)
                        {
                            var target = Eval(mc.Target);

                            // A method on a membrane: find the organ in its declaration and call
                            // it with the instance's fields in scope, so `r` means this record's
                            // `r` rather than any outer binding of that name.
                            if (target.Type == BioType.Membrane)
                            {
                                var st = (BioStruct)target.Raw;
                                var decl = _membranes[st.TypeName];
                                var method = decl.Methods.FirstOrDefault(x => x.Name == mc.Name);
                                if (method is null)
                                {
                                    var have = decl.Methods.Select(x => x.Name).ToList();
                                    throw new BioRuntimeError(
                                        $"membrane '{st.TypeName}' has no method '{mc.Name}'"
                                        + (have.Count > 0 ? " (it has: " + string.Join(", ", have) + ")" : ""),
                                        mc.Tok);
                                }

                                var frame = new Dictionary<string, Cell>(StringComparer.Ordinal);
                                                foreach (var kv in st.Fields) frame[kv.Key] = new Cell(kv.Value, true, mc.Tok);

                                                var closure = new BioClosure(
                                                    st.TypeName + "." + mc.Name, method.Params, method.ReturnType, method.Body,
                                                    new List<Dictionary<string, Cell>> { frame });

                                                // Copy the frame back after the call. The fields were copied INTO
                                                // the scope, so without this `h = h + 1` inside a method would
                                                // update a local and the instance would never change - which is
                                                // not what a method means.
                                                var result = CallClosure(closure, mc.Args, mc.Tok);
                                                foreach (var kv in frame)
                                                    if (st.Fields.ContainsKey(kv.Key)) st.Fields[kv.Key] = kv.Value.Val;
                                                return result;
                            }

                            // Strings carry their own methods. Kept separate from colony's because the
                            // arities and the error text differ, and merging them produced messages that
                            // mentioned the wrong type.
                                        if (target.Type == BioType.Rna)
                                        {
                                            string str = (string)target.Raw;
                                            string RnaArg(int i, string what)
                                            {
                                                var v = Eval(mc.Args[i]);
                                                if (v.Type != BioType.Rna)
                                                    throw new BioRuntimeError($"{what} must be rna, got {v.TypeLabel}", mc.Tok);
                                                return (string)v.Raw;
                                            }
                                            int DnaArg(int i, string what)
                                            {
                                                var v = Eval(mc.Args[i]);
                                                if (v.Type != BioType.Dna)
                                                    throw new BioRuntimeError($"{what} must be dna, got {v.TypeLabel}", mc.Tok);
                                                return (int)(double)v.Raw;
                                            }

                                            switch (mc.Name)
                                            {
                                                case "slice":
                                                {
                                                    Require(mc, 1, 2);
                                                    int a = DnaArg(0, "slice start");
                                                    if (a < 0) a += str.Length;
                                                    int b = mc.Args.Count == 2 ? DnaArg(1, "slice end") : str.Length;
                                                    if (b < 0) b += str.Length;
                                                    if (b < a) throw new BioRuntimeError(
                                                        $"slice({a}, {b}) ends before it starts", mc.Tok);
                                                    a = Math.Clamp(a, 0, str.Length);
                                                    b = Math.Clamp(b, 0, str.Length);
                                                    return new Value(BioType.Rna, str.Substring(a, b - a));
                                                }
                                                case "indexOf":
                                                {
                                                    Require(mc, 1);
                                                    return new Value(BioType.Dna, (double)str.IndexOf(RnaArg(0, "indexOf"), StringComparison.Ordinal));
                                                }
                                                case "contains":
                                                {
                                                    Require(mc, 1);
                                                    return new Value(BioType.Enzyme, str.Contains(RnaArg(0, "contains"), StringComparison.Ordinal));
                                                }
                                                case "startsWith":
                                                {
                                                    Require(mc, 1);
                                                    return new Value(BioType.Enzyme, str.StartsWith(RnaArg(0, "startsWith"), StringComparison.Ordinal));
                                                }
                                                case "endsWith":
                                                {
                                                    Require(mc, 1);
                                                    return new Value(BioType.Enzyme, str.EndsWith(RnaArg(0, "endsWith"), StringComparison.Ordinal));
                                                }
                                                case "upper":
                                                    Require(mc, 0);
                                                    return new Value(BioType.Rna, str.ToUpperInvariant());
                                                case "lower":
                                                    Require(mc, 0);
                                                    return new Value(BioType.Rna, str.ToLowerInvariant());
                                                case "trim":
                                                    Require(mc, 0);
                                                    return new Value(BioType.Rna, str.Trim());
                                                case "chars":
                                                {
                                                    Require(mc, 0);
                                                    var chars = str.Select(c => new Value(BioType.Rna, c.ToString())).ToList();
                                                    return new Value(BioType.Colony, chars);
                                                }
                                                case "split":
                                                {
                                                    Require(mc, 1);
                                                    var parts = str.Split(RnaArg(0, "split separator"))
                                                                   .Select(x => new Value(BioType.Rna, x)).ToList();
                                                    return new Value(BioType.Colony, parts);
                                                }
                                                default:
                                                    throw new BioRuntimeError(
                                                        $"rna has no method '{mc.Name}'. Available: slice, indexOf, contains, " +
                                                        "startsWith, endsWith, upper, lower, trim, chars, split", mc.Tok);
                                            }
                                        }

                                        if (target.Type != BioType.Colony)
                                            throw new BioRuntimeError($".{mc.Name}() is not defined on {target.TypeLabel}", mc.Tok);

            var list = (List<Value>)target.Raw;

            // Mutating methods record the intent explicitly: these change the array in
                        // place, so a fossil must be refused for all of them, not just inject.
                        if (mc.Name is "inject" or "insert" or "remove" or "reverse" or "sort" or "clear")
                            EnsureTargetMutable(mc);

            switch (mc.Name)
            {
                case "inject":
                {
                    Require(mc, 1);
                    list.Add(Eval(mc.Args[0]));
                    return Value.Void();
                }
                case "length":
                    Require(mc, 0);
                    return new Value(BioType.Dna, (double)list.Count);

                case "insert":
                {
                    Require(mc, 2);
                    int at = IndexOf(Eval(mc.Args[0]), list.Count, mc.Tok, allowEnd: true);
                    list.Insert(at, Eval(mc.Args[1]));
                    return Value.Void();
                }
                case "remove":
                {
                    Require(mc, 1);
                    int at = IndexOf(Eval(mc.Args[0]), list.Count, mc.Tok);
                    var removed = list[at];
                    list.RemoveAt(at);
                    return removed;
                }
                case "clear":
                    Require(mc, 0);
                    list.Clear();
                    return Value.Void();

                case "contains":
                {
                    Require(mc, 1);
                    var needle = Eval(mc.Args[0]);
                    return new Value(BioType.Enzyme, list.Any(v => Equals(v, needle)));
                }
                case "indexOf":
                {
                    Require(mc, 1);
                    var needle = Eval(mc.Args[0]);
                    int found = list.FindIndex(v => Equals(v, needle));
                    return new Value(BioType.Dna, (double)found);   // -1 when absent
                }

                case "reverse":
                    Require(mc, 0);
                    list.Reverse();
                    return Value.Void();

                case "sort":
                {
                    // Numbers only, and descending is opt-in. Sorting mixed types is
                    // refused rather than given an arbitrary order.
                    Require(mc, 0, 1);
                    bool desc = mc.Args.Count == 1 && Eval(mc.Args[0]).Truthy;
                    foreach (var v in list)
                        if (v.Type != BioType.Dna)
                            throw new BioRuntimeError(
                                $"sort() needs a colony of dna; found {Name(v.Type)}", mc.Tok);
                    var nums = list.Select(v => (double)v.Raw).ToList();
                    nums.Sort();
                    if (desc) nums.Reverse();
                    list.Clear();
                    foreach (var n in nums) list.Add(new Value(BioType.Dna, n));
                    return Value.Void();
                }

                case "sum":
                {
                    Require(mc, 0);
                    double total = 0;
                    foreach (var v in list)
                    {
                        if (v.Type != BioType.Dna)
                            throw new BioRuntimeError(
                                $"sum() needs a colony of dna; found {Name(v.Type)}", mc.Tok);
                        total += (double)v.Raw;
                    }
                    return new Value(BioType.Dna, total);
                }
                case "min":
                case "max":
                {
                    Require(mc, 0);
                    if (list.Count == 0)
                        throw new BioRuntimeError($"{mc.Name}() on an empty colony", mc.Tok);
                    foreach (var v in list)
                        if (v.Type != BioType.Dna)
                            throw new BioRuntimeError(
                                $"{mc.Name}() needs a colony of dna; found {Name(v.Type)}", mc.Tok);
                    var ns = list.Select(v => (double)v.Raw);
                    return new Value(BioType.Dna, mc.Name == "min" ? ns.Min() : ns.Max());
                }

                case "first":
                    Require(mc, 0);
                    if (list.Count == 0) throw new BioRuntimeError("first() on an empty colony", mc.Tok);
                    return list[0];
                case "last":
                    Require(mc, 0);
                    if (list.Count == 0) throw new BioRuntimeError("last() on an empty colony", mc.Tok);
                    return list[^1];

                case "slice":
                {
                    // [a, b) - end-exclusive, and negatives count from the end.
                    Require(mc, 1, 2);
                    int a = IndexOf(Eval(mc.Args[0]), list.Count, mc.Tok, allowEnd: true);
                    int b = mc.Args.Count == 2
                        ? IndexOf(Eval(mc.Args[1]), list.Count, mc.Tok, allowEnd: true)
                        : list.Count;
                    if (b < a) throw new BioRuntimeError(
                        $"slice({a}, {b}) ends before it starts", mc.Tok);
                    return new Value(BioType.Colony, list.GetRange(a, b - a));
                }
                case "copy":
                    Require(mc, 0);
                    return new Value(BioType.Colony, new List<Value>(list));

                case "join":
                {
                    Require(mc, 0, 1);
                    string sep = mc.Args.Count == 1 ? Display(Eval(mc.Args[0])) : "";
                    return new Value(BioType.Rna, string.Join(sep, list.Select(Display)));
                }

                default:
                    throw new BioRuntimeError(
                        $"colony has no method '{mc.Name}'. Available: inject, length, insert, " +
                        "remove, clear, contains, indexOf, reverse, sort, sum, min, max, " +
                        "first, last, slice, copy, join", mc.Tok);
            }
        }

        private void Require(MethodCall mc, params int[] allowed)
        {
            if (!allowed.Contains(mc.Args.Count))
                throw new BioRuntimeError(
                    $"{mc.Name}() takes " + (allowed.Length == 1
                        ? $"{allowed[0]} argument(s)"
                        : string.Join(" or ", allowed) + " arguments")
                    + $", got {mc.Args.Count}", mc.Tok);
        }

        // Shared index resolution: negatives count from the end, and `allowEnd`
        // permits exactly list.Count (a valid insert/slice position, not a valid read).
        private int IndexOf(Value key, int count, Token tok, bool allowEnd = false)
        {
            if (key.Type != BioType.Dna)
                throw new BioRuntimeError("an index must be dna", tok);
            int i = (int)(double)key.Raw;
            if (i < 0) i += count;
            int upper = allowEnd ? count : count - 1;
            if (i < 0 || i > upper)
                throw new BioRuntimeError(
                    $"index {i} is outside the colony (size {count})", tok);
            return i;
        }

        // absorb() reads one line from the input stream.
        // absorb("prompt") prints the prompt first, without a newline.
        private Value BuiltinAbsorb(Call c)
        {
            if (c.Args.Count > 1)
                throw new BioRuntimeError($"absorb() takes 0 or 1 argument(s), got {c.Args.Count}", c.Tok);
            if (c.Args.Count == 1)
            {
                _out.Write(Display(Eval(c.Args[0])));
                _out.Flush();
            }
            string? line = _in.ReadLine();
            // End of input is not an error, but it must be distinguishable from an
            // empty line - returning "" silently would hide a broken pipe.
            if (line is null)
                throw new BioRuntimeError(
                    "absorb() reached the end of input", c.Tok);
            return new Value(BioType.Rna, line);
        }

        // ord / chr / file reading. These are the primitives a self-hosted lexer needs:
// it has to open its own source and walk it one character at a time.
    private Value BuiltinFile(Call c)
    {
        switch (c.Name)
        {
            case "ord":
            {
                if (c.Args.Count != 1)
                    throw new BioRuntimeError("ord() takes exactly one argument", c.Tok);
                var v = Eval(c.Args[0]);
                if (v.Type != BioType.Rna)
                    throw new BioRuntimeError($"ord() needs rna, got {v.TypeLabel}", c.Tok);
                string s = (string)v.Raw;
                if (s.Length == 0)
                    throw new BioRuntimeError("ord() of an empty string", c.Tok);
                return new Value(BioType.Dna, (double)s[0]);
            }
            case "chr":
            {
                if (c.Args.Count != 1)
                    throw new BioRuntimeError("chr() takes exactly one argument", c.Tok);
                var v = Eval(c.Args[0]);
                if (v.Type != BioType.Dna)
                    throw new BioRuntimeError($"chr() needs dna, got {v.TypeLabel}", c.Tok);
                int code = (int)(double)v.Raw;
                if (code < 0 || code > 0x10FFFF)
                    throw new BioRuntimeError($"chr({code}) is not a character code", c.Tok);
                return new Value(BioType.Rna, char.ConvertFromUtf32(code));
            }
            case "readFile":
            {
                if (c.Args.Count != 1)
                    throw new BioRuntimeError("readFile() takes exactly one argument", c.Tok);
                var v = Eval(c.Args[0]);
                if (v.Type != BioType.Rna)
                    throw new BioRuntimeError($"readFile() needs a path, got {v.TypeLabel}", c.Tok);
                string path = (string)v.Raw;
                if (!File.Exists(path))
                    throw new BioRuntimeError($"readFile: no such file '{path}'", c.Tok);
                try
                {
                    // Normalise CRLF to LF. A lexer that has to reason about both
                    // line endings is a lexer with a bug in it.
                    string text = File.ReadAllText(path, System.Text.Encoding.UTF8)
                                      .Replace("\r\n", "\n").Replace('\r', '\n');
                    return new Value(BioType.Rna, text);
                }
                catch (Exception e)
                {
                    throw new BioRuntimeError($"readFile('{path}') failed: {e.Message}", c.Tok);
                }
            }
            case "writeFile":
            case "appendFile":
            {
                if (c.Args.Count != 2)
                    throw new BioRuntimeError($"{c.Name}() takes a path and a string", c.Tok);
                var pv = Eval(c.Args[0]);
                var cv = Eval(c.Args[1]);
                if (pv.Type != BioType.Rna)
                    throw new BioRuntimeError($"{c.Name}() needs a path, got {pv.TypeLabel}", c.Tok);
                string path = (string)pv.Raw;
                // Display() so numbers and booleans can be written without a cast.
                string text = Display(cv);
                try
                {
                    if (c.Name == "writeFile") File.WriteAllText(path, text, System.Text.Encoding.UTF8);
                    else File.AppendAllText(path, text, System.Text.Encoding.UTF8);
                    return Value.Void();
                }
                catch (Exception e)
                {
                    throw new BioRuntimeError($"{c.Name}('{path}') failed: {e.Message}", c.Tok);
                }
            }
            case "fileExists":
            {
                if (c.Args.Count != 1)
                    throw new BioRuntimeError("fileExists() takes exactly one argument", c.Tok);
                var v = Eval(c.Args[0]);
                if (v.Type != BioType.Rna)
                    throw new BioRuntimeError($"fileExists() needs a path, got {v.TypeLabel}", c.Tok);
                return new Value(BioType.Enzyme, File.Exists((string)v.Raw));
            }
            default:
                throw new BioRuntimeError($"unhandled builtin '{c.Name}'", c.Tok);
        }
    }

    // rna(x) and dna(x) convert between text and number explicitly.
        private Value BuiltinConvert(Call c)
        {
            if (c.Args.Count != 1)
                throw new BioRuntimeError($"{c.Name}() takes exactly one argument", c.Tok);
            var v = Eval(c.Args[0]);

            if (c.Name == "rna")
                return new Value(BioType.Rna, Display(v));

            // dna(...)
            if (v.Type == BioType.Dna) return v;
            if (v.Type == BioType.Enzyme) return new Value(BioType.Dna, (bool)v.Raw ? 1.0 : 0.0);
            if (v.Type == BioType.Rna)
            {
                if (double.TryParse((string)v.Raw, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double d))
                    return new Value(BioType.Dna, d);
                throw new BioRuntimeError(
                    $"dna(\"{v.Raw}\") is not a number", c.Tok);
            }
            throw new BioRuntimeError($"cannot convert {Name(v.Type)} to dna", c.Tok);
        }

    // inject() mutates the array in place. If the array came from a fossil, that
    // is a fossil being modified - refuse it, or the keyword means nothing.
    private void EnsureTargetMutable(MethodCall mc)
    {
        if (mc.Target is not Ident id) return;              // an expression, not a binding
        for (int i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].TryGetValue(id.Name, out var c))
            {
                if (!c.Mutable)
                    throw new BioRuntimeError(
                        $"'{id.Name}' is a fossil; .inject() would mutate it", mc.Tok);
                return;
            }
        }
    }

    // ------------------------------------------------------------------ helpers
    private void RequireType(Value v, TypeRef want, Token tok, string msg)
            {
                bool ok;

                if (want.Kind == BioType.Membrane)
                {
                    // The parser cannot tell a membrane name from a trait name, so the
                    // interpreter resolves it: a trait is satisfied by promising it, a
                    // membrane by being that exact membrane.
                    if (_traits.TryGetValue(want.Name!, out var trait))
                    {
                        ok = v.Type == BioType.Membrane && v.TypeName is not null
                             && _membranes.TryGetValue(v.TypeName, out var mdecl)
                             && mdecl.Traits.Any(t => t.Trait == trait.Name);
                    }
                    else
                    {
                        ok = v.Type == BioType.Membrane
                             && string.Equals(v.TypeName, want.Name, StringComparison.Ordinal);
                    }
                }
                else
                {
                    ok = v.Type == want.Kind;
                }

                if (!ok) throw new BioRuntimeError(msg, tok);
            }

        // The declared type of a value, for assignment checks against an existing cell.
        private static TypeRef ToTypeRef(Value v) =>
            v.Type == BioType.Membrane ? TypeRef.Membrane(v.TypeName ?? "membrane") : new TypeRef(v.Type);

    private static string Name(BioType t) => t switch
    {
        BioType.Rna => "rna",
        BioType.Dna => "dna",
        BioType.Enzyme => "enzyme",
        BioType.Colony => "colony",
        _ => "void",
    };

    // How a value reads when printed. dna drops the .0 when it is whole, which is
    // decision 1 from the header.
    public static string Display(Value v) => v.Type switch
    {
        BioType.Rna => (string)v.Raw,
        BioType.Enzyme => (bool)v.Raw ? "active" : "dormant",
        BioType.Dna => Fmt((double)v.Raw),
        BioType.Colony => "[" + string.Join(", ", ((List<Value>)v.Raw).Select(Display)) + "]",
                BioType.Membrane => FormatStruct((BioStruct)v.Raw),
                BioType.Organ => "<organ " + ((BioClosure)v.Raw).Label + ">",
                _ => "void",
            };

            // Point { x = 1, y = 2 } - named, so printing two different records is not
            // ambiguous, and fields in declaration order so output is stable.
            private static string FormatStruct(BioStruct s) =>
                s.TypeName + " { " + string.Join(", ", s.Fields.Select(kv => kv.Key + " = " + Display(kv.Value))) + " }";

    private static string Fmt(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return d.ToString(CultureInfo.InvariantCulture);
        if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15)
            return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
        return d.ToString("0.############", CultureInfo.InvariantCulture);
    }
}