// BioLang v1.0 - lexer and token definitions.
//
// The keyword set is the language's whole identity, so it lives in exactly one
// place: a single dictionary. If a keyword is added to the spec and not to
// Keywords, the parser reports it as an unknown identifier rather than as a
// syntax error, which is the correct failure and an easy one to trace.
using System.Globalization;

namespace BioLang;

public enum TokKind
{
    // structure
    Organism, Nucleus, Organ, Cell, Fossil,
    // control
    Mutate, Adapt, Replicate, Return,
    // added in v1.1: loop control and iteration
    Traverse, Sever, Skip,
    // added in v1.1: compound types and modules
    Membrane, Graft,
    // added in v1.2: anonymous functions
    Spore,
    // added in v1.2: traits - declared behaviour a membrane can promise
    Trait, Witnesses,
    // io
    Secrete, Absorb,
    // types + literals
    Rna, Dna, Enzyme, Colony, Active, Dormant,
    Void,
    // values
    Identifier, Number, String, Bool,
    // punctuation
    LBrace, RBrace, LParen, RParen, LBracket, RBracket,
    Comma, Dot, Semicolon, Arrow, Assign,
    // operators
    Plus, Minus, Star, Slash,
    Lt, Gt, Le, Ge, EqEq, NotEq,
    // end
    EOF
}

public sealed record Token(TokKind Kind, string Text, int Line, int Col)
{
    public override string ToString() => $"{Kind}({Text}) at {Line}:{Col}";
}

public sealed class BioSyntaxError : Exception
{
    public BioSyntaxError(string msg, Token tok)
        : base($"{msg} (line {tok.Line}, col {tok.Col}: '{tok.Text}')") { }
}

public sealed class Lexer
{
    // The literal keyword mapping from the specification.
    private static readonly Dictionary<string, TokKind> Keywords = new(StringComparer.Ordinal)
    {
        ["organism"]  = TokKind.Organism,
        ["nucleus"]   = TokKind.Nucleus,
        ["organ"]     = TokKind.Organ,
        ["cell"]      = TokKind.Cell,
        ["fossil"]    = TokKind.Fossil,
        ["mutate"]    = TokKind.Mutate,
        ["adapt"]     = TokKind.Adapt,
        ["replicate"] = TokKind.Replicate,
        // v1.1: traverse is the counted for-loop; sever is break; skip is continue
        ["traverse"]  = TokKind.Traverse,
        ["sever"]     = TokKind.Sever,
        ["skip"]      = TokKind.Skip,
        // v1.1: a membrane is a struct; graft pulls in another organism
        ["membrane"]  = TokKind.Membrane,
        ["graft"]     = TokKind.Graft,
        // v1.2: a spore is an anonymous function - a value you can pass around
        ["spore"]     = TokKind.Spore,
        // v1.2: a trait names behaviour; witnesses is how a membrane promises it
        ["trait"]     = TokKind.Trait,
        ["witnesses"] = TokKind.Witnesses,
        ["secrete"]   = TokKind.Secrete,
        ["absorb"]    = TokKind.Absorb,
        ["active"]    = TokKind.Active,
        ["dormant"]   = TokKind.Dormant,
        ["void"]      = TokKind.Void,
        ["rna"]       = TokKind.Rna,
        ["dna"]       = TokKind.Dna,
        ["enzyme"]    = TokKind.Enzyme,
        ["colony"]    = TokKind.Colony,
        ["return"]    = TokKind.Return,
    };

    private readonly string _src;
    private int _i, _line = 1, _col = 1;

    public Lexer(string src) => _src = src;

    private char Cur => _i < _src.Length ? _src[_i] : '\0';
    private char Peek(int n = 1) => _i + n < _src.Length ? _src[_i + n] : '\0';

    private void Advance()
    {
        if (Cur == '\n') { _line++; _col = 1; } else { _col++; }
        _i++;
    }

    private void SkipTrivia()
    {
        for (; ; )
        {
            // whitespace, including newlines - BioLang is not newline-sensitive
            if (Cur is ' ' or '\t' or '\r' or '\n') { Advance(); continue; }
            // // line comment
            if (Cur == '/' && Peek() == '/')
            {
                while (Cur != '\0' && Cur != '\n') Advance();
                continue;
            }
            // /* block comment */
            if (Cur == '/' && Peek() == '*')
            {
                Advance(); Advance();
                while (Cur != '\0' && !(Cur == '*' && Peek() == '/')) Advance();
                if (Cur != '\0') { Advance(); Advance(); }
                continue;
            }
            return;
        }
    }

