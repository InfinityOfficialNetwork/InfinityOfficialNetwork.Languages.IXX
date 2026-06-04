using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfinityOfficialNetwork.Languages.IXX;

// --- Symbol Table Models ---
public class VariableSymbol
{
	public string Name { get; }
	public TypeInfo Type { get; }
	public bool IsLocal { get; }
	public VariableSymbol(string name, TypeInfo type, bool isLocal) { Name = name; Type = type; IsLocal = isLocal; }
}

public class FunctionSymbol
{
	public string Name { get; }
	public TypeInfo ReturnType { get; }
	public IReadOnlyList<VariableSymbol> Parameters { get; }
	public FunctionSymbol(string name, TypeInfo returnType, IReadOnlyList<VariableSymbol> parameters) { Name = name; ReturnType = returnType; Parameters = parameters; }
}

public record LoopAction(TokenType Action, AsgStatement TargetLoop);

// --- Object-Oriented Graph Nodes ---
public abstract class AsgNode
{
	public int Line { get; }
	public int Column { get; }
	protected AsgNode(int line, int col) { Line = line; Column = col; }
	public abstract T Accept<T>(IAsgVisitor<T> visitor);
}

public abstract class AsgStatement : AsgNode
{
	public string Comment { get; set; }
	protected AsgStatement(int line, int col, string cmt) : base(line, col) { Comment = cmt; }
}

public abstract class AsgExpression : AsgNode
{
	public TypeInfo Type { get; protected set; }
	protected AsgExpression(TypeInfo type, int line, int col) : base(line, col) { Type = type; }
}

public class AsgFunction : AsgNode
{
	public FunctionSymbol Symbol { get; }
	public List<AsgStatement> Body { get; }
	public string Comment { get; }
	public AsgFunction(FunctionSymbol symbol, List<AsgStatement> body, int line, int col, string cmt) : base(line, col) { Symbol = symbol; Body = body; Comment = cmt; }
	public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this);
}

public class AsgBlock : AsgStatement { public List<AsgStatement> Statements { get; } public AsgBlock(List<AsgStatement> stmts, int l, int c, string cmt) : base(l, c, cmt) { Statements = stmts; } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }
public class AsgReturn : AsgStatement { public AsgExpression Expression { get; } public AsgReturn(AsgExpression expr, int l, int c, string cmt) : base(l, c, cmt) { Expression = expr; } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }
public class AsgVariableDecl : AsgStatement { public VariableSymbol Symbol { get; } public AsgExpression InitialValue { get; } public AsgVariableDecl(VariableSymbol sym, AsgExpression init, int l, int c, string cmt) : base(l, c, cmt) { Symbol = sym; InitialValue = init; } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }
public class AsgAssignment : AsgStatement { public VariableSymbol Symbol { get; } public AsgExpression Expression { get; } public AsgAssignment(VariableSymbol sym, AsgExpression expr, int l, int c, string cmt) : base(l, c, cmt) { Symbol = sym; Expression = expr; } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }
public class AsgIf : AsgStatement { public AsgExpression Condition { get; } public AsgStatement ThenBranch { get; } public AsgStatement ElseBranch { get; } public AsgIf(AsgExpression cond, AsgStatement tBranch, AsgStatement eBranch, int l, int c, string cmt) : base(l, c, cmt) { Condition = cond; ThenBranch = tBranch; ElseBranch = eBranch; } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }
public class AsgWhile : AsgStatement { public AsgExpression Condition { get; set; } public AsgStatement Body { get; set; } public AsgWhile(int l, int c, string cmt) : base(l, c, cmt) { } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }
public class AsgDoWhile : AsgStatement { public AsgStatement Body { get; set; } public AsgExpression Condition { get; set; } public AsgDoWhile(int l, int c, string cmt) : base(l, c, cmt) { } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }
public class AsgLoopControl : AsgStatement { public IReadOnlyList<LoopAction> Actions { get; } public AsgLoopControl(IReadOnlyList<LoopAction> actions, int l, int c, string cmt) : base(l, c, cmt) { Actions = actions; } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }
public class AsgErrorStatement : AsgStatement { public AsgErrorStatement(int l, int c) : base(l, c, null) { } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }

