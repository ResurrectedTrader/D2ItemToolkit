using System.Collections.Generic;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// ITEM_CalcWeaponAttackSpeed 0x62a710 driven from the embedded AnimData.D2.
    /// </summary>
    public class AttackSpeedTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly AttackSpeedCalculator Calculator =
            new AttackSpeedCalculator(Data, Items);

        private static ItemIdentity Item(string code)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Code = code;
            item.Flags = ItemRecordFlags.Identified;
            return item;
        }

        private static ItemViewer Player(int classId)
        {
            var viewer = new ItemViewer();
            viewer.UnitType = 0;
            viewer.ClassId = classId;
            viewer.Level = 40;
            return viewer;
        }

        // A mercenary is a MONSTER: dwUnitType 1, dwClassId a monstats row. These four are the only
        // rows hireling.txt `Class` names, post-splice (monstats.txt's Expansion divider is row 410,
        // so 271 and 338 are unaffected by it and 560/561 sit after it).
        private const int RogueHireling = 271;    // roguehire, Code "RG", BaseW "hth"
        private const int Act2Hireling = 338;     // act2hire,  Code "GU", BaseW "hth"
        private const int Act3Hireling = 359;     // act3hire,  Code "IW", BaseW "1hs"
        private const int Act5Hireling = 561;     // act5hire2, Code "0A", BaseW "2hs"

        private static ItemViewer Monster(int classId)
        {
            var viewer = new ItemViewer();
            viewer.UnitType = 1;
            viewer.ClassId = classId;
            viewer.Level = 40;
            return viewer;
        }

        private static string SpeedLineFor(ItemViewer viewer, string code, int ias)
        {
            var stats = new Dictionary<int, int>();
            stats[ItemStatReader.PackStatKey(0, 93)] = ias;

            var sections = new RecordSections(
                Data, Items, new ItemTypeTree(Data.ItemTypes), Item(code), viewer, stats,
                null, new Dictionary<int, int>(), null);

            return sections.GetSection(ItemTooltipSection.AttackSpeed);
        }

        [Fact]
        public void The_embedded_animdata_parses()
        {
            Assert.NotNull(Data.AnimData);
            Assert.True(Data.AnimData.RowCount > 1000, "records: " + Data.AnimData.RowCount);
        }

        [Fact]
        public void The_name_hash_is_an_unsigned_byte_sum()
        {
            // 'P' + 'A' = 80 + 65 = 145, and lower case folds first.
            Assert.Equal(145, AnimDataFile.Hash("PA"));
            Assert.Equal(145, AnimDataFile.Hash("pa"));

            // Wraps at 256 rather than widening (0x66a926 accumulates into a byte).
            Assert.Equal(AnimDataFile.Hash("PA"), AnimDataFile.Hash("PA") & 0xFF);
        }

        [Theory]
        [InlineData(0, "AMA11hs")]   // Amazon
        [InlineData(3, "PAA11hs")]   // Paladin
        [InlineData(5, "DZA11hs")]   // Druid — PlrType row 5 after the Expansion divider is dropped
        [InlineData(6, "AIA11hs")]   // Assassin
        public void The_animation_name_is_token_plus_mode_plus_weapon_class(int classId, string name)
        {
            // Short Sword's wclass is lower-case "1hs" in weapons.txt and is copied verbatim;
            // only ANIMDATA_GetRecordByNameHash upper-cases, and it does so on its own copy.
            Assert.Equal(name, Calculator.AnimationName(Item("ssd"), Player(classId)));
        }

        [Fact]
        public void There_is_no_name_without_a_viewer()
        {
            Assert.Null(Calculator.AnimationName(Item("ssd"), null));

            Assert.False(Calculator.TryCalculate(Item("ssd"), null, null, out _));
        }

        [Fact]
        public void A_paladin_short_sword_resolves_to_a_real_animation()
        {
            AnimDataFile.Record record;
            Assert.True(Data.AnimData.TryGet("PAA11HS", out record));
            Assert.True(record.FramesPerDirection > 0);
            Assert.True(record.AnimationSpeed > 0);

            int speed;
            Assert.True(Calculator.TryCalculate(Item("ssd"), Player(3), null, out speed));

            // (frames << 8) / (animSpeed * (0 + 100 + 0) / 100).
            Assert.Equal(
                (record.FramesPerDirection << 8) / record.AnimationSpeed, speed);
        }

        [Fact]
        public void Faster_attack_rate_lowers_the_speed_value()
        {
            var stats = new Dictionary<int, int>();
            stats[ItemStatReader.PackStatKey(0, 93)] = 40;

            int plain;
            int hasted;
            Assert.True(Calculator.TryCalculate(Item("ssd"), Player(3), null, out plain));
            Assert.True(Calculator.TryCalculate(Item("ssd"), Player(3), stats, out hasted));

            Assert.True(hasted < plain, plain + " -> " + hasted);
        }

        [Fact]
        public void An_unknown_animation_falls_back_to_forty_five()
        {
            // A ring has no wclass, so the name degenerates and misses every record. 0x62a7c5
            // returns 45 in that case rather than failing.
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode("rin");

            int speed;
            Assert.True(Calculator.TryCalculate(item, Player(3), null, out speed));
            Assert.Equal(AttackSpeedCalculator.MissingAnimationSpeed, speed);
        }

        [Fact]
        public void A_speed_27_weapon_with_no_player_class_lands_on_bucket_zero()
        {
            // With no player unit the class index is -1 (0x486274), so dword_722078[2*-1] reads
            // dword_721F10's last dword — a 5. 5*(27-10)+5 = 90 then indexes ONE PAST that table,
            // onto dword_722078[0] = 0, and word_721E88[0] is locale 4088. A non-player viewer is
            // a legal call here even though the game never makes it, so this must not throw.
            //
            // The animation is the MONSTER one now, so the tuning stat is against "IWSC1hs" —
            // the Act 3 mercenary, the one hireling whose mode-7 name resolves at all.
            ItemViewer viewer = Monster(Act3Hireling);

            var stats = new Dictionary<int, int>();
            stats[ItemStatReader.PackStatKey(0, 68)] = -34;

            int speed;
            Assert.True(Calculator.TryCalculate(Item("ssd"), viewer, stats, out speed));
            Assert.Equal(27, speed);

            var types = new ItemTypeTree(Data.ItemTypes);
            var sections = new RecordSections(
                Data, Items, types, Item("ssd"), viewer, stats, null, null, null);

            Assert.EndsWith(
                Data.Strings.GetByIndex(SectionStringIds.FirstSpeedWord)
                + Data.Strings.GetByIndex(DescStringIds.Newline),
                sections.GetSection(ItemTooltipSection.AttackSpeed),
                System.StringComparison.Ordinal);
        }

        private static string SpeedLine(int mergedIas, int baseIas)
        {
            var stats = new Dictionary<int, int>();
            stats[ItemStatReader.PackStatKey(0, 93)] = mergedIas;

            var baseStats = new Dictionary<int, int>();
            baseStats[ItemStatReader.PackStatKey(0, 93)] = baseIas;

            var sections = new RecordSections(
                Data, Items, new ItemTypeTree(Data.ItemTypes), Item("ssd"), Player(3),
                stats, null, baseStats, null);

            return sections.GetSection(ItemTooltipSection.AttackSpeed);
        }

        [Fact]
        public void The_speed_word_is_coloured_by_the_attack_rate_BONUS_not_the_total()
        {
            // 0x486224 reads STATLIST_GetStatBonusFromLists 0x625560, which is merged MINUS base
            // (0x625570). An item carrying attack rate on its own BASE array contributes nothing
            // to the bonus, so the word stays uncoloured even though the merged stat is non-zero.
            //
            // No shipped weapon has a base stat 93, so neither the corpus nor the adversarial
            // sweep can tell the two predicates apart — this is the only thing that pins it.
            string marker = ItemTooltipColor.Marker + "3";

            Assert.Contains(marker, SpeedLine(40, 0), System.StringComparison.Ordinal);
            Assert.DoesNotContain(marker, SpeedLine(40, 40), System.StringComparison.Ordinal);

            // A partial bonus still colours it.
            Assert.Contains(marker, SpeedLine(40, 15), System.StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(RogueHireling, "RGSChth")]
        [InlineData(Act2Hireling, "GUSChth")]
        [InlineData(Act3Hireling, "IWSC1hs")]
        [InlineData(Act5Hireling, "0ASC2hs")]
        public void A_mercenary_names_the_animation_from_monstats(int classId, string name)
        {
            // COMPOSIT_BuildCofPath's monster arm 0x64f6db: monstats `Code` (+16) + MonMode[7]
            // `token` (+32) + COMPOSIT_ResolveWeaponClass, which for mode 7 returns monstats2
            // `BaseW` (+16) reached through the `MonStatsEx` link (0x64f0e7). All three offsets and
            // the link were read back out of monstats.bin / monstats2.bin / monmode.bin.
            //
            // MonMode row 7 is CAST — "SC" — not attack. PlrMode row 7 is Attack1. The mode index
            // is the same literal 7 pushed at 0x62a7a2 for both.
            Assert.Equal(name, Calculator.AnimationName(Item("7o7"), Monster(classId)));
        }

        [Fact]
        public void A_mercenarys_weapon_class_comes_from_monstats2_not_from_the_item()
        {
            // The caller seeds *a7 with the item's own wclass at 0x62a79c, but the monster arm
            // OVERWRITES it (0x64f751) — unlike the player arm, which keeps it because a9 is 0.
            // So swapping a two-handed polearm for a one-handed sword changes nothing.
            Assert.Equal(
                Calculator.AnimationName(Item("7o7"), Monster(Act2Hireling)),
                Calculator.AnimationName(Item("ssd"), Monster(Act2Hireling)));
        }

        [Fact]
        public void A_mercenarys_ogre_axe_still_writes_the_line()
        {
            // The reported symptom is a null line, because the name was built from
            // PlrType[classId] and no PlrType row 338 exists.
            //
            // "GUSChth" is not in AnimData.D2 — the Act 2 mercenary has no cast animation — so
            // ANIMDATA_GetFramesSpeedAndTrigger fails and 0x62a7c5 returns 45. 45 >= 28 takes the
            // 0x486231 arm, bucket 5, word_721E88[15] = locale 4093.
            Assert.Equal(
                "Polearm Class - ÿc3Very Slow Attack Speed\n",
                SpeedLineFor(Monster(Act2Hireling), "7o7", 30));

            int speed;
            Assert.True(
                Calculator.TryCalculate(Item("7o7"), Monster(Act2Hireling), null, out speed));
            Assert.Equal(AttackSpeedCalculator.MissingAnimationSpeed, speed);
        }

        [Fact]
        public void The_act_three_mercenary_is_the_one_hireling_whose_animation_resolves()
        {
            // "IWSC1hs" IS in AnimData.D2 (18 frames at speed 256), so this one takes the real
            // arithmetic rather than the 45 fallback — which is what keeps the fallback from being
            // the only thing the monster arm is ever tested through.
            Assert.Equal(
                "Polearm Class - ÿc3Fast Attack Speed\n",
                SpeedLineFor(Monster(Act3Hireling), "7o7", 30));
        }

        [Fact]
        public void The_line_is_timed_against_the_CLIENT_PLAYER_not_the_viewer()
        {
            // INV_FormatAttackSpeedText never reads the tooltip's own unit. It calls
            // GetPlayerUnit_0 (0x463de0) twice — 0x486201 for the frame lookup and 0x486250 for the
            // bucket's class offset — so hovering a MERC's polearm shows the speed the CHARACTER
            // would swing it at. The merc is still the viewer for everything else on the tooltip,
            // which is why the two units have to be supplied separately.
            //
            // A real capture is what settled this: the game drew `Very Fast` for a merc-equipped
            // Bonehew, and the merc's own animation ("GUSChth", absent from AnimData.D2) gives
            // `Very Slow`.
            var stats = new Dictionary<int, int>();
            stats[ItemStatReader.PackStatKey(0, 93)] = 30;

            var types = new ItemTypeTree(Data.ItemTypes);

            var withoutPlayer = new RecordSections(
                Data, Items, types, Item("7o7"), Monster(Act2Hireling), stats, null, null, null);

            var withPlayer = new RecordSections(
                Data, Items, types, Item("7o7"), Monster(Act2Hireling), stats, null, null, null,
                Player(1));

            Assert.Equal(
                "Polearm Class - ÿc3Very Slow Attack Speed\n",
                withoutPlayer.GetSection(ItemTooltipSection.AttackSpeed));

            Assert.Equal(
                "Polearm Class - ÿc3Very Fast Attack Speed\n",
                withPlayer.GetSection(ItemTooltipSection.AttackSpeed));
        }

        [Fact]
        public void A_player_viewer_is_untouched_by_the_monster_arm()
        {
            Assert.Equal("PAA1stf", Calculator.AnimationName(Item("7o7"), Player(3)));

            Assert.Equal(
                "Polearm Class - ÿc3Very Fast Attack Speed\n",
                SpeedLineFor(Player(3), "7o7", 30));
        }

        [Fact]
        public void A_viewer_that_is_neither_player_nor_monster_has_no_name()
        {
            // 0x64f5d1: unit types other than 0, 1 and 2 fall straight out of the switch with the
            // name buffer never written. Objects (2) have an arm, but no item is ever described
            // against one, so it is not modelled either.
            var objectViewer = new ItemViewer();
            objectViewer.UnitType = 2;
            objectViewer.ClassId = Act2Hireling;
            Assert.Null(Calculator.AnimationName(Item("7o7"), objectViewer));

            // 0x64f6e6 range-checks the class against the monstats record count and, failing it,
            // returns leaving the buffer uninitialised. There is nothing defined to reproduce.
            var unknownMonster = new ItemViewer();
            unknownMonster.UnitType = 1;
            unknownMonster.ClassId = Data.MonsterStats.RowCount;
            Assert.Null(Calculator.AnimationName(Item("7o7"), unknownMonster));
        }
    }
}
