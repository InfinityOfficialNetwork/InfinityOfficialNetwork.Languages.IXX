using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;
using System.Globalization;

namespace InfinityOfficialNetwork.Languages.IXX;

// ============================================================================
//               DIAGNOSTIC SYSTEM
// ============================================================================
public enum DiagnosticSeverity { Warning, Error }

public record Diagnostic(string Code, string Message, int Line, int Column, DiagnosticSeverity Severity = DiagnosticSeverity.Error);

public class DiagnosticBag
{
	public List<Diagnostic> Diagnostics { get; } = new();
	public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

	public void Report(string code, string message, int line, int column, DiagnosticSeverity severity = DiagnosticSeverity.Error)
	{
		Diagnostics.Add(new Diagnostic(code, message, line, column, severity));
	}

	public void PrintDiagnostics(string sourceCode)
	{
		var lines = sourceCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
		foreach (var d in Diagnostics.OrderBy(d => d.Line).ThenBy(d => d.Column))
		{
			Console.ForegroundColor = d.Severity == DiagnosticSeverity.Error ? ConsoleColor.Red : ConsoleColor.Yellow;
			Console.WriteLine($"[{d.Code}] {d.Severity} at line {d.Line}, col {d.Column}: {d.Message}");
			Console.ResetColor();

			if (d.Line > 0 && d.Line <= lines.Length)
			{
				string lineText = lines[d.Line - 1].Replace('\t', ' ');
				Console.WriteLine($"    {lineText}");

				if (d.Column > 0 && d.Column <= lineText.Length + 1)
				{
					Console.ForegroundColor = ConsoleColor.Cyan;
					Console.WriteLine("    " + new string(' ', d.Column - 1) + "^");
					Console.ResetColor();
				}
			}
			Console.WriteLine();
		}
	}
}

// --- Model Definitions ---
public enum TokenType
{
	Return, Identifier, Number, LParen, RParen, LBrace, RBrace,
	Comma, Semicolon, Plus, Minus, Star, Slash, TypeKeyword, BooleanLiteral,
	DoubleAmpersand, DoubleBar, Ampersand, Bar, Caret, Tilde, Exclamation, Equal,
	DoubleEqual, NotEqual, LessEqual, GreaterEqual, LessThan, GreaterThan,
	If, Else, While, Do, Break, Continue, EOF
}

public record Token(TokenType Type, string Value, int Line, int Column, string Comment = null);

public record TypeInfo(string Name, bool IsSigned, bool IsFloat, int Bits, bool IsByte, bool IsBool)
{
	public bool IsVoid => Name == "void";
	public bool IsInteger => !IsFloat && !IsByte && !IsBool && !IsVoid && Name != "error";

	public static readonly TypeInfo I8 = new("i8", true, false, 8, false, false);
	public static readonly TypeInfo I16 = new("i16", true, false, 16, false, false);
	public static readonly TypeInfo I32 = new("i32", true, false, 32, false, false);
	public static readonly TypeInfo I64 = new("i64", true, false, 64, false, false);

	public static readonly TypeInfo UI8 = new("ui8", false, false, 8, false, false);
	public static readonly TypeInfo UI16 = new("ui16", false, false, 16, false, false);
	public static readonly TypeInfo UI32 = new("ui32", false, false, 32, false, false);
	public static readonly TypeInfo UI64 = new("ui64", false, false, 64, false, false);

	public static readonly TypeInfo F32 = new("f32", false, true, 32, false, false);
	public static readonly TypeInfo F64 = new("f64", false, true, 64, false, false);

	public static readonly TypeInfo Byte = new("byte", false, false, 8, true, false);
	public static readonly TypeInfo Bool = new("bool", false, false, 1, false, true);
	public static readonly TypeInfo Void = new("void", false, false, 0, false, false);
	public static readonly TypeInfo Error = new("error", false, false, 0, false, false);

	public static TypeInfo FromString(string name) => name switch
	{
		"i8" => I8,
		"i16" => I16,
		"i32" => I32,
		"i64" => I64,
		"ui8" => UI8,
		"ui16" => UI16,
		"ui32" => UI32,
		"ui64" => UI64,
		"f32" => F32,
		"f64" => F64,
		"byte" => Byte,
		"bool" => Bool,
		"void" => Void,
		_ => throw new Exception($"Unknown type keyword: '{name}'")
	};