public class AsgConstant : AsgExpression { public string Value { get; } public AsgConstant(string val, TypeInfo type, int l, int c) : base(type, l, c) { Value = val; } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }
public class AsgVariableAccess : AsgExpression { public VariableSymbol Symbol { get; } public AsgVariableAccess(VariableSymbol sym, int l, int c) : base(sym.Type, l, c) { Symbol = sym; } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }
public class AsgBinaryOperation : AsgExpression { public AsgExpression Left { get; } public TokenType Operator { get; } public AsgExpression Right { get; } public AsgBinaryOperation(AsgExpression left, TokenType op, AsgExpression right, TypeInfo t, int l, int c) : base(t, l, c) { Left = left; Operator = op; Right = right; } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }
public class AsgFunctionCall : AsgExpression { public FunctionSymbol Symbol { get; } public List<AsgExpression> Arguments { get; } public AsgFunctionCall(FunctionSymbol sym, List<AsgExpression> args, int l, int c) : base(sym.ReturnType, l, c) { Symbol = sym; Arguments = args; } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }
public class AsgCast : AsgExpression { public AsgExpression Expression { get; } public AsgCast(TypeInfo target, AsgExpression expr, int l, int c) : base(target, l, c) { Expression = expr; } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }
public class AsgUnaryOperation : AsgExpression { public TokenType Operator { get; } public AsgExpression Expression { get; } public AsgUnaryOperation(TokenType op, AsgExpression expr, TypeInfo t, int l, int c) : base(t, l, c) { Operator = op; Expression = expr; } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }
public class AsgErrorExpression : AsgExpression { public AsgErrorExpression(int l, int c) : base(TypeInfo.Error, l, c) { } public override T Accept<T>(IAsgVisitor<T> visitor) => visitor.Visit(this); }

public interface IAsgVisitor<out T>
{
	T Visit(AsgFunction node); T Visit(AsgBlock node); T Visit(AsgReturn node); T Visit(AsgVariableDecl node);
	T Visit(AsgAssignment node); T Visit(AsgIf node); T Visit(AsgWhile node); T Visit(AsgDoWhile node);
	T Visit(AsgLoopControl node); T Visit(AsgErrorStatement node); T Visit(AsgConstant node);
	T Visit(AsgVariableAccess node); T Visit(AsgBinaryOperation node); T Visit(AsgFunctionCall node);
	T Visit(AsgCast node); T Visit(AsgUnaryOperation node); T Visit(AsgErrorExpression node);
}

// --- Semantic Analyzer ---
public class SemanticAnalyzer : IAstVisitor<AsgNode>
{
	private readonly Dictionary<string, VariableSymbol> _activeScope = new();
	private readonly Dictionary<string, FunctionSymbol> _functions = new();
	private readonly Stack<AsgStatement> _activeLoops = new();

	private FunctionSymbol _currentFunction;
	private DiagnosticBag _diagnostics;

	public List<AsgFunction> Analyze(List<FunctionDeclarationNode> functions, DiagnosticBag diagnostics)
	{
		_diagnostics = diagnostics;

		// Pass 1: Declare all Function Symbols
		foreach (var func in functions)
		{
			var paramSymbols = func.Signature.Parameters.Select(p => new VariableSymbol(p.Name, p.Type, false)).ToList();
			_functions[func.Signature.Name] = new FunctionSymbol(func.Signature.Name, func.Signature.ReturnType, paramSymbols);
		}

		// Pass 2: Graph Assembly
		var asgFunctions = new List<AsgFunction>();
		foreach (var func in functions) asgFunctions.Add((AsgFunction)func.Accept(this));
		return asgFunctions;
	}

	private void Log(string code, string msg, int line, int col) => _diagnostics.Report(code, msg, line, col);

	public AsgNode Visit(FunctionDeclarationNode node)
	{
		_currentFunction = _functions[node.Signature.Name];
		_activeScope.Clear();
		_activeLoops.Clear();

		foreach (var param in _currentFunction.Parameters) _activeScope[param.Name] = param;

		var body = new List<AsgStatement>();
		foreach (var stmt in node.Body) body.Add((AsgStatement)stmt.Accept(this));

		return new AsgFunction(_currentFunction, body, node.Line, node.Column, node.Signature.Comment);
	}

