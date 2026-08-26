using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SleddersLuaRuntime.Core
{
    internal static class SimpleJson
    {
        public static object? Deserialize(string? json)
        {
            if (json == null) return null;
            return new Parser(json).ParseDocument();
        }

        public static string Serialize(object? value, bool pretty)
        {
            var builder = new StringBuilder();
            WriteValue(builder, value, pretty, 0);
            return builder.ToString();
        }

        private static void WriteValue(StringBuilder builder, object? value, bool pretty, int depth)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            var text = value as string;
            if (text != null)
            {
                WriteString(builder, text);
                return;
            }

            if (value is bool)
            {
                builder.Append((bool)value ? "true" : "false");
                return;
            }

            if (IsNumber(value))
            {
                if (value is double doubleValue && (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue)))
                    throw new InvalidOperationException("JSON cannot represent NaN or infinity.");
                if (value is float floatValue && (float.IsNaN(floatValue) || float.IsInfinity(floatValue)))
                    throw new InvalidOperationException("JSON cannot represent NaN or infinity.");
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }

            var dictionary = value as IDictionary;
            if (dictionary != null)
            {
                WriteObject(builder, dictionary, pretty, depth);
                return;
            }

            var enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                WriteArray(builder, enumerable, pretty, depth);
                return;
            }

            WriteString(builder, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        private static void WriteObject(StringBuilder builder, IDictionary dictionary, bool pretty, int depth)
        {
            builder.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (!first) builder.Append(',');
                if (pretty)
                {
                    builder.AppendLine();
                    Indent(builder, depth + 1);
                }
                WriteString(builder, Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty);
                builder.Append(pretty ? ": " : ":");
                WriteValue(builder, entry.Value, pretty, depth + 1);
                first = false;
            }
            if (pretty && !first)
            {
                builder.AppendLine();
                Indent(builder, depth);
            }
            builder.Append('}');
        }

        private static void WriteArray(StringBuilder builder, IEnumerable values, bool pretty, int depth)
        {
            builder.Append('[');
            bool first = true;
            foreach (object? item in values)
            {
                if (!first) builder.Append(',');
                if (pretty)
                {
                    builder.AppendLine();
                    Indent(builder, depth + 1);
                }
                WriteValue(builder, item, pretty, depth + 1);
                first = false;
            }
            if (pretty && !first)
            {
                builder.AppendLine();
                Indent(builder, depth);
            }
            builder.Append(']');
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 32)
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }

        private static void Indent(StringBuilder builder, int depth)
        {
            builder.Append(' ', depth * 2);
        }

        private static bool IsNumber(object value)
        {
            TypeCode code = Type.GetTypeCode(value.GetType());
            return code == TypeCode.Byte || code == TypeCode.SByte || code == TypeCode.Int16 || code == TypeCode.UInt16 ||
                   code == TypeCode.Int32 || code == TypeCode.UInt32 || code == TypeCode.Int64 || code == TypeCode.UInt64 ||
                   code == TypeCode.Single || code == TypeCode.Double || code == TypeCode.Decimal;
        }

        private sealed class Parser
        {
            private readonly string _json;
            private int _index;

            public Parser(string json)
            {
                _json = json;
            }

            public object? ParseDocument()
            {
                object? value = ParseValue();
                SkipWhitespace();
                if (_index != _json.Length)
                    throw new FormatException("Unexpected trailing data in JSON.");
                return value;
            }

            public object? ParseValue()
            {
                SkipWhitespace();
                if (_index >= _json.Length) return null;
                char c = _json[_index];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't') { ConsumeLiteral("true"); return true; }
                if (c == 'f') { ConsumeLiteral("false"); return false; }
                if (c == 'n') { ConsumeLiteral("null"); return null; }
                return ParseNumber();
            }

            private Dictionary<string, object?> ParseObject()
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                Expect('{');
                SkipWhitespace();
                if (TryConsume('}')) return result;
                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    result[key] = ParseValue();
                    SkipWhitespace();
                    if (TryConsume('}')) break;
                    Expect(',');
                }
                return result;
            }

            private List<object?> ParseArray()
            {
                var result = new List<object?>();
                Expect('[');
                SkipWhitespace();
                if (TryConsume(']')) return result;
                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (TryConsume(']')) break;
                    Expect(',');
                }
                return result;
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (_index < _json.Length)
                {
                    char c = _json[_index++];
                    if (c == '"') return builder.ToString();
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }
                    if (_index >= _json.Length) throw new FormatException("Invalid JSON escape.");
                    c = _json[_index++];
                    switch (c)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _json.Length) throw new FormatException("Invalid JSON unicode escape.");
                            string hex = _json.Substring(_index, 4);
                            builder.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _index += 4;
                            break;
                        default: throw new FormatException("Invalid JSON escape.");
                    }
                }
                throw new FormatException("Unterminated JSON string.");
            }

            private object ParseNumber()
            {
                int start = _index;
                if (_json[_index] == '-') _index++;
                while (_index < _json.Length && char.IsDigit(_json[_index])) _index++;
                if (_index < _json.Length && _json[_index] == '.')
                {
                    _index++;
                    while (_index < _json.Length && char.IsDigit(_json[_index])) _index++;
                }
                if (_index < _json.Length && (_json[_index] == 'e' || _json[_index] == 'E'))
                {
                    _index++;
                    if (_index < _json.Length && (_json[_index] == '+' || _json[_index] == '-')) _index++;
                    while (_index < _json.Length && char.IsDigit(_json[_index])) _index++;
                }
                string token = _json.Substring(start, _index - start);
                double value;
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    throw new FormatException("Invalid JSON number: " + token);
                return value;
            }

            private void ConsumeLiteral(string literal)
            {
                if (_index + literal.Length > _json.Length || string.Compare(_json, _index, literal, 0, literal.Length, StringComparison.Ordinal) != 0)
                    throw new FormatException("Invalid JSON literal.");
                _index += literal.Length;
            }

            private void Expect(char expected)
            {
                SkipWhitespace();
                if (_index >= _json.Length || _json[_index] != expected)
                    throw new FormatException("Expected '" + expected + "' in JSON.");
                _index++;
            }

            private bool TryConsume(char expected)
            {
                SkipWhitespace();
                if (_index < _json.Length && _json[_index] == expected)
                {
                    _index++;
                    return true;
                }
                return false;
            }

            private void SkipWhitespace()
            {
                while (_index < _json.Length && char.IsWhiteSpace(_json[_index])) _index++;
            }
        }
    }
}
