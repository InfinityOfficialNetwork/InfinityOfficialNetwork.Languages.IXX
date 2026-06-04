using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfinityOfficialNetwork.Languages.IXX;
public class Parser
{
	public List<FunctionDeclarationNode> Parse(IEnumerable<Token> tokens, DiagnosticBag diagnostics)
	{
		var stream = new TokenStream(tokens);
		var grammar = new IxxGrammar();
		var functionDefinitions = new Dictionary<string, FunctionSignature>();

		// PASS 1: Signatures
		while (stream.Peek().Type != TokenType.EOF)
		{
			stream.ResetFurthestError();
			var sigRes = grammar.FunctionSignature.Parse(stream);
			if (!sigRes.Success)
			{
				diagnostics.Diagnostics.Add(stream.FurthestError ?? new Diagnostic("PAR0000", "Fatal Syntax Error", stream.Peek().Line, stream.Peek().Column));
				SkipToNextFunction(stream);
				continue;
			}

			functionDefinitions[sigRes.Value.Name] = sigRes.Value;
			var brace = stream.Consume();
			if (brace.Type != TokenType.LBrace)
			{
				diagnostics.Report("PAR0002", "Expected '{' to begin function body", brace.Line, brace.Column);
				SkipToNextFunction(stream);
				continue;
			}

			int depth = 1;
			while (depth > 0 && stream.Peek().Type != TokenType.EOF)
			{
				var t = stream.Consume();
				if (t.Type == TokenType.LBrace) depth++;
				if (t.Type == TokenType.RBrace) depth--;
			}
		}

		// PASS 2: Implementations
		stream.Restore(0);
		var functions = new List<FunctionDeclarationNode>();
		var funcDeclRule = grammar.FunctionImplementation(functionDefinitions);

		while (stream.Peek().Type != TokenType.EOF)
		{
			stream.ResetFurthestError();
			var funcRes = funcDeclRule.Parse(stream);
			if (!funcRes.Success)
			{
				diagnostics.Diagnostics.Add(stream.FurthestError ?? new Diagnostic("PAR0000", "Fatal Syntax Error", stream.Peek().Line, stream.Peek().Column));
				SkipToNextFunction(stream);
				continue;
			}
			functions.Add(funcRes.Value);
		}

		return functions;
	}

	private void SkipToNextFunction(TokenStream stream)
	{
		if (stream.Peek().Type != TokenType.EOF) stream.Consume();
		while (stream.Peek().Type != TokenType.EOF)
		{
			if (stream.Peek().Type == TokenType.TypeKeyword) break;
			stream.Consume();
		}
	}
}

public record ParseResult<T>(bool Success, T Value);

public abstract class Rule<T>
{
	public abstract ParseResult<T> Parse(TokenStream stream);
	public Rule<T> Or(Rule<T> other) => new ChoiceRule<T>(this, other);
	public static Rule<T> operator |(Rule<T> left, Rule<T> right) => left.Or(right);
	public Rule<R> Map<R>(Func<T, R> mapper) => new MapRule<T, R>(this, mapper);
	public Rule<R> Then<T2, R>(Rule<T2> next, Func<T, T2, R> combiner) => new SeqRule<T, T2, R>(this, next, combiner);
	public Rule<T> KeepLeft<T2>(Rule<T2> next) => Then(next, (a, b) => a);
	public Rule<T2> KeepRight<T2>(Rule<T2> next) => Then(next, (a, b) => b);
	public Rule<List<T>> Many() => new ManyRule<T>(this);
	public Rule<T> Optional() => new OptionalRule<T>(this);
}

public class MatchTokenRule : Rule<Token>
{
	private readonly TokenType _type;
	public MatchTokenRule(TokenType type) { _type = type; }
	public override ParseResult<Token> Parse(TokenStream stream)
	{
		var peek = stream.Peek();
		if (peek.Type == _type) return new ParseResult<Token>(true, stream.Consume());
		stream.RecordError("PAR0001", $"Expected '{_type}' but got '{peek.Type}'", peek.Line, peek.Column);
		return new ParseResult<Token>(false, default);
	}
}