	public AsgNode Visit(ParameterNode node) => null;

	public AsgNode Visit(ReturnStatementNode node)
	{
		var expr = (AsgExpression)node.Expression.Accept(this);
		if (expr.Type == TypeInfo.Error) return new AsgErrorStatement(node.Line, node.Column);

		if (expr.Type != _currentFunction.ReturnType)
		{
			if (TypeInfo.CanImplicitlyCast(expr.Type, _currentFunction.ReturnType)) expr = GenerateCast(expr, _currentFunction.ReturnType);
			else { Log("SEM0001", $"Cannot convert return expression '{expr.Type.Name}' to '{_currentFunction.ReturnType.Name}'", node.Line, node.Column); return new AsgErrorStatement(node.Line, node.Column); }
		}
		return new AsgReturn(expr, node.Line, node.Column, node.Comment);
	}

	public AsgNode Visit(VariableDeclarationNode node)
	{
		var sym = new VariableSymbol(node.Name, node.Type, true);
		_activeScope[node.Name] = sym;

		AsgExpression initExpr = null;
		if (node.InitialValue != null)
		{
			initExpr = (AsgExpression)node.InitialValue.Accept(this);
			if (initExpr.Type != node.Type)
			{
				if (TypeInfo.CanImplicitlyCast(initExpr.Type, node.Type)) initExpr = GenerateCast(initExpr, node.Type);
				else Log("SEM0001", $"Cannot implicitly convert initializer of type '{initExpr.Type.Name}' to '{node.Type.Name}'", node.Line, node.Column);
			}
		}

		return new AsgVariableDecl(sym, initExpr, node.Line, node.Column, node.Comment);
	}

	public AsgNode Visit(AssignmentStatementNode node)
	{
		if (!_activeScope.TryGetValue(node.Name, out var sym))
		{
			Log("SEM0002", $"Undefined variable '{node.Name}'", node.Line, node.Column);
			return new AsgErrorStatement(node.Line, node.Column);
		}
		if (!sym.IsLocal)
		{
			Log("SEM0003", $"Cannot assign to immutable parameter '{node.Name}'", node.Line, node.Column);
			return new AsgErrorStatement(node.Line, node.Column);
		}

		var expr = (AsgExpression)node.Expression.Accept(this);
		if (expr.Type != sym.Type)
		{
			if (TypeInfo.CanImplicitlyCast(expr.Type, sym.Type)) expr = GenerateCast(expr, sym.Type);
			else Log("SEM0001", $"Cannot implicitly convert assignment value '{expr.Type.Name}' to '{sym.Type.Name}'", node.Line, node.Column);
		}

		return new AsgAssignment(sym, expr, node.Line, node.Column, node.Comment);
	}

	public AsgNode Visit(BlockStatementNode node)
	{
		var stmts = new List<AsgStatement>();
		foreach (var stmt in node.Statements) stmts.Add((AsgStatement)stmt.Accept(this));
		return new AsgBlock(stmts, node.Line, node.Column, node.Comment);
	}

	public AsgNode Visit(IfStatementNode node)
	{
		var cond = (AsgExpression)node.Condition.Accept(this);
		if (cond.Type != TypeInfo.Bool) cond = GenerateCast(cond, TypeInfo.Bool);

		var tBranch = (AsgStatement)node.ThenBranch.Accept(this);
		var eBranch = node.ElseBranch != null ? (AsgStatement)node.ElseBranch.Accept(this) : null;

		return new AsgIf(cond, tBranch, eBranch, node.Line, node.Column, node.Comment);
	}

	public AsgNode Visit(WhileStatementNode node)
	{
		var whileGraphNode = new AsgWhile(node.Line, node.Column, node.Comment);
		_activeLoops.Push(whileGraphNode);

		whileGraphNode.Condition = (AsgExpression)node.Condition.Accept(this);
		if (whileGraphNode.Condition.Type != TypeInfo.Bool) whileGraphNode.Condition = GenerateCast(whileGraphNode.Condition, TypeInfo.Bool);

		whileGraphNode.Body = (AsgStatement)node.Body.Accept(this);

		_activeLoops.Pop();
		return whileGraphNode;
	}

