using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;
using System.Globalization;

namespace InfinityOfficialNetwork.Languages.IXX;

// --- Model Definitions ---
public enum TokenType
{
	Return, Identifier, Number, LParen, RParen, LBrace, RBrace,
	Comma, Semicolon, Plus, Minus, Star, Slash, TypeKeyword, BooleanLiteral,
	DoubleAmpersand, DoubleBar, Ampersand, Bar, Caret, Tilde, Exclamation, EOF
}
public record Token(TokenType Type, string Value, int Line, int Column);

// --- Type System Infrastructure ---
public record TypeInfo(string Name, bool IsSigned, bool IsFloat, int Bits, bool IsByte, bool IsBool)
{
	public bool IsInteger => !IsFloat && !IsByte && !IsBool && Name != "error";

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

	// The poison type used to silence cascading compile errors
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
		"error" => "i32", // Fallback type mapping for safety
		_ => Name
	};

	public static bool CanImplicitlyCast(TypeInfo from, TypeInfo to)
	{
		if (from == Error || to == Error) return true; // Suppress cascade warnings
		if (from == to) return true;
		if (from.IsByte || to.IsByte) return false;
		if (from.IsBool || to.IsBool) return false;

		if (from.IsInteger && to.IsInteger)
		{
			if (from.IsSigned == to.IsSigned)
			{
				return from.Bits < to.Bits;
			}
			else if (!from.IsSigned && to.IsSigned)
			{
				return from.Bits < to.Bits;
			}
			return false;
		}

		if (from.IsFloat && to.IsFloat)
		{
			return from.Bits < to.Bits;
		}

		if (from.IsInteger && to.IsFloat)
		{
			return from.Bits < to.Bits;
		}

		return false;
	}

	public static TypeInfo GetCommonType(TypeInfo left, TypeInfo right)
	{
		if (left == Error || right == Error) return Error;
		if (left == right) return left;
		if (left.IsByte || right.IsByte) throw new Exception("Type Error: Cannot perform arithmetic operations on 'byte' type directly. Cast to an arithmetic type first.");
		if (left.IsBool || right.IsBool) throw new Exception("Type Error: Cannot perform arithmetic operations on 'bool' type directly. Cast to an arithmetic type first.");

		if (CanImplicitlyCast(left, right)) return right;
		if (CanImplicitlyCast(right, left)) return left;

		throw new Exception($"Type Mismatch: Cannot implicitly convert between '{left.Name}' and '{right.Name}'. An explicit cast is required.");
	}
}

// --- Abstract Syntax Tree (AST) Nodes ---
public interface IAstNode { T Accept<T>(IAstVisitor<T> visitor); }

// Dedicated Function Signature definition 
public record FunctionSignature(TypeInfo ReturnType, string Name, List<ParameterNode> Parameters);

public record ParameterNode(TypeInfo Type, string Name) : IAstNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record FunctionDeclarationNode(FunctionSignature Signature, List<IStatementNode> Body) : IAstNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public interface IStatementNode : IAstNode { }
public record ReturnStatementNode(IExpressionNode Expression) : IStatementNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public interface IExpressionNode : IAstNode { }
public record ConstantExpressionNode(string Value, TypeInfo Type) : IExpressionNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record VariableExpressionNode(string Identifier) : IExpressionNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record BinaryOperationNode(IExpressionNode Left, TokenType Operator, IExpressionNode Right) : IExpressionNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record FunctionCallExpressionNode(string FunctionName, List<IExpressionNode> Arguments) : IExpressionNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record CastExpressionNode(TypeInfo TargetType, IExpressionNode Expression) : IExpressionNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record UnaryOperationNode(TokenType Operator, IExpressionNode Expression) : IExpressionNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }

// --- Visitor Interface ---
public record VisitorResult(string Value, TypeInfo Type);

public interface IAstVisitor<out T>
{
	T Visit(FunctionDeclarationNode node);
	T Visit(ParameterNode node);
	T Visit(ReturnStatementNode node);
	T Visit(ConstantExpressionNode node);
	T Visit(VariableExpressionNode node);
	T Visit(BinaryOperationNode node);
	T Visit(FunctionCallExpressionNode node);
	T Visit(CastExpressionNode node);
	T Visit(UnaryOperationNode node);
}

