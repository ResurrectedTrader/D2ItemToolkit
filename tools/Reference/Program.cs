using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace D2ItemToolkit.Tools
{
    /// <summary>
    /// Emits, for each record in a corpus, exactly what the C# engine renders. The TypeScript test
    /// suite replays the same corpus and compares, so any divergence is attributable to a single
    /// record rather than to the implementation as a whole.
    ///
    /// Usage:
    ///   Reference &lt;corpus.json&gt; &lt;out.json&gt;
    ///
    /// The corpus is an array of cases; each case is `{ "name", "record", "player"? }` where
    /// `record` and `player` are unit documents in the capture format.
    /// </summary>
    public static class Program
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemTypeTree Types = new ItemTypeTree(Data.ItemTypes);

        private static readonly SetTable Sets = new SetTable(
            Data.Sets, Data.SetItems, Data.Strings);

        private static readonly SocketStatSynthesis SocketStats =
            new SocketStatSynthesis(Data, Items, Types);

        private static void AddInto(
            IDictionary<int, int> into, IEnumerable<KeyValuePair<int, int>> from)
        {
            foreach (KeyValuePair<int, int> stat in from)
            {
                int existing;
                into[stat.Key] = into.TryGetValue(stat.Key, out existing)
                    ? existing + stat.Value
                    : stat.Value;
            }
        }

        public static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: Reference <corpus.json> <out.json>");
                return 2;
            }

            using (JsonDocument corpus = JsonDocument.Parse(File.ReadAllText(args[0])))
            {
                var results = new List<string>();

                foreach (JsonElement testCase in corpus.RootElement.EnumerateArray())
                {
                    results.Add(Render(testCase));
                }

                File.WriteAllText(args[1], "[\n  " + string.Join(",\n  ", results) + "\n]\n");
            }

            return 0;
        }

        /// <summary>
        /// One case, emitted as a JSON object. Intermediate views are included as well as the final
        /// string: when the two implementations disagree, knowing WHICH view diverged localises the
        /// fault immediately instead of leaving the whole pipeline suspect.
        /// </summary>
        private static string Render(JsonElement testCase)
        {
            string name = testCase.GetProperty("name").GetString();

            var payload = new StringBuilder();
            payload.Append("{ \"name\": ").Append(Quote(name));

            try
            {
                // Parsed INSIDE the try. Reading the record is now a throwing operation — a
                // malformed document raises JsonException rather than falling back — and outside
                // the try that escapes the per-case loop and aborts the entire reference run, so
                // one bad case would take the whole corpus with it instead of recording itself as
                // an error the differential can compare.
                Unit record = Unit.FromJson(testCase.GetProperty("record"));

                JsonElement playerDoc;
                Unit wearer = testCase.TryGetProperty("player", out playerDoc)
                    ? Unit.FromJson(playerDoc)
                    : null;

                ItemViewer viewer = wearer == null ? null : ItemRecordReader.ReadViewer(wearer);

                ItemIdentity item = ItemRecordReader.ReadIdentity(record);

                // Read BEFORE the socket synthesis: ITEM_RecalcAllEquippedItems 0x4c1350 throws an
                // equipped set item's fillers away (0x4c1658 / 0x4c1661), so `isEquipped` decides
                // whether there is a contribution at all and TooltipEngine.RenderSetItem passes it.
                SetItemTooltipInput setInput = ReadSetInput(testCase);

                SortedDictionary<int, int> stats =
                    ItemStatReader.ReconstructView(record, ItemStatView.Equipped());
                SortedDictionary<int, int> baseStats =
                    ItemStatReader.ReconstructView(record, ItemStatView.BaseOnly());
                SortedDictionary<int, int> modifierStats =
                    ItemStatReader.ReconstructView(record, ItemStatView.Modifiers());

                // Mirrors TooltipEngine.Compose: a captured gem or rune has no stat chain, so its
                // contribution is rebuilt from gems.txt. Omitting it here would leave the whole
                // synthesis outside the differential.
                SortedDictionary<int, int> synthesised =
                    SocketStats.Contributions(record, setInput.IsEquipped);
                AddInto(stats, synthesised);
                AddInto(modifierStats, synthesised);

                ItemStatOps.Resolve(stats, baseStats, Data.ItemStatCost);

                payload.Append(", \"views\": {")
                    .Append("\"equipped\": ").Append(Pack(stats))
                    .Append(", \"base\": ").Append(Pack(baseStats))
                    .Append(", \"modifiers\": ").Append(Pack(modifierStats))
                    .Append("}");

                var sections = new RecordSections(
                    Data, Items, Types, item, viewer, stats,
                    ItemStatReader.ReadSockets(record), baseStats,
                    ItemRecordReader.ReadSocketUnits(record));

                var composer = new ItemTooltipComposer(
                    sections, sections.CreateModifierGenerator(modifierStats));

                ItemTooltipContext context = sections.CreateContext();

                // Game state, not unit state, so it is carried on the case rather than derived.
                JsonElement shopMode;
                context.ShopMode = testCase.TryGetProperty("shopMode", out shopMode)
                    ? shopMode.GetInt32()
                    : 0;

                ItemTooltipKind kind = ItemTooltipComposer.Classify(context);

                payload.Append(", \"kind\": ").Append(Quote(kind.ToString()));

                IReadOnlyList<ItemTooltipLine> lines;
                int maxLength = ItemTooltipComposer.MaxTooltipLength;

                if (kind == ItemTooltipKind.IdentifiedSetItem)
                {
                    // The generic composer still REFUSES a set item, and that refusal is behaviour
                    // worth comparing — it used to be the only thing this corpus recorded for one.
                    payload.Append(", \"genericRefusal\": ")
                        .Append(Quote(Refusal(composer, context, modifierStats)));

                    var builder = new SetItemTooltipBuilder(Data, Sets, Items, Types);

                    SetItemTooltipContent content = builder.Build(
                        record, item, viewer, stats, setInput, wearer);

                    lines = content == null
                        ? new ItemTooltipLine[0]
                        : composer.ComposeSetItem(context, content, modifierStats);

                    payload.Append(", \"set\": ").Append(PackSetContent(content));

                    // 0x48db0b -> 0x48db1d with no length test: this path has no 1023 cut.
                    maxLength = ItemTooltipComposer.UnlimitedTooltipLength;
                }
                else
                {
                    lines = kind == ItemTooltipKind.Book
                        ? composer.ComposeBook(context)
                        : composer.Compose(context, modifierStats);
                }

                payload.Append(", \"sections\": ").Append(PackSections(sections));
                payload.Append(", \"lines\": ").Append(PackLines(lines));
                payload.Append(", \"rendered\": ")
                    .Append(Quote(composer.Render(lines, false, maxLength)));

                // Render drops every marker the composer would add, so on its own it leaves the
                // whole marker-placement rule outside the differential — `marker-ac-25` carries a
                // coloured Defense line directly above a block line and still could not tell the
                // two implementations apart.
                payload.Append(", \"colored\": ")
                    .Append(Quote(composer.RenderWithColorCodes(
                        lines, ItemTooltipColor.Marker, false, maxLength)));
            }
            catch (Exception e)
            {
                // A throw is itself observable behaviour worth comparing — Compose refuses a set
                // item and a book, and the TypeScript must refuse the same ones.
                payload.Append(", \"error\": ").Append(Quote(e.GetType().Name));
            }

            payload.Append(" }");
            return payload.ToString();
        }

        /// <summary>
        /// The generic Compose refuses a set item. Recorded per case so the refusal stays inside
        /// the differential now that set items render through their own writer.
        /// </summary>
        private static string Refusal(
            ItemTooltipComposer composer,
            ItemTooltipContext context,
            SortedDictionary<int, int> modifierStats)
        {
            try
            {
                composer.Compose(context, modifierStats);
                return "none";
            }
            catch (Exception e)
            {
                return e.GetType().Name;
            }
        }

        /// <summary>
        /// The optional `set` object of a corpus case — everything ITEM_BuildSetItemTooltip needs
        /// that the item document cannot say.
        /// </summary>
        private static SetItemTooltipInput ReadSetInput(JsonElement testCase)
        {
            var input = new SetItemTooltipInput();

            JsonElement set;
            if (!testCase.TryGetProperty("set", out set))
            {
                return input;
            }

            JsonElement value;

            if (set.TryGetProperty("ownedSetItemIds", out value))
            {
                var owned = new List<int>();
                foreach (JsonElement id in value.EnumerateArray())
                {
                    owned.Add(id.GetInt32());
                }

                input.OwnedSetItemIds = owned;
            }

            if (set.TryGetProperty("wornMaskIncludingSelf", out value))
            {
                input.WornMaskIncludingSelf = value.GetInt32();
            }

            if (set.TryGetProperty("wornMaskExcludingSelf", out value))
            {
                input.WornMaskExcludingSelf = value.GetInt32();
            }

            if (set.TryGetProperty("isEquipped", out value))
            {
                input.IsEquipped = value.ValueKind == JsonValueKind.True;
            }

            if (set.TryGetProperty("fullSetStats", out value))
            {
                var full = new List<KeyValuePair<int, int>>();
                foreach (JsonElement stat in value.EnumerateArray())
                {
                    JsonElement layer;
                    int at = stat.TryGetProperty("layer", out layer) ? layer.GetInt32() : 0;

                    full.Add(new KeyValuePair<int, int>(
                        ItemStatReader.PackStatKey(at, stat.GetProperty("id").GetInt32()),
                        stat.GetProperty("value").GetInt32()));
                }

                input.FullSetStats = full;
            }

            return input;
        }

        /// <summary>
        /// The four derived buffers. Emitted separately from `lines` because a divergence in the
        /// piece list, the tier selection or the set name has three different causes and only one
        /// of them is the composer's.
        /// </summary>
        private static string PackSetContent(SetItemTooltipContent content)
        {
            if (content == null)
            {
                return "null";
            }

            var pieces = new List<string>();
            foreach (SetPieceLine piece in content.Pieces)
            {
                pieces.Add("{\"text\": " + Quote(piece.Text)
                    + ", \"owned\": " + (piece.Owned ? "true" : "false") + "}");
            }

            return "{\"pieces\": [" + string.Join(", ", pieces)
                + "], \"setName\": " + Quote(content.SetName)
                + ", \"fullSetText\": " + Quote(content.FullSetText)
                + ", \"partialText\": " + Quote(content.PartialText) + "}";
        }

        private static string Pack(SortedDictionary<int, int> view)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<int, int> entry in view)
            {
                parts.Add("\"" + ItemStatReader.LayerFromKey(entry.Key) + "/"
                    + ItemStatReader.StatFromKey(entry.Key) + "\": " + entry.Value);
            }

            return "{" + string.Join(", ", parts) + "}";
        }

        private static string PackSections(RecordSections sections)
        {
            var parts = new List<string>();
            foreach (ItemTooltipSection section in Enum.GetValues(typeof(ItemTooltipSection)))
            {
                // Not a real section — the pre-assignment default. Querying it would add a key
                // the TypeScript side does not produce.
                if (section == ItemTooltipSection.None)
                {
                    continue;
                }

                string text;
                try
                {
                    text = sections.GetSection(section);
                }
                catch (Exception e)
                {
                    text = "<<" + e.GetType().Name + ">>";
                }

                if (!string.IsNullOrEmpty(text))
                {
                    parts.Add("\"" + section + "\": " + Quote(text));
                }
            }

            return "{" + string.Join(", ", parts) + "}";
        }

        private static string PackLines(IReadOnlyList<ItemTooltipLine> lines)
        {
            var parts = new List<string>();
            foreach (ItemTooltipLine line in lines)
            {
                parts.Add("{\"section\": \"" + line.Section + "\", \"color\": " + line.Color
                    + ", \"text\": " + Quote(line.Text) + "}");
            }

            return "[" + string.Join(", ", parts) + "]";
        }

        /// <summary>
        /// Escapes every character outside printable ASCII as \uXXXX. The engine's output is full of
        /// U+00FF colour markers, and a comparison that renders them as raw bytes would depend on
        /// the file encoding on both sides.
        /// </summary>
        private static string Quote(string text)
        {
            if (text == null)
            {
                return "null";
            }

            var builder = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"') builder.Append("\\\"");
                else if (c == '\\') builder.Append("\\\\");
                else if (c >= 32 && c < 127) builder.Append(c);
                else builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            }

            return builder.Append('"').ToString();
        }
    }
}
