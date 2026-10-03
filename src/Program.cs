// BioLang v1.0 - command-line runner.
//
//   biolang <file.bio>      run a program
//   biolang --tokens <f>    dump the token stream (debugging a lexer change)
//   biolang --ast <f>       parse and report the tree shape without running
//
// Exit codes: 0 ok, 1 syntax error, 2 runtime error, 3 usage.
//
// This class is deliberately NOT called Program: the AST root record from
// Parser.cs is named Program, and two type names meaning different things in one
// namespace is a compile error waiting to happen.
using System.Text;

namespace BioLang;

internal static class Runner
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("  usage: biolang [--tokens|--ast] <file.bio>");
            return 3;
        }

        string mode = "run";
        string path = args[0];
        if (args[0].StartsWith("--"))
        {
            mode = args[0][2..];
            if (args.Length < 2) { Console.Error.WriteLine("  missing file"); return 3; }
            path = args[1];
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"  no such file: {path}");
            return 3;
        }

        List<Token> tokens;
        try
        {
            tokens = new Lexer(File.ReadAllText(path, Encoding.UTF8)).Tokenize();
        }
        catch (BioSyntaxError e)
        {
            Console.Error.WriteLine("  LEX ERROR: " + e.Message);
            return 1;
        }

        if (mode == "tokens")
        {
            foreach (var t in tokens) Console.WriteLine("  " + t);
            return 0;
        }

        Program program;
        try
        {
            program = new Parser(tokens).ParseProgram();
        }
        catch (BioSyntaxError e)
        {
            Console.Error.WriteLine("  SYNTAX ERROR: " + e.Message);
            return 1;
        }

        if (mode == "ast")
        {
            Console.WriteLine($"  organism : {program.Name}");
            Console.WriteLine($"  organs   : {string.Join(", ", program.Functions.Select(f => f.Name))}");
            Console.WriteLine($"  nucleus  : {(program.Main is null ? "absent" : "present")}");
            return 0;
        }

        try
        {
            new Interpreter(program, Console.Out).Run();
            return 0;
        }
        catch (BioRuntimeError e)
        {
            Console.Error.WriteLine("  RUNTIME ERROR: " + e.Message);
            return 2;
        }
    }
}
