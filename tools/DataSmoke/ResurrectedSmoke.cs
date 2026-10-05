using System;
using System.IO;
using System.Reflection;

namespace D2ItemToolkit.DataSmoke
{
    /// <summary>
    /// The D2R half: checks the embedded <c>data/d2r</c> tree against an extraction of the game's
    /// <c>data/data</c> directory, and the txt loader against the game's own compiled <c>.bin</c>
    /// tables, which carry what the field tables produced from the same txt.
    ///
    ///   DataSmoke d2r &lt;extraction&gt;/data/data
    /// </summary>
    internal static class ResurrectedSmoke
    {
        private const string Prefix = "D2ItemToolkit.Data.d2r.";

        private static readonly string[][] Trees =
        {
            new[] { "excel.base.", "global/excel/base/" },
            new[] { "excel.", "global/excel/" },
            new[] { "strings.", "local/lng/strings/" },
            new[] { "strings-legacy.", "local/lng/strings-legacy/" },
            new[] { "global.", "global/" },
        };

        // Record sizes from the DATATBLS_Load*Txt field tables.
        private const int ItemStatCostRecord = 324;
        private static readonly int[] StringFieldOffsets = { 52, 54, 56, 62, 64, 66 };
        private static readonly string[] StringFieldColumns =
        {
            "descstrpos", "descstrneg", "descstr2", "dgrpstrpos", "dgrpstrneg", "dgrpstr2",
        };

        public static int Run(string root)
        {
            int failures = CompareEmbedded(root);

            foreach (GameVariant variant in new[] { GameVariant.ReignOfTheWarlock, GameVariant.Resurrected })
            {
                string excel = Path.Combine(
                    root, variant == GameVariant.Resurrected ? "global/excel/base" : "global/excel");

                foreach (bool legacy in new[] { false, true })
                {
                    D2DataFiles data = D2DataFiles.LoadEmbedded(
                        variant, new ResurrectedTextOptions { LegacyGraphics = legacy });
                    failures += CheckStringIds(data, excel, variant + (legacy ? " legacy" : " hd"));
                }

                failures += CheckRowCounts(D2DataFiles.LoadEmbedded(variant), excel, variant.ToString());
            }

            Console.WriteLine(failures == 0 ? "d2r: all checks passed" : "d2r: " + failures + " failures");
            return failures == 0 ? 0 : 1;
        }