	public string ToLlvmType() => Name switch
	{
		"byte" => "i8",
		"bool" => "i1",
		"ui8" => "i8",
		"ui16" => "i16",
		"ui32" => "i32",
		"ui64" => "i64",
		"f32" => "float",
		"f64" => "double",
		"void" => "void",
		"error" => "i32",
		_ => Name
	};

	public static bool CanImplicitlyCast(TypeInfo from, TypeInfo to)
	{
		if (from == Error || to == Error) return true;
		if (from == to) return true;
		if (from.IsByte || to.IsByte || from.IsBool || to.IsBool || from.IsVoid || to.IsVoid) return false;

		if (from.IsInteger && to.IsInteger)
		{
			if (from.IsSigned == to.IsSigned) return from.Bits < to.Bits;
			else if (!from.IsSigned && to.IsSigned) return from.Bits < to.Bits;
			return false;
		}

		if (from.IsFloat && to.IsFloat) return from.Bits < to.Bits;
		if (from.IsInteger && to.IsFloat) return from.Bits < to.Bits;
		return false;
	}

	public static TypeInfo GetCommonType(TypeInfo left, TypeInfo right)
	{
		if (left == Error || right == Error) return Error;
		if (left == right) return left;
		if (left.IsByte || right.IsByte) throw new Exception("Cannot perform arithmetic operations on 'byte' type directly.");
		if (left.IsBool || right.IsBool) throw new Exception("Cannot perform arithmetic operations on 'bool' type directly.");
		if (left.IsVoid || right.IsVoid) throw new Exception("Cannot perform operations on 'void' type.");
		if (CanImplicitlyCast(left, right)) return right;
		if (CanImplicitlyCast(right, left)) return left;
		throw new Exception($"Cannot implicitly convert between '{left.Name}' and '{right.Name}'.");
	}
}
// ============================================================================
//               PHASE 1: RAW ABSTRACT SYNTAX TREE (AST)
// ============================================================================


// ============================================================================
//               FRONTEND: LEXER, EBNF COMBINATORS, & PARSER
// ============================================================================




// ============================================================================
//               PHASE 2: ABSTRACT SEMANTIC GRAPH (ASG)
// ============================================================================



// ============================================================================
//               EXECUTION PIPELINE
// ============================================================================
public class Program
{
	public static void Main()
	{
		string sourceCode = @"
			// Let's test the Graph Loop Referencing & Named Variables
			
			i32 evaluate_loops() {
				i32 limit = 5;
				i32 current = 0;
				
				// This comment should attach to the header logic
				while (current < limit) {
					current = current + 1;
					if (current == 3) {
						// Skip printing (or doing logic) for the 3rd iteration
						continue;
					}
				}
				
				return current;
			}
			
			i32 main() {
				i32 output = evaluate_loops();
				return output;
			}
		";

		try
		{
			var diagnostics = new DiagnosticBag();
			var lexer = new Lexer();
			var parser = new Parser();
			var semanticAnalyzer = new SemanticAnalyzer();
			var generator = new LlvmIrGeneratorVisitor();

			// Phase 1: Parse to loosely-typed AST
			var tokens = lexer.Tokenize(sourceCode, diagnostics);
			var rawAst = parser.Parse(tokens, diagnostics);

			// Phase 2: Analyze and generate highly-typed ASG
			var asgGraph = semanticAnalyzer.Analyze(rawAst, diagnostics);

			if (diagnostics.HasErrors)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"\n[Build Failed] {diagnostics.Diagnostics.Count} Diagnostics Generated:\n");
				Console.ResetColor();

				diagnostics.PrintDiagnostics(sourceCode);
				return;
			}

			// Phase 3: Translation of the Cyclic ASG directly to LLVM IR
			var irResult = generator.GenerateModule(asgGraph);

			Console.WriteLine("BEGIN PROGRAM IR");
			Console.WriteLine(irResult);
			Console.WriteLine("END PROGRAM IR");

			int res = ProgramLoader.RunLLVMProgram(Encoding.ASCII.GetBytes(irResult));
			Console.WriteLine("Program exited with code {0}", res);
		}
		catch (Exception ex)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"[Fatal Error] {ex.Message}");
			Console.ResetColor();
		}
	}
}