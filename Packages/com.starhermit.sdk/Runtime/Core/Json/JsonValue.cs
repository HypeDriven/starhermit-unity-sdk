using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Starhermit.Json
{
    /// <summary>
    /// An immutable JSON value: the SDK's whole wire representation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every response is parsed into this tree and then read by hand-written codecs. Nothing in the
    /// SDK maps JSON onto CLR types by reflection, so IL2CPP with high managed stripping cannot
    /// silently remove a member the wire format needs, and remote JSON can never name a type to
    /// activate.
    /// </para>
    /// <para>
    /// The tree keeps everything the server sent, including members no SDK version knows about, so a
    /// model can expose its untouched source through <c>RawJson</c> and forward-compatible callers can
    /// read fields that shipped after the SDK did. Numbers keep their exact source text: a 64-bit id
    /// that would lose precision as a <c>double</c> - the WebGL JSON hazard - round-trips unharmed.
    /// </para>
    /// <para>
    /// A member that was absent is <see cref="Missing"/> rather than <see cref="Null"/>, which is what
    /// lets <see cref="Optional{T}"/> distinguish "not sent" from "explicitly null" when building a
    /// PATCH body.
    /// </para>
    /// </remarks>
    public sealed class JsonValue
    {
        private readonly JsonKind _kind;
        private readonly bool _boolean;
        private readonly string? _text;
        private readonly IReadOnlyList<JsonValue>? _items;
        private readonly IReadOnlyList<KeyValuePair<string, JsonValue>>? _members;
        private Dictionary<string, int>? _index;

        /// <summary>A member or element that was not present in the payload.</summary>
        public static readonly JsonValue Missing = new JsonValue(JsonKind.Missing, false, null, null, null);

        /// <summary>The JSON <c>null</c> literal.</summary>
        public static readonly JsonValue Null = new JsonValue(JsonKind.Null, false, null, null, null);

        /// <summary>The JSON <c>true</c> literal.</summary>
        public static readonly JsonValue True = new JsonValue(JsonKind.Boolean, true, null, null, null);

        /// <summary>The JSON <c>false</c> literal.</summary>
        public static readonly JsonValue False = new JsonValue(JsonKind.Boolean, false, null, null, null);

        /// <summary>An empty JSON object.</summary>
        public static readonly JsonValue EmptyObject = Object(new KeyValuePair<string, JsonValue>[0]);

        /// <summary>An empty JSON array.</summary>
        public static readonly JsonValue EmptyArray = Array(new JsonValue[0]);

        private JsonValue(
            JsonKind kind,
            bool boolean,
            string? text,
            IReadOnlyList<JsonValue>? items,
            IReadOnlyList<KeyValuePair<string, JsonValue>>? members)
        {
            _kind = kind;
            _boolean = boolean;
            _text = text;
            _items = items;
            _members = members;
        }

        /// <summary>The JSON type of this value.</summary>
        public JsonKind Kind => _kind;

        /// <summary>True when the member was absent from the payload.</summary>
        public bool IsMissing => _kind == JsonKind.Missing;

        /// <summary>True when this value is the explicit <c>null</c> literal.</summary>
        public bool IsNull => _kind == JsonKind.Null;

        /// <summary>True when the member was absent or explicitly null - the usual "no value" test.</summary>
        public bool IsNullOrMissing => _kind == JsonKind.Missing || _kind == JsonKind.Null;

        /// <summary>True when this value is a JSON object.</summary>
        public bool IsObject => _kind == JsonKind.Object;

        /// <summary>True when this value is a JSON array.</summary>
        public bool IsArray => _kind == JsonKind.Array;

        /// <summary>Creates a JSON string value.</summary>
        /// <param name="value">The string contents.</param>
        public static JsonValue String(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new JsonValue(JsonKind.String, false, value, null, null);
        }

        /// <summary>Creates a JSON boolean value.</summary>
        /// <param name="value">The boolean contents.</param>
        public static JsonValue Boolean(bool value) => value ? True : False;

        /// <summary>Creates a JSON number from a 64-bit integer, preserving its exact value.</summary>
        /// <param name="value">The integer contents.</param>
        public static JsonValue Number(long value) =>
            new JsonValue(JsonKind.Number, false, value.ToString(CultureInfo.InvariantCulture), null, null);

        /// <summary>Creates a JSON number from a double.</summary>
        /// <param name="value">The numeric contents. Must be finite.</param>
        public static JsonValue Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "JSON cannot represent NaN or infinity.");
            return new JsonValue(JsonKind.Number, false, value.ToString("R", CultureInfo.InvariantCulture), null, null);
        }

        /// <summary>Creates a JSON number from a decimal.</summary>
        /// <param name="value">The numeric contents.</param>
        public static JsonValue Number(decimal value) =>
            new JsonValue(JsonKind.Number, false, value.ToString(CultureInfo.InvariantCulture), null, null);

        /// <summary>Creates a JSON number directly from its source text, as the parser does.</summary>
        /// <param name="rawText">Number text that is already valid JSON.</param>
        public static JsonValue RawNumber(string rawText)
        {
            if (rawText == null) throw new ArgumentNullException(nameof(rawText));
            return new JsonValue(JsonKind.Number, false, rawText, null, null);
        }

        /// <summary>Creates a JSON array.</summary>
        /// <param name="items">The elements, in order.</param>
        public static JsonValue Array(IReadOnlyList<JsonValue> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            return new JsonValue(JsonKind.Array, false, null, items, null);
        }

        /// <summary>Creates a JSON object.</summary>
        /// <param name="members">The members, in the order they should be written.</param>
        public static JsonValue Object(IReadOnlyList<KeyValuePair<string, JsonValue>> members)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            return new JsonValue(JsonKind.Object, false, null, null, members);
        }

        /// <summary>The members of this object, in payload order. Empty for every other kind.</summary>
        public IReadOnlyList<KeyValuePair<string, JsonValue>> Members =>
            _members ?? (IReadOnlyList<KeyValuePair<string, JsonValue>>)System.Array.Empty<KeyValuePair<string, JsonValue>>();

        /// <summary>The elements of this array, in order. Empty for every other kind.</summary>
        public IReadOnlyList<JsonValue> Items =>
            _items ?? (IReadOnlyList<JsonValue>)System.Array.Empty<JsonValue>();

        /// <summary>Element count for an array, member count for an object, and zero otherwise.</summary>
        public int Count => _items?.Count ?? _members?.Count ?? 0;

        /// <summary>
        /// Looks up an object member. Returns <see cref="Missing"/> when this value is not an object or
        /// has no such member, so reading an optional field never needs a guard.
        /// </summary>
        /// <param name="name">The wire name of the member.</param>
        public JsonValue this[string name]
        {
            get
            {
                if (_members == null || name == null) return Missing;
                var index = _index;
                if (index == null)
                {
                    index = new Dictionary<string, int>(_members.Count, StringComparer.Ordinal);
                    for (var i = 0; i < _members.Count; i++) index[_members[i].Key] = i;
                    _index = index;
                }

                return index.TryGetValue(name, out var position) ? _members[position].Value : Missing;
            }
        }

        /// <summary>Looks up an array element. Returns <see cref="Missing"/> when out of range.</summary>
        /// <param name="index">Zero-based element index.</param>
        public JsonValue this[int index] =>
            _items != null && index >= 0 && index < _items.Count ? _items[index] : Missing;

        /// <summary>Reads a string value.</summary>
        /// <exception cref="StarhermitSerializationException">The value is not a string.</exception>
        public string AsString() =>
            _kind == JsonKind.String
                ? _text!
                : throw Mismatch(JsonKind.String);

        /// <summary>Reads a string value, or null when the member is absent or null.</summary>
        /// <exception cref="StarhermitSerializationException">The value is present but not a string.</exception>
        public string? AsStringOrNull() => IsNullOrMissing ? null : AsString();

        /// <summary>Reads a boolean value.</summary>
        /// <exception cref="StarhermitSerializationException">The value is not a boolean.</exception>
        public bool AsBoolean() =>
            _kind == JsonKind.Boolean ? _boolean : throw Mismatch(JsonKind.Boolean);

        /// <summary>Reads a boolean, falling back to <paramref name="fallback"/> when absent or null.</summary>
        /// <param name="fallback">Value to use when the member is absent or null.</param>
        public bool AsBooleanOrDefault(bool fallback = false) => IsNullOrMissing ? fallback : AsBoolean();

        /// <summary>Reads a nullable boolean.</summary>
        public bool? AsBooleanOrNull() => IsNullOrMissing ? (bool?)null : AsBoolean();

        /// <summary>The exact source text of a number.</summary>
        /// <exception cref="StarhermitSerializationException">The value is not a number.</exception>
        public string AsNumberText() =>
            _kind == JsonKind.Number ? _text! : throw Mismatch(JsonKind.Number);

        /// <summary>Reads a 32-bit integer.</summary>
        public int AsInt32()
        {
            var text = AsNumberText();
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
            return checked((int)AsInt64());
        }

        /// <summary>Reads a 64-bit integer without going through <c>double</c>, so large ids stay exact.</summary>
        public long AsInt64()
        {
            var text = AsNumberText();
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var asDecimal))
                return (long)decimal.Truncate(asDecimal);
            throw new StarhermitSerializationException($"'{text}' is not an integer.");
        }

        /// <summary>Reads a double.</summary>
        public double AsDouble()
        {
            var text = AsNumberText();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return value;
            throw new StarhermitSerializationException($"'{text}' is not a number.");
        }

        /// <summary>Reads a decimal.</summary>
        public decimal AsDecimal()
        {
            var text = AsNumberText();
            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return value;
            return (decimal)AsDouble();
        }

        /// <summary>Reads a 32-bit integer, falling back when absent or null.</summary>
        /// <param name="fallback">Value to use when the member is absent or null.</param>
        public int AsInt32OrDefault(int fallback = 0) => IsNullOrMissing ? fallback : AsInt32();

        /// <summary>Reads a 64-bit integer, falling back when absent or null.</summary>
        /// <param name="fallback">Value to use when the member is absent or null.</param>
        public long AsInt64OrDefault(long fallback = 0) => IsNullOrMissing ? fallback : AsInt64();

        /// <summary>Reads a nullable 32-bit integer.</summary>
        public int? AsInt32OrNull() => IsNullOrMissing ? (int?)null : AsInt32();

        /// <summary>Reads a nullable 64-bit integer.</summary>
        public long? AsInt64OrNull() => IsNullOrMissing ? (long?)null : AsInt64();

        /// <summary>Reads a nullable double.</summary>
        public double? AsDoubleOrNull() => IsNullOrMissing ? (double?)null : AsDouble();

        /// <summary>Reads a nullable decimal.</summary>
        public decimal? AsDecimalOrNull() => IsNullOrMissing ? (decimal?)null : AsDecimal();

        /// <summary>Reads a GUID written in any canonical string form.</summary>
        public Guid AsGuid()
        {
            var text = AsString();
            if (Guid.TryParse(text, out var value)) return value;
            throw new StarhermitSerializationException($"'{text}' is not a GUID.");
        }

        /// <summary>Reads a nullable GUID. An empty string is treated as absent.</summary>
        public Guid? AsGuidOrNull()
        {
            if (IsNullOrMissing) return null;
            var text = AsString();
            if (text.Length == 0) return null;
            if (Guid.TryParse(text, out var value)) return value;
            throw new StarhermitSerializationException($"'{text}' is not a GUID.");
        }

        /// <summary>Reads an ISO-8601 timestamp and normalises it to UTC.</summary>
        public DateTimeOffset AsDateTimeOffset()
        {
            var text = AsString();
            if (DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var value))
            {
                return value.ToUniversalTime();
            }

            throw new StarhermitSerializationException($"'{text}' is not an ISO-8601 timestamp.");
        }

        /// <summary>Reads a nullable ISO-8601 timestamp in UTC.</summary>
        public DateTimeOffset? AsDateTimeOffsetOrNull() =>
            IsNullOrMissing ? (DateTimeOffset?)null : AsDateTimeOffset();

        /// <summary>Reads the array, throwing when the value is present but is not an array.</summary>
        /// <remarks>An absent or null member reads as an empty list, which is what callers want for
        /// collection members the server may omit.</remarks>
        public IReadOnlyList<JsonValue> AsArray()
        {
            if (IsNullOrMissing) return System.Array.Empty<JsonValue>();
            if (_kind != JsonKind.Array) throw Mismatch(JsonKind.Array);
            return _items!;
        }

        /// <summary>Projects each element of an array through <paramref name="read"/>.</summary>
        /// <typeparam name="T">The element type to produce.</typeparam>
        /// <param name="read">Converter applied to every element.</param>
        public IReadOnlyList<T> AsList<T>(Func<JsonValue, T> read)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            var source = AsArray();
            if (source.Count == 0) return System.Array.Empty<T>();
            var result = new T[source.Count];
            for (var i = 0; i < source.Count; i++) result[i] = read(source[i]);
            return result;
        }

        /// <summary>Reads an object as a string-keyed dictionary, preserving member order.</summary>
        /// <typeparam name="T">The value type to produce.</typeparam>
        /// <param name="read">Converter applied to every member value.</param>
        public IReadOnlyDictionary<string, T> AsDictionary<T>(Func<JsonValue, T> read)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            if (IsNullOrMissing) return new Dictionary<string, T>(0);
            if (_kind != JsonKind.Object) throw Mismatch(JsonKind.Object);
            var result = new Dictionary<string, T>(_members!.Count, StringComparer.Ordinal);
            foreach (var member in _members!) result[member.Key] = read(member.Value);
            return result;
        }

        /// <summary>Requires an object, so a codec can fail loudly on a shape it cannot read.</summary>
        public JsonValue RequireObject() =>
            _kind == JsonKind.Object ? this : throw Mismatch(JsonKind.Object);

        /// <summary>Serialises this value back to compact JSON text.</summary>
        public string ToJson()
        {
            var builder = new StringBuilder(256);
            var writer = new JsonWriter(builder);
            writer.WriteValue(this);
            return builder.ToString();
        }

        /// <summary>Returns the JSON text of this value.</summary>
        public override string ToString() => IsMissing ? "<missing>" : ToJson();

        private StarhermitSerializationException Mismatch(JsonKind expected) =>
            new StarhermitSerializationException(
                _kind == JsonKind.Missing
                    ? $"Expected a JSON {expected.ToString().ToLowerInvariant()} but the member was absent."
                    : $"Expected a JSON {expected.ToString().ToLowerInvariant()} but found {_kind.ToString().ToLowerInvariant()}.");
    }
}