// --- Services ---
public class Lexer
{
	public IEnumerable<Token> Tokenize(string source)
	{
		// Pass 1: Sanitize Input (Remove comments to ensure safe parsing)
		string sanitized = Regex.Replace(source, @"//.*?$", "", RegexOptions.Multiline);
		sanitized = Regex.Replace(sanitized, @"/\*.*?\*/", "", RegexOptions.Singleline);

		var tokens = new List<Token>();
		var patterns = new (TokenType Type, string Pattern)[] {
				(TokenType.TypeKeyword, @"\b(i8|i16|i32|i64|ui8|ui16|ui32|ui64|f32|f64|byte|bool)\b"),
				(TokenType.BooleanLiteral, @"\b(true|false)\b"),
				(TokenType.Return, @"\breturn\b"),
				(TokenType.Identifier, @"[a-zA-Z_][a-zA-Z0-9_]*"),
				(TokenType.Number, @"[0-9]+(\.[0-9]+)?f?"),
				(TokenType.LParen, @"\("), (TokenType.RParen, @"\)"), (TokenType.LBrace, @"\{"),
				(TokenType.RBrace, @"\}"), (TokenType.Comma, @","), (TokenType.Semicolon, @";"),
				(TokenType.Plus, @"\+"), (TokenType.Minus, @"-"), (TokenType.Star, @"\*"), (TokenType.Slash, @"/"),
				(TokenType.DoubleAmpersand, @"&&"),
				(TokenType.DoubleBar, @"\|\|"),
				(TokenType.Ampersand, @"&"),
				(TokenType.Bar, @"\|"),
				(TokenType.Caret, @"\^"),
				(TokenType.Tilde, @"~"),
				(TokenType.Exclamation, @"!"),
			};
		int pos = 0;

		// Pass 2: Token Extraction
		while (pos < sanitized.Length)
		{
			if (char.IsWhiteSpace(sanitized[pos])) { pos++; continue; }

			bool matchFound = false;
			foreach (var (type, pattern) in patterns)
			{
				var match = Regex.Match(sanitized.Substring(pos), "^" + pattern);
				if (match.Success) { tokens.Add(new Token(type, match.Value, 0, 0)); pos += match.Length; matchFound = true; break; }
			}
			if (!matchFound) throw new Exception($"Lexical Error: Unexpected symbol at {pos} in sanitized string.");
		}
		tokens.Add(new Token(TokenType.EOF, string.Empty, 0, 0));
		return tokens;
	}
}

public class Parser
{
	private List<Token> _tokens;
	private int _cursor;
	private Dictionary<string, FunctionSignature> _functionDefinitions = new();

	public List<FunctionDeclarationNode> Parse(IEnumerable<Token> tokens)
	{
		_tokens = tokens.ToList();
		_functionDefinitions.Clear();

		// Pass 1: Extract signatures (enables forward declarations inherently)
		_cursor = 0;
		while (Peek().Type != TokenType.EOF)
		{
			ExtractFunctionSignature();
		}

		// Pass 2: Extract Implementations and complete AST Construction
		_cursor = 0;
		var functions = new List<FunctionDeclarationNode>();
		while (Peek().Type != TokenType.EOF)
		{
			functions.Add(ParseFunctionImplementation());
		}

		return functions;
	}

	private void ExtractFunctionSignature()
	{
		var retType = TypeInfo.FromString(Consume(TokenType.TypeKeyword).Value);
		var name = Consume(TokenType.Identifier).Value;

		Consume(TokenType.LParen);
		var parameters = new List<ParameterNode>();
		while (Peek().Type != TokenType.RParen)
		{
			var paramType = TypeInfo.FromString(Consume(TokenType.TypeKeyword).Value);
			parameters.Add(new ParameterNode(paramType, Consume(TokenType.Identifier).Value));
			if (Peek().Type == TokenType.Comma) Consume(TokenType.Comma);
		}
		Consume(TokenType.RParen);

		_functionDefinitions[name] = new FunctionSignature(retType, name, parameters);

		// Safely bypass implementation body parsing in Pass 1
		Consume(TokenType.LBrace);
		int braceCount = 1;
		while (braceCount > 0 && Peek().Type != TokenType.EOF)
		{
			var token = Consume();
			if (token.Type == TokenType.LBrace) braceCount++;
			else if (token.Type == TokenType.RBrace) braceCount--;
		}
	}

