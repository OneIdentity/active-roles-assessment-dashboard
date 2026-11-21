using System.Globalization;
using System.Text;

namespace ActiveRolesDashboard.Models.Definitions.Evaluation;

/// <summary>
/// Parser for the definition expression language: a small, side-effect free language used in
/// KPI filters. It supports literals (strings, numbers, true/false/null), attribute names,
/// <c>$parameters</c>, comparison (<c>== != &lt; &lt;= &gt; &gt;=</c>), logic (<c>&amp;&amp; || !</c>),
/// arithmetic (<c>+ - * /</c>), parentheses and calls to built-in functions only.
/// Attribute names may contain letters, digits, <c>-</c> and <c>_</c> (e.g. <c>msDS-SiteName</c>);
/// quote other names with backticks.
/// </summary>
public static class ExpressionParser
{
    public static Expr Parse(string text)
    {
        var tokens = Tokenize(text);
        var parser = new Parser(tokens, text);
        var expr = parser.ParseOr();
        parser.Expect(TokenKind.End);
        return expr;
    }

    private enum TokenKind { Identifier, Parameter, String, Number, Operator, LParen, RParen, Comma, End }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            var start = i;
            if (c is '\'' or '"')
            {
                var sb = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= text.Length) throw new ExpressionException($"Unterminated string at {start}.");
                    if (text[i] == '\\' && i + 1 < text.Length) { sb.Append(text[i + 1]); i += 2; continue; }
                    if (text[i] == c) { i++; break; }
                    sb.Append(text[i++]);
                }
                tokens.Add(new Token(TokenKind.String, sb.ToString(), start));
            }
            else if (c == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end < 0) throw new ExpressionException($"Unterminated quoted name at {start}.");
                tokens.Add(new Token(TokenKind.Identifier, text[(i + 1)..end], start));
                i = end + 1;
            }
            else if (char.IsDigit(c))
            {
                if (c == '0' && i + 1 < text.Length && (text[i + 1] is 'x' or 'X'))
                {
                    i += 2;
                    while (i < text.Length && Uri.IsHexDigit(text[i])) i++;
                    var hex = long.Parse(text[(start + 2)..i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    tokens.Add(new Token(TokenKind.Number, hex.ToString(CultureInfo.InvariantCulture), start));
                }
                else
                {
                    while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                    tokens.Add(new Token(TokenKind.Number, text[start..i], start));
                }
            }
            else if (c == '$' || char.IsLetter(c) || c == '_')
            {
                i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '-' or '.')) i++;
                var word = text[start..i];
                tokens.Add(c == '$'
                    ? new Token(TokenKind.Parameter, word[1..], start)
                    : new Token(TokenKind.Identifier, word, start));
            }
            else if (c == '(') { tokens.Add(new Token(TokenKind.LParen, "(", i++)); }
            else if (c == ')') { tokens.Add(new Token(TokenKind.RParen, ")", i++)); }
            else if (c == ',') { tokens.Add(new Token(TokenKind.Comma, ",", i++)); }
            else
            {
                var two = i + 1 < text.Length ? text.Substring(i, 2) : string.Empty;
                if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||")
                {
                    tokens.Add(new Token(TokenKind.Operator, two, i));
                    i += 2;
                }
                else if (c is '<' or '>' or '!' or '+' or '-' or '*' or '/')
                {
                    tokens.Add(new Token(TokenKind.Operator, c.ToString(), i++));
                }
                else
                {
                    throw new ExpressionException($"Unexpected character '{c}' at {i}.");
                }
            }
        }
        tokens.Add(new Token(TokenKind.End, string.Empty, text.Length));
        return tokens;
    }

    private sealed class Parser(List<Token> tokens, string text)
    {
        private int _pos;

        private Token Current => tokens[_pos];

        public void Expect(TokenKind kind)
        {
            if (Current.Kind != kind)
                throw new ExpressionException($"Expected {kind} at {Current.Position} in '{text}' but found '{Current.Text}'.");
            _pos++;
        }

        private bool TryOperator(params string[] ops)
        {
            if (Current.Kind == TokenKind.Operator && ops.Contains(Current.Text))
            {
                _pos++;
                return true;
            }
            return false;
        }

        public Expr ParseOr()
        {
            var left = ParseAnd();
            while (TryOperator("||")) left = new BinaryExpr("||", left, ParseAnd());
            return left;
        }

        private Expr ParseAnd()
        {
            var left = ParseComparison();
            while (TryOperator("&&")) left = new BinaryExpr("&&", left, ParseComparison());
            return left;
        }

        private Expr ParseComparison()
        {
            var left = ParseAdditive();
            var op = Current.Text;
            if (TryOperator("==", "!=", "<", "<=", ">", ">="))
                left = new BinaryExpr(op, left, ParseAdditive());
            return left;
        }

        private Expr ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                var op = Current.Text;
                if (!TryOperator("+", "-")) return left;
                left = new BinaryExpr(op, left, ParseMultiplicative());
            }
        }

        private Expr ParseMultiplicative()
        {
            var left = ParseUnary();
            while (true)
            {
                var op = Current.Text;
                if (!TryOperator("*", "/")) return left;
                left = new BinaryExpr(op, left, ParseUnary());
            }
        }

        private Expr ParseUnary()
        {
            if (TryOperator("!")) return new UnaryExpr("!", ParseUnary());
            if (TryOperator("-")) return new UnaryExpr("-", ParseUnary());
            return ParsePrimary();
        }

        private Expr ParsePrimary()
        {
            var token = Current;
            switch (token.Kind)
            {
                case TokenKind.String:
                    _pos++;
                    return new LiteralExpr(token.Text);
                case TokenKind.Number:
                    _pos++;
                    return new LiteralExpr(double.Parse(token.Text, CultureInfo.InvariantCulture));
                case TokenKind.Parameter:
                    _pos++;
                    return new ParameterExpr(token.Text);
                case TokenKind.LParen:
                    _pos++;
                    var inner = ParseOr();
                    Expect(TokenKind.RParen);
                    return inner;
                case TokenKind.Identifier:
                    _pos++;
                    switch (token.Text)
                    {
                        case "true": return new LiteralExpr(true);
                        case "false": return new LiteralExpr(false);
                        case "null": return new LiteralExpr(null);
                    }
                    if (Current.Kind != TokenKind.LParen) return new AttributeExpr(token.Text);

                    _pos++;
                    var args = new List<Expr>();
                    if (Current.Kind != TokenKind.RParen)
                    {
                        args.Add(ParseOr());
                        while (Current.Kind == TokenKind.Comma)
                        {
                            _pos++;
                            args.Add(ParseOr());
                        }
                    }
                    Expect(TokenKind.RParen);
                    if (!BuiltInFunctions.IsKnown(token.Text, args.Count))
                        throw new ExpressionException($"Unknown function '{token.Text}' with {args.Count} argument(s) at {token.Position}.");
                    return new CallExpr(token.Text, args);
                default:
                    throw new ExpressionException($"Unexpected '{token.Text}' at {token.Position} in '{text}'.");
            }
        }
    }
}

public sealed class ExpressionException(string message) : Exception(message);

public abstract record Expr;

public sealed record LiteralExpr(object? Value) : Expr;

public sealed record AttributeExpr(string Name) : Expr;

public sealed record ParameterExpr(string Name) : Expr;

public sealed record UnaryExpr(string Operator, Expr Operand) : Expr;

public sealed record BinaryExpr(string Operator, Expr Left, Expr Right) : Expr;

public sealed record CallExpr(string Name, IReadOnlyList<Expr> Args) : Expr;
