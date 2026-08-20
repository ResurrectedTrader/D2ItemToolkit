using Xunit;

namespace D2ItemToolkit.Tests
{
    public class ItemTypeTreeTests
    {
        private static readonly ItemTypeTree Tree =
            new ItemTypeTree(D2DataFiles.LoadEmbedded().ItemTypes);

        // Resolved by CODE, never by a hard-coded index: the engine's 45/50/57 are just where these
        // codes land in the compiled table, and the row count differs between shipped versions.
        private static readonly int Blunt = Tree.Row("blun");
        private static readonly int Weapon = Tree.Row("weap");
        private static readonly int AnyArmor = Tree.Row("armo");

        [Fact]
        public void The_codes_the_engine_tests_all_resolve()
        {
            Assert.True(Blunt >= 0);
            Assert.True(Weapon >= 0);
            Assert.True(AnyArmor >= 0);
            Assert.Equal(-1, Tree.Row("nosuchcode"));
            Assert.Equal(-1, Tree.Row(null));
            Assert.Equal(Tree.RowCount, D2DataFiles.LoadEmbedded().ItemTypes.RowCount);
        }

        [Fact]
        public void A_type_is_under_itself()
        {
            Assert.True(Tree.IsUnder(Blunt, Blunt));
        }

        [Fact]
        public void The_blunt_closure_reaches_every_leaf_through_both_hops()
        {
            // One hop: Equiv1 = blun.
            foreach (string code in new[] { "club", "hamm", "mace" })
            {
                Assert.True(Tree.IsUnder(Tree.Row(code), Blunt), code);
            }

            // Two hops: scep/wand/staf -> rod -> blun. This is the case a naive
            // direct-children test gets wrong.
            Assert.True(Tree.IsUnder(Tree.Row("rod"), Blunt));
            foreach (string code in new[] { "scep", "wand", "staf" })
            {
                Assert.True(Tree.IsUnder(Tree.Row(code), Blunt), code);
            }

            // Edged weapons are not blunt.
            foreach (string code in new[] { "swor", "axe", "bow", "helm" })
            {
                Assert.False(Tree.IsUnder(Tree.Row(code), Blunt), code);
            }
        }

        [Fact]
        public void The_closure_is_transitive_up_to_the_roots()
        {
            Assert.True(Tree.IsUnder(Tree.Row("club"), Tree.Row("mele")));
            Assert.True(Tree.IsUnder(Tree.Row("club"), Weapon));
            Assert.True(Tree.IsUnder(Tree.Row("helm"), AnyArmor));
        }

        [Fact]
        public void A_second_type_is_only_consulted_when_it_is_positive()
        {
            int club = Tree.Row("club");
            int sword = Tree.Row("swor");

            Assert.True(Tree.IsOfType(sword, club, Blunt));
            Assert.False(Tree.IsOfType(sword, -1, Blunt));

            // Row 0 is never retried: the game requires the second type to be > 0.
            Assert.False(Tree.IsOfType(sword, 0, Blunt));

            // A hit on the first type short-circuits.
            Assert.True(Tree.IsOfType(club, -1, Blunt));
        }

        [Fact]
        public void Out_of_range_rows_are_not_under_anything()
        {
            Assert.False(Tree.IsUnder(-1, Blunt));
            Assert.False(Tree.IsUnder(999, Blunt));
            Assert.False(Tree.IsUnder(Blunt, -1));
            Assert.False(Tree.IsUnder(Blunt, 999));
        }

        [Fact]
        public void Some_rows_declare_a_second_parent_so_the_walk_is_a_dag()
        {
            TxtFile types = D2DataFiles.LoadEmbedded().ItemTypes;

            int withEquiv2 = 0;
            for (int row = 0; row < types.RowCount; ++row)
            {
                if (types.GetString(row, "Equiv2").Trim().Length != 0)
                {
                    ++withEquiv2;
                }
            }

            Assert.True(withEquiv2 > 0, "no row has Equiv2; a chain walk would suffice");
        }
    }
}
