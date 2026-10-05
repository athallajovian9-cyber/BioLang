using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace BioLang
{
    /// <summary>
    /// Translates parsed BioLang AST into portable C99 / C++ code,
    /// which can be compiled into native x86_64 machine code via MSVC (cl.exe).
    /// </summary>
    public static class CodeGen
    {
        public static string EmitC(Program program)
        {
            var sb = new StringBuilder();
            sb.AppendLine("/* BioLang Native Machine Code Backend (Generated C99/C++) */");
            sb.AppendLine("#include <stdio.h>");
            sb.AppendLine("#include <stdlib.h>");
            sb.AppendLine("#include <stdbool.h>");
            sb.AppendLine("#include <string.h>");
            sb.AppendLine("#include <math.h>");
            sb.AppendLine();

            sb.AppendLine("// BioLang Runtime Primitives");
            sb.AppendLine("typedef double dna_t;");
            sb.AppendLine("typedef const char* rna_t;");
            sb.AppendLine("typedef bool enzyme_t;");
            sb.AppendLine("#define active true");
            sb.AppendLine("#define dormant false");
            sb.AppendLine();

            sb.AppendLine("static void bio_print_dna(dna_t n) {");
            sb.AppendLine("    if (floor(n) == n) printf(\"%.0f\\n\", n);");
            sb.AppendLine("    else printf(\"%g\\n\", n);");
            sb.AppendLine("}");
            sb.AppendLine("static void bio_print_rna(rna_t s) { printf(\"%s\\n\", s ? s : \"\"); }");
            sb.AppendLine("static void bio_print_enzyme(enzyme_t b) { printf(\"%s\\n\", b ? \"active\" : \"dormant\"); }");
            sb.AppendLine();

            // Emit function forward declarations
            foreach (var fn in program.Functions)
            {
                string retType = MapType(fn.ReturnType);
                sb.Append($"{retType} bio_{fn.Name}(");
                for (int i = 0; i < fn.Params.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append($"{MapType(fn.Params[i].Type)} {fn.Params[i].Name}");
                }
                sb.AppendLine(");");
            }
            sb.AppendLine();

            // Emit function definitions
            foreach (var fn in program.Functions)
            {
                string retType = MapType(fn.ReturnType);
                sb.Append($"{retType} bio_{fn.Name}(");
                for (int i = 0; i < fn.Params.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append($"{MapType(fn.Params[i].Type)} {fn.Params[i].Name}");
                }
                sb.AppendLine(") {");
                
                foreach (var stmt in fn.Body.Body)
                {
                    EmitStatement(sb, stmt, "    ");
                }
                sb.AppendLine("}");
                sb.AppendLine();
            }

            // Emit Main Entry Point (nucleus)
            sb.AppendLine("int main(int argc, char** argv) {");
            if (program.Main != null)
            {
                foreach (var stmt in program.Main.Body.Body)
                {
                    EmitStatement(sb, stmt, "    ");
                }
            }
            sb.AppendLine("    return 0;");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string MapType(TypeRef typeRef)
        {
            if (typeRef == null) return "void";
            return typeRef.Kind switch
            {
                BioType.Dna => "dna_t",
                BioType.Rna => "rna_t",
                BioType.Enzyme => "enzyme_t",
                _ => "void"
            };
        }

        private static void EmitStatement(StringBuilder sb, Stmt stmt, string indent)
        {
            if (stmt is VarDecl v)
            {
                string cType = MapType(v.Type);
                sb.Append($"{indent}{cType} {v.Name} = ");
                if (v.Init != null) EmitExpression(sb, v.Init);
                else sb.Append("0");
                sb.AppendLine(";");
            }
            else if (stmt is Assign a)
            {
                sb.Append($"{indent}{a.Name} = ");
                EmitExpression(sb, a.Value);
                sb.AppendLine(";");
            }
            else if (stmt is ExprStmt e)
            {
                sb.Append(indent);
                EmitExpression(sb, e.Value);
                sb.AppendLine(";");
            }
            else if (stmt is ReturnStmt r)
            {
                sb.Append($"{indent}return ");
                if (r.Value != null) EmitExpression(sb, r.Value);
                sb.AppendLine(";");
            }
            else if (stmt is PrintStmt p)
            {
                if (p.Value is StrLit)
                {
                    sb.Append($"{indent}bio_print_rna(");
                }
                else if (p.Value is BoolLit)
                {
                    sb.Append($"{indent}bio_print_enzyme(");
                }
                else
                {
                    sb.Append($"{indent}bio_print_dna(");
                }
                EmitExpression(sb, p.Value);
                sb.AppendLine(");");
            }
            else if (stmt is IfStmt ifStmt)
            {
                sb.Append($"{indent}if (");
                EmitExpression(sb, ifStmt.Cond);
                sb.AppendLine(") {");
                foreach (var s in ifStmt.Then.Body) EmitStatement(sb, s, indent + "    ");
                if (ifStmt.Else != null && ifStmt.Else.Body.Count > 0)
                {
                    sb.AppendLine($"{indent}}} else {{");
                    foreach (var s in ifStmt.Else.Body) EmitStatement(sb, s, indent + "    ");
                }
                sb.AppendLine($"{indent}}}");
            }
            else if (stmt is ForStmt loop)
            {
                sb.Append($"{indent}for (");
                if (loop.Init is VarDecl vd)
                {
                    sb.Append($"{MapType(vd.Type)} {vd.Name} = ");
                    if (vd.Init != null) EmitExpression(sb, vd.Init);
                    else sb.Append("0");
                }
                sb.Append("; ");
                if (loop.Cond != null) EmitExpression(sb, loop.Cond);
                sb.Append("; ");
                if (loop.Step is Assign sa)
                {
                    sb.Append($"{sa.Name} = ");
                    EmitExpression(sb, sa.Value);
                }
                sb.AppendLine(") {");
                foreach (var s in loop.Body.Body) EmitStatement(sb, s, indent + "    ");
                sb.AppendLine($"{indent}}}");
            }
        }

        private static void EmitExpression(StringBuilder sb, Expr expr)
        {
            if (expr is NumLit num)
            {
                sb.Append(num.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (expr is StrLit str)
            {
                sb.Append($"\"{EscapeString(str.Value)}\"");
            }
            else if (expr is BoolLit b)
            {
                sb.Append(b.Value ? "active" : "dormant");
            }
            else if (expr is Ident id)
            {
                sb.Append(id.Name);
            }
            else if (expr is Binary bin)
            {
                sb.Append("(");
                EmitExpression(sb, bin.L);
                sb.Append($" {bin.Op} ");
                EmitExpression(sb, bin.R);
                sb.Append(")");
            }
            else if (expr is Call call)
            {
                sb.Append($"bio_{call.Name}(");
                for (int i = 0; i < call.Args.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    EmitExpression(sb, call.Args[i]);
                }
                sb.Append(")");
            }
            else
            {
                sb.Append("0");
            }
        }

        private static string EscapeString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
