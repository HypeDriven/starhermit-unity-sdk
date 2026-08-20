using System;
using System.Collections.Generic;
using System.Text;

namespace Starhermit.Json
{
    /// <summary>
    /// A hand-written recursive-descent JSON reader.
    /// </summary>
    /// <remarks>
    /// The SDK parses every payload itself rather than binding one of Unity's serializers: JsonUtility
    /// cannot express dictionaries, optional members or unknown fields, and a reflection-based reader
    /// is exactly what managed stripping breaks. Parsing is bounded by a maximum nesting depth so a
    /// hostile or corrupt payload cannot exhaust the stack.
    /// </remarks>
    public static class JsonParser
    {
        /// <summary>Nesting depth accepted by default.</summary>
        public const int DefaultMaxDepth = 64;

        /// <summary>Parses JSON text.</summary>
        /// <param name="text">The JSON document.</param>
        /// <param name="maxDepth">Maximum accepted nesting depth.</param>
        /// <returns>The parsed value.</returns>
        /// <exception cref="StarhermitSerializationException">The text is not valid JSON.</exception>
        public static JsonValue Parse(string text, int maxDepth = DefaultMaxDepth)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var state = new Cursor(text, maxDepth);
            state.SkipWhitespace();
            var value = state.ReadValue(0);
            state.SkipWhitespace();
            if (!state.AtEnd) throw state.Error("Unexpected trailing content after the JSON value.");
            return value;
        }

        /// <summary>Parses UTF-8 encoded JSON bytes.</summary>
        /// <param name="utf8">The JSON document as UTF-8 bytes.</param>
        /// <param name="maxDepth">Maximum accepted nesting depth.</param>
        /// <returns>The parsed value.</returns>
        public static JsonValue Parse(byte[] utf8, int maxDepth = DefaultMaxDepth)
        {
            if (utf8 == null) throw new ArgumentNullException(nameof(utf8));
            return Parse(DecodeUtf8(utf8, 0, utf8.Length), maxDepth);
        }

        /// <summary>Parses a region of UTF-8 encoded JSON bytes.</summary>
        /// <param name="utf8">Buffer holding the document.</param>
        /// <param name="offset">Start of the document within the buffer.</param>
        /// <param name="count">Length of the document in bytes.</param>
        /// <param name="maxDepth">Maximum accepted nesting depth.</param>
        /// <returns>The parsed value.</returns>
        public static JsonValue Parse(byte[] utf8, int offset, int count, int maxDepth = DefaultMaxDepth)
        {
            if (utf8 == null) throw new ArgumentNullException(nameof(utf8));
            return Parse(DecodeUtf8(utf8, offset, count), maxDepth);
        }

