using Xunit;

namespace D2ItemToolkit.Tests
{
    public class CubeMainTableTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        [Fact]
        public void CubeMain_txt_is_loaded_from_the_embedded_resources()
        {
            Assert.NotNull(Data.CubeMain);
            Assert.Equal(151, Data.CubeMain.RowCount);
        }

        [Fact]
        public void The_crafted_recipes_are_the_rows_whose_output_is_crf()
        {
            int crafted = 0;
            for (int row = 0; row < Data.CubeMain.RowCount; ++row)
            {
                if (Data.CubeMain.GetString(row, "output").Contains("crf"))
                {
                    ++crafted;

                    // Every one of them ships enabled, so none can be dismissed as unreachable.
                    Assert.Equal(1, Data.CubeMain.GetInt(row, "enabled"));
                }
            }

            Assert.Equal(36, crafted);
        }

        [Fact]
        public void A_crafted_recipe_carries_its_fixed_mods_with_ranges()
        {
            int row = -1;
            for (int i = 0; i < Data.CubeMain.RowCount; ++i)
            {
                if (Data.CubeMain.GetString(i, "description").Contains("hitpower helm"))
                {
                    row = i;
                    break;
                }
            }

            Assert.True(row >= 0);

            // These are the mods the recipe adds on top of the random affixes it also rolls, and
            // the only record of their ranges.
            Assert.Equal("thorns", Data.CubeMain.GetString(row, "mod 2").Trim());
            Assert.Equal(3, Data.CubeMain.GetInt(row, "mod 2 min"));
            Assert.Equal(7, Data.CubeMain.GetInt(row, "mod 2 max"));

            Assert.Equal("ac-miss", Data.CubeMain.GetString(row, "mod 3").Trim());
            Assert.Equal(25, Data.CubeMain.GetInt(row, "mod 3 min"));
            Assert.Equal(50, Data.CubeMain.GetInt(row, "mod 3 max"));
        }

        [Fact]
        public void Min_and_max_are_not_always_a_range()
        {
            int row = -1;
            for (int i = 0; i < Data.CubeMain.RowCount; ++i)
            {
                if (Data.CubeMain.GetString(i, "description").Contains("hitpower helm"))
                {
                    row = i;
                    break;
                }
            }

            Assert.True(row >= 0);

            // gethit-skill is a func-11 property, which reads min as the chance and max as the
            // skill level rather than as the two ends of one range. Hence min > max here, and
            // hence a range reconstruction must switch on the property's func rather than
            // assuming every {min, max} pair is an interval.
            Assert.Equal("gethit-skill", Data.CubeMain.GetString(row, "mod 1").Trim());
            Assert.Equal(5, Data.CubeMain.GetInt(row, "mod 1 min"));
            Assert.Equal(4, Data.CubeMain.GetInt(row, "mod 1 max"));
        }
    }
}