public class SeqRule<T1, T2, R> : Rule<R> { Rule<T1> r1; Rule<T2> r2; Func<T1, T2, R> map; public SeqRule(Rule<T1> r1, Rule<T2> r2, Func<T1, T2, R> map) { this.r1 = r1; this.r2 = r2; this.map = map; } public override ParseResult<R> Parse(TokenStream stream) { int cp = stream.Checkpoint(); var res1 = r1.Parse(stream); if (!res1.Success) { stream.Restore(cp); return new ParseResult<R>(false, default); } var res2 = r2.Parse(stream); if (!res2.Success) { stream.Restore(cp); return new ParseResult<R>(false, default); } return new ParseResult<R>(true, map(res1.Value, res2.Value)); } }
public class ChoiceRule<T> : Rule<T> { Rule<T> r1, r2; public ChoiceRule(Rule<T> r1, Rule<T> r2) { this.r1 = r1; this.r2 = r2; } public override ParseResult<T> Parse(TokenStream stream) { int cp = stream.Checkpoint(); var res1 = r1.Parse(stream); if (res1.Success) return res1; stream.Restore(cp); var res2 = r2.Parse(stream); if (res2.Success) return res2; stream.Restore(cp); return new ParseResult<T>(false, default); } }
public class ManyRule<T> : Rule<List<T>> { Rule<T> r; public ManyRule(Rule<T> r) { this.r = r; } public override ParseResult<List<T>> Parse(TokenStream stream) { var list = new List<T>(); while (true) { int cp = stream.Checkpoint(); var res = r.Parse(stream); if (res.Success) list.Add(res.Value); else { stream.Restore(cp); break; } } return new ParseResult<List<T>>(true, list); } }
public class OptionalRule<T> : Rule<T> { Rule<T> r; public OptionalRule(Rule<T> r) { this.r = r; } public override ParseResult<T> Parse(TokenStream stream) { int cp = stream.Checkpoint(); var res = r.Parse(stream); if (res.Success) return res; stream.Restore(cp); return new ParseResult<T>(true, default); } }
public class MapRule<T, R> : Rule<R> { Rule<T> r; Func<T, R> map; public MapRule(Rule<T> r, Func<T, R> map) { this.r = r; this.map = map; } public override ParseResult<R> Parse(TokenStream stream) { var res = r.Parse(stream); if (res.Success) return new ParseResult<R>(true, map(res.Value)); return new ParseResult<R>(false, default); } }
public class DeferredRule<T> : Rule<T> { public Rule<T> Inner { get; set; } public override ParseResult<T> Parse(TokenStream stream) => Inner.Parse(stream); }

public class IxxGrammar
{
	public static Rule<Token> Match(TokenType t) => new MatchTokenRule(t);

	public Rule<Token> TypeKwd = Match(TokenType.TypeKeyword);
	public Rule<Token> Ident = Match(TokenType.Identifier);
	public Rule<Token> LParen = Match(TokenType.LParen);
	public Rule<Token> RParen = Match(TokenType.RParen);
	public Rule<Token> LBrace = Match(TokenType.LBrace);
	public Rule<Token> RBrace = Match(TokenType.RBrace);
	public Rule<Token> Comma = Match(TokenType.Comma);
	public Rule<Token> Semi = Match(TokenType.Semicolon);
	public Rule<Token> RetKwd = Match(TokenType.Return);
	public Rule<Token> IfKwd = Match(TokenType.If);
	public Rule<Token> WhileKwd = Match(TokenType.While);
	public Rule<Token> DoKwd = Match(TokenType.Do);

	public DeferredRule<IExpressionNode> Expr = new();
	public Rule<FunctionSignature> FunctionSignature;