	private FunctionDeclarationNode ParseFunctionImplementation()
	{
		// Skip past signature declaration types as we already compiled them in Pass 1
		Consume(TokenType.TypeKeyword);
		var name = Consume(TokenType.Identifier).Value;
		var signature = _functionDefinitions[name];

		Consume(TokenType.LParen);
		while (Peek().Type != TokenType.RParen) Consume(); // Fast forward params
		Consume(TokenType.RParen);

		Consume(TokenType.LBrace);
		var body = new List<IStatementNode>();
		while (Peek().Type != TokenType.RBrace)
		{
			body.Add(ParseReturnStatement());
		}
		Consume(TokenType.RBrace);

		return new FunctionDeclarationNode(signature, body);
	}

	private ReturnStatementNode ParseReturnStatement()
	{
		Consume(TokenType.Return);
		var expr = ParseExpression();
		Consume(TokenType.Semicolon);
		return new ReturnStatementNode(expr);
	}

	private IExpressionNode ParseExpression() => ParseLogicalOr();

	private IExpressionNode ParseLogicalOr()
	{
		var left = ParseLogicalAnd();
		while (Peek().Type == TokenType.DoubleBar)
		{
			var op = Consume().Type;
			var right = ParseLogicalAnd();
			left = new BinaryOperationNode(left, op, right);
		}
		return left;
	}

	private IExpressionNode ParseLogicalAnd()
	{
		var left = ParseBitwiseOr();
		while (Peek().Type == TokenType.DoubleAmpersand)
		{
			var op = Consume().Type;
			var right = ParseBitwiseOr();
			left = new BinaryOperationNode(left, op, right);
		}
		return left;
	}

	private IExpressionNode ParseBitwiseOr()
	{
		var left = ParseBitwiseXor();
		while (Peek().Type == TokenType.Bar)
		{
			var op = Consume().Type;
			var right = ParseBitwiseXor();
			left = new BinaryOperationNode(left, op, right);
		}
		return left;
	}

	private IExpressionNode ParseBitwiseXor()
	{
		var left = ParseBitwiseAnd();
		while (Peek().Type == TokenType.Caret)
		{
			var op = Consume().Type;
			var right = ParseBitwiseAnd();
			left = new BinaryOperationNode(left, op, right);
		}
		return left;
	}

	private IExpressionNode ParseBitwiseAnd()
	{
		var left = ParseAdditive();
		while (Peek().Type == TokenType.Ampersand)
		{
			var op = Consume().Type;
			var right = ParseAdditive();
			left = new BinaryOperationNode(left, op, right);
		}
		return left;
	}

	private IExpressionNode ParseAdditive()
	{
		var left = ParseMultiplicative();
		while (Peek().Type == TokenType.Plus || Peek().Type == TokenType.Minus)
		{
			var op = Consume().Type;
			var right = ParseMultiplicative();
			left = new BinaryOperationNode(left, op, right);
		}
		return left;
	}

	private IExpressionNode ParseMultiplicative()
	{
		var left = ParseUnary();
		while (Peek().Type == TokenType.Star || Peek().Type == TokenType.Slash)
		{
			var op = Consume().Type;
			var right = ParseUnary();
			left = new BinaryOperationNode(left, op, right);
		}
		return left;
	}

