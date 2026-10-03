// BioLang v1.1 - command-line runner.
//
//   biolang <file.bio>              run a program
//   biolang --tokens <f>            dump the token stream (debugging a lexer change)
//   biolang --ast <f>               parse and report the tree shape without running
//   biolang --ast-json-raw <f>      parse ONE file, no graft resolution (self-host check)
//   biolang --max-loop <n> <f>      raise the loop cap for long-running work
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
        string mode = "run";
        int maxLoop = 1_000_000;
        var rest = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--tokens":
                case "--ast":
                case "--ast-json":
                case "--ast-json-raw":
                    mode = args[i][2..];
                    break;
                case "--max-loop":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out maxLoop))
                    {
                        Console.Error.WriteLine("  --max-loop needs a number");
                        return 3;
                    }
                    break;
                default:
                    rest.Add(args[i]);
                    break;
            }
        }

        if (rest.Count == 0)
        {
            Console.Error.WriteLine(
                "  usage: biolang [--tokens|--ast] [--max-loop N] <file.bio>");
            return 3;
        }

        string path = rest[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"  no such file: {path}");
            return 3;
        }

        // --tokens inspects one file in isolation; graft resolution would hide
        // the tokens that belong to the file actually named on the command line.
        if (mode == "tokens")
        {
            try
            {
                var toks = new Lexer(File.ReadAllText(path, Encoding.UTF8)).Tokenize();
                foreach (var t in toks) Console.WriteLine("  " + t);
                return 0;
            }
            catch (BioSyntaxError e)
            {
                Console.Error.WriteLine("  LEX ERROR: " + e.Message);
                return 1;
            }
        }

        // --ast-json-raw parses ONE file and resolves nothing. That is what a
        // parser does; merging a grafted organism is the Loader's job. Comparing
        // against the Loader-resolved tree is comparing a parser against a
        // loader, which is not the same test.
        if (mode == "ast-json-raw")
        {
            try
            {
                var rawTokens = new Lexer(File.ReadAllText(path, Encoding.UTF8)).Tokenize();
                Console.WriteLine(AstJson.Dump(new Parser(rawTokens).ParseProgram()));
                return 0;
            }
            catch (BioSyntaxError e)
            {
                Console.Error.WriteLine("  SYNTAX ERROR: " + e.Message);
                return 1;
            }
        }

        Program program;
        try
        {
            program = Loader.Load(path);        // resolves graft before execution
        }
        catch (BioSyntaxError e)
        {
            Console.Error.WriteLine("  SYNTAX ERROR: " + e.Message);
            return 1;
        }
        catch (BioRuntimeError e)
        {
            Console.Error.WriteLine("  LOAD ERROR: " + e.Message);
            return 2;
        }

        if (mode == "ast")
        {
            Console.WriteLine($"  organism : {program.Name}");
            Console.WriteLine($"  organs   : {string.Join(", ", program.Functions.Select(f => f.Name))}");
            Console.WriteLine($"  nucleus  : {(program.Main is null ? "absent" : "present")}");
            Console.WriteLine($"  top-level: {program.TopLevel.Count} statement(s)");
            return 0;
        }

        // The full tree, for byte-comparison against the self-hosted parser.
        if (mode == "ast-json")
        {
            Console.WriteLine(AstJson.Dump(program));
            return 0;
        }

        try
        {
            var interp = new Interpreter(program, Console.Out, Console.In)
            {
                MaxLoopIterations = maxLoop,
            };
            interp.Run();
            return 0;
        }
        catch (BioRuntimeError e)
        {
            Console.Error.WriteLine("  RUNTIME ERROR: " + e.Message);
            return 2;
        }
    }
}