    public List<Token> Tokenize()
    {
        var toks = new List<Token>();

        for (; ; )
        {
            SkipTrivia();
            if (Cur == '\0') break;

            int line = _line, col = _col;
            char c = Cur;

            // two-character operators first, or "<=" lexes as "<" then "="
            if (c == '-' && Peek() == '>') { Advance(); Advance(); toks.Add(new(TokKind.Arrow, "->", line, col)); continue; }
            if (c == '=' && Peek() == '=') { Advance(); Advance(); toks.Add(new(TokKind.EqEq, "==", line, col)); continue; }
            if (c == '!' && Peek() == '=') { Advance(); Advance(); toks.Add(new(TokKind.NotEq, "!=", line, col)); continue; }
            if (c == '<' && Peek() == '=') { Advance(); Advance(); toks.Add(new(TokKind.Le, "<=", line, col)); continue; }
            if (c == '>' && Peek() == '=') { Advance(); Advance(); toks.Add(new(TokKind.Ge, ">=", line, col)); continue; }

            // string literal
            if (c == '"')
            {
                Advance();
                var sb = new System.Text.StringBuilder();
                while (Cur != '"')
                {
                    if (Cur == '\0') throw new BioSyntaxError("unterminated string", new Token(TokKind.String, sb.ToString(), line, col));
                    if (Cur == '\\')
                    {
                        Advance();
                        // The carriage-return escape MUST be handled. Without it the
                        // backslash fell through to the default, which produced the
                        // string "r" for a CR escape - so a whitespace test written as
                        // a CR literal matched the letter r, and every identifier
                        // starting with r was skipped as whitespace. Rewriting an
                        // unknown escape to its own letter is what made that
                        // invisible, so unknown escapes are refused, not guessed at.
                        char esc = Cur;
                        char? mapped = esc switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            'r' => (char)13,
                            '0' => '\0',
                            '"' => '"',
                            '\\' => '\\',
                            _ => null,
                        };
                        if (mapped is null)
                            throw new BioSyntaxError(
                                "unknown escape backslash-" + esc + " in a string literal",
                                new Token(TokKind.String, sb.ToString(), line, col));
                        sb.Append(mapped.Value);
                        Advance();
                        continue;
                    }
                    sb.Append(Cur);
                    Advance();
                }
                Advance();
                toks.Add(new(TokKind.String, sb.ToString(), line, col));
                continue;
            }

            // number literal
            if (char.IsDigit(c))
            {
                var sb = new System.Text.StringBuilder();
                while (char.IsDigit(Cur) || Cur == '.') { sb.Append(Cur); Advance(); }
                toks.Add(new(TokKind.Number, sb.ToString(), line, col));
                continue;
            }

            // identifier or keyword
            if (char.IsLetter(c) || c == '_')
            {
                var sb = new System.Text.StringBuilder();
                while (char.IsLetterOrDigit(Cur) || Cur == '_') { sb.Append(Cur); Advance(); }
                string word = sb.ToString();
                toks.Add(new(Keywords.TryGetValue(word, out var kw) ? kw : TokKind.Identifier, word, line, col));
                continue;
            }

            // single-character tokens
            TokKind? single = c switch
            {
                '{' => TokKind.LBrace, '}' => TokKind.RBrace,
                '(' => TokKind.LParen, ')' => TokKind.RParen,
                '[' => TokKind.LBracket, ']' => TokKind.RBracket,
                ',' => TokKind.Comma, '.' => TokKind.Dot,
                ';' => TokKind.Semicolon, '=' => TokKind.Assign,
                '+' => TokKind.Plus, '-' => TokKind.Minus,
                '*' => TokKind.Star, '/' => TokKind.Slash,
                '<' => TokKind.Lt, '>' => TokKind.Gt,
                _ => null,
            };
            if (single is null) throw new BioSyntaxError($"unexpected character '{c}'", new Token(TokKind.EOF, c.ToString(), line, col));
            Advance();
            toks.Add(new(single.Value, c.ToString(), line, col));
        }

        toks.Add(new(TokKind.EOF, "<eof>", _line, _col));
        return toks;
    }
}