	private IExpressionNode ParseUnary()
	{
		if (Peek().Type == TokenType.Exclamation || Peek().Type == TokenType.Tilde)
		{
			var op = Consume().Type;
			var expr = ParseUnary();
			return new UnaryOperationNode(op, expr);
		}

		// Parse explicit casts
		if (Peek().Type == TokenType.LParen && Peek(1).Type == TokenType.TypeKeyword && Peek(2).Type == TokenType.RParen)
		{
			Consume(TokenType.LParen);
			var targetTypeName = Consume(TokenType.TypeKeyword).Value;
			Consume(TokenType.RParen);
			var targetType = TypeInfo.FromString(targetTypeName);
			var expr = ParseUnary();
			return new CastExpressionNode(targetType, expr);
		}

		return ParsePrimary();
	}

	private IExpressionNode ParsePrimary()
	{
		var token = Consume();
		if (token.Type == TokenType.Number)
		{
			string val = token.Value;
			TypeInfo type;
			if (val.EndsWith("f"))
			{
				type = TypeInfo.F32;
				val = val.Substring(0, val.Length - 1);
			}
			else if (val.Contains("."))
			{
				type = TypeInfo.F64;
			}
			else
			{
				type = TypeInfo.I32;
			}
			return new ConstantExpressionNode(val, type);
		}

		if (token.Type == TokenType.BooleanLiteral)
		{
			return new ConstantExpressionNode(token.Value, TypeInfo.Bool);
		}

		if (token.Type == TokenType.Identifier)
		{
			if (Peek().Type == TokenType.LParen)
			{
				Consume(TokenType.LParen);
				var arguments = new List<IExpressionNode>();
				while (Peek().Type != TokenType.RParen)
				{
					arguments.Add(ParseExpression());
					if (Peek().Type == TokenType.Comma) Consume(TokenType.Comma);
				}
				Consume(TokenType.RParen);
				return new FunctionCallExpressionNode(token.Value, arguments);
			}
			return new VariableExpressionNode(token.Value);
		}

		if (token.Type == TokenType.LParen)
		{
			return ParseParenExpression();
		}

		throw new Exception($"Unexpected token in expression: {token.Type}");
	}

	private IExpressionNode ParseParenExpression()
	{
		var expr = ParseExpression();
		Consume(TokenType.RParen);
		return expr;
	}

	private Token Consume(TokenType? type = null)
	{
		var token = _tokens[_cursor++];
		if (type.HasValue && token.Type != type) throw new Exception($"Expected {type} but got {token.Type}");
		return token;
	}

	private Token Peek(int offset = 0)
	{
		if (_cursor + offset >= _tokens.Count) return _tokens.Last();
		return _tokens[_cursor + offset];
	}
}

public class LlvmIrGeneratorVisitor : IAstVisitor<VisitorResult>
{
	private readonly StringBuilder _sb = new();
	private int _registerCount = 1;
	private TypeInfo _currentReturnType;

	private readonly Dictionary<string, TypeInfo> _symbolTable = new();
	private readonly Dictionary<string, (TypeInfo ReturnType, List<TypeInfo> ParamTypes)> _functionTable = new();

	// The diagnostics error tracking list
	private readonly List<string> _errors = new();
	public List<string> Errors => _errors;

	private void LogError(string msg) => _errors.Add(msg);

	// Dedicated orchestration method for the new Function AST structure
	public string GenerateModule(List<FunctionDeclarationNode> functions)
	{
		_sb.Clear();
		_sb.AppendLine("; Generated by InfinityOfficialNetwork IXX Compiler");

		foreach (var func in functions)
		{
			_functionTable[func.Signature.Name] = (func.Signature.ReturnType, func.Signature.Parameters.Select(p => p.Type).ToList());
		}

		foreach (var func in functions)
		{
			func.Accept(this);
		}

		return _sb.ToString();
	}

	public VisitorResult Visit(FunctionDeclarationNode node)
	{
		_registerCount = 1;
		_currentReturnType = node.Signature.ReturnType;
		_symbolTable.Clear();

		foreach (var param in node.Signature.Parameters)
		{
			_symbolTable[param.Name] = param.Type;
		}

		var args = string.Join(", ", node.Signature.Parameters.Select(p => p.Accept(this).Value));
		_sb.AppendLine($"define {node.Signature.ReturnType.ToLlvmType()} @{node.Signature.Name}({args}) {{");
		foreach (var stmt in node.Body) stmt.Accept(this);
		_sb.AppendLine("}\n");

		return new VisitorResult(string.Empty, null);
	}