	public IxxGrammar()
	{
		var Param = TypeKwd.Then(Ident, (t, id) => new ParameterNode(TypeInfo.FromString(t.Value), id.Value, t.Line, t.Column, t.Comment));
		var ParamList = Param.Then(Comma.KeepRight(Param).Many(), (first, rest) => { rest.Insert(0, first); return rest; }).Optional().Map(l => l ?? new List<ParameterNode>());

		FunctionSignature = TypeKwd.Then(Ident, (t, id) => (t, id)).KeepLeft(LParen).Then(ParamList, (tid, p) => new FunctionSignature(TypeInfo.FromString(tid.t.Value), tid.id.Value, p, tid.t.Line, tid.t.Column, tid.t.Comment)).KeepLeft(RParen);

		var NumLit = Match(TokenType.Number).Map(t => {
			string val = t.Value;
			TypeInfo type = val.EndsWith("f") ? TypeInfo.F32 : val.Contains(".") ? TypeInfo.F64 : TypeInfo.I32;
			if (val.EndsWith("f")) val = val.Substring(0, val.Length - 1);
			return (IExpressionNode)new ConstantExpressionNode(val, type, t.Line, t.Column);
		});
		var BoolLit = Match(TokenType.BooleanLiteral).Map(t => (IExpressionNode)new ConstantExpressionNode(t.Value, TypeInfo.Bool, t.Line, t.Column));
		var VarRef = Ident.Map(t => (IExpressionNode)new VariableExpressionNode(t.Value, t.Line, t.Column));

		var ArgList = Expr.Then(Comma.KeepRight(Expr).Many(), (first, rest) => { rest.Insert(0, first); return rest; }).Optional().Map(l => l ?? new List<IExpressionNode>());
		var FuncCall = Ident.KeepLeft(LParen).Then(ArgList, (id, args) => (IExpressionNode)new FunctionCallExpressionNode(id.Value, args, id.Line, id.Column)).KeepLeft(RParen);
		var ParenExpr = LParen.KeepRight(Expr).KeepLeft(RParen);

		var Primary = FuncCall | NumLit | BoolLit | VarRef | ParenExpr;

		var Unary = new DeferredRule<IExpressionNode>();
		var CastExpr = LParen.KeepRight(TypeKwd).KeepLeft(RParen).Then(Unary, (t, u) => (IExpressionNode)new CastExpressionNode(TypeInfo.FromString(t.Value), u, t.Line, t.Column));
		var UnaryOp = Match(TokenType.Exclamation) | Match(TokenType.Tilde);
		var PrefixExpr = UnaryOp.Then(Unary, (op, u) => (IExpressionNode)new UnaryOperationNode(op.Type, u, op.Line, op.Column));

		Unary.Inner = PrefixExpr | CastExpr | Primary;

		var Multiplicative = LeftAssoc(Unary, TokenType.Star, TokenType.Slash);
		var Additive = LeftAssoc(Multiplicative, TokenType.Plus, TokenType.Minus);
		var Relational = LeftAssoc(Additive, TokenType.LessThan, TokenType.LessEqual, TokenType.GreaterThan, TokenType.GreaterEqual);
		var Equality = LeftAssoc(Relational, TokenType.DoubleEqual, TokenType.NotEqual);
		var BitAnd = LeftAssoc(Equality, TokenType.Ampersand);
		var BitXor = LeftAssoc(BitAnd, TokenType.Caret);
		var BitOr = LeftAssoc(BitXor, TokenType.Bar);
		var LogAnd = LeftAssoc(BitOr, TokenType.DoubleAmpersand);
		var LogOr = LeftAssoc(LogAnd, TokenType.DoubleBar);

		Expr.Inner = LogOr;
	}

	private Rule<IExpressionNode> LeftAssoc(Rule<IExpressionNode> next, params TokenType[] ops)
	{
		Rule<Token> opRule = Match(ops[0]);
		for (int i = 1; i < ops.Length; i++) opRule = opRule | Match(ops[i]);
		return next.Then(opRule.Then(next, (op, rhs) => (op, rhs)).Many(), (lhs, tails) => {
			var expr = lhs;
			foreach (var tail in tails) expr = new BinaryOperationNode(expr, tail.op.Type, tail.rhs, expr.Line, expr.Column);
			return expr;
		});
	}

	public Rule<FunctionDeclarationNode> FunctionImplementation(Dictionary<string, FunctionSignature> signatures)
	{
		var Stmt = new DeferredRule<IStatementNode>();

		var BlockStmt = LBrace.Then(Stmt.Many(), (lb, stmts) => (IStatementNode)new BlockStatementNode(stmts, lb.Line, lb.Column, lb.Comment)).KeepLeft(RBrace);

		var IfStmt = IfKwd.Then(
			LParen.KeepRight(Expr).KeepLeft(RParen).Then(Stmt, (c, t) => (c, t)).Then(Match(TokenType.Else).KeepRight(Stmt).Optional(), (p, e) => (p.c, p.t, e)),
			(ifT, body) => (IStatementNode)new IfStatementNode(body.c, body.t, body.e, ifT.Line, ifT.Column, ifT.Comment)
		);

		var WhileStmt = WhileKwd.Then(LParen.KeepRight(Expr).KeepLeft(RParen).Then(Stmt, (c, b) => (c, b)),
			(wT, b) => (IStatementNode)new WhileStatementNode(b.c, b.b, wT.Line, wT.Column, wT.Comment));

		var DoWhileStmt = DoKwd.Then(Stmt.KeepLeft(WhileKwd).KeepLeft(LParen).Then(Expr, (b, c) => (b, c)).KeepLeft(RParen).KeepLeft(Semi),
			(dT, pair) => (IStatementNode)new DoWhileStatementNode(pair.b, pair.c, dT.Line, dT.Column, dT.Comment));

		var LoopControlOp = Match(TokenType.Break) | Match(TokenType.Continue);
		var LoopControlStmt = LoopControlOp.Then(LoopControlOp.Many(), (first, rest) => { rest.Insert(0, first); return (first, rest); })
			.KeepLeft(Semi).Map(tup => (IStatementNode)new LoopControlStatementNode(tup.rest.Select(o => o.Type).ToList(), tup.first.Line, tup.first.Column, tup.first.Comment));

		var RetStmt = RetKwd.Then(Expr, (retToken, expr) => (IStatementNode)new ReturnStatementNode(expr, retToken.Line, retToken.Column, retToken.Comment)).KeepLeft(Semi);

		var VarDecl = TypeKwd.Then(Ident, (t, id) => (t, id))
			.Then(Match(TokenType.Equal).KeepRight(Expr).Optional(), (tid, init) => (IStatementNode)new VariableDeclarationNode(TypeInfo.FromString(tid.t.Value), tid.id.Value, init, tid.t.Line, tid.t.Column, tid.t.Comment))
			.KeepLeft(Semi);

		var AssignStmt = Ident.Then(Match(TokenType.Equal).KeepRight(Expr), (id, expr) => (IStatementNode)new AssignmentStatementNode(id.Value, expr, id.Line, id.Column, id.Comment)).KeepLeft(Semi);

		Stmt.Inner = BlockStmt | IfStmt | WhileStmt | DoWhileStmt | LoopControlStmt | RetStmt | VarDecl | AssignStmt;

		return FunctionSignature.KeepLeft(LBrace).Then(Stmt.Many(), (sigAst, body) => new FunctionDeclarationNode(signatures[sigAst.Name], body)).KeepLeft(RBrace);
	}
}

