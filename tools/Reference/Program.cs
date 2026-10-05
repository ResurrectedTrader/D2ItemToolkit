using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    ///   Reference &lt;corpus.json&gt; &lt;out.json&gt; [variant]
    ///
    /// The variant (default Lod114d) picks the embedded tables, so the same tool produces the D2R
    /// reference from a D2R corpus.
    ///
    /// The corpus is an array of cases; each case is `{ "name", "record", "player"? }` where
    /// `record` and `player` are unit documents in the capture format.
    /// </summary>
    public static class Program
    {
        // Set once in Main from the optional variant argument; every case renders against it.
        private static D2DataFiles Data;
        private static ItemTable Items;
        private static ItemTypeTree Types;
        private static SetTable Sets;
        private static RolledRangeReconstructor Ranges;

        // The facade, for the two layers that exist to police the OPT-IN render modes. Those route
        // by tooltip kind, which the hand-built path below deliberately does itself — reusing the
        // engine keeps this from re-implementing that routing a second time.
        private static TooltipEngine Engine;

        private static SocketStatSynthesis SocketStats;

        private static readonly string[] SlimLayers = { "name", "sections", "rendered", "colored", "error" };

        private static string Slim(string payload)
        {
            using (JsonDocument document = JsonDocument.Parse(payload))
            {
                var parts = new List<string>();
                foreach (string layer in SlimLayers)
                {
                    JsonElement value;
                    if (document.RootElement.TryGetProperty(layer, out value))
                    {
                        parts.Add(Quote(layer) + ": " + value.GetRawText());
                    }
                }

                return "{ " + string.Join(", ", parts) + " }";
            }
        }

        private static void LoadTables(GameVariant variant, string language, bool legacy)
        {
            Data = D2DataFiles.LoadEmbedded(
                variant,
                language == null && !legacy
                    ? null
                    : new ResurrectedTextOptions { Language = language ?? "enUS", LegacyGraphics = legacy });
            Items = new ItemTable(Data.Weapons, Data.Armor, Data.Misc);
            Types = new ItemTypeTree(Data.ItemTypes);
            Sets = new SetTable(Data.Sets, Data.SetItems, Data.Strings);
            Ranges = new RolledRangeReconstructor(Data, Items, Types, new MagicAffixTable(Data), Sets);
            Engine = TooltipEngine.FromData(Data);
            SocketStats = new SocketStatSynthesis(Data, Items, Types);
        }

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
            // Reference d2r-suite <rotw-corpus.json> <base-corpus.json> <outDir>: every D2R reference
            // the TypeScript differential replays, in one run.
            if (args.Length == 4 && args[0] == "d2r-suite")
            {
                return WriteSuite(args[1], args[2], args[3]);
            }

            // Reference <corpus> <out> [variant [locale [legacy|hd [slim]]]]. `slim` keeps only the
            // text layers, which is all a locale changes, so 13 locales do not cost 13 full references.
            GameVariant variant = GameVariant.Lod114d;
            if (args.Length < 2 || (args.Length > 2 && !Enum.TryParse(args[2], out variant)))
            {
                Console.Error.WriteLine(
                    "usage: Reference <corpus.json> <out.json> [variant [locale [legacy|hd [slim]]]]");
                return 2;
            }

            WriteReference(
                args[0], args[1], variant, args.Length > 3 ? args[3] : null,
                args.Length > 4 && args[4] == "legacy", args.Length > 5 && args[5] == "slim");
            return 0;
        }

        private static readonly string[] Locales =
        {
            "enUS", "deDE", "esES", "frFR", "itIT", "koKR", "plPL", "ruRU", "zhCN", "zhTW", "esMX",
            "jaJP", "ptBR",
        };

        private static int WriteSuite(string rotwCorpus, string baseCorpus, string outDir)
        {
            WriteReference(rotwCorpus, Path.Combine(outDir, "d2r-rotw-expected.json"),
                GameVariant.ReignOfTheWarlock, null, false, false);
            WriteReference(baseCorpus, Path.Combine(outDir, "d2r-base-expected.json"),
                GameVariant.Resurrected, null, false, false);

            foreach (string locale in Locales)
            {
                foreach (bool legacy in new[] { false, true })
                {
                    string file = "d2r-rotw-" + locale + (legacy ? "-legacy" : string.Empty) + ".json";
                    WriteReference(rotwCorpus, Path.Combine(outDir, "locales", file),
                        GameVariant.ReignOfTheWarlock, locale, legacy, true);
                }
            }

            return 0;
        }

        private static void WriteReference(
            string corpusPath, string outPath, GameVariant variant, string language, bool legacy, bool slim)
        {
            LoadTables(variant, language, legacy);

            using (JsonDocument corpus = JsonDocument.Parse(File.ReadAllText(corpusPath)))
            {
                var results = new List<string>();

                foreach (JsonElement testCase in corpus.RootElement.EnumerateArray())
                {
                    string rendered = Render(testCase);
                    results.Add(slim ? Slim(rendered) : rendered);
                }

                string directory = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outPath, "[\n  " + string.Join(",\n  ", results) + "\n]\n");
            }
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

                SetItemTooltipInput setInput = ReadSetInput(testCase, record, wearer);

                SortedDictionary<int, int> stats =
                    ItemStatReader.ReconstructView(record, ItemStatView.Equipped());
                SortedDictionary<int, int> baseStats =
                    ItemStatReader.ReconstructView(record, ItemStatView.BaseOnly());
                SortedDictionary<int, int> modifierStats =
                    ItemStatReader.ReconstructView(record, ItemStatView.Modifiers());

                // Mirrors TooltipEngine.Compose: a captured gem or rune has no stat chain, so its
                // contribution is rebuilt from gems.txt. Omitting it here would leave the whole
                // synthesis outside the differential.
                SortedDictionary<int, int> synthesised = SocketStats.Contributions(record);
                AddInto(stats, synthesised);
                AddInto(modifierStats, synthesised);

                var preOp = new Dictionary<int, int>(stats);
                ItemStatOps.Resolve(stats, baseStats, Data.ItemStatCost);

                // Mirrors TooltipEngine.Compose's D2R ops 4/5 against the viewer (0x14020c57d).
                if (Data.IsResurrected && viewer != null && (viewer.UnitType == 0 || viewer.UnitType == 1))
                {
                    ItemStatOps.ResolveLevelScaled(stats, preOp, Data.ItemStatCost.LevelScaledEntries, viewer.Stat);
                }

                payload.Append(", \"views\": {")
                    .Append("\"equipped\": ").Append(Pack(stats))
                    .Append(", \"base\": ").Append(Pack(baseStats))
                    .Append(", \"modifiers\": ").Append(Pack(modifierStats))
                    .Append("}");

                // The roll-range reconstruction. It reaches property handlers no rendering path
                // touches — the affix, unique, runeword and superior codes — so without it those
                // branches are invisible to the differential, which is exactly how the colour-3
                // marker gap survived.
                payload.Append(", \"ranges\": ").Append(
                    PackRanges(Ranges.Reconstruct(
                        item,
                        modifierStats,
                        SocketStats.FillerProperties(record),
                        // The tiers the WEARER has earned, not null. Passing null left
                        // RollSources.SetBonus reached by zero of the 935 cases, so the whole
                        // earned-set fold sat outside the differential.
                        Engine.EarnedSetIdsOf(wearer))));

                // The TOTALS surface, which shares nothing with the render path: it folds the
                // gems.txt synthesis and op 13 into one merged view, so none of that is reachable
                // through the layers above.
                payload.Append(", \"mergedStats\": ")
                    .Append(PackMergedStats(Engine.MergedStats(record)));

                // The two opt-in render modes, as text. Without these the annotation formatter, the
                // range colour and the socket-block layout are all outside the differential —
                // exercised only by hand-written tests on each side, which cannot catch the two
                // implementations agreeing to differ.
                // The damage NUMBERS, which the rendered string only carries as text. Both engines
                // route to their damage values separately from the writer, so this is what keeps
                // the two routings comparable rather than only each comparable to its own string.
                payload.Append(", \"damage\": ").Append(PackDamage(Engine.Damage(record, wearer)));

                payload.Append(", \"annotated\": ").Append(Quote(Annotated(record, wearer)));
                payload.Append(", \"socketsSplit\": ").Append(Quote(SocketsSplit(record, wearer)));
                payload.Append(", \"breakdown\": ").Append(Breakdown(record, wearer));

                var sections = new RecordSections(
                    Data, Items, Types, item, viewer, stats,
                    ItemStatReader.ReadSockets(record), baseStats,
                    ItemRecordReader.ReadSocketUnits(record));

                var composer = new ItemTooltipComposer(
                    sections, sections.CreateModifierGenerator(modifierStats));

                // Game state, not unit state, so it is carried on the case rather than derived.
                JsonElement difficulty, desecrated;
                ItemTooltipContext context = sections.CreateContext(
                    testCase.TryGetProperty("difficulty", out difficulty) ? difficulty.GetInt32() : 0,
                    testCase.TryGetProperty("desecratedZones", out desecrated) && desecrated.GetBoolean());

                // Game state, not unit state, so it is carried on the case rather than derived.
                JsonElement shopMode;
                context.ShopMode = testCase.TryGetProperty("shopMode", out shopMode)
                    ? shopMode.GetInt32()
                    : 0;

                ItemTooltipKind kind = ItemTooltipComposer.Classify(context);

                payload.Append(", \"kind\": ").Append(Quote(kind.ToString()));

                IReadOnlyList<ItemTooltipLine> lines;
                // D2R grows its result and never cuts it (0x1401d654c).
                int maxLength = Data.IsResurrected
                    ? ItemTooltipComposer.UnlimitedTooltipLength
                    : ItemTooltipComposer.MaxTooltipLength;

                if (kind == ItemTooltipKind.IdentifiedSetItem)
                {
                    // The generic composer still REFUSES a set item, and that refusal is behaviour
                    // worth comparing.
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
        /// The explicit override when a case carries a `set` member, and otherwise whatever the
        /// VIEWER implies — mirroring Render, which derives rather than defaulting to "none". A case
        /// with no `set` and no viewer still gets the empty input.
        /// </summary>
        private static SetItemTooltipInput ReadSetInput(JsonElement testCase, Unit record, Unit wearer)
        {
            JsonElement set;
            if (!testCase.TryGetProperty("set", out set))
            {
                return Engine.SetStateOf(record, wearer);
            }

            var input = new SetItemTooltipInput();

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

        /// <summary>
        /// The item rendered with the range annotation on and a distinct colour, so the composite
        /// formatter, the decoded packed values and the marker wrapping are all compared.
        ///
        /// ShowItemLevel rides along: it shares the marker-wrapping helper, and the suffix has to
        /// land on the item's own name rather than the base name below it, which is a display-order
        /// question no other layer asks.
        /// </summary>
        private static string Annotated(Unit record, Unit wearer)
        {
            var options = new TooltipOptions();
            options.Ranges = new RangeDisplay();
            options.Ranges.Color = ItemTooltipColor.White;
            options.ShowItemLevel = true;

            return Engine.Render(record, wearer, options).ColoredText;
        }

        /// <summary>
        /// The item rendered with its fillers split out, ranges on — so the block order, the
        /// per-filler headers and the jewel-versus-gem range choice are all compared.
        /// </summary>
        private static string SocketsSplit(Unit record, Unit wearer)
        {
            var options = new TooltipOptions();
            options.Sockets = SocketMode.Separated;
            options.Ranges = new RangeDisplay();

            return Engine.Render(record, wearer, options).ColoredText;
        }

        /// <summary>
        /// The four buckets with ranges on. Breakdown was outside the differential entirely, which
        /// left its per-bucket span choice — the item's own for three of them, the fillers' for the
        /// fourth — checked only by hand-written tests on each side.
        /// </summary>
        private static string Breakdown(Unit record, Unit wearer)
        {
            var options = new TooltipOptions();
            options.Ranges = new RangeDisplay();

            TooltipBreakdown b = Engine.Breakdown(record, wearer, options);

            return "{\"base\": " + PackTexts(b.Base)
                + ", \"magic\": " + PackTexts(b.Magic)
                + ", \"sockets\": " + PackTexts(b.Sockets)
                + ", \"setBonuses\": " + PackTexts(b.SetBonuses) + "}";
        }

        private static string PackTexts(IReadOnlyList<ItemTooltipLine> lines)
        {
            var texts = new List<string>();
            foreach (ItemTooltipLine line in lines)
            {
                // "" rather than Quote's null: the TypeScript side coalesces a null text to the
                // empty string, and a JSON null here would make the two disagree on a line no case
                // currently produces.
                texts.Add(Quote(line.Text ?? string.Empty));
            }

            return "[" + string.Join(", ", texts) + "]";
        }

        /// <summary>The merged-stat totals, flags included, so both engines agree on all of it.</summary>
        private static string PackDamage(ItemDamage damage)
        {
            var lines = new List<string>();
            foreach (ItemDamageRange line in damage.Lines)
            {
                lines.Add("{\"kind\": \"" + line.Kind
                    + "\", \"min\": " + line.Min
                    + ", \"max\": " + line.Max
                    + ", \"modified\": " + (line.Modified ? "true" : "false") + "}");
            }

            return "[" + string.Join(", ", lines) + "]";
        }

        private static string PackMergedStats(ItemMergedStats merged)
        {
            var stats = new List<string>();
            foreach (MergedStat stat in merged.Stats)
            {
                stats.Add("{\"stat\": " + stat.StatId
                    + ", \"layer\": " + stat.Layer
                    + ", \"value\": " + stat.Value + "}");
            }

            return "{\"stats\": [" + string.Join(", ", stats)
                + "]"
                + ", \"excludedPackedStats\": ["
                + string.Join(", ", merged.ExcludedPackedStats) + "]}";
        }

        private static string PackRanges(ItemRollRanges ranges)
        {
            var stats = PackStatRanges(ranges.Stats);

            var layers = new List<string>();
            foreach (RolledLayerRange range in ranges.LayerVaries)
            {
                layers.Add("{\"stat\": " + range.StatId
                    + ", \"layerLow\": " + range.LayerLow
                    + ", \"layerHigh\": " + range.LayerHigh
                    + ", \"value\": " + range.Value
                    + ", \"sources\": " + (int)range.Sources + "}");
            }

            return "{\"stats\": [" + string.Join(", ", stats)
                + "], \"layerVaries\": [" + string.Join(", ", layers)
                + "], \"outOfRange\": [" + string.Join(", ", ranges.OutOfRange)
                + "], \"unattributed\": [" + string.Join(", ", ranges.Unattributed)
                + "], \"itemLevelDependent\": [" + string.Join(", ", ranges.ItemLevelDependent)
                + "], \"unsupportedFuncs\": [" + string.Join(", ", ranges.UnsupportedFuncs)
                + "], \"craftedRecipeUnknown\": "
                + (ranges.CraftedRecipeUnknown ? "true" : "false")
                + ", \"craftedRecipe\": " + ranges.CraftedRecipe
                + ", \"choices\": [" + string.Join(", ", ranges.Choices.Select(PackChoice)) + "]}";
        }

        private static List<string> PackStatRanges(IEnumerable<RolledStatRange> ranges)
        {
            var stats = new List<string>();
            foreach (RolledStatRange range in ranges)
            {
                stats.Add("{\"stat\": " + range.StatId
                    + ", \"layer\": " + range.Layer
                    + ", \"low\": " + range.Low
                    + ", \"high\": " + range.High
                    + ", \"displayLow\": " + range.DisplayLow
                    + ", \"displayHigh\": " + range.DisplayHigh
                    + ", \"sources\": " + (int)range.Sources + "}");
            }

            return stats;
        }

        private static string PackChoice(RolledChoice choice)
        {
            var options = choice.Options.Select(option =>
                "{\"entry\": " + option.Entry
                + ", \"propertyId\": " + option.PropertyId
                + ", \"code\": " + Quote(option.Code ?? string.Empty)
                + ", \"weight\": " + option.Weight
                + ", \"paramLow\": " + option.ParamLow
                + ", \"paramHigh\": " + option.ParamHigh
                + ", \"min\": " + option.Min
                + ", \"max\": " + option.Max
                + ", \"variants\": [" + string.Join(", ", option.Variants.Select(variant =>
                    "{\"param\": " + variant.Param
                    + ", \"stats\": [" + string.Join(", ", PackStatRanges(variant.Stats)) + "]}"))
                + "], \"nested\": " + (option.Nested == null ? "null" : PackChoice(option.Nested)) + "}");

            var consistent = choice.Consistent.Select(outcome =>
                "[" + string.Join(", ", outcome.Select(pick =>
                    "{\"option\": " + pick.Option + ", \"param\": " + pick.Param + "}")) + "]");

            return "{\"groupRow\": " + choice.GroupRow
                + ", \"code\": " + Quote(choice.Code ?? string.Empty)
                + ", \"sources\": " + (int)choice.Sources
                + ", \"pickMode\": " + (int)choice.PickMode
                + ", \"countLow\": " + choice.CountLow
                + ", \"countHigh\": " + choice.CountHigh
                + ", \"resolution\": " + (int)choice.Resolution
                + ", \"options\": [" + string.Join(", ", options)
                + "], \"consistent\": [" + string.Join(", ", consistent) + "]}";
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
                // StatId and Layer are public members a caller reads, and they were NOT compared:
                // one implementation decoded the damage line's layer a second time and reported 0
                // for every line, which nothing here could see.
                string shown = line.ShownStats == null
                    ? "null"
                    : "[" + string.Join(", ", line.ShownStats) + "]";

                parts.Add("{\"section\": \"" + line.Section + "\", \"color\": " + line.Color
                    + ", \"statId\": " + line.StatId + ", \"layer\": " + line.Layer
                    + ", \"shownStats\": " + shown
                    + ", \"aggregated\": " + (line.Aggregated ? "true" : "false")
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