	public VisitorResult Visit(ParameterNode node) => new VisitorResult($"{node.Type.ToLlvmType()} %{node.Name}", node.Type);

	public VisitorResult Visit(ReturnStatementNode node)
	{
		var exprResult = node.Expression.Accept(this);
		if (exprResult.Type == TypeInfo.Error) return new VisitorResult(string.Empty, null);

		if (exprResult.Type != _currentReturnType)
		{
			if (TypeInfo.CanImplicitlyCast(exprResult.Type, _currentReturnType))
			{
				var casted = GenerateCast(exprResult.Value, exprResult.Type, _currentReturnType);
				_sb.AppendLine($"  ret {_currentReturnType.ToLlvmType()} {casted.Value}");
			}
			else
			{
				LogError($"Cannot implicitly convert return expression of type '{exprResult.Type.Name}' to function's declared return type '{_currentReturnType.Name}'");
			}
		}
		else
		{
			_sb.AppendLine($"  ret {_currentReturnType.ToLlvmType()} {exprResult.Value}");
		}
		return new VisitorResult(string.Empty, null);
	}

	public VisitorResult Visit(ConstantExpressionNode node) => new VisitorResult(node.Value, node.Type);

	public VisitorResult Visit(VariableExpressionNode node)
	{
		if (_symbolTable.TryGetValue(node.Identifier, out var type))
		{
			return new VisitorResult($"%{node.Identifier}", type);
		}
		LogError($"Undefined identifier reference: '{node.Identifier}'");
		return new VisitorResult($"%_err_{node.Identifier}", TypeInfo.Error);
	}

	public VisitorResult Visit(BinaryOperationNode node)
	{
		var leftResult = node.Left.Accept(this);
		var rightResult = node.Right.Accept(this);

		// Logical Operations require strict type checks for boolean inputs
		if (node.Operator == TokenType.DoubleAmpersand || node.Operator == TokenType.DoubleBar)
		{
			if (leftResult.Type != TypeInfo.Error && rightResult.Type != TypeInfo.Error)
			{
				if (leftResult.Type != TypeInfo.Bool || rightResult.Type != TypeInfo.Bool)
				{
					LogError($"Logical operators (&&, ||) strictly require operands of type 'bool'. Got '{leftResult.Type.Name}' and '{rightResult.Type.Name}' instead.");
					return new VisitorResult($"%.t{_registerCount++}", TypeInfo.Error);
				}
			}
			else
			{
				return new VisitorResult($"%.t{_registerCount++}", TypeInfo.Error);
			}

			string logicalOp = node.Operator == TokenType.DoubleAmpersand ? "and" : "or";
			string logicalReg = $"%.t{_registerCount++}";
			_sb.AppendLine($"  {logicalReg} = {logicalOp} i1 {leftResult.Value}, {rightResult.Value}");
			return new VisitorResult(logicalReg, TypeInfo.Bool);
		}

		TypeInfo commonType;
		try
		{
			commonType = TypeInfo.GetCommonType(leftResult.Type, rightResult.Type);
		}
		catch (Exception ex)
		{
			LogError(ex.Message);
			return new VisitorResult($"%.t{_registerCount++}", TypeInfo.Error);
		}

		if (commonType == TypeInfo.Error)
		{
			return new VisitorResult($"%.t{_registerCount++}", TypeInfo.Error);
		}

		// Reject bitwise checks on Float targets
		if ((node.Operator == TokenType.Ampersand || node.Operator == TokenType.Bar || node.Operator == TokenType.Caret) && commonType.IsFloat)
		{
			LogError("Bitwise operations are unsupported on floating-point values.");
			return new VisitorResult($"%.t{_registerCount++}", TypeInfo.Error);
		}

		var castLeft = GenerateCast(leftResult.Value, leftResult.Type, commonType);
		var castRight = GenerateCast(rightResult.Value, rightResult.Type, commonType);

		string reg = $"%.t{_registerCount++}";
		string llvmType = commonType.ToLlvmType();

		string op;
		if (commonType.IsFloat)
		{
			op = node.Operator switch
			{
				TokenType.Plus => "fadd",
				TokenType.Minus => "fsub",
				TokenType.Star => "fmul",
				TokenType.Slash => "fdiv",
				_ => "fadd"
			};
		}
		else
		{
			op = node.Operator switch
			{
				TokenType.Plus => "add",
				TokenType.Minus => "sub",
				TokenType.Star => "mul",
				TokenType.Slash => commonType.IsSigned ? "sdiv" : "udiv",
				TokenType.Ampersand => "and",
				TokenType.Bar => "or",
				TokenType.Caret => "xor",
				_ => "add"
			};
		}

		_sb.AppendLine($"  {reg} = {op} {llvmType} {castLeft.Value}, {castRight.Value}");
		return new VisitorResult(reg, commonType);
	}

