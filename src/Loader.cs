// BioLang v1.1 - module loading.
//
// `graft "file.bio"` pulls another organism into this one. It is resolved here,
// before the interpreter ever runs, because the functions it contributes must
// exist as a complete set: a call cannot be resolved against a file that is
// loaded part-way through execution.
//
// Paths are relative to the file that writes the graft, not to the working
// directory. That is the only reading that works when a program is run from
// somewhere else.
using System.Text;

namespace BioLang;

public static class Loader
{
    public static Program Load(string path)
    {
        var loading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return LoadRecursive(Path.GetFullPath(path), loading, null);
    }

    private static Program LoadRecursive(string fullPath, HashSet<string> loading, Token? from)
    {
        if (!loading.Add(fullPath))
        {
            // Report the chain, not just the repeated file - a cycle message that
            // names one file leaves you guessing where it came from.
            string chain = string.Join(" -> ", loading);
            throw new BioRuntimeError(
                $"graft cycle: '{Path.GetFileName(fullPath)}' is already being loaded ({chain})",
                from);
        }

        if (!File.Exists(fullPath))
            throw new BioRuntimeError($"graft target not found: {fullPath}", from);

        List<Token> tokens;
        try
        {
            tokens = new Lexer(File.ReadAllText(fullPath, Encoding.UTF8)).Tokenize();
        }
        catch (BioSyntaxError e)
        {
            throw new BioSyntaxError(
                $"in grafted file {Path.GetFileName(fullPath)}: {e.Message}", from ?? new Token(TokKind.EOF, "", 0, 0));
        }

        Program parsed = new Parser(tokens).ParseProgram();

        var functions = new List<FunctionDecl>(parsed.Functions);
        var membranes = new List<MembraneDecl>(parsed.Membranes);
        var traits = new List<TraitDecl>(parsed.Traits);
        var top = new List<Stmt>();
        string dir = Path.GetDirectoryName(fullPath) ?? ".";

        foreach (Stmt s in parsed.TopLevel)
        {
            if (s is GraftStmt g)
            {
                Program child = LoadRecursive(Path.GetFullPath(Path.Combine(dir, g.Path)), loading, g.Tok);

                // Two entry points is ambiguous, so it is refused rather than
                // silently picking one.
                if (child.Main is not null && from is not null)
                    throw new BioRuntimeError(
                        $"grafted file '{Path.GetFileName(fullPath)}' declares its own nucleus(); " +
                        "only the main organism may have one", g.Tok);

                functions.AddRange(child.Functions);
                membranes.AddRange(child.Membranes);
                traits.AddRange(child.Traits);
                top.AddRange(child.TopLevel);
            }
            else
            {
                top.Add(s);
            }
        }

        loading.Remove(fullPath);
        return parsed with { Functions = functions, Membranes = membranes, Traits = traits, TopLevel = top };
    }
}