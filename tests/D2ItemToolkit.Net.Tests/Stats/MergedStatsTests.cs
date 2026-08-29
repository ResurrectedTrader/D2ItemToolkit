using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The three things a stored item cannot answer from its raw statlists — a filler's stats are
    /// not in the capture at all, an item's own stats are split across lists with no total anywhere,
    /// and op 13 is unapplied — and the two cases where these totals deliberately differ from what
    /// the game is currently granting.
    ///
    /// The Crest and Vipermagi fixtures are records taken from a live capture, including the base
    /// defence of 127 that ITEMMOD_MaximizeStatForEnhanced 0x65ccc0 forces on an `ac%` armour; the
    /// rest are authored to isolate one rule each.
    /// </summary>
    public class MergedStatsTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();
        private static readonly TooltipEngine Engine = TooltipEngine.Embedded;
        private static readonly ItemTable Items = new ItemTable(Data.Weapons, Data.Armor, Data.Misc);

        private const int StatDefense = 31;
        private const int StatArmorPercent = 16;
        private const int StatFireResist = 39;
        private const int StatMaxLife = 7;
        private const int LocationEquipped = 1;
        private const int LocationStash = 3;

        /// <summary>setitems.txt post-splice, 0-based. `xsk`, a Death Mask.</summary>
        private const int TalRashasHoradricCrest = 80;

        /// <summary>UniqueItems.txt post-splice, 0-based. `xea`, a Serpentskin Armor.</summary>
        private const int SkinOfTheVipermagi = 210;

        private static int ValueOf(ItemMergedStats merged, int statId, int layer = 0)
        {
            foreach (MergedStat stat in merged.Stats)
            {
                if (stat.StatId == statId && stat.Layer == layer)
                {
                    return stat.Value;
                }
            }

            return 0;
        }

        /// <summary>
        /// The captured Tal Rasha's Horadric Crest: base list 31 = 76, its own set mods on the 0x40
        /// list, an Um rune in its one socket.
        /// </summary>
        private static Unit CrestWithUm(int location)
        {
            var um = new Unit();
            um.UnitType = 4;
            um.ClassId = Items.ClassIdForCode("r22");
            um.ItemFlags = ItemRecordFlags.Identified;

            var helm = new Unit();
            helm.UnitType = 4;
            helm.ClassId = Items.ClassIdForCode("xsk");
            helm.Quality = ItemQualityNo.Set;
            helm.FileIndex = TalRashasHoradricCrest;
            helm.ItemFlags = ItemRecordFlags.Identified | ItemRecordFlags.Socketed;
            helm.Location = location;
            helm.X = 1;

            helm.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended)
                    .Add(StatDefense, 76).Add(72, 16).Add(73, 20).Add(194, 1));

            helm.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic)
                    .Add(StatMaxLife, 60 << 8).Add(9, 30 << 8).Add(StatDefense, 45)
                    .Add(39, 15).Add(41, 15).Add(43, 15).Add(45, 15)
                    .Add(60, 10).Add(62, 10));

            helm.Items.Add(um);
            return helm;
        }

        [Fact]
        public void An_items_own_stats_are_summed_across_its_lists()
        {
            // 3b: the base array holds 76 and the affix list 45, and the tooltip prints 121. Nothing
            // in the raw chain holds 121, so `defence >= 100` could never match this item.
            ItemMergedStats merged = Engine.MergedStats(CrestWithUm(LocationStash));

            Assert.Equal(121, ValueOf(merged, StatDefense));
        }

        [Fact]
        public void A_runes_stats_are_synthesised_from_gems_txt()
        {
            // 3a: an Um arrives with an EMPTY stat chain, so its `Helms: All Resistances +15` is
            // nowhere in the capture. The Crest grants 15 of its own, so a correct merge reads 30 —
            // and that is the number the user searches for.
            Unit carried = CrestWithUm(LocationStash);

            Assert.Equal(30, ValueOf(Engine.MergedStats(carried), StatFireResist));

            var withoutSockets = new MergedStatsOptions();
            withoutSockets.IncludeSockets = false;

            Assert.Equal(15, ValueOf(Engine.MergedStats(carried, withoutSockets), StatFireResist));
        }

        [Fact]
        public void A_worn_set_piece_keeps_its_filler_totals()
        {
            // The GAME throws the Um away when the piece is worn (0x4c15fd gates the recalc loop on
            // quality 5). These totals answer what the ITEM grants, which wearing it does not change,
            // so both states read 30.
            Assert.Equal(30, ValueOf(Engine.MergedStats(CrestWithUm(LocationEquipped)), StatFireResist));
            Assert.Equal(30, ValueOf(Engine.MergedStats(CrestWithUm(LocationStash)), StatFireResist));
        }

        [Fact]
        public void Op_13_is_applied_and_the_percent_survives()
        {
            // 3c: Skin of the Vipermagi is base 127 under a fixed 120% enhanced defence, and the
            // game prints 279. The percent stays in the merge because the tooltip draws it as its
            // own line, so a caller indexing "+120% Enhanced Defense" still finds it.
            var armor = new Unit();
            armor.UnitType = 4;
            armor.ClassId = Items.ClassIdForCode("xea");
            armor.Quality = ItemQualityNo.Unique;
            armor.FileIndex = SkinOfTheVipermagi;
            armor.ItemFlags = ItemRecordFlags.Identified;

            armor.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended).Add(StatDefense, 127));
            armor.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic).Add(StatArmorPercent, 120));

            ItemMergedStats merged = Engine.MergedStats(armor);

            Assert.Equal(279, ValueOf(merged, StatDefense));
            Assert.Equal(120, ValueOf(merged, StatArmorPercent));
        }

        [Fact]
        public void Values_come_back_raw_rather_than_display_scaled()
        {
            // The Crest's `+60 to Life` is stored 8.8 fixed point. A consumer's bounds are derived
            // from the same itemstatcost scale, so shifting here would need a second scale beside
            // it and the two would drift.
            ItemMergedStats merged = Engine.MergedStats(CrestWithUm(LocationStash));

            Assert.Equal(60 << 8, ValueOf(merged, StatMaxLife));
        }

        [Fact]
        public void Packed_encodings_are_excluded_rather_than_summed_or_zeroed()
        {
            // Stat 204 packs `(maxCharges << 8) + current`. Adding two of those produces a number
            // that looks real and is not, so it is left out — and ABSENT rather than zero, because
            // a zero would satisfy every "at most N" bound a caller applied to it.
            const int StatChargedSkill = 204;

            var wand = new Unit();
            wand.UnitType = 4;
            wand.ClassId = Items.ClassIdForCode("wnd");
            wand.Quality = ItemQualityNo.Magic;
            wand.ItemFlags = ItemRecordFlags.Identified;

            wand.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic)
                    .Add(StatChargedSkill, (9 << 8) + 5, 56)
                    .Add(StatFireResist, 20));

            ItemMergedStats merged = Engine.MergedStats(wand);

            Assert.DoesNotContain(merged.Stats, s => s.StatId == StatChargedSkill);
            Assert.Contains(StatChargedSkill, merged.ExcludedPackedStats);

            // The rest of the item is unaffected.
            Assert.Equal(20, ValueOf(merged, StatFireResist));
        }

        [Fact]
        public void Layers_never_merge()
        {
            // `+1 to Fire Skills` and `+1 to Cold Skills` are one stat at two layers.
            const int StatElementalSkills = 126;

            var amulet = new Unit();
            amulet.UnitType = 4;
            amulet.ClassId = Items.ClassIdForCode("amu");
            amulet.Quality = ItemQualityNo.Magic;
            amulet.ItemFlags = ItemRecordFlags.Identified;

            amulet.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic)
                    .Add(StatElementalSkills, 1, 1)
                    .Add(StatElementalSkills, 2, 2));

            ItemMergedStats merged = Engine.MergedStats(amulet);

            Assert.Equal(1, ValueOf(merged, StatElementalSkills, 1));
            Assert.Equal(2, ValueOf(merged, StatElementalSkills, 2));
        }

        [Fact]
        public void Set_bonuses_are_excluded_by_default_and_opt_in()
        {
            Unit crest = CrestWithUm(LocationStash);

            // A 2-piece tier the record already carries, on its own state list.
            crest.StatsLists.Add(
                new UnitStatList(ItemStatListStates.ItemSet1, ItemStatListFlags.Magic)
                    .Add(StatFireResist, 50));

            Assert.Equal(30, ValueOf(Engine.MergedStats(crest), StatFireResist));

            var withBonuses = new MergedStatsOptions();
            withBonuses.IncludeSetBonuses = true;

            Assert.Equal(80, ValueOf(Engine.MergedStats(crest, withBonuses), StatFireResist));
        }

        [Fact]
        public void A_socketed_jewels_own_affixes_are_counted()
        {
            // A jewel carries CAPTURED stats, so the gems.txt synthesis deliberately returns
            // nothing for it — and folding fillers in through the synthesis ALONE therefore counted
            // it zero times while SocketFillerStats reported it. The same hole swallowed every
            // filler of a server-side capture, which records the mods the engine already assigned.
            var jewel = new Unit();
            jewel.UnitType = 4;
            jewel.ClassId = Items.ClassIdForCode("jew");
            jewel.Quality = ItemQualityNo.Magic;
            jewel.ItemFlags = ItemRecordFlags.Identified;
            jewel.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic).Add(StatFireResist, 15));

            var helm = new Unit();
            helm.UnitType = 4;
            helm.ClassId = Items.ClassIdForCode("xsk");
            helm.ItemFlags = ItemRecordFlags.Identified | ItemRecordFlags.Socketed;
            helm.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended).Add(StatDefense, 76).Add(194, 1));
            helm.Items.Add(jewel);

            Assert.Equal(15, ValueOf(Engine.MergedStats(helm), StatFireResist));

            // The two entry points must agree about the same filler.
            Assert.Equal(
                15, Engine.SocketFillerStats(jewel, helm).Single(s => s.StatId == StatFireResist).Value);

            var withoutSockets = new MergedStatsOptions();
            withoutSockets.IncludeSockets = false;

            Assert.Equal(0, ValueOf(Engine.MergedStats(helm, withoutSockets), StatFireResist));
        }

        [Fact]
        public void Op_13_reads_the_BASE_defense_not_the_merged_one()
        {
            // ItemStatOps' own doc calls this load-bearing: the percent applies to the BASE array,
            // never to base-plus-affixes. A fixture whose defence lives only on the base list cannot
            // tell the two apart, so this one splits it 100 base + 100 affix under +100%.
            var armor = new Unit();
            armor.UnitType = 4;
            armor.ClassId = Items.ClassIdForCode("xea");
            armor.Quality = ItemQualityNo.Magic;
            armor.ItemFlags = ItemRecordFlags.Identified;

            armor.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended).Add(StatDefense, 100));
            armor.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic)
                    .Add(StatDefense, 100).Add(StatArmorPercent, 100));

            // 100 + 100 + (100 base * 100%) = 300. Reading the merged 200 as the base gives 400.
            Assert.Equal(300, ValueOf(Engine.MergedStats(armor), StatDefense));
        }

        [Fact]
        public void A_stat_that_cancels_to_zero_is_absent_rather_than_zero()
        {
            // Absent and 0 read the same way to a summing consumer, but not to one applying an
            // "at most N" bound — a leaked zero would satisfy every such bound.
            var ring = new Unit();
            ring.UnitType = 4;
            ring.ClassId = Items.ClassIdForCode("rin");
            ring.Quality = ItemQualityNo.Magic;
            ring.ItemFlags = ItemRecordFlags.Identified;

            ring.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic).Add(StatFireResist, 20));
            ring.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic).Add(StatFireResist, -20));

            Assert.DoesNotContain(
                Engine.MergedStats(ring).Stats, s => s.StatId == StatFireResist);
        }

        [Fact]
        public void An_UNEARNED_set_tier_is_excluded_even_with_bonuses_on()
        {
            // The opt-in reads the record's own tiers, and an unearned tier keeps STATLIST_SET
            // while an earned one has it cleared. That bit — not the state number — is what
            // separates them, and every captured set item carries both kinds.
            Unit crest = CrestWithUm(LocationStash);

            crest.StatsLists.Add(
                new UnitStatList(ItemStatListStates.ItemSet1, ItemStatListFlags.Magic)
                    .Add(StatFireResist, 50));
            crest.StatsLists.Add(
                new UnitStatList(
                        ItemStatListStates.ItemSet1 + 1,
                        ItemStatListFlags.Magic | ItemStatListFlags.Set)
                    .Add(StatFireResist, 500));

            var withBonuses = new MergedStatsOptions();
            withBonuses.IncludeSetBonuses = true;

            // 15 own + 15 rune + 50 earned. The unearned 500 stays out.
            Assert.Equal(80, ValueOf(Engine.MergedStats(crest, withBonuses), StatFireResist));
        }

        [Fact]
        public void Stats_come_back_layer_major()
        {
            // The key is `(layer << 16) | stat`, so ascending key order sorts by LAYER first. A
            // caller binary-searching by stat id would be wrong, which is why the order is
            // documented and pinned rather than left to the dictionary.
            var amulet = new Unit();
            amulet.UnitType = 4;
            amulet.ClassId = Items.ClassIdForCode("amu");
            amulet.Quality = ItemQualityNo.Magic;
            amulet.ItemFlags = ItemRecordFlags.Identified;

            amulet.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic)
                    .Add(127, 1)
                    .Add(83, 2, 1)
                    .Add(StatFireResist, 20));

            var order = Engine.MergedStats(amulet).Stats
                .Select(s => s.StatId + "/" + s.Layer)
                .ToList();

            Assert.Equal(new[] { "39/0", "127/0", "83/1" }, order);
        }

        [Fact]
        public void A_fillers_own_contribution_is_available_separately()
        {
            // Option B's half: what to store against the filler's own row, so "which item has a gem
            // granting magic find" is answerable and a per-socket breakdown has something to show.
            Unit crest = CrestWithUm(LocationStash);

            var um = Engine.SocketFillerStats(crest.Items[0], crest).ToList();

            Assert.Equal(15, um.Single(s => s.StatId == StatFireResist).Value);

            // Keyed by the HOST: an Um is +22 all resist in a shield and +15 in a helm, and the
            // difference is gems.txt `gemapplytype`, which is why the host has to be passed.
            var shield = new Unit();
            shield.UnitType = 4;
            shield.ClassId = Items.ClassIdForCode("lrg");
            shield.ItemFlags = ItemRecordFlags.Identified | ItemRecordFlags.Socketed;

            var inShield = Engine.SocketFillerStats(crest.Items[0], shield).ToList();

            Assert.Equal(22, inShield.Single(s => s.StatId == StatFireResist).Value);
        }
        /// <summary>
        /// A worn set piece keeps its socket fillers. The GAME does not — 0x4c15fd gates a loop on
        /// quality 5 that detaches the item's stat list and rebuilds it through
        /// ITEM_ProcessSetItemEquip, so the character is granted 15 rather than 30.
        /// </summary>
        [Fact]
        public void A_worn_set_piece_keeps_its_socket_fillers()
        {
            Unit worn = CrestWithUm(LocationEquipped);

            // The Crest grants res-all 15 of its OWN and an Um grants a helm another 15, so the two
            // are indistinguishable by presence — only the NUMBER says whether the rune counted.
            Assert.Contains("All Resistances +30", Mods(Engine.Render(worn)));
            Assert.Equal(30, ValueOf(Engine.MergedStats(worn), StatFireResist));
        }

        [Fact]
        public void Wearing_a_set_piece_does_not_change_what_it_grants()
        {
            // Every surface answers the same question, so nothing about the fillers moves when the
            // piece goes on the body. The set sections legitimately differ — wearing it is what
            // lights a tier — so the modifier block is what is compared.
            Assert.Equal(
                Mods(Engine.Render(CrestWithUm(LocationStash))),
                Mods(Engine.Render(CrestWithUm(LocationEquipped))));

            Assert.Equal(
                ValueOf(Engine.MergedStats(CrestWithUm(LocationStash)), StatFireResist),
                ValueOf(Engine.MergedStats(CrestWithUm(LocationEquipped)), StatFireResist));
        }

        private static string[] Mods(Tooltip tip)
        {
            return Sections(tip, ItemTooltipSection.Modifiers);
        }

        private static string[] Sections(Tooltip tip, ItemTooltipSection section)
        {
            return tip.Lines
                .Where(l => l.Section == section)
                .Select(l => System.Text.RegularExpressions.Regex
                    .Replace(l.Text ?? string.Empty, "ÿc.", string.Empty).TrimEnd('\n'))
                .Where(t => t.Length != 0)
                .ToArray();
        }

    }
}