public interface IAstNode { int Line { get; } int Column { get; } T Accept<T>(IAstVisitor<T> visitor); }

public record FunctionSignature(TypeInfo ReturnType, string Name, List<ParameterNode> Parameters, int Line, int Column, string Comment = null);
public record FunctionDeclarationNode(FunctionSignature Signature, List<IStatementNode> Body) : IAstNode { public int Line => Signature.Line; public int Column => Signature.Column; public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record ParameterNode(TypeInfo Type, string Name, int Line, int Column, string Comment = null) : IAstNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }

public interface IStatementNode : IAstNode { string Comment { get; } }
public record BlockStatementNode(List<IStatementNode> Statements, int Line, int Column, string Comment = null) : IStatementNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record ReturnStatementNode(IExpressionNode Expression, int Line, int Column, string Comment = null) : IStatementNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record VariableDeclarationNode(TypeInfo Type, string Name, IExpressionNode InitialValue, int Line, int Column, string Comment = null) : IStatementNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record AssignmentStatementNode(string Name, IExpressionNode Expression, int Line, int Column, string Comment = null) : IStatementNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record IfStatementNode(IExpressionNode Condition, IStatementNode ThenBranch, IStatementNode ElseBranch, int Line, int Column, string Comment = null) : IStatementNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record WhileStatementNode(IExpressionNode Condition, IStatementNode Body, int Line, int Column, string Comment = null) : IStatementNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record DoWhileStatementNode(IStatementNode Body, IExpressionNode Condition, int Line, int Column, string Comment = null) : IStatementNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record LoopControlStatementNode(List<TokenType> Actions, int Line, int Column, string Comment = null) : IStatementNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }

public interface IExpressionNode : IAstNode { }
public record ConstantExpressionNode(string Value, TypeInfo Type, int Line, int Column) : IExpressionNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record VariableExpressionNode(string Identifier, int Line, int Column) : IExpressionNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record BinaryOperationNode(IExpressionNode Left, TokenType Operator, IExpressionNode Right, int Line, int Column) : IExpressionNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record FunctionCallExpressionNode(string FunctionName, List<IExpressionNode> Arguments, int Line, int Column) : IExpressionNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record CastExpressionNode(TypeInfo TargetType, IExpressionNode Expression, int Line, int Column) : IExpressionNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }
public record UnaryOperationNode(TokenType Operator, IExpressionNode Expression, int Line, int Column) : IExpressionNode { public T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this); }

public interface IAstVisitor<out T>
{
	T Visit(FunctionDeclarationNode node); T Visit(ParameterNode node);
	T Visit(ReturnStatementNode node); T Visit(VariableDeclarationNode node); T Visit(AssignmentStatementNode node);
	T Visit(BlockStatementNode node); T Visit(IfStatementNode node); T Visit(WhileStatementNode node);
	T Visit(DoWhileStatementNode node); T Visit(LoopControlStatementNode node);
	T Visit(ConstantExpressionNode node); T Visit(VariableExpressionNode node); T Visit(BinaryOperationNode node);
	T Visit(FunctionCallExpressionNode node); T Visit(CastExpressionNode node); T Visit(UnaryOperationNode node);
}