	public AsgNode Visit(DoWhileStatementNode node)
	{
		var doGraphNode = new AsgDoWhile(node.Line, node.Column, node.Comment);
		_activeLoops.Push(doGraphNode);

		doGraphNode.Body = (AsgStatement)node.Body.Accept(this);

		doGraphNode.Condition = (AsgExpression)node.Condition.Accept(this);
		if (doGraphNode.Condition.Type != TypeInfo.Bool) doGraphNode.Condition = GenerateCast(doGraphNode.Condition, TypeInfo.Bool);

		_activeLoops.Pop();
		return doGraphNode;
	}

	public AsgNode Visit(LoopControlStatementNode node)
	{
		var loops = _activeLoops.ToArray();
		var actions = new List<LoopAction>();
		int loopIdx = 0;

		foreach (var act in node.Actions)
		{
			if (loopIdx >= loops.Length)
			{
				Log("SEM0006", "Loop control statement targeted more loop boundaries than are active.", node.Line, node.Column);
				return new AsgErrorStatement(node.Line, node.Column);
			}
			actions.Add(new LoopAction(act, loops[loopIdx]));
			if (act == TokenType.Break) loopIdx++;
		}

		return new AsgLoopControl(actions, node.Line, node.Column, node.Comment);
	}

	public AsgNode Visit(ConstantExpressionNode node) => new AsgConstant(node.Value, node.Type, node.Line, node.Column);

	public AsgNode Visit(VariableExpressionNode node)
	{
		if (_activeScope.TryGetValue(node.Identifier, out var sym)) return new AsgVariableAccess(sym, node.Line, node.Column);
		Log("SEM0002", $"Undefined identifier reference: '{node.Identifier}'", node.Line, node.Column);
		return new AsgErrorExpression(node.Line, node.Column);
	}

	public AsgNode Visit(BinaryOperationNode node)
	{
		var left = (AsgExpression)node.Left.Accept(this);
		var right = (AsgExpression)node.Right.Accept(this);

		if (node.Operator is TokenType.DoubleAmpersand or TokenType.DoubleBar)
		{
			if (left.Type != TypeInfo.Bool || right.Type != TypeInfo.Bool)
			{
				Log("SEM0005", $"Logical operators (&&, ||) require 'bool'. Got '{left.Type.Name}' and '{right.Type.Name}'.", node.Line, node.Column);
				return new AsgErrorExpression(node.Line, node.Column);
			}
			return new AsgBinaryOperation(left, node.Operator, right, TypeInfo.Bool, node.Line, node.Column);
		}

		TypeInfo commonType;
		try { commonType = TypeInfo.GetCommonType(left.Type, right.Type); }
		catch (Exception ex) { Log("SEM0001", ex.Message, node.Line, node.Column); return new AsgErrorExpression(node.Line, node.Column); }

		if (node.Operator is TokenType.Ampersand or TokenType.Bar or TokenType.Caret)
		{
			if (commonType.IsFloat) { Log("SEM0008", "Bitwise operations unsupported on floats.", node.Line, node.Column); return new AsgErrorExpression(node.Line, node.Column); }
			return new AsgBinaryOperation(GenerateCast(left, commonType), node.Operator, GenerateCast(right, commonType), commonType, node.Line, node.Column);
		}

		if (node.Operator is TokenType.DoubleEqual or TokenType.NotEqual or TokenType.LessThan or TokenType.LessEqual or TokenType.GreaterThan or TokenType.GreaterEqual)
		{
			return new AsgBinaryOperation(GenerateCast(left, commonType), node.Operator, GenerateCast(right, commonType), TypeInfo.Bool, node.Line, node.Column);
		}

		return new AsgBinaryOperation(GenerateCast(left, commonType), node.Operator, GenerateCast(right, commonType), commonType, node.Line, node.Column);
	}

