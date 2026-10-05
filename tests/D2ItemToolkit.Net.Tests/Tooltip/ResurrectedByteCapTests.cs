using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// D2R's 1023-byte buffers: the modifier walk's strlcat (sub_1401E8BE0, 0x1401e91f6) and the
    /// colour wrap (D2RGFX_D2R_Text_ApplyColorCode 0x14008c9f0) leave 1019 bytes of modifier text.
    /// A socketed Arm of King Leoric in ruRU is past it.
    /// </summary>
    public class ResurrectedByteCapTests
    {
        private static readonly TooltipEngine Russian = TooltipEngine.ForVariant(
            GameVariant.ReignOfTheWarlock, new ResurrectedTextOptions { Language = "ruRU" });

        private static Unit Leoric(bool socketed)
        {
            int row = Russian.Data.UniqueItems.FindRow("index", "Arm of King Leoric");
            var item = new Unit();
            item.UnitType = 4;
            item.Quality = ItemQualityNo.Unique;
            item.FileIndex = row;
            item.ClassId = Russian.Items.ClassIdForCode(Russian.Data.UniqueItems.GetString(row, "code").Trim());
            item.ItemFlags = ItemRecordFlags.Identified | (socketed ? ItemRecordFlags.Socketed : 0);
            item.ItemLevel = 85;

            // Every rolled stat at its high end, as the game would hold a max roll.
            var mods = new UnitStatList(0, ItemStatListFlags.Magic);
            foreach (RolledStatRange range in Russian.Ranges(item).Stats)
            {
                mods.Add(range.StatId, range.High, range.Layer);
            }

            item.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended)
                .Add(21, 10).Add(22, 22).Add(72, 50).Add(73, 50).Add(194, socketed ? 1 : 0));
            item.StatsLists.Add(mods);

            if (socketed)
            {
                var jewel = new Unit();
                jewel.UnitType = 4;
                jewel.ClassId = Russian.Items.ClassIdForCode("jew");
                jewel.Quality = ItemQualityNo.Magic;
                jewel.ItemFlags = ItemRecordFlags.Identified;
                jewel.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Magic).Add(93, 15));
                item.Items.Add(jewel);
            }

            return item;
        }

        private static Unit Necromancer()
        {
            var viewer = new Unit();
            viewer.UnitType = 0;
            viewer.ClassId = 2;
            viewer.StatsLists.Add(new UnitStatList(0, 0).Add(0, 200).Add(2, 200).Add(12, 90));
            return viewer;
        }

        [Fact]
        public void Unsocketed_the_block_is_whole()
        {
            string[] lines = Russian.Render(Leoric(false), Necromancer()).Text.Split('\n');
            Assert.Contains(
                "Вероятность 5% применить умение «Костяной дух» 10-го уровня при получении урона", lines);
        }

        [Fact]
        public void A_jewel_pushes_the_top_modifier_row_past_the_cap_and_glues_the_next_buffer_on()
        {
            string[] lines = Russian.Render(Leoric(true), Necromancer()).Text.Split('\n');

            Assert.DoesNotContain(
                "Вероятность 5% применить умение «Костяной дух» 10-го уровня при получении урона", lines);
            Assert.Single(lines, line => line.StartsWith(
                "Вероятность 5% применить умение «Костяной дух» 10-го уровня при получении урон"
                + ItemTooltipColor.Marker));
        }

        [Fact]
        public void The_glued_row_is_exact()
        {
            // 4 + 1022 bytes: the wrap drops the last three, "а\n", of the top modifier row.
            Assert.Contains(
                "Вероятность 5% применить умение «Костяной дух» 10-го уровня при получении урон"
                + ItemTooltipColor.Marker + "0Посох – " + ItemTooltipColor.Marker + "3высокая скорость атаки",
                Russian.Render(Leoric(true), Necromancer()).Text.Split('\n'));
        }

        [Fact]
        public void English_is_far_under_the_cap()
        {
            TooltipEngine english = TooltipEngine.ForVariant(GameVariant.ReignOfTheWarlock);
            Assert.Contains(
                "5% Chance to cast level 10 Bone Spirit when struck",
                english.Render(Leoric(true), Necromancer()).Text.Split('\n'));
        }

        // Griswold's Redemption, 4 sockets, the first four RotW unique Colossal Jewels at max roll.
        private const string RedemptionWithColossalJewels = @"{ ""unitType"": 4, ""classId"": 213, ""quality"": 5, ""itemFlags"": 2064, ""fileIndex"": 83, ""itemLevel"": 99, ""location"": 0, ""statsLists"": [ { ""stateNo"": 0, ""flags"": 2147483648, ""stats"": [ { ""id"": 72, ""value"": 250 } ] }, { ""stateNo"": 0, ""flags"": 64, ""stats"": [ { ""id"": 17, ""layer"": 0, ""value"": 240 }, { ""id"": 18, ""layer"": 0, ""value"": 240 }, { ""id"": 91, ""layer"": 0, ""value"": -20 }, { ""id"": 93, ""layer"": 0, ""value"": 40 }, { ""id"": 122, ""layer"": 0, ""value"": 200 }, { ""id"": 194, ""layer"": 0, ""value"": 4 } ] } ] , ""items"": [ { ""unitType"": 4, ""classId"": 690, ""quality"": 7, ""fileIndex"": 420, ""itemFlags"": 16, ""statsLists"": [ { ""stateNo"": 0, ""flags"": 64, ""stats"": [ { ""id"": 57, ""layer"": 0, ""value"": 975 }, { ""id"": 58, ""layer"": 0, ""value"": 975 }, { ""id"": 59, ""layer"": 0, ""value"": 25 }, { ""id"": 79, ""layer"": 0, ""value"": 50 }, { ""id"": 80, ""layer"": 0, ""value"": 35 }, { ""id"": 85, ""layer"": 0, ""value"": 5 }, { ""id"": 326, ""layer"": 0, ""value"": 1 }, { ""id"": 332, ""layer"": 0, ""value"": 10 }, { ""id"": 336, ""layer"": 0, ""value"": 10 }, { ""id"": 201, ""layer"": 4377, ""value"": 1 } ] } ] }, { ""unitType"": 4, ""classId"": 690, ""quality"": 7, ""fileIndex"": 421, ""itemFlags"": 16, ""statsLists"": [ { ""stateNo"": 0, ""flags"": 64, ""stats"": [ { ""id"": 50, ""layer"": 0, ""value"": 1 }, { ""id"": 51, ""layer"": 0, ""value"": 75 }, { ""id"": 79, ""layer"": 0, ""value"": 50 }, { ""id"": 80, ""layer"": 0, ""value"": 35 }, { ""id"": 85, ""layer"": 0, ""value"": 5 }, { ""id"": 330, ""layer"": 0, ""value"": 10 }, { ""id"": 334, ""layer"": 0, ""value"": 10 }, { ""id"": 201, ""layer"": 15065, ""value"": 1 } ] } ] }, { ""unitType"": 4, ""classId"": 690, ""quality"": 7, ""fileIndex"": 422, ""itemFlags"": 16, ""statsLists"": [ { ""stateNo"": 0, ""flags"": 64, ""stats"": [ { ""id"": 54, ""layer"": 0, ""value"": 10 }, { ""id"": 55, ""layer"": 0, ""value"": 30 }, { ""id"": 56, ""layer"": 0, ""value"": 125 }, { ""id"": 79, ""layer"": 0, ""value"": 50 }, { ""id"": 80, ""layer"": 0, ""value"": 35 }, { ""id"": 85, ""layer"": 0, ""value"": 5 }, { ""id"": 331, ""layer"": 0, ""value"": 10 }, { ""id"": 335, ""layer"": 0, ""value"": 10 }, { ""id"": 201, ""layer"": 2585, ""value"": 1 } ] } ] }, { ""unitType"": 4, ""classId"": 690, ""quality"": 7, ""fileIndex"": 423, ""itemFlags"": 16, ""statsLists"": [ { ""stateNo"": 0, ""flags"": 64, ""stats"": [ { ""id"": 48, ""layer"": 0, ""value"": 20 }, { ""id"": 49, ""layer"": 0, ""value"": 60 }, { ""id"": 79, ""layer"": 0, ""value"": 50 }, { ""id"": 80, ""layer"": 0, ""value"": 35 }, { ""id"": 85, ""layer"": 0, ""value"": 5 }, { ""id"": 329, ""layer"": 0, ""value"": 10 }, { ""id"": 333, ""layer"": 0, ""value"": 10 }, { ""id"": 201, ""layer"": 2969, ""value"": 1 } ] } ] } ] }";

        [Fact]
        public void On_the_set_path_the_socket_text_shares_the_budget()
        {
            // UI_DrawSetItemDescBox 0x1401d49ad / 0x1401d49fb / 0x1401d4da7: socket text (24 bytes)
            // and modifiers in one wrapped buffer, so 995 bytes of modifiers survive, not 1023.
            Assert.Contains(
                "Вероятность 1% применить умение «Ледяной доспех» 25-го уровня при получении уро"
                + ItemTooltipColor.Marker + "0Требуемый уровень: 75-й",
                Russian.Render(UnitJson.Read(RedemptionWithColossalJewels), Necromancer()).Text.Split('\n'));
        }
    }
}
