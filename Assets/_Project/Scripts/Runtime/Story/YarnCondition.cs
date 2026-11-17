using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectVita.Story
{
    /// <summary>
    /// CSV의 조건 칸(unlock_if, show_if 등)을 Yarn 변수로 판정한다. Yarn의 &lt;&lt;if&gt;&gt;와 같은 모양으로 쓴다.
    /// 예: $met_stranger / not $door_open / $day >= 2 / $a and ($b or $c) / $name == "앨리스"
    /// 빈 칸은 항상 참이다. 선언되지 않은 변수는 false(0, "")로 본다.
    /// </summary>
    public static class YarnCondition
    {
        public static bool Evaluate(string expression, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(expression)) return true;
            var storage = GameVariables.Storage;
            try
            {
                // 저장소에 값이 없으면 Yarn의 <<declare>> 초기값을 쓴다 (TryGetValue가 둘 다 본다).
                var parser = new Parser(Tokenize(expression), name =>
                {
                    try
                    {
                        if (storage.TryGetValue<bool>(name, out var b)) return b;
                        if (storage.TryGetValue<float>(name, out var f)) return f;
                        if (storage.TryGetValue<string>(name, out var s)) return s;
                    }
                    catch (InvalidOperationException)
                    {
                        // Yarn Program이 아직 연결되지 않았다. 저장된 값이 없으면 false로 본다.
                    }
                    return false;
                });
                var result = parser.ParseExpression();
                parser.ExpectEnd();
                return Truthy(result);
            }
            catch (FormatException e)
            {
                error = $"조건 '{expression}'을 읽지 못했다: {e.Message}";
                return false;
            }
        }

        /// <summary>문법만 검사한다 (임포트 때 쓴다). 변수는 모두 false로 본다.</summary>
        public static bool TryCheckSyntax(string expression, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(expression)) return true;
            try
            {
                var parser = new Parser(Tokenize(expression), _ => false);
                parser.ParseExpression();
                parser.ExpectEnd();
                return true;
            }
            catch (FormatException e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>조건에 쓰인 $변수 이름들 (검사, 선언 생성용).</summary>
        public static IEnumerable<string> Variables(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) yield break;
            List<string> tokens;
            try { tokens = Tokenize(expression); }
            catch (FormatException) { yield break; }
            foreach (var t in tokens)
                if (t.StartsWith("$")) yield return t;
        }

        static bool Truthy(object v) => v switch
        {
            bool b => b,
            float f => Math.Abs(f) > float.Epsilon,
            string s => s.Length > 0,
            _ => false,
        };

        // ------------------------------------------------------------ 토큰

        static List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (c == '$' || char.IsLetter(c) || c == '_')
                {
                    int start = i++;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.')) i++;
                    tokens.Add(text.Substring(start, i - start));
                    continue;
                }
                if (char.IsDigit(c) || (c == '-' && i + 1 < text.Length && char.IsDigit(text[i + 1]) && PreviousIsOperator(tokens)))
                {
                    int start = i++;
                    while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                    tokens.Add(text.Substring(start, i - start));
                    continue;
                }
                if (c == '"')
                {
                    var sb = new StringBuilder("\"");
                    i++;
                    while (i < text.Length && text[i] != '"') sb.Append(text[i++]);
                    if (i >= text.Length) throw new FormatException("따옴표가 닫히지 않았다");
                    i++;
                    tokens.Add(sb.ToString());
                    continue;
                }
                string two = i + 1 < text.Length ? text.Substring(i, 2) : null;
                if (two is "==" or "!=" or ">=" or "<=" or "&&" or "||")
                {
                    tokens.Add(two);
                    i += 2;
                    continue;
                }
                if (c is '>' or '<' or '!' or '(' or ')')
                {
                    tokens.Add(c.ToString());
                    i++;
                    continue;
                }
                throw new FormatException($"'{c}'를 모른다");
            }
            return tokens;
        }

        static bool PreviousIsOperator(List<string> tokens) =>
            tokens.Count == 0 || tokens[tokens.Count - 1] is "(" or "==" or "!=" or ">=" or "<=" or ">" or "<" or "and" or "or" or "not" or "&&" or "||" or "!";

        // ------------------------------------------------------------ 파서 (재귀 하강)

        sealed class Parser
        {
            readonly List<string> tokens;
            readonly Func<string, object> lookup;
            int pos;

            public Parser(List<string> tokens, Func<string, object> lookup)
            {
                this.tokens = tokens;
                this.lookup = lookup;
            }

            string Peek => pos < tokens.Count ? tokens[pos] : null;
            string Next() => tokens[pos++];

            bool Accept(params string[] options)
            {
                if (Peek == null) return false;
                foreach (var o in options)
                    if (string.Equals(Peek, o, StringComparison.OrdinalIgnoreCase))
                    {
                        pos++;
                        return true;
                    }
                return false;
            }

            public void ExpectEnd()
            {
                if (Peek != null) throw new FormatException($"'{Peek}' 근처를 읽지 못했다");
            }

            public object ParseExpression() => ParseOr();

            object ParseOr()
            {
                var left = ParseAnd();
                while (Accept("or", "||"))
                {
                    var right = ParseAnd();
                    left = Truthy(left) || Truthy(right);
                }
                return left;
            }

            object ParseAnd()
            {
                var left = ParseNot();
                while (Accept("and", "&&"))
                {
                    var right = ParseNot();
                    left = Truthy(left) && Truthy(right);
                }
                return left;
            }

            object ParseNot()
            {
                if (Accept("not", "!")) return !Truthy(ParseNot());
                return ParseComparison();
            }

            object ParseComparison()
            {
                var left = ParsePrimary();
                string op = Peek?.ToLowerInvariant();
                switch (op)
                {
                    case "==": case "is": case "eq": case "!=": case "neq":
                    case ">": case "gt": case "<": case "lt": case ">=": case "gte": case "<=": case "lte":
                        pos++;
                        return Compare(left, op, ParsePrimary());
                }
                return left;
            }

            object ParsePrimary()
            {
                if (Peek == null) throw new FormatException("식이 중간에 끝났다");
                if (Accept("("))
                {
                    var v = ParseExpression();
                    if (!Accept(")")) throw new FormatException("괄호가 닫히지 않았다");
                    return v;
                }
                string t = Next();
                if (t.StartsWith("$")) return lookup(t);
                if (t.StartsWith("\"")) return t.Substring(1);
                if (t.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (t.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
                if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return f;
                throw new FormatException($"'{t}'를 모른다 (변수는 $로 시작한다)");
            }

            static object Compare(object a, string op, object b)
            {
                if (a is float fa && b is float fb)
                {
                    return op switch
                    {
                        "==" or "is" or "eq" => Math.Abs(fa - fb) < 0.0001f,
                        "!=" or "neq" => Math.Abs(fa - fb) >= 0.0001f,
                        ">" or "gt" => fa > fb,
                        "<" or "lt" => fa < fb,
                        ">=" or "gte" => fa >= fb,
                        _ => fa <= fb,
                    };
                }
                bool equal = Equals(a, b) || (a is bool ba && b is bool bb && ba == bb);
                return op switch
                {
                    "==" or "is" or "eq" => equal,
                    "!=" or "neq" => !equal,
                    _ => throw new FormatException("크기 비교는 숫자끼리만 할 수 있다"),
                };
            }
        }
    }
}