	public AsgNode Visit(FunctionCallExpressionNode node)
	{
		if (!_functions.TryGetValue(node.FunctionName, out var sym))
		{
			Log("SEM0002", $"Call to undefined function: '{node.FunctionName}'", node.Line, node.Column);
			return new AsgErrorExpression(node.Line, node.Column);
		}
		if (node.Arguments.Count != sym.Parameters.Count)
		{
			Log("SEM0004", $"Arity mismatch. Expected {sym.Parameters.Count} arguments, got {node.Arguments.Count}", node.Line, node.Column);
			return new AsgErrorExpression(node.Line, node.Column);
		}

		var args = new List<AsgExpression>();
		for (int i = 0; i < node.Arguments.Count; i++)
		{
			var arg = (AsgExpression)node.Arguments[i].Accept(this);
			var targetType = sym.Parameters[i].Type;

			if (arg.Type != targetType)
			{
				if (TypeInfo.CanImplicitlyCast(arg.Type, targetType)) arg = GenerateCast(arg, targetType);
				else Log("SEM0001", $"Cannot convert argument {i + 1} '{arg.Type.Name}' to '{targetType.Name}'", node.Line, node.Column);
			}
			args.Add(arg);
		}

		return new AsgFunctionCall(sym, args, node.Line, node.Column);
	}

	public AsgNode Visit(CastExpressionNode node)
	{
		var expr = (AsgExpression)node.Expression.Accept(this);
		try { return GenerateCast(expr, node.TargetType); }
		catch (Exception ex) { Log("SEM0001", ex.Message, node.Line, node.Column); return new AsgErrorExpression(node.Line, node.Column); }
	}

	public AsgNode Visit(UnaryOperationNode node)
	{
		var expr = (AsgExpression)node.Expression.Accept(this);
		if (node.Operator == TokenType.Exclamation)
		{
			if (expr.Type != TypeInfo.Bool) Log("SEM0005", $"Logical NOT (!) requires 'bool'.", node.Line, node.Column);
			return new AsgUnaryOperation(node.Operator, expr, TypeInfo.Bool, node.Line, node.Column);
		}
		if (node.Operator == TokenType.Tilde)
		{
			if (!expr.Type.IsInteger && expr.Type != TypeInfo.Bool) Log("SEM0005", $"Bitwise NOT (~) requires integer or bool.", node.Line, node.Column);
			return new AsgUnaryOperation(node.Operator, expr, expr.Type, node.Line, node.Column);
		}
		return new AsgErrorExpression(node.Line, node.Column);
	}

	private AsgExpression GenerateCast(AsgExpression expr, TypeInfo to)
	{
		var from = expr.Type;
		if (from == to) return expr;
		if (from == TypeInfo.Error || to == TypeInfo.Error) return new AsgErrorExpression(expr.Line, expr.Column);

		if (expr is AsgConstant c)
		{
			if (c.Value == "true" || c.Value == "false")
			{
				bool isTrue = c.Value == "true";
				if (to.IsInteger) return new AsgConstant(isTrue ? "1" : "0", to, c.Line, c.Column);
				if (to.IsFloat) return new AsgConstant(isTrue ? "1.0" : "0.0", to, c.Line, c.Column);
			}

			if (double.TryParse(c.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedConst))
			{
				if (to.IsBool) return new AsgConstant(parsedConst != 0.0 ? "true" : "false", to, c.Line, c.Column);
				if (from.IsInteger && to.IsInteger) return new AsgConstant(((long)parsedConst).ToString(CultureInfo.InvariantCulture), to, c.Line, c.Column);
				if (from.IsFloat && to.IsFloat) return new AsgConstant(parsedConst.ToString(CultureInfo.InvariantCulture), to, c.Line, c.Column);
				if (from.IsInteger && to.IsFloat) return new AsgConstant(parsedConst.ToString(CultureInfo.InvariantCulture), to, c.Line, c.Column);
				if (from.IsFloat && to.IsInteger) return new AsgConstant(((long)parsedConst).ToString(CultureInfo.InvariantCulture), to, c.Line, c.Column);
			}
		}

		return new AsgCast(to, expr, expr.Line, expr.Column);
	}
}