	public VisitorResult Visit(FunctionCallExpressionNode node)
	{
		if (!_functionTable.TryGetValue(node.FunctionName, out var sig))
		{
			LogError($"Call to undefined function: '{node.FunctionName}'");
			return new VisitorResult($"%.t{_registerCount++}", TypeInfo.Error);
		}

		if (node.Arguments.Count != sig.ParamTypes.Count)
		{
			LogError($"Arity mismatch. Function '{node.FunctionName}' expects {sig.ParamTypes.Count} arguments, but got {node.Arguments.Count}");
			return new VisitorResult($"%.t{_registerCount++}", TypeInfo.Error);
		}

		var evaluatedArgs = new List<string>();
		bool hasArgError = false;

		for (int i = 0; i < node.Arguments.Count; i++)
		{
			var argResult = node.Arguments[i].Accept(this);
			if (argResult.Type == TypeInfo.Error)
			{
				hasArgError = true;
				continue;
			}
			var targetParamType = sig.ParamTypes[i];

			if (argResult.Type != targetParamType)
			{
				if (TypeInfo.CanImplicitlyCast(argResult.Type, targetParamType))
				{
					var casted = GenerateCast(argResult.Value, argResult.Type, targetParamType);
					evaluatedArgs.Add($"{targetParamType.ToLlvmType()} {casted.Value}");
				}
				else
				{
					LogError($"Cannot implicitly convert argument {i + 1} from type '{argResult.Type.Name}' to expected type '{targetParamType.Name}' in call to '{node.FunctionName}'");
					hasArgError = true;
				}
			}
			else
			{
				evaluatedArgs.Add($"{targetParamType.ToLlvmType()} {argResult.Value}");
			}
		}

		if (hasArgError)
		{
			return new VisitorResult($"%.t{_registerCount++}", TypeInfo.Error);
		}

		string reg = $"%.t{_registerCount++}";
		_sb.AppendLine($"  {reg} = call {sig.ReturnType.ToLlvmType()} @{node.FunctionName}({string.Join(", ", evaluatedArgs)})");
		return new VisitorResult(reg, sig.ReturnType);
	}

	public VisitorResult Visit(CastExpressionNode node)
	{
		var exprResult = node.Expression.Accept(this);
		if (exprResult.Type == TypeInfo.Error) return new VisitorResult($"%.t{_registerCount++}", TypeInfo.Error);
		try
		{
			var castResult = GenerateCast(exprResult.Value, exprResult.Type, node.TargetType);
			return new VisitorResult(castResult.Value, castResult.Type);
		}
		catch (Exception ex)
		{
			LogError(ex.Message);
			return new VisitorResult($"%.t{_registerCount++}", TypeInfo.Error);
		}
	}