        /// <summary>
        /// Attempts to parse JSON text, reporting failure instead of throwing. Used where a payload is
        /// advisory - an error body that may not be JSON at all, for instance.
        /// </summary>
        /// <param name="text">The candidate JSON document.</param>
        /// <param name="value">The parsed value on success.</param>
        /// <returns>True when the text parsed.</returns>
        public static bool TryParse(string? text, out JsonValue value)
        {
            value = JsonValue.Missing;
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                value = Parse(text!);
                return true;
            }
            catch (StarhermitSerializationException)
            {
                return false;
            }
        }

        private static string DecodeUtf8(byte[] utf8, int offset, int count)
        {
            // A UTF-8 BOM is legal in the wild and is not part of the document.
            if (count >= 3 && utf8[offset] == 0xEF && utf8[offset + 1] == 0xBB && utf8[offset + 2] == 0xBF)
            {
                offset += 3;
                count -= 3;
            }

            return Encoding.UTF8.GetString(utf8, offset, count);
        }

        private struct Cursor
        {
            private readonly string _text;
            private readonly int _maxDepth;
            private int _position;

            internal Cursor(string text, int maxDepth)
            {
                _text = text;
                _maxDepth = maxDepth < 1 ? 1 : maxDepth;
                _position = 0;
            }

            internal bool AtEnd => _position >= _text.Length;

            internal StarhermitSerializationException Error(string message) =>
                new StarhermitSerializationException($"{message} (at offset {_position})");

            internal void SkipWhitespace()
            {
                while (_position < _text.Length)
                {
                    var c = _text[_position];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _position++;
                    else break;
                }
            }

            internal JsonValue ReadValue(int depth)
            {
                if (depth > _maxDepth) throw Error("JSON nesting is deeper than the configured limit.");
                if (AtEnd) throw Error("Unexpected end of JSON.");

                switch (_text[_position])
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return JsonValue.String(ReadString());
                    case 't': Expect("true"); return JsonValue.True;
                    case 'f': Expect("false"); return JsonValue.False;
                    case 'n': Expect("null"); return JsonValue.Null;
                    default: return JsonValue.RawNumber(ReadNumber());
                }
            }

            private JsonValue ReadObject(int depth)
            {
                _position++; // '{'
                var members = new List<KeyValuePair<string, JsonValue>>();
                SkipWhitespace();
                if (!AtEnd && _text[_position] == '}')
                {
                    _position++;
                    return JsonValue.Object(members);
                }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_position] != '"') throw Error("Expected a member name.");
                    var name = ReadString();
                    SkipWhitespace();
                    if (AtEnd || _text[_position] != ':') throw Error("Expected ':' after a member name.");
                    _position++;
                    SkipWhitespace();
                    members.Add(new KeyValuePair<string, JsonValue>(name, ReadValue(depth + 1)));
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unexpected end of JSON inside an object.");
                    var c = _text[_position++];
                    if (c == ',') continue;
                    if (c == '}') return JsonValue.Object(members);
                    throw Error("Expected ',' or '}' in an object.");
                }
            }

            private JsonValue ReadArray(int depth)
            {
                _position++; // '['
                var items = new List<JsonValue>();
                SkipWhitespace();
                if (!AtEnd && _text[_position] == ']')
                {
                    _position++;
                    return JsonValue.Array(items);
                }

                while (true)
                {
                    SkipWhitespace();
                    items.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unexpected end of JSON inside an array.");
                    var c = _text[_position++];
                    if (c == ',') continue;
                    if (c == ']') return JsonValue.Array(items);
                    throw Error("Expected ',' or ']' in an array.");
                }
            }

            private string ReadString()
            {
                _position++; // opening quote
                var start = _position;
                StringBuilder? builder = null;

                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string.");
                    var c = _text[_position];

                    if (c == '"')
                    {
                        var result = builder == null
                            ? _text.Substring(start, _position - start)
                            : builder.Append(_text, start, _position - start).ToString();
                        _position++;
                        return result;
                    }

                    if (c == '\\')
                    {
                        builder ??= new StringBuilder();
                        builder.Append(_text, start, _position - start);
                        _position++;
                        if (AtEnd) throw Error("Unterminated escape sequence.");
                        var escape = _text[_position++];
                        switch (escape)
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
                                if (_position + 4 > _text.Length) throw Error("Truncated \\u escape.");
                                var code = 0;
                                for (var i = 0; i < 4; i++)
                                {
                                    var digit = HexValue(_text[_position + i]);
                                    if (digit < 0) throw Error("Invalid \\u escape.");
                                    code = (code << 4) | digit;
                                }

                                _position += 4;
                                // Surrogate pairs arrive as two escapes; keeping them paired is what makes
                                // astral-plane characters (emoji in chat, for one) survive a round trip.
                                builder.Append((char)code);
                                break;
                            default: throw Error($"Unsupported escape '\\{escape}'.");
                        }

                        start = _position;
                        continue;
                    }

                    if (c < ' ') throw Error("Unescaped control character in a string.");
                    _position++;
                }
            }

            private string ReadNumber()
            {
                var start = _position;
                if (!AtEnd && (_text[_position] == '-' || _text[_position] == '+')) _position++;
                var digits = 0;
                while (!AtEnd && _text[_position] >= '0' && _text[_position] <= '9')
                {
                    _position++;
                    digits++;
                }

                if (!AtEnd && _text[_position] == '.')
                {
                    _position++;
                    while (!AtEnd && _text[_position] >= '0' && _text[_position] <= '9')
                    {
                        _position++;
                        digits++;
                    }
                }

                if (!AtEnd && (_text[_position] == 'e' || _text[_position] == 'E'))
                {
                    _position++;
                    if (!AtEnd && (_text[_position] == '-' || _text[_position] == '+')) _position++;
                    var exponentDigits = 0;
                    while (!AtEnd && _text[_position] >= '0' && _text[_position] <= '9')
                    {
                        _position++;
                        exponentDigits++;
                    }

                    if (exponentDigits == 0) throw Error("Malformed exponent.");
                }

                if (digits == 0) throw Error("Expected a JSON value.");
                return _text.Substring(start, _position - start);
            }

            private void Expect(string literal)
            {
                if (_position + literal.Length > _text.Length ||
                    string.CompareOrdinal(_text, _position, literal, 0, literal.Length) != 0)
                {
                    throw Error($"Expected '{literal}'.");
                }

                _position += literal.Length;
            }

            private static int HexValue(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }
        }
    }
}
