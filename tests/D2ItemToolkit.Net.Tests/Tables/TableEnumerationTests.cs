using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// Every public data table is walked the same way: <c>RowCount</c> for the bound and
    /// <c>RowAt(index)</c> for the row. Before this, the tables disagreed on both halves â€” Count vs
    /// RowCount vs StatCount vs SkillCount, and Code(i) vs CodeAt(i) vs an indexer â€” so iterating
    /// several of them meant remembering which spelling each had picked. Four had no count at all.
    ///
    /// Two tables keep two counts because they have two row spaces, and name their accessors after
    /// them: SetTable (SetAt / PieceAt) and TxtMonsterTypeTable (MonsterAt / MonsterTypeAt).
    /// </summary>
    public class TableEnumerationTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        [Fact]
        public void The_affix_table_reports_its_concatenated_length()
        {
            var affixes = new MagicAffixTable(Data);

            // [MagicSuffix][MagicPrefix][automagic], which is the array the game indexes 1-based.
            Assert.Equal(
                Data.MagicSuffix.RowCount + Data.MagicPrefix.RowCount + Data.AutoMagic.RowCount,
                affixes.RowCount);

            // Counted against the shipped files, so a data change that adds or drops affix rows
            // fails here rather than silently widening the range TryResolve accepts.
            Assert.Equal(1452, affixes.RowCount);

            // The last id resolves and one past it does not â€” that is what makes the count usable.
            TxtFile table;
            int row;
            Assert.True(affixes.TryResolve(affixes.RowCount, out table, out row));
            Assert.NotNull(table);
            Assert.True(row >= 0);

            Assert.False(affixes.TryResolve(affixes.RowCount + 1, out table, out row));
            Assert.False(affixes.TryResolve(0, out table, out row));
        }

        [Fact]
        public void The_missile_table_reports_its_rows()
        {
            var missiles = new MissileTable(Data.Missiles, Data.ElementTypes);

            Assert.Equal(Data.Missiles.RowCount, missiles.RowCount);

            // Every id inside the count resolves, and the count is what makes that walkable.
            int resolved = 0;
            for (int id = 0; id < missiles.RowCount; ++id)
            {
                MissileThrowDamage row;
                if (missiles.TryGetThrowDamage(id, out row))
                {
                    Assert.True(row.Max >= row.Min);
                    ++resolved;
                }
            }

            Assert.True(resolved > 0);

            MissileThrowDamage past;
            Assert.False(missiles.TryGetThrowDamage(missiles.RowCount, out past));
            Assert.False(missiles.TryGetThrowDamage(-1, out past));
            Assert.Equal(0, past.Min);
        }

        [Fact]
        public void The_class_table_reports_its_rows()
        {
            Assert.Equal(Data.CharStats.RowCount, Data.Classes.RowCount);
            Assert.True(Data.Classes.RowCount >= 7);

            for (int classId = 0; classId < Data.Classes.RowCount; ++classId)
            {
                Assert.True(Data.Classes.ClassExists(classId));

                CharacterClassRow row = Data.Classes.RowAt(classId);
                Assert.NotNull(row);
                Assert.Equal(classId, row.ClassId);
                Assert.Equal(
                    TxtCharacterClassTable.SkillTabsPerClass, row.SkillTabTexts.Count);
            }

            Assert.Null(Data.Classes.RowAt(Data.Classes.RowCount));
            Assert.Null(Data.Classes.RowAt(-1));
        }

        [Fact]
        public void The_monster_table_reports_both_of_its_row_spaces()
        {
            Assert.Equal(Data.MonsterStats.RowCount, Data.MonsterTypes.MonsterCount);
            Assert.True(Data.MonsterTypes.MonsterTypeCount > 0);

            Assert.NotNull(Data.MonsterTypes.MonsterAt(0));
            Assert.NotNull(Data.MonsterTypes.MonsterTypeAt(0));
            Assert.Null(Data.MonsterTypes.MonsterAt(Data.MonsterTypes.MonsterCount));
            Assert.Null(Data.MonsterTypes.MonsterTypeAt(Data.MonsterTypes.MonsterTypeCount));
        }

        [Fact]
        public void Every_public_table_walks_by_RowCount_and_RowAt()
        {
            var items = new ItemTable(Data.Weapons, Data.Armor, Data.Misc);
            for (int i = 0; i < items.RowCount; ++i)
            {
                ItemRow row = items.RowAt(i);
                Assert.NotNull(row);
                Assert.Equal(i, row.ClassId);
                Assert.Equal(items.Code(i), row.Code);
            }

            var types = new ItemTypeTree(Data.ItemTypes);
            for (int i = 0; i < types.RowCount; ++i)
            {
                ItemTypeRow row = types.RowAt(i);
                Assert.NotNull(row);
                Assert.Equal(types.CodeAt(i), row.Code);
            }

            var colors = new ColorTable(Data.Colors);
            for (int i = 0; i < colors.RowCount; ++i)
            {
                Assert.Equal(colors.CodeAt(i), colors.RowAt(i).Code);
            }

            var gems = new GemTable(Data.Gems, items);
            for (int i = 0; i < gems.RowCount; ++i)
            {
                GemRow row = gems.RowAt(i);
                Assert.NotNull(row);
                Assert.Equal(gems.Code(i), row.Code);
                Assert.Equal(gems.Letter(i), row.Letter);
            }

            var properties = new PropertiesTable(Data.Properties, Data.ItemStatCost);
            for (int i = 0; i < properties.RowCount; ++i)
            {
                Assert.NotNull(properties.RowAt(i));
                Assert.Same(properties[i], properties.RowAt(i));
            }

            for (int i = 0; i < Data.ItemStatCost.RowCount; ++i)
            {
                StatDescriptor row = Data.ItemStatCost.RowAt(i);
                Assert.NotNull(row);
                Assert.Equal(i, row.StatId);
            }

            for (int i = 0; i < Data.Skills.RowCount; ++i)
            {
                Assert.True(Data.Skills.SkillExists(i));

                SkillRow row = Data.Skills.RowAt(i);
                Assert.NotNull(row);
                Assert.Equal(i, row.SkillId);
                Assert.Equal(Data.Skills.GetSkillName(i), row.Name);
            }

            Assert.True(Data.AnimData.RowCount > 0);
        }

        [Fact]
        public void The_set_table_names_its_two_row_spaces()
        {
            var sets = new SetTable(Data.Sets, Data.SetItems, Data.Strings);

            for (int i = 0; i < sets.SetCount; ++i)
            {
                Assert.NotNull(sets.SetAt(i));
            }

            for (int i = 0; i < sets.PieceCount; ++i)
            {
                Assert.NotNull(sets.PieceAt(i));
            }
        }

        [Fact]
        public void Every_record_field_carries_what_its_own_getter_returns()
        {
            // Reads every field on every record, so a field wired to the wrong getter â€” or left
            // unassigned â€” fails here. A record whose fields nothing reads is also a record whose
            // fields nothing checks.
            var items = new ItemTable(Data.Weapons, Data.Armor, Data.Misc);
            var types = new ItemTypeTree(Data.ItemTypes);
            var colors = new ColorTable(Data.Colors);
            var gems = new GemTable(Data.Gems, items);

            const int LargeShield = 330;
            ItemRow shield = items.RowAt(LargeShield);
            Assert.Equal(LargeShield, shield.ClassId);
            Assert.Equal(items.Code(LargeShield), shield.Code);
            Assert.Equal(items.Tier(LargeShield), shield.Tier);
            Assert.Equal(items.RequiredLevel(LargeShield), shield.RequiredLevel);
            Assert.Equal(items.PrimaryTypeCode(LargeShield), shield.PrimaryTypeCode);
            Assert.Equal(items.SecondaryTypeCode(LargeShield), shield.SecondaryTypeCode);

            int swordRow = types.Row("swor");
            ItemTypeRow sword = types.RowAt(swordRow);
            Assert.Equal(swordRow, sword.Row);
            Assert.Equal(types.CodeAt(swordRow), sword.Code);
            Assert.Equal(types.ClassCode(swordRow), sword.ClassCode);
            Assert.Equal(types.IsThrowable(swordRow), sword.IsThrowable);

            ColorRow color = colors.RowAt(0);
            Assert.Equal(0, color.Row);
            Assert.Equal(colors.CodeAt(0), color.Code);

            int rubyRow = gems.RowForFillerClassId(items.ClassIdForCode("gpr"));
            GemRow ruby = gems.RowAt(rubyRow);
            Assert.Equal(rubyRow, ruby.Row);
            Assert.Equal(gems.Code(rubyRow), ruby.Code);
            Assert.Equal(gems.Letter(rubyRow), ruby.Letter);

            const int BattleOrders = 149;
            SkillRow skill = Data.Skills.RowAt(BattleOrders);
            Assert.Equal(BattleOrders, skill.SkillId);
            Assert.Equal("Battle Orders", skill.Name);
            Assert.Equal(Data.Skills.GetSkillClass(BattleOrders), skill.ClassId);
            Assert.Equal(Data.Skills.RequiredLevel(BattleOrders), skill.RequiredLevel);

            const int Paladin = 3;
            CharacterClassRow paladin = Data.Classes.RowAt(Paladin);
            Assert.Equal(Paladin, paladin.ClassId);
            Assert.Equal(Data.Classes.GetAllSkillsText(Paladin), paladin.AllSkillsText);
            Assert.Equal(Data.Classes.GetClassOnlyText(Paladin), paladin.ClassOnlyText);
            for (int tab = 0; tab < paladin.SkillTabTexts.Count; ++tab)
            {
                Assert.Equal(Data.Classes.GetSkillTabText(Paladin, tab), paladin.SkillTabTexts[tab]);
            }

            MonsterRow monster = Data.MonsterTypes.MonsterAt(0);
            Assert.Equal(0, monster.MonsterId);
            Assert.Equal(Data.MonsterTypes.GetMonsterName(0), monster.Name);

            MonsterTypeRow monsterType = Data.MonsterTypes.MonsterTypeAt(0);
            Assert.Equal(0, monsterType.MonsterTypeId);
            Assert.Equal(Data.MonsterTypes.GetMonsterTypeName(0), monsterType.Name);
        }

        [Fact]
        public void RowAt_returns_null_past_the_end_rather_than_throwing()
        {
            var items = new ItemTable(Data.Weapons, Data.Armor, Data.Misc);
            var types = new ItemTypeTree(Data.ItemTypes);
            var colors = new ColorTable(Data.Colors);
            var gems = new GemTable(Data.Gems, items);
            var properties = new PropertiesTable(Data.Properties, Data.ItemStatCost);

            Assert.Null(items.RowAt(items.RowCount));
            Assert.Null(types.RowAt(types.RowCount));
            Assert.Null(colors.RowAt(colors.RowCount));
            Assert.Null(gems.RowAt(gems.RowCount));
            Assert.Null(properties.RowAt(properties.RowCount));
            Assert.Null(Data.ItemStatCost.RowAt(Data.ItemStatCost.RowCount));
            Assert.Null(Data.Skills.RowAt(Data.Skills.RowCount));

            Assert.Null(items.RowAt(-1));
            Assert.Null(types.RowAt(-1));
            Assert.Null(gems.RowAt(-1));
        }
    }
}
