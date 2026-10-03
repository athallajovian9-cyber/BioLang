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
public sealed record Value(BioType Type, object Raw)
{
    public static Value Void() => new(BioType.Void, 0.0);
    public bool Truthy => Type switch
    {
        BioType.Enzyme => (bool)Raw,
        BioType.Dna => (double)Raw != 0,
        BioType.Rna => ((string)Raw).Length > 0,
        BioType.Colony => ((List<Value>)Raw).Count > 0,
        _ => false,
    };
}

public sealed class Interpreter
{
    private readonly Program _program;
    private readonly Dictionary<string, FunctionDecl> _fns = new(StringComparer.Ordinal);
    private readonly List<Dictionary<string, Cell>> _scopes = new();
        private readonly TextWriter _out;
        private readonly TextReader _in;
    // `Mutable` records how the cell was declared, so mutation can be refused.
    private sealed record Cell(Value Val, bool Mutable, Token Tok)
    {
        public Cell With(Value v) => new(v, Mutable, Tok);
    }

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
                RequireType(v, d.Type, d.Tok, $"cannot store {Name(v.Type)} in {Name(d.Type)} '{d.Name}'");
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
                RequireType(v, cell.Val.Type, a.Tok,
                    $"cannot store {Name(v.Type)} in {Name(cell.Val.Type)} '{a.Name}'");
                SetCell(a.Name, cell.With(v));
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

            case Ident i:
                return Lookup(i.Name, i.Tok).Val;

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
                if (t.Type != BioType.Colony)
                    throw new BioRuntimeError($"'.{m.Name}' is only defined on colony", m.Tok);
                if (m.Name == "length")
                    return new Value(BioType.Dna, (double)((List<Value>)t.Raw).Count);
                throw new BioRuntimeError($"colony has no member '{m.Name}'", m.Tok);
            }

            case MethodCall mc: return CallMethod(mc);

            case Index ix:
            {
                var t = Eval(ix.Target);
                var k = Eval(ix.Key);
                if (t.Type != BioType.Colony)
                    throw new BioRuntimeError("indexing is only defined on colony", ix.Tok);
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

            if (!_fns.TryGetValue(c.Name, out var fn))
                throw new BioRuntimeError($"no organ named '{c.Name}'", c.Tok);

        if (c.Args.Count != fn.Params.Count)
            throw new BioRuntimeError(
                $"organ '{fn.Name}' takes {fn.Params.Count} argument(s), got {c.Args.Count}", c.Tok);

        var argv = c.Args.Select(Eval).ToList();
        for (int i = 0; i < argv.Count; i++)
            RequireType(argv[i], fn.Params[i].Type, c.Tok,
                $"argument {i + 1} of '{fn.Name}' expects {Name(fn.Params[i].Type)}, " +
                $"got {Name(argv[i].Type)}");

        PushScope();
        for (int i = 0; i < argv.Count; i++)
            Define(fn.Params[i].Name, new Cell(argv[i], true, fn.Params[i].Tok), fn.Params[i].Tok);

        Value result;
        try
        {
            ExecBlock(fn.Body);
            // falling off the end of a non-void function is a bug in the program
            throw new BioRuntimeError(
                $"organ '{fn.Name}' finished without returning a {Name(fn.ReturnType)}", fn.Tok);
        }
        catch (ReturnSignal r)
                {
                    result = r.Value;
                }
                catch (BreakSignal)
                {
                    throw new BioRuntimeError(
                        $"sever is only valid inside a loop, and this one is inside organ '{fn.Name}'", fn.Tok);
                }
                catch (ContinueSignal)
                {
                    throw new BioRuntimeError(
                        $"skip is only valid inside a loop, and this one is inside organ '{fn.Name}'", fn.Tok);
                }
                finally
        {
            PopScope();
        }

        RequireType(result, fn.ReturnType, c.Tok,
            $"organ '{fn.Name}' declared -> {Name(fn.ReturnType)} but returned {Name(result.Type)}");
        return result;
    }

    private Value CallMethod(MethodCall mc)
        {
            var target = Eval(mc.Target);
            if (target.Type != BioType.Colony)
                throw new BioRuntimeError($"'.{mc.Name}' is only defined on colony", mc.Tok);

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
    private static void RequireType(Value v, BioType want, Token tok, string msg)
    {
        if (v.Type != want) throw new BioRuntimeError(msg, tok);
    }

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
        _ => "void",
    };

    private static string Fmt(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return d.ToString(CultureInfo.InvariantCulture);
        if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15)
            return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
        return d.ToString("0.############", CultureInfo.InvariantCulture);
    }
}