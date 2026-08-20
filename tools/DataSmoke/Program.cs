using System;
using System.Collections.Generic;

namespace D2ItemToolkit.DataSmoke
{
    /// <summary>
    /// Runs the description engine against a real MPQ extraction. Not a test — a way to see
    /// what the engine actually produces from shipped data.
    /// </summary>
    public static class Program
    {
        private sealed class Values : IStatValueSource
        {
            public readonly Dictionary<int, int> Base = new Dictionary<int, int>();
            public readonly Dictionary<int, int> Player = new Dictionary<int, int>();

            // Empty today; kept as a seam so a case can supply item stats without this collapsing
            // back into a hardcoded `return 0`.
            // ReSharper disable once CollectionNeverUpdated.Local
            public readonly Dictionary<int, int> Item = new Dictionary<int, int>();
            public int Class = 3;

            public int GetBaseStatValue(int statId, int layer)
            {
                int v;
                return Base.TryGetValue(statId, out v) ? v : 0;
            }

            public int GetPlayerStatValue(int statId)
            {
                int v;
                return Player.TryGetValue(statId, out v) ? v : 0;
            }

            public int GetItemStatValue(int statId)
            {
                int v;
                return Item.TryGetValue(statId, out v) ? v : 0;
            }

            public int PlayerClass { get { return Class; } }
            public bool IsItemOfType(int itemTypeId) { return false; }
            public bool DescribedUnitIsItem { get { return true; } }
            public bool ItemTableAllowsDurability { get { return true; } }
            public int GetTxtMaxDurability() { return 0; }
        }

        public static int Main(string[] args)
        {
            // Point it at a live MPQ extraction to check the embedded copy has not drifted:
            //   DataSmoke <excelDir> <localeDir>
            // With no arguments it uses the embedded tables, so it runs anywhere — including CI,
            // where there is no extraction to read.
            D2DataFiles data;
            if (args.Length > 0)
            {
                string excel = args[0];
                string locale = args.Length > 1
                    ? args[1]
                    : throw new ArgumentException("a locale directory is required alongside excel");

                Console.WriteLine("loading from disk: " + excel);
                data = D2DataFiles.Load(excel, locale);
            }
            else
            {
                Console.WriteLine("loading embedded tables");
                data = D2DataFiles.LoadEmbedded();
            }

            Console.WriteLine("=== tables ===");
            Console.WriteLine("stats            : " + data.ItemStatCost.RowCount);
            Console.WriteLine("described stats  : " + data.ItemStatCost.StatIdsByDescPriority.Count);
            Console.WriteLine("SkillIdShift     : " + data.ItemStatCost.SkillIdShift);
            Console.WriteLine("skills           : " + data.Skills.RowCount);
            Console.WriteLine();

            Console.WriteLine("=== punctuation the engine depends on ===");
            foreach (int id in new[] { 3852, 3994, 3995, 3997, 3998, 4001, 4002, 4003, 5382 })
            {
                Console.WriteLine(id + " = " + Escape(data.Strings.GetByIndex(id)));
            }

            Console.WriteLine();
            Console.WriteLine("=== first 12 stats in emission order ===");
            for (int i = 0; i < 12 && i < data.ItemStatCost.StatIdsByDescPriority.Count; ++i)
            {
                int statId = data.ItemStatCost.StatIdsByDescPriority[i];
                StatDescriptor stat;
                data.ItemStatCost.TryGetStat(statId, out stat);
                Console.WriteLine(
                    "  stat {0,4}  pri {1,3}  func {2,2}  val {3}  pos \"{4}\"",
                    statId, stat.DescPriority, stat.DescFunc, stat.DescVal,
                    Escape(data.Strings.GetByIndex(stat.DescStrPos)));
            }

            Console.WriteLine();
            Console.WriteLine("=== a described item ===");

            var values = new Values();
            values.Base[39] = 30;   // fire resist
            values.Base[43] = 30;   // cold resist
            values.Base[41] = 30;   // lightning resist
            values.Base[45] = 30;   // poison resist
            values.Base[48] = 5;    // fire min
            values.Base[49] = 12;   // fire max
            values.Player[12] = 80; // level, for op scaling

            var stats = new List<KeyValuePair<int, int>>
            {
                Entry(0, 25),    // strength
                Entry(2, 20),    // dexterity
                Entry(7, 60),    // life
                Entry(31, 150),  // defense
                Entry(39, 30), Entry(41, 30), Entry(43, 30), Entry(45, 30),
                Entry(48, 5), Entry(49, 12),
                Entry(80, 35),   // magic find
                Entry(60, 7),    // life steal
            };

            ItemDescriptionGenerator generator = data.CreateGenerator(values);
            IReadOnlyList<ItemDescriptionLine> lines = generator.Describe(stats);

            foreach (ItemDescriptionLine line in lines)
            {
                Console.WriteLine("  " + Escape(line.Text));
            }

            Console.WriteLine();
            Console.WriteLine("joined (inline, as the tooltip does):");
            Console.WriteLine(Escape(generator.Join(lines)));

            return 0;
        }

        private static KeyValuePair<int, int> Entry(int statId, int value)
        {
            return new KeyValuePair<int, int>(ItemStatReader.PackStatKey(0, statId), value);
        }

        private static string Escape(string text)
        {
            if (text == null)
            {
                return "<null>";
            }

            var builder = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (c == '\n') builder.Append("\\n");
                else if (c == '\r') builder.Append("\\r");
                else if (c < 32 || c > 126) builder.Append("\\u").Append(((int)c).ToString("X4"));
                else builder.Append(c);
            }

            return builder.ToString();
        }
    }
}