        private static int CompareEmbedded(string root)
        {
            Assembly assembly = typeof(D2DataFiles).GetTypeInfo().Assembly;
            int failures = 0, compared = 0;

            foreach (string resource in assembly.GetManifestResourceNames())
            {
                if (!resource.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string rest = resource.Substring(Prefix.Length);
                string path = null;
                foreach (string[] tree in Trees)
                {
                    // "excel.base." also starts with "excel.", so the table lists it first.
                    if (rest.StartsWith(tree[0], StringComparison.Ordinal))
                    {
                        path = tree[1] + rest.Substring(tree[0].Length);
                        break;
                    }
                }

                string full = Path.Combine(root, path ?? rest);
                byte[] embedded;
                using (Stream stream = assembly.GetManifestResourceStream(resource))
                using (var copy = new MemoryStream())
                {
                    if (stream == null)
                    {
                        continue;
                    }

                    stream.CopyTo(copy);
                    embedded = copy.ToArray();
                }

                ++compared;
                if (!File.Exists(full))
                {
                    Console.WriteLine("MISSING in extraction: " + full);
                    ++failures;
                    continue;
                }

                if (!Same(embedded, File.ReadAllBytes(full)))
                {
                    Console.WriteLine("DRIFT: " + path);
                    ++failures;
                }
            }

            Console.WriteLine("compared " + compared + " embedded d2r files, " + failures + " differ");
            return failures;
        }

        private static int CheckStringIds(D2DataFiles data, string excel, string label)
        {
            byte[] bin = File.ReadAllBytes(Path.Combine(excel, "itemstatcost.bin"));
            TxtFile txt = TxtFile.Load(File.ReadAllBytes(Path.Combine(excel, "itemstatcost.txt")), data.Variant);
            int count = BitConverter.ToInt32(bin, 0);
            int failures = 0;

            for (int row = 0; row < count && row < txt.RowCount; ++row)
            {
                int record = 4 + (row * ItemStatCostRecord);
                for (int field = 0; field < StringFieldOffsets.Length; ++field)
                {
                    int compiled = BitConverter.ToUInt16(bin, record + StringFieldOffsets[field]);
                    int ours = (ushort)data.Strings.ResolveKey(txt.GetString(row, StringFieldColumns[field]));
                    if (compiled != ours && failures++ < 10)
                    {
                        Console.WriteLine(
                            label + ": row " + row + " " + StringFieldColumns[field] + " compiled " +
                            compiled + " ours " + ours);
                    }
                }
            }

            Console.WriteLine(label + ": itemstatcost string ids, " + failures + " mismatches");
            return failures;
        }

        private static int CheckRowCounts(D2DataFiles data, string excel, string label)
        {
            int failures = 0;
            failures += Rows(label, excel, "itemstatcost.bin", data.ItemStatCost.RowCount);
            failures += Rows(label, excel, "itemtypes.bin", data.ItemTypes.RowCount);
            failures += Rows(label, excel, "uniqueitems.bin", data.UniqueItems.RowCount);
            failures += Rows(label, excel, "setitems.bin", data.SetItems.RowCount);
            failures += Rows(label, excel, "sets.bin", data.Sets.RowCount);
            failures += Rows(label, excel, "runes.bin", data.Runes.RowCount);
            failures += Rows(label, excel, "properties.bin", data.Properties.RowCount);
            failures += Rows(label, excel, "gems.bin", data.Gems.RowCount);
            failures += Rows(label, excel, "charstats.bin", data.CharStats.RowCount);
            failures += Rows(label, excel, "skills.bin", data.SkillRows.RowCount);
            failures += Rows(label, excel, "missiles.bin", data.Missiles.RowCount);
            failures += Rows(label, excel, "monstats.bin", data.MonsterStats.RowCount);
            failures += Rows(label, excel, "qualityitems.bin", data.QualityItems.RowCount);
            failures += Rows(label, excel, "lowqualityitems.bin", data.LowQualityItems.RowCount);
            failures += Rows(label, excel, "belts.bin", data.Belts.RowCount);
            failures += Rows(label, excel, "magicsuffix.bin", data.MagicSuffix.RowCount);
            failures += Rows(label, excel, "magicprefix.bin", data.MagicPrefix.RowCount);
            failures += Rows(label, excel, "automagic.bin", data.AutoMagic.RowCount);
            failures += Rows(label, excel, "raresuffix.bin", data.RareSuffix.RowCount);
            failures += Rows(label, excel, "rareprefix.bin", data.RarePrefix.RowCount);
            failures += Rows(label, excel, "weapons.bin", data.Weapons.RowCount);
            failures += Rows(label, excel, "armor.bin", data.Armor.RowCount);
            failures += Rows(label, excel, "misc.bin", data.Misc.RowCount);
            failures += Rows(label, excel, "propertygroups.bin", data.PropertyGroups.RowCount);
            failures += Rows(label, excel, "states.bin", data.States.RowCount);
            failures += Rows(label, excel, "montype.bin", data.MonTypeRows.RowCount);
            return failures;
        }

        // A missing .bin is reported rather than skipped, so a renamed table cannot hide.
        private static int Rows(string label, string excel, string bin, int ours)
        {
            string path = Path.Combine(excel, bin);
            if (!File.Exists(path))
            {
                Console.WriteLine(label + ": " + bin + " absent");
                return 1;
            }

            int compiled = BitConverter.ToInt32(File.ReadAllBytes(path), 0);
            bool same = compiled == ours;
            Console.WriteLine(label + ": " + bin + " compiled " + compiled + " ours " + ours + (same ? "" : "  <-- MISMATCH"));
            return same ? 0 : 1;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; ++i)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
