using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace InfinityOfficialNetwork.Languages.IXX;
public class TokenStream
{
	public List<Token> Tokens { get; }
	public int Cursor { get; set; }
	public int FurthestCursor { get; private set; }
	public Diagnostic FurthestError { get; private set; }

	public TokenStream(IEnumerable<Token> tokens) { Tokens = tokens.ToList(); Cursor = 0; }
	public Token Peek() => Cursor < Tokens.Count ? Tokens[Cursor] : Tokens.Last();
	public Token Consume() => Cursor < Tokens.Count ? Tokens[Cursor++] : Tokens.Last();
	public int Checkpoint() => Cursor;
	public void Restore(int cp) => Cursor = cp;

	public void ResetFurthestError() { FurthestCursor = Cursor; FurthestError = null; }

	public void RecordError(string code, string error, int line, int column)
	{
		if (Cursor >= FurthestCursor)
		{
			FurthestCursor = Cursor;
			FurthestError = new Diagnostic(code, error, line, column);
		}
	}
}

public class Lexer
{
	public IEnumerable<Token> Tokenize(string source, DiagnosticBag diagnostics)
	{
		var tokens = new List<Token>();
		var patterns = new (TokenType Type, string Pattern)[] {
				(TokenType.TypeKeyword, @"\b(i8|i16|i32|i64|ui8|ui16|ui32|ui64|f32|f64|byte|bool|void)\b"),
				(TokenType.BooleanLiteral, @"\b(true|false)\b"), (TokenType.Return, @"\breturn\b"),
				(TokenType.If, @"\bif\b"), (TokenType.Else, @"\belse\b"), (TokenType.While, @"\bwhile\b"),
				(TokenType.Do, @"\bdo\b"), (TokenType.Break, @"\bbreak\b"), (TokenType.Continue, @"\bcontinue\b"),
				(TokenType.Identifier, @"[a-zA-Z_][a-zA-Z0-9_]*"), (TokenType.Number, @"[0-9]+(\.[0-9]+)?f?"),
				(TokenType.LParen, @"\("), (TokenType.RParen, @"\)"), (TokenType.LBrace, @"\{"), (TokenType.RBrace, @"\}"),
				(TokenType.Comma, @","), (TokenType.Semicolon, @";"), (TokenType.Plus, @"\+"), (TokenType.Minus, @"-"),
				(TokenType.Star, @"\*"), (TokenType.Slash, @"/"), (TokenType.DoubleAmpersand, @"&&"), (TokenType.DoubleBar, @"\|\|"),
				(TokenType.DoubleEqual, @"=="), (TokenType.NotEqual, @"!="), (TokenType.LessEqual, @"<="), (TokenType.GreaterEqual, @">="),
				(TokenType.LessThan, @"<"), (TokenType.GreaterThan, @">"),
				(TokenType.Ampersand, @"&"), (TokenType.Bar, @"\|"), (TokenType.Caret, @"\^"), (TokenType.Tilde, @"~"),
				(TokenType.Exclamation, @"!"), (TokenType.Equal, @"="),
			};

		int pos = 0, line = 1, col = 1;
		StringBuilder currentComment = new StringBuilder();

		void Advance(int n)
		{
			for (int i = 0; i < n; i++) { if (source[pos + i] == '\n') { line++; col = 1; } else { col++; } }
			pos += n;
		}

		while (pos < source.Length)
		{
			if (char.IsWhiteSpace(source[pos])) { Advance(1); continue; }
			if (pos + 1 < source.Length && source[pos] == '/' && source[pos + 1] == '/')
			{
				int end = source.IndexOf('\n', pos + 2);
				int len = end == -1 ? source.Length - pos : end - pos;
				currentComment.AppendLine(source.Substring(pos, len).Trim());
				Advance(len); continue;
			}
			if (pos + 1 < source.Length && source[pos] == '/' && source[pos + 1] == '*')
			{
				int end = source.IndexOf("*/", pos + 2);
				int len = end == -1 ? source.Length - pos : end + 2 - pos;
				currentComment.AppendLine(source.Substring(pos, len).Trim());
				Advance(len); continue;
			}

			bool matchFound = false;
			foreach (var (type, pattern) in patterns)
			{
				var match = Regex.Match(source.Substring(pos), "^" + pattern);
				if (match.Success)
				{
					string cmtStr = currentComment.Length > 0 ? currentComment.ToString().TrimEnd() : null;
					currentComment.Clear();
					tokens.Add(new Token(type, match.Value, line, col, cmtStr));
					Advance(match.Length);
					matchFound = true;
					break;
				}
			}

			if (!matchFound) { diagnostics.Report("LEX0001", $"Unexpected character '{source[pos]}'", line, col); Advance(1); }
		}

		tokens.Add(new Token(TokenType.EOF, string.Empty, line, col, currentComment.Length > 0 ? currentComment.ToString().TrimEnd() : null));
		return tokens;
	}
}