	public VisitorResult Visit(UnaryOperationNode node)
	{
		var exprResult = node.Expression.Accept(this);
		if (exprResult.Type == TypeInfo.Error) return new VisitorResult($"%.t{_registerCount++}", TypeInfo.Error);
		string reg = $"%.t{_registerCount++}";

		if (node.Operator == TokenType.Exclamation)
		{
			if (exprResult.Type != TypeInfo.Bool)
			{
				LogError($"Logical NOT (!) requires the operand to be of type 'bool'. Got '{exprResult.Type.Name}' instead.");
				return new VisitorResult(reg, TypeInfo.Error);
			}
			_sb.AppendLine($"  {reg} = xor i1 {exprResult.Value}, true");
			return new VisitorResult(reg, TypeInfo.Bool);
		}

		if (node.Operator == TokenType.Tilde)
		{
			if (!exprResult.Type.IsInteger && exprResult.Type != TypeInfo.Bool)
			{
				LogError($"Bitwise NOT (~) requires the operand to be an integer or bool type. Got '{exprResult.Type.Name}' instead.");
				return new VisitorResult(reg, TypeInfo.Error);
			}
			string llvmType = exprResult.Type.ToLlvmType();
			string mask = exprResult.Type == TypeInfo.Bool ? "true" : "-1";
			_sb.AppendLine($"  {reg} = xor {llvmType} {exprResult.Value}, {mask}");
			return new VisitorResult(reg, exprResult.Type);
		}

		LogError($"Unsupported unary operator '{node.Operator}'");
		return new VisitorResult(reg, TypeInfo.Error);
	}

	private (string Value, TypeInfo Type) GenerateCast(string val, TypeInfo from, TypeInfo to)
	{
		if (from == to) return (val, to);

		// Constant folding for Boolean Values
		if (val == "true" || val == "false")
		{
			bool isTrue = val == "true";
			if (to.IsInteger) return (isTrue ? "1" : "0", to);
			if (to.IsFloat) return (isTrue ? "1.0" : "0.0", to);
		}

		// Constant folding optimizations during compile time (culture-invariant)
		if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedConst))
		{
			if (to.IsBool)
			{
				return (parsedConst != 0.0 ? "true" : "false", to);
			}
			if (from.IsInteger && to.IsInteger)
			{
				return (((long)parsedConst).ToString(CultureInfo.InvariantCulture), to);
			}
			if (from.IsFloat && to.IsFloat)
			{
				return (parsedConst.ToString(CultureInfo.InvariantCulture), to);
			}
			if (from.IsInteger && to.IsFloat)
			{
				return (parsedConst.ToString(CultureInfo.InvariantCulture), to);
			}
			if (from.IsFloat && to.IsInteger)
			{
				return (((long)parsedConst).ToString(CultureInfo.InvariantCulture), to);
			}
		}

		if (from.ToLlvmType() == to.ToLlvmType())
		{
			return (val, to);
		}

		string reg = $"%.t{_registerCount++}";
		string fromLlvm = from.ToLlvmType();
		string toLlvm = to.ToLlvmType();

		// Handle Boolean Cast conversions strictly
		if (from.IsBool)
		{
			if (to.IsInteger)
			{
				_sb.AppendLine($"  {reg} = zext i1 {val} to {toLlvm}");
				return (reg, to);
			}
			if (to.IsFloat)
			{
				string tempReg = $"%.t{_registerCount++}";
				_sb.AppendLine($"  {tempReg} = zext i1 {val} to i32");
				_sb.AppendLine($"  {reg} = uitofp i32 {tempReg} to {toLlvm}");
				return (reg, to);
			}
		}

		if (to.IsBool)
		{
			if (from.IsInteger || from.IsByte)
			{
				_sb.AppendLine($"  {reg} = icmp ne {fromLlvm} {val}, 0");
				return (reg, to);
			}
			if (from.IsFloat)
			{
				_sb.AppendLine($"  {reg} = fcmp une {fromLlvm} {val}, 0.0");
				return (reg, to);
			}
		}

		if (from.IsInteger && to.IsInteger)
		{
			if (from.Bits < to.Bits)
			{
				string op = from.IsSigned ? "sext" : "zext";
				_sb.AppendLine($"  {reg} = {op} {fromLlvm} {val} to {toLlvm}");
			}
			else
			{
				_sb.AppendLine($"  {reg} = trunc {fromLlvm} {val} to {toLlvm}");
			}
		}
		else if (from.IsFloat && to.IsFloat)
		{
			if (from.Bits < to.Bits)
			{
				_sb.AppendLine($"  {reg} = fpext {fromLlvm} {val} to {toLlvm}");
			}
			else
			{
				_sb.AppendLine($"  {reg} = fptrunc {fromLlvm} {val} to {toLlvm}");
			}
		}
		else if (from.IsInteger && to.IsFloat)
		{
			string op = from.IsSigned ? "sitofp" : "uitofp";
			_sb.AppendLine($"  {reg} = {op} {fromLlvm} {val} to {toLlvm}");
		}
		else if (from.IsFloat && to.IsInteger)
		{
			string op = to.IsSigned ? "fptosi" : "fptoui";
			_sb.AppendLine($"  {reg} = {op} {fromLlvm} {val} to {toLlvm}");
		}
		else if (from.IsByte || to.IsByte)
		{
			var resolvedFrom = from.IsByte ? TypeInfo.UI8 : from;
			var resolvedTo = to.IsByte ? TypeInfo.UI8 : to;
			var (castedValue, _) = GenerateCast(val, resolvedFrom, resolvedTo);
			return (castedValue, to);
		}
		else
		{
			throw new Exception($"Generator Error: Unsupported casting logic between '{from.Name}' and '{to.Name}'");
		}

		return (reg, to);
	}
}

