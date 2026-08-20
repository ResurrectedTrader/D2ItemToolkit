using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// TXT_magicaffixes_GetLine 0x633ee0 — the counterpart of MagicAffixTable.test.ts, which had
    /// no C# equivalent. The three affix files are addressed as ONE 1-based array, and getting the
    /// spill points wrong silently renames every rare item.
    /// </summary>
    public class MagicAffixTableTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();
        private static readonly MagicAffixTable Affixes = new MagicAffixTable(Data);

        // "of Lightning", the first suffix carrying a class restriction: sorceress, levelreq 18,
        // classlevelreq 9.
        private const int OfLightning = 438;

        private static ItemViewer Viewer(int classId)
        {
            var viewer = new ItemViewer();
            viewer.ClassId = classId;
            return viewer;
        }

        private static TxtFile Table(int id)
        {
            TxtFile table;
            Assert.True(Affixes.TryResolve(id, out table, out _), "unresolved id " + id);
            return table;
        }

        private static int Row(int id)
        {
            int row;
            Assert.True(Affixes.TryResolve(id, out _, out row), "unresolved id " + id);
            return row;
        }

        [Fact]
        public void Is_one_one_based_array_in_the_order_suffix_prefix_automagic()
        {
            Assert.Equal(747, Data.MagicSuffix.RowCount);
            Assert.Equal(669, Data.MagicPrefix.RowCount);
            Assert.Equal(36, Data.AutoMagic.RowCount);

            // Id 1 is the first SUFFIX row.
            Assert.Same(Data.MagicSuffix, Table(1));
            Assert.Equal(0, Row(1));
            Assert.Equal("of Health", Data.MagicSuffix.GetString(0, "Name"));

            Assert.Same(Data.MagicSuffix, Table(747));
            Assert.Equal(746, Row(747));
            Assert.Equal("of the Vampire", Data.MagicSuffix.GetString(746, "Name"));

            // An id past the suffix count spills into the prefixes.
            Assert.Same(Data.MagicPrefix, Table(748));
            Assert.Equal(0, Row(748));
            Assert.Same(Data.MagicPrefix, Table(749));
            Assert.Equal(1, Row(749));
            Assert.Equal("Sturdy", Data.MagicPrefix.GetString(1, "Name"));

            // And past the prefixes into automagic.
            Assert.Same(Data.AutoMagic, Table(1417));
            Assert.Equal(0, Row(1417));
            Assert.Equal("Fletcher's", Data.AutoMagic.GetString(0, "Name"));

            Assert.Same(Data.AutoMagic, Table(1452));
            Assert.Equal(35, Row(1452));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(1453)]
        [InlineData(100000)]
        public void Rejects_ids_off_both_ends(int id)
        {
            Assert.False(Affixes.TryResolve(id, out _, out _));
        }

        [Fact]
        public void Folds_the_level_requirement_upward_keeping_the_running_maximum()
        {
            // ITEMS_nullsub 0x628830.
            Assert.Equal(18, Data.MagicSuffix.GetInt(OfLightning - 1, "levelreq"));

            Assert.Equal(18, Affixes.RaiseLevelRequirement(0, OfLightning, null));
            Assert.Equal(18, Affixes.RaiseLevelRequirement(18, OfLightning, null));
            Assert.Equal(50, Affixes.RaiseLevelRequirement(50, OfLightning, null));

            // An id that resolves to nothing leaves the running maximum alone.
            Assert.Equal(7, Affixes.RaiseLevelRequirement(7, 0, null));
            Assert.Equal(7, Affixes.RaiseLevelRequirement(7, 1453, null));
        }

        [Fact]
        public void Prefers_classlevelreq_when_the_affix_is_restricted_to_the_viewer_own_class()
        {
            Assert.Equal("sor", Data.MagicSuffix.GetString(OfLightning - 1, "class"));
            Assert.Equal(9, Data.MagicSuffix.GetInt(OfLightning - 1, "classlevelreq"));
            Assert.Equal(1, Data.Skills.ClassIdForCode("sor"));

            Assert.Equal(9, Affixes.RaiseLevelRequirement(0, OfLightning, Viewer(1)));

            // A different class, or none at all, takes levelreq.
            Assert.Equal(18, Affixes.RaiseLevelRequirement(0, OfLightning, Viewer(0)));
            Assert.Equal(18, Affixes.RaiseLevelRequirement(0, OfLightning, Viewer(-1)));
            Assert.Equal(18, Affixes.RaiseLevelRequirement(0, OfLightning, null));
        }

        [Fact]
        public void Treats_an_unrestricted_affix_as_class_ff()
        {
            // nClass is 0xFF when the affix has no class restriction, so no viewer class matches.
            Assert.Equal(0xFF, MagicAffixTable.NoClass);
            Assert.Equal(string.Empty, Data.MagicSuffix.GetString(0, "class"));

            Assert.Equal(
                Data.MagicSuffix.GetInt(0, "levelreq"),
                Affixes.RaiseLevelRequirement(0, 1, Viewer(0xFF)));
        }
    }
}
