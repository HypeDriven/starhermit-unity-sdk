using System;
using NUnit.Framework;
using Starhermit.Json;

namespace Starhermit.Tests
{
    /// <summary>
    /// The JSON layer is hand-written precisely so managed stripping cannot break it, which means its
    /// correctness is entirely on these tests rather than on a library's reputation.
    /// </summary>
    [TestFixture]
    public class JsonTests
    {
        [Test]
        public void Parse_NestedDocument_ReadsEveryKind()
        {
            var json = JsonParser.Parse("{\"a\":1,\"b\":\"two\",\"c\":true,\"d\":null,\"e\":[1,2],\"f\":{\"g\":1.5}}");

            Assert.AreEqual(JsonKind.Object, json.Kind);
            Assert.AreEqual(1, json["a"].AsInt32());
            Assert.AreEqual("two", json["b"].AsString());
            Assert.IsTrue(json["c"].AsBoolean());
            Assert.IsTrue(json["d"].IsNull);
            Assert.AreEqual(2, json["e"].AsArray().Count);
            Assert.AreEqual(1.5d, json["f"]["g"].AsDouble(), 0.0001);
        }

        [Test]
        public void Parse_AbsentMember_IsMissingNotNull()
        {
            var json = JsonParser.Parse("{\"present\":null}");

            Assert.IsTrue(json["present"].IsNull, "an explicit null is null");
            Assert.IsFalse(json["present"].IsMissing, "an explicit null is not missing");
            Assert.IsTrue(json["absent"].IsMissing, "an absent member is missing");
            Assert.IsTrue(json["absent"].IsNullOrMissing);
        }

        [Test]
        public void Parse_LargeInteger_KeepsExactValue()
        {
            // A 64-bit id read through a double loses the low bits: this is the WebGL JSON hazard the
            // number handling exists to avoid.
            var json = JsonParser.Parse("{\"id\":9007199254740993}");

            Assert.AreEqual(9007199254740993L, json["id"].AsInt64());
        }

        [Test]
        public void Parse_EscapesAndSurrogatePairs_RoundTrip()
        {
            var json = JsonParser.Parse("{\"text\":\"line\\nbreak \\\"quoted\\\" \\u00e9 \\ud83d\\ude00\"}");
            var text = json["text"].AsString();

            Assert.AreEqual("line\nbreak \"quoted\" é 😀", text);

            var round = JsonParser.Parse(JsonWriter.SerializeObject(w => w.Write("text", text)));
            Assert.AreEqual(text, round["text"].AsString(), "escaping must survive a round trip");
        }

        [Test]
        public void Parse_UnknownMembers_ArePreserved()
        {
            var json = JsonParser.Parse("{\"known\":1,\"shippedLater\":{\"nested\":true}}");

            Assert.IsTrue(json["shippedLater"]["nested"].AsBoolean(),
                "a member the SDK does not map must still be readable");
            StringAssert.Contains("shippedLater", json.ToJson());
        }

        [Test]
        public void Parse_DeeplyNested_IsRefusedAtTheDepthLimit()
        {
            var deep = new string('[', 200) + new string(']', 200);

            Assert.Throws<StarhermitSerializationException>(() => JsonParser.Parse(deep));
        }

        [Test]
        public void Parse_Malformed_Throws()
        {
            Assert.Throws<StarhermitSerializationException>(() => JsonParser.Parse("{\"a\":}"));
            Assert.Throws<StarhermitSerializationException>(() => JsonParser.Parse("{\"a\":1"));
            Assert.Throws<StarhermitSerializationException>(() => JsonParser.Parse("{\"a\":1}trailing"));
            Assert.Throws<StarhermitSerializationException>(() => JsonParser.Parse("{\"a\":01x}"));
        }

        [Test]
        public void TryParse_NonJsonBody_ReportsFailureWithoutThrowing()
        {
            Assert.IsFalse(JsonParser.TryParse("<html>gateway error</html>", out _));
            Assert.IsFalse(JsonParser.TryParse(null, out _));
        }

        [Test]
        public void AsString_OnWrongKind_ThrowsSerializationError()
        {
            var json = JsonParser.Parse("{\"n\":5}");

            Assert.Throws<StarhermitSerializationException>(() => json["n"].AsString());
            Assert.Throws<StarhermitSerializationException>(() => json["missing"].AsString());
        }

        [Test]
        public void AsArray_OnAbsentMember_ReadsAsEmpty()
        {
            var json = JsonParser.Parse("{}");

            Assert.AreEqual(0, json["items"].AsArray().Count,
                "a collection the server omitted reads as empty, not as an error");
        }

        [Test]
        public void AsDateTimeOffset_NormalisesToUtc()
        {
            var json = JsonParser.Parse("{\"at\":\"2026-08-20T14:30:00+02:00\"}");
            var value = json["at"].AsDateTimeOffset();

            Assert.AreEqual(TimeSpan.Zero, value.Offset);
            Assert.AreEqual(12, value.Hour);
        }

        [Test]
        public void Writer_ProducesValidJsonForEveryMemberHelper()
        {
            var id = Guid.NewGuid();
            var at = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

            var text = JsonWriter.SerializeObject(writer =>
            {
                writer.Write("s", "text");
                writer.Write("b", true);
                writer.Write("i", 42L);
                writer.Write("d", 1.25d);
                writer.Write("g", id);
                writer.Write("t", at);
                writer.WriteIfPresent("skipped", (string?)null);
                writer.WriteArray("list", new[] { 1, 2, 3 }, (w, v) => w.WriteNumber(v));
            });

            var json = JsonParser.Parse(text);
            Assert.AreEqual("text", json["s"].AsString());
            Assert.IsTrue(json["b"].AsBoolean());
            Assert.AreEqual(42, json["i"].AsInt32());
            Assert.AreEqual(1.25d, json["d"].AsDouble(), 0.0001);
            Assert.AreEqual(id, json["g"].AsGuid());
            Assert.AreEqual(at, json["t"].AsDateTimeOffset());
            Assert.IsTrue(json["skipped"].IsMissing, "a null optional member is omitted entirely");
            Assert.AreEqual(3, json["list"].AsArray().Count);
        }

        [Test]
        public void Optional_DistinguishesUnsetFromExplicitNull()
        {
            var unset = Optional<string>.Unset;
            var explicitNull = Optional<string>.Set(null!);
            var value = Optional<string>.Set("name");

            Assert.IsFalse(unset.IsSet);
            Assert.IsTrue(explicitNull.IsSet);
            Assert.IsNull(explicitNull.Value);
            Assert.AreEqual("name", value.Value);
            Assert.Throws<InvalidOperationException>(() => _ = unset.Value);
        }
    }
}