public class Program
{
	public static void Main()
	{
		string irPath = "output.ll";
		string exePath = OperatingSystem.IsWindows() ? "ixx_program.exe" : "ixx_program";

		// Program contains explicit casts, solving all parameter widening mismatches
		string sourceCode = @"
			// ============================================================================
			//               IXX COMPILER COMPREHENSIVE INTEGRATION TEST
			// ============================================================================

			// 5. Orchestration & Final Compilation Assertion
			i32 main() {
				// Fixed: Added explicit casting to preserve strict signed-unsigned requirements
				return (i32)check_flags(false, (ui32)1024, (ui32)1024) 
					 * process_byte((byte)255, 15) 
					 + (i32)complex_bitwise((ui16)256, (ui32)65536, (ui64)1234567) 
					 + (i32)calculate_trig(1.5f, 10);
			}

			// 1. Bitwise Manipulation Function
			ui64 complex_bitwise(ui16 a, ui32 b, ui64 c) {
				return ~((ui64)(a | b) ^ c);
			}

			// 2. Strict Boolean and Logical Flag Verification
			bool check_flags(bool enable, ui32 flags, ui32 mask) {
				return !enable && (bool)(flags & mask);
			}

			// 3. Floating Point Calculations
			f64 calculate_trig(f32 angle, i32 scale) {
				return (f64)angle * (f64)scale + 3.141592653589793;
			}

			// 4. Non-Arithmetic Byte Handling
			i32 process_byte(byte rawByte, i32 mask) {
				return ((i32)rawByte & mask) + 120;
			}
		";

		try
		{
			// 1. Compile Source to LLVM IR
			var lexer = new Lexer();
			var parser = new Parser();
			var generator = new LlvmIrGeneratorVisitor();

			var tokens = lexer.Tokenize(sourceCode);

			// AST is now strictly a List of FunctionDeclarationNodes
			var ast = parser.Parse(tokens);

			var irResult = generator.GenerateModule(ast);

			// If any semantic errors were captured, write them all and abort before calling Clang
			if (generator.Errors.Any())
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"\n[Build Failed] {generator.Errors.Count} Semantic Errors Found:");
				foreach (var err in generator.Errors)
				{
					Console.WriteLine($"  * {err}");
				}
				Console.ResetColor();
				return;
			}

			//File.WriteAllText(irPath, irResult);
			//Console.WriteLine($"[Build] IR generated: {irPath}");

			// 2. Call Clang to link and compile
			//CompileWithClang(irPath, exePath);

			//execute program with loader

			int res = ProgramLoader.RunLLVMProgram(Encoding.ASCII.GetBytes(irResult));
			Console.WriteLine("Program exited with code {0}", res);
		}
		catch (Exception ex)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"[Error] {ex.Message}");
			Console.ResetColor();
		}
	}
}