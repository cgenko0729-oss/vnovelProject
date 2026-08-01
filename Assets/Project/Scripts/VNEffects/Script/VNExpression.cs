using System;
using System.Collections.Generic;
using System.Globalization;

namespace VNEffects
{
    /// <summary>
    /// VN 条件表达式解析器。整数以 0/非 0 表示假/真；只读取 VNFlags，不产生副作用。
    /// </summary>
    public static class VNExpression
    {
        public static bool TryEvaluate(
            string expression,
            Func<string, int> valueResolver,
            out bool result,
            out string error)
        {
            result = false;
            if (valueResolver == null)
            {
                error = "没有提供变量读取器";
                return false;
            }

            var parser = new Parser(expression, valueResolver, null);
            if (!parser.TryParse(true, out long value, out error)) return false;
            result = value != 0;
            return true;
        }

        public static bool TryValidate(string expression, out string error)
        {
            var parser = new Parser(expression, _ => 0, null);
            return parser.TryParse(false, out _, out error);
        }

        public static bool TryCollectIdentifiers(
            string expression,
            ICollection<string> identifiers,
            out string error)
        {
            if (identifiers == null)
            {
                error = "没有提供变量集合";
                return false;
            }

            var parser = new Parser(expression, _ => 0, name => identifiers.Add(name));
            return parser.TryParse(false, out _, out error);
        }

        sealed class Parser
        {
            readonly string _source;
            readonly Func<string, int> _resolver;
            readonly Action<string> _identifierVisitor;
            int _position;
            string _error;

            public Parser(
                string source,
                Func<string, int> resolver,
                Action<string> identifierVisitor)
            {
                _source = source ?? string.Empty;
                _resolver = resolver;
                _identifierVisitor = identifierVisitor;
            }

            public bool TryParse(bool evaluate, out long value, out string error)
            {
                value = 0;
                _position = 0;
                _error = null;
                SkipWhitespace();
                if (_position >= _source.Length)
                {
                    error = "表达式不能为空";
                    return false;
                }

                try
                {
                    value = ParseOr(evaluate);
                    SkipWhitespace();
                    if (_error == null && _position < _source.Length)
                        Fail($"无法识别的内容「{_source.Substring(_position)}」");
                }
                catch (OverflowException)
                {
                    Fail("整数运算溢出");
                }

                error = _error;
                return _error == null;
            }

            long ParseOr(bool evaluate)
            {
                long left = ParseAnd(evaluate);
                while (_error == null && Match("||"))
                {
                    bool leftTrue = left != 0;
                    long right = ParseAnd(evaluate && !leftTrue);
                    if (evaluate) left = leftTrue || right != 0 ? 1 : 0;
                }
                return left;
            }

            long ParseAnd(bool evaluate)
            {
                long left = ParseComparison(evaluate);
                while (_error == null && Match("&&"))
                {
                    bool leftTrue = left != 0;
                    long right = ParseComparison(evaluate && leftTrue);
                    if (evaluate) left = leftTrue && right != 0 ? 1 : 0;
                }
                return left;
            }

            long ParseComparison(bool evaluate)
            {
                long left = ParseAdd(evaluate);
                string op = MatchAny(">=", "<=", "==", "!=", ">", "<");
                if (op == null) return left;

                long right = ParseAdd(evaluate);
                if (!evaluate) return 0;
                switch (op)
                {
                    case ">=": return left >= right ? 1 : 0;
                    case "<=": return left <= right ? 1 : 0;
                    case "==": return left == right ? 1 : 0;
                    case "!=": return left != right ? 1 : 0;
                    case ">": return left > right ? 1 : 0;
                    default: return left < right ? 1 : 0;
                }
            }

            long ParseAdd(bool evaluate)
            {
                long left = ParseMultiply(evaluate);
                while (_error == null)
                {
                    string op = MatchAny("+", "-");
                    if (op == null) break;
                    long right = ParseMultiply(evaluate);
                    if (!evaluate) continue;
                    left = op == "+" ? checked(left + right) : checked(left - right);
                }
                return left;
            }

            long ParseMultiply(bool evaluate)
            {
                long left = ParseUnary(evaluate);
                while (_error == null)
                {
                    string op = MatchAny("*", "/", "%");
                    if (op == null) break;
                    long right = ParseUnary(evaluate);
                    if (!evaluate) continue;
                    if ((op == "/" || op == "%") && right == 0)
                    {
                        Fail(op == "/" ? "不能除以 0" : "不能对 0 取余");
                        return 0;
                    }
                    switch (op)
                    {
                        case "*": left = checked(left * right); break;
                        case "/": left = checked(left / right); break;
                        default: left = checked(left % right); break;
                    }
                }
                return left;
            }

            long ParseUnary(bool evaluate)
            {
                if (Match("!"))
                {
                    long value = ParseUnary(evaluate);
                    return evaluate ? (value == 0 ? 1 : 0) : 0;
                }
                if (Match("+")) return ParseUnary(evaluate);
                if (Match("-"))
                {
                    long value = ParseUnary(evaluate);
                    return evaluate ? checked(-value) : 0;
                }
                return ParsePrimary(evaluate);
            }

            long ParsePrimary(bool evaluate)
            {
                SkipWhitespace();
                if (Match("("))
                {
                    long value = ParseOr(evaluate);
                    if (!Match(")")) Fail("缺少右括号 )");
                    return value;
                }

                if (_position >= _source.Length)
                {
                    Fail("表达式意外结束");
                    return 0;
                }

                int start = _position;
                if (char.IsDigit(_source[_position]))
                {
                    while (_position < _source.Length && char.IsDigit(_source[_position]))
                        _position++;
                    string number = _source.Substring(start, _position - start);
                    if (!long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture,
                            out long value))
                    {
                        Fail($"整数「{number}」超出范围", start);
                        return 0;
                    }
                    return evaluate ? value : 0;
                }

                while (_position < _source.Length && !IsIdentifierDelimiter(_source[_position]))
                    _position++;
                if (_position == start)
                {
                    Fail($"这里不能使用字符「{_source[_position]}」");
                    _position++;
                    return 0;
                }

                string name = _source.Substring(start, _position - start);
                _identifierVisitor?.Invoke(name);
                return evaluate ? _resolver(name) : 0;
            }

            static bool IsIdentifierDelimiter(char c)
            {
                return char.IsWhiteSpace(c) || c == '(' || c == ')' || c == '!' ||
                       c == '<' || c == '>' || c == '=' || c == '&' || c == '|' ||
                       c == '+' || c == '-' || c == '*' || c == '/' || c == '%';
            }

            string MatchAny(params string[] candidates)
            {
                foreach (string candidate in candidates)
                    if (Match(candidate)) return candidate;
                return null;
            }

            bool Match(string token)
            {
                SkipWhitespace();
                if (_position + token.Length > _source.Length) return false;
                if (string.CompareOrdinal(_source, _position, token, 0, token.Length) != 0)
                    return false;
                _position += token.Length;
                return true;
            }

            void SkipWhitespace()
            {
                while (_position < _source.Length && char.IsWhiteSpace(_source[_position]))
                    _position++;
            }

            void Fail(string message, int position = -1)
            {
                if (_error != null) return;
                int column = (position >= 0 ? position : _position) + 1;
                _error = $"第 {column} 列：{message}";
            }
        }
    }
}
