using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2ItemToolkit
{
    /// <summary>
    /// The capture format, as System.Text.Json options rather than a hand-written reader.
    ///
    /// The producer's keys are already the lowerCamelCase of the C# member names, so
    /// <see cref="JsonNamingPolicy.CamelCase"/> maps every one of them without a single
    /// attribute — `unitType`, `classId`, `itemFlags`, `statsLists`, `gfxIndex`, `flagsEx`.
    ///
    /// The absent-is-not-zero members need no converter either: the serializer only assigns
    /// properties that are PRESENT, so <see cref="Unit"/>'s constructor defaults survive. That is
    /// what makes an absent `flagsEx` mean expansion rather than classic, and an absent `classId`
    /// mean "no such row" rather than row 0.
    ///
    /// Two things do need converters, and both are in this file.
    /// </summary>
    internal static class UnitJson
    {
        internal static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

            // The document carries fields we do not model — a wearer's `name`, the manager's
            // rendering hints. An item's grid position is NOT among them: it is modelled as
            // Location and X. Strictness about VALUES is worth having; strictness about
            // unknown MEMBERS would reject every real capture.
            //
            // Note this is the default, and is stated only because the opposite would break the
            // producer's document immediately.
            UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement,
        };

        internal static Unit Read(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Object
                ? element.Deserialize<Unit>(Options)
                : new Unit();
        }

        internal static Unit Read(string json)
        {
            if (json == null) throw new ArgumentNullException("json");

            // Parsed through JsonDocument first so a non-object root takes the SAME path as the
            // JsonElement overload. Deserialising the string directly would throw on `5` or `[]`
            // while the element overload returned a default unit — two entry points to the same
            // reader disagreeing about the same document.
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                return Read(document.RootElement);
            }
        }

        /// <summary>
        /// Re-emits ANY <see cref="IUnit"/>, not just a <see cref="Unit"/> — serialised against the
        /// interface so a custom implementation writes the same document.
        /// </summary>
        internal static string Write(IUnit unit)
        {
            if (unit == null) throw new ArgumentNullException("unit");

            return JsonSerializer.Serialize(unit, Options);
        }
    }

    /// <summary>
    /// A wearer's MERGED stat list. Exists so the narrowing below applies to this nesting alone.
    ///
    /// Attaching <see cref="Int32NarrowingConverter"/> to <c>UnitStat.Value</c> would be wrong:
    /// <see cref="UnitStat"/> is the element type of BOTH this list and
    /// <see cref="UnitStatList.Stats"/>, and the two have opposite rules. A merged value is
    /// deliberately widened by the producer and must be narrowed back; a per-statlist value is
    /// genuinely int32, so one outside the range is malformed and should not be silently wrapped
    /// into a plausible number.
    /// </summary>
    internal sealed class MergedStatsConverter : JsonConverter<List<UnitStat>>
    {
        public override List<UnitStat> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var stats = new List<UnitStat>();

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Expected an array of merged stats.");
            }

            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                stats.Add(ReadStat(ref reader));
            }

            return stats;
        }

        private static UnitStat ReadStat(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected a merged stat object.");
            }

            var stat = new UnitStat();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string name = reader.GetString();
                reader.Read();

                if (name == ItemStatKeys.StatId)
                {
                    stat.Id = reader.GetInt32();
                }
                else if (name == ItemStatKeys.StatValue)
                {
                    stat.Value = Int32NarrowingConverter.Narrow(ref reader);
                }
                else if (name == ItemStatKeys.StatLayer)
                {
                    stat.Layer = reader.GetInt32();
                }
                else
                {
                    reader.Skip();
                }
            }

            return stat;
        }

        public override void Write(
            Utf8JsonWriter writer, List<UnitStat> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (UnitStat stat in value)
            {
                writer.WriteStartObject();
                writer.WriteNumber(ItemStatKeys.StatId, stat.Id);
                writer.WriteNumber(ItemStatKeys.StatValue, stat.Value);
                if (stat.Layer != 0)
                {
                    writer.WriteNumber(ItemStatKeys.StatLayer, stat.Layer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// A stat value, narrowed to the game's own int32.
    ///
    /// The game stores every stat as int32, but a producer serialising an UNSIGNED one has to
    /// widen it or emit a negative — experience at level 99 is ~3.52 billion, past int.MaxValue
    /// and inside uint.MaxValue. Plain int32 deserialisation THROWS on that. Narrowing unchecked
    /// restores the exact 32 bits the game holds, so the round trip is lossless for every value
    /// the game can actually store.
    /// </summary>
    internal sealed class Int32NarrowingConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return Narrow(ref reader);
        }

        /// <summary>The same narrowing, callable without a converter instance or options.</summary>
        internal static int Narrow(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException("Expected a number for a stat value.");
            }

            int narrow;
            if (reader.TryGetInt32(out narrow))
            {
                return narrow;
            }

            long wide;
            if (reader.TryGetInt64(out wide))
            {
                return unchecked((int)wide);
            }

            ulong unsignedWide;
            if (reader.TryGetUInt64(out unsignedWide))
            {
                return unchecked((int)unsignedWide);
            }

            throw new JsonException("Stat value is not an integer.");
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

    /// <summary>
    /// One affix triple, always exactly <see cref="Unit.MaxAffixSlots"/> long.
    ///
    /// The game struct is wMagicPrefix[3]. Nothing in the engine requires three — ReadIdentity
    /// clamps — but a consumer reading <see cref="Unit.MagicPrefix"/> directly would run off the
    /// end of a document that sent fewer, so this pads. A document that sends more is truncated
    /// rather than trusted: a fourth slot is not a slot the game has.
    /// </summary>
    internal sealed class AffixTripleConverter : JsonConverter<List<int>>
    {
        public override List<int> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var slots = new List<int>(new int[Unit.MaxAffixSlots]);

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Expected an array for an affix triple.");
            }

            int index = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (index < slots.Count)
                {
                    slots[index] = reader.GetInt32();
                }

                ++index;
            }

            return slots;
        }

        public override void Write(
            Utf8JsonWriter writer, List<int> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (int slot in value)
            {
                writer.WriteNumberValue(slot);
            }

            writer.WriteEndArray();
        }
    }
}
