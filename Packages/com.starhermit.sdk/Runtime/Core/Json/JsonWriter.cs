using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Starhermit.Json
{
    /// <summary>
    /// A minimal, allocation-conscious JSON writer that tracks structure so callers cannot emit a
    /// malformed document by forgetting a comma or a colon.
    /// </summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _builder;
        private readonly List<bool> _hasMembers = new List<bool>(8);
        private bool _expectingValue;

        /// <summary>Creates a writer that appends to <paramref name="builder"/>.</summary>
        /// <param name="builder">Destination for the JSON text.</param>
        public JsonWriter(StringBuilder builder)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        /// <summary>Builds a JSON document from a writing callback.</summary>
        /// <param name="write">Callback that writes exactly one JSON value.</param>
        /// <returns>The JSON text.</returns>
        public static string Serialize(Action<JsonWriter> write)
        {
            if (write == null) throw new ArgumentNullException(nameof(write));
            var builder = new StringBuilder(256);
            write(new JsonWriter(builder));
            return builder.ToString();
        }

        /// <summary>Builds a JSON object document from a writing callback.</summary>
        /// <param name="writeMembers">Callback that writes the object's members.</param>
        /// <returns>The JSON text of the object.</returns>
        public static string SerializeObject(Action<JsonWriter> writeMembers)
        {
            if (writeMembers == null) throw new ArgumentNullException(nameof(writeMembers));
            return Serialize(writer =>
            {
                writer.WriteStartObject();
                writeMembers(writer);
                writer.WriteEndObject();
            });
        }

        /// <summary>Starts an object.</summary>
        public void WriteStartObject()
        {
            BeginValue();
            _builder.Append('{');
            _hasMembers.Add(false);
        }

        /// <summary>Ends the current object.</summary>
        public void WriteEndObject()
        {
            if (_hasMembers.Count == 0) throw new InvalidOperationException("No object is open.");
            _hasMembers.RemoveAt(_hasMembers.Count - 1);
            _builder.Append('}');
        }

        /// <summary>Starts an array.</summary>
        public void WriteStartArray()
        {
            BeginValue();
            _builder.Append('[');
            _hasMembers.Add(false);
        }

        /// <summary>Ends the current array.</summary>
        public void WriteEndArray()
        {
            if (_hasMembers.Count == 0) throw new InvalidOperationException("No array is open.");
            _hasMembers.RemoveAt(_hasMembers.Count - 1);
            _builder.Append(']');
        }

        /// <summary>Writes a member name; the next write supplies its value.</summary>
        /// <param name="name">The wire name.</param>
        public void WritePropertyName(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            BeginValue();
            AppendEscaped(name);
            _builder.Append(':');
            _expectingValue = true;
        }

        /// <summary>Writes a JSON null.</summary>
        public void WriteNull()
        {
            BeginValue();
            _builder.Append("null");
        }

        /// <summary>Writes a boolean.</summary>
        /// <param name="value">The value to write.</param>
        public void WriteBoolean(bool value)
        {
            BeginValue();
            _builder.Append(value ? "true" : "false");
        }

        /// <summary>Writes a 64-bit integer.</summary>
        /// <param name="value">The value to write.</param>
        public void WriteNumber(long value)
        {
            BeginValue();
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Writes a double.</summary>
        /// <param name="value">The value to write. Must be finite.</param>
        public void WriteNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "JSON cannot represent NaN or infinity.");
            BeginValue();
            _builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>Writes a decimal.</summary>
        /// <param name="value">The value to write.</param>
        public void WriteNumber(decimal value)
        {
            BeginValue();
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Writes a string.</summary>
        /// <param name="value">The value to write. Null writes a JSON null.</param>
        public void WriteString(string? value)
        {
            if (value == null)
            {
                WriteNull();
                return;
            }

            BeginValue();
            AppendEscaped(value);
        }

        /// <summary>Writes a GUID in canonical string form.</summary>
        /// <param name="value">The value to write.</param>
        public void WriteGuid(Guid value) => WriteString(value.ToString("D", CultureInfo.InvariantCulture));

        /// <summary>Writes a timestamp as an ISO-8601 UTC string.</summary>
        /// <param name="value">The value to write.</param>
        public void WriteDateTimeOffset(DateTimeOffset value) =>
            WriteString(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));

        /// <summary>Writes an already-parsed JSON value verbatim.</summary>
        /// <param name="value">The value to write. Missing values are written as null.</param>
        public void WriteValue(JsonValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            switch (value.Kind)
            {
                case JsonKind.Missing:
                case JsonKind.Null:
                    WriteNull();
                    break;
                case JsonKind.Boolean:
                    WriteBoolean(value.AsBoolean());
                    break;
                case JsonKind.Number:
                    BeginValue();
                    _builder.Append(value.AsNumberText());
                    break;
                case JsonKind.String:
                    WriteString(value.AsString());
                    break;
                case JsonKind.Array:
                    WriteStartArray();
                    foreach (var item in value.Items) WriteValue(item);
                    WriteEndArray();
                    break;
                default:
                    WriteStartObject();
                    foreach (var member in value.Members)
                    {
                        WritePropertyName(member.Key);
                        WriteValue(member.Value);
                    }

                    WriteEndObject();
                    break;
            }
        }

        /// <summary>Writes a string member.</summary>
        /// <param name="name">Wire name.</param>
        /// <param name="value">Value; null writes a JSON null.</param>
        public void Write(string name, string? value)
        {
            WritePropertyName(name);
            WriteString(value);
        }

        /// <summary>Writes a boolean member.</summary>
        /// <param name="name">Wire name.</param>
        /// <param name="value">Value.</param>
        public void Write(string name, bool value)
        {
            WritePropertyName(name);
            WriteBoolean(value);
        }

        /// <summary>Writes an integer member.</summary>
        /// <param name="name">Wire name.</param>
        /// <param name="value">Value.</param>
        public void Write(string name, long value)
        {
            WritePropertyName(name);
            WriteNumber(value);
        }

        /// <summary>Writes a double member.</summary>
        /// <param name="name">Wire name.</param>
        /// <param name="value">Value.</param>
        public void Write(string name, double value)
        {
            WritePropertyName(name);
            WriteNumber(value);
        }

        /// <summary>Writes a GUID member.</summary>
        /// <param name="name">Wire name.</param>
        /// <param name="value">Value.</param>
        public void Write(string name, Guid value)
        {
            WritePropertyName(name);
            WriteGuid(value);
        }

        /// <summary>Writes a timestamp member in UTC.</summary>
        /// <param name="name">Wire name.</param>
        /// <param name="value">Value.</param>
        public void Write(string name, DateTimeOffset value)
        {
            WritePropertyName(name);
            WriteDateTimeOffset(value);
        }

        /// <summary>Writes a member holding an already-parsed JSON value.</summary>
        /// <param name="name">Wire name.</param>
        /// <param name="value">Value.</param>
        public void Write(string name, JsonValue value)
        {
            WritePropertyName(name);
            WriteValue(value);
        }

        /// <summary>Writes a member only when the value is not null, leaving it absent otherwise.</summary>
        /// <param name="name">Wire name.</param>
        /// <param name="value">Value; when null nothing is written at all.</param>
        public void WriteIfPresent(string name, string? value)
        {
            if (value != null) Write(name, value);
        }

        /// <summary>Writes a nullable boolean member only when it has a value.</summary>
        /// <param name="name">Wire name.</param>
        /// <param name="value">Value.</param>
        public void WriteIfPresent(string name, bool? value)
        {
            if (value.HasValue) Write(name, value.Value);
        }

        /// <summary>Writes a nullable integer member only when it has a value.</summary>
        /// <param name="name">Wire name.</param>
        /// <param name="value">Value.</param>
        public void WriteIfPresent(string name, long? value)
        {
            if (value.HasValue) Write(name, value.Value);
        }

        /// <summary>Writes a nullable double member only when it has a value.</summary>
        /// <param name="name">Wire name.</param>
        /// <param name="value">Value.</param>
        public void WriteIfPresent(string name, double? value)
        {
            if (value.HasValue) Write(name, value.Value);
        }

        /// <summary>Writes a nullable GUID member only when it has a value.</summary>
        /// <param name="name">Wire name.</param>
        /// <param name="value">Value.</param>
        public void WriteIfPresent(string name, Guid? value)
        {
            if (value.HasValue) Write(name, value.Value);
        }

        /// <summary>Writes a nullable timestamp member only when it has a value.</summary>
        /// <param name="name">Wire name.</param>
        /// <param name="value">Value.</param>
        public void WriteIfPresent(string name, DateTimeOffset? value)
        {
            if (value.HasValue) Write(name, value.Value);
        }

        /// <summary>Writes an array member from a sequence, using a per-element callback.</summary>
        /// <typeparam name="T">Element type.</typeparam>
        /// <param name="name">Wire name.</param>
        /// <param name="items">Elements to write.</param>
        /// <param name="writeItem">Callback writing one element.</param>
        public void WriteArray<T>(string name, IEnumerable<T> items, Action<JsonWriter, T> writeItem)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (writeItem == null) throw new ArgumentNullException(nameof(writeItem));
            WritePropertyName(name);
            WriteStartArray();
            foreach (var item in items) writeItem(this, item);
            WriteEndArray();
        }

        private void BeginValue()
        {
            if (_expectingValue)
            {
                _expectingValue = false;
                return;
            }

            var depth = _hasMembers.Count;
            if (depth == 0) return;
            if (_hasMembers[depth - 1]) _builder.Append(',');
            else _hasMembers[depth - 1] = true;
        }

        private void AppendEscaped(string value)
        {
            _builder.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': _builder.Append("\\\""); break;
                    case '\\': _builder.Append("\\\\"); break;
                    case '\b': _builder.Append("\\b"); break;
                    case '\f': _builder.Append("\\f"); break;
                    case '\n': _builder.Append("\\n"); break;
                    case '\r': _builder.Append("\\r"); break;
                    case '\t': _builder.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            _builder.Append("\\u");
                            _builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            _builder.Append(c);
                        }

                        break;
                }
            }

            _builder.Append('"');
        }
    }
}
