using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace D2ItemToolkit.Tools
{
    /// <summary>
    /// Builds the differential corpus by walking the shipped tables, so the cases exercise real
    /// items rather than what someone thought to write down. Hand-picked cases are added on top for
    /// the branches that only fire on a specific item — the Horadric Cube's usage line, a Skull's
    /// comma-joined socket block, a Voodoo Head's refused smite line, and so on.
    ///
    /// Usage: Corpus &lt;out.json&gt;
    /// </summary>
    public static class Program
    {
        // Set once in Main from the optional variant argument.
        private static D2DataFiles Data;
        private static ItemTable Items;
        private static MagicAffixTable Affixes;

        public static int Main(string[] args)
        {
            GameVariant variant = GameVariant.Lod114d;
            if (args.Length < 1 || (args.Length > 1 && !Enum.TryParse(args[1], out variant)))
            {
                Console.Error.WriteLine("usage: Corpus <out.json> [Lod114d|Resurrected|ReignOfTheWarlock]");
                return 2;
            }

            Data = D2DataFiles.LoadEmbedded(variant);
            Items = new ItemTable(Data.Weapons, Data.Armor, Data.Misc);
            Affixes = new MagicAffixTable(Data);

            var cases = new List<string>();

            if (Data.IsResurrected)
            {
                AddResurrectedCases(cases);
            }

            AddQualitySweep(cases);
            AddSocketCases(cases);
            AddViewerCases(cases);
            AddNamedCases(cases);
            AddThinSectionCases(cases);
            AddSetItemCases(cases);
            AddDescPriorityTieCases(cases);
            AddRollRangeCases(cases);
            AddCraftedRecipeCases(cases);
            AddSetDerivationCases(cases);
            AddSelfStatFillerCases(cases);

            File.WriteAllText(args[0], "[\n  " + string.Join(",\n  ", cases) + "\n]\n");
            Console.WriteLine(cases.Count + " cases");
            return 0;
        }

        /// <summary>
        /// One item of each type crossed with every quality. This is what reaches the naming arms,
        /// the requirement writers and the durability / defense / damage sections.
        /// </summary>
        private static void AddQualitySweep(List<string> cases)
        {
            string[] codes = { "lrg", "ssd", "bsw", "aar", "cap", "gpr", "r08", "tbk", "box", "ne1", "tax" };

            foreach (string code in codes)
            {
                int classId = Items.ClassIdForCode(code);
                if (classId < 0)
                {
                    continue;
                }

                for (int quality = 1; quality <= 9; ++quality)
                {
                    foreach (int flags in new[] { 0, 16, 16 | 0x800, 16 | 0x400000, 16 | 0x4000000 })
                    {
                        cases.Add(Case(
                            code + "-q" + quality + "-f" + flags,
                            Record(classId, quality, flags,
                                "{ \"id\": 31, \"value\": 120 }, { \"id\": 72, \"value\": 40 }, "
                                + "{ \"id\": 73, \"value\": 62 }, { \"id\": 21, \"value\": 8 }, "
                                + "{ \"id\": 22, \"value\": 15 }",
                                "{ \"id\": 39, \"value\": 25 }, { \"id\": 18, \"value\": 150 }, "
                                + "{ \"id\": 17, \"value\": 150 }"),
                            null));
                    }
                }
            }
        }

        /// <summary>Sockets drive their own view, the "Gemmed" name arm and the filler blocks.</summary>
        private static void AddSocketCases(List<string> cases)
        {
            int host = Items.ClassIdForCode("lrg");
            string[] fillers = { "gcv", "gpr", "skz", "r01", "r08", "jew" };

            foreach (string filler in fillers)
            {
                int fillerId = Items.ClassIdForCode(filler);
                if (fillerId < 0)
                {
                    continue;
                }

                cases.Add(Case(
                    "socketed-" + filler,
                    "{ \"unitType\": 4, \"classId\": " + host + ", \"quality\": 2, "
                    + "\"itemFlags\": " + (16 | 0x800) + ", "
                    + "\"statsLists\": [ { \"stateNo\": 0, \"flags\": 2147483648, "
                    + "\"stats\": [ { \"id\": 31, \"value\": 100 }, { \"id\": 194, \"value\": 1 } ] } ], "
                    + "\"items\": [ { \"unitType\": 4, \"classId\": " + fillerId + ", "
                    + "\"statsLists\": [ { \"stateNo\": 0, \"flags\": 64, "
                    + "\"stats\": [ { \"id\": 39, \"value\": 30 } ] } ] } ] }",
                    null));

                // The filler on its own takes the socket-filler description path instead.
                cases.Add(Case(
                    "loose-" + filler,
                    Record(fillerId, 2, 16, string.Empty, string.Empty),
                    null));
            }

            // The CAPTURED shape: a client-side producer never instantiates a filler's mods, so a
            // gem or rune arrives with no chain at all and SocketStatSynthesis rebuilds it from
            // gems.txt. Every host below has a different `gemapplytype`, because the slot comes
            // from the HOST — a rune in a sword and the same rune in a shield are different lines.
            string[] hosts = { "ssd", "cap", "lrg" };

            foreach (string hostCode in hosts)
            {
                int hostId = Items.ClassIdForCode(hostCode);
                if (hostId < 0)
                {
                    continue;
                }

                foreach (string filler in fillers)
                {
                    int fillerId = Items.ClassIdForCode(filler);
                    if (fillerId < 0)
                    {
                        continue;
                    }

                    cases.Add(Case(
                        "synth-" + hostCode + "-" + filler,
                        "{ \"unitType\": 4, \"classId\": " + hostId + ", \"quality\": 2, "
                        + "\"itemFlags\": " + (16 | 0x800) + ", "
                        + "\"statsLists\": [ { \"stateNo\": 0, \"flags\": 2147483648, "
                        + "\"stats\": [ { \"id\": 31, \"value\": 100 } ] } ], "
                        + "\"items\": [ "
                        + "{ \"unitType\": 4, \"classId\": " + fillerId + " }, "
                        + "{ \"unitType\": 4, \"classId\": " + fillerId + " } ] }",
                        Player(3, 50)));
                }
            }
        }

        /// <summary>
        /// A viewer changes class gates, requirement colours, attack speed and every per-level
        /// stat. Levels are chosen to straddle the requirement boundaries.
        /// </summary>
        private static void AddViewerCases(List<string> cases)
        {
            int classId = Items.ClassIdForCode("lrg");

            for (int playerClass = 0; playerClass <= 6; ++playerClass)
            {
                foreach (int level in new[] { 1, 30, 50, 99 })
                {
                    cases.Add(Case(
                        "viewer-c" + playerClass + "-l" + level,
                        Record(classId, 2, 16,
                            "{ \"id\": 31, \"value\": 120 }",
                            "{ \"id\": 39, \"value\": 25 }, { \"id\": 214, \"value\": 16 }"),
                        Player(playerClass, level)));
                }
            }

            // No viewer at all is a legal library call and takes different branches: the speed
            // bucket overrun, the block-chance cap and every per-level stat scaling to zero.
            cases.Add(Case(
                "viewer-none",
                Record(classId, 2, 16, "{ \"id\": 31, \"value\": 120 }",
                    "{ \"id\": 214, \"value\": 16 }"),
                null));
        }

        /// <summary>Branches that only one shipped item reaches.</summary>
        private static void AddNamedCases(List<string> cases)
        {
            // Quest usage lines, the book path, the throwing-potion arm, the smite refusal.
            foreach (string code in new[] { "box", "bkd", "leg", "hdm", "tbk", "ibk", "gpm", "ne1", "pa1" })
            {
                int classId = Items.ClassIdForCode(code);
                if (classId < 0)
                {
                    continue;
                }

                cases.Add(Case(
                    "named-" + code,
                    Record(classId, 2, 16, "{ \"id\": 70, \"value\": 20 }", string.Empty),
                    Player(3, 50)));
            }

            // A runeword: the 0x4000000 arm reads magicPrefix[0] as a locale id, not an affix.
            int crs = Items.ClassIdForCode("crs");
            cases.Add(Case(
                "runeword-ancients-pledge",
                "{ \"unitType\": 4, \"classId\": " + crs + ", \"quality\": 2, "
                + "\"itemFlags\": " + (16 | 0x4000000 | 0x800) + ", "
                + "\"magicPrefix\": [20507, 0, 0], "
                + "\"statsLists\": [ { \"stateNo\": 171, \"flags\": 64, "
                + "\"stats\": [ { \"id\": 39, \"value\": 30 } ] } ] }",
                Player(3, 50)));

            // Set bonuses drive the set views and the refusal in Compose. STATLIST_SET is what
            // separates the two: an unearned tier keeps the bit, an earned one has had it cleared.
            int classIdSet = Items.ClassIdForCode("aar");
            foreach (bool unearned in new[] { true, false })
            {
                cases.Add(Case(
                    "setbonus-" + (unearned ? "unearned" : "earned"),
                    "{ \"unitType\": 4, \"classId\": " + classIdSet + ", \"quality\": 5, "
                    + "\"itemFlags\": 16, \"fileIndex\": 0, "
                    + "\"statsLists\": [ { \"stateNo\": 165, \"flags\": " + (unearned ? 8256 : 64)
                    + ", \"stats\": [ { \"id\": 0, \"value\": 20 } ] } ] }",
                    Player(3, 50)));
            }
        }

        /// <summary>
        /// Sections the sweep above reaches rarely or not at all. Measured from a corpus run:
        /// CharmDescription was never reached, and RuneLetters / AttackSpeed / SmiteOrKickDamage
        /// were in low single figures. A branch nothing exercises is a branch the differential
        /// comparison cannot police.
        /// </summary>
        private static void AddThinSectionCases(List<string> cases)
        {
            // CharmDescription — gated on the charm itemtype, which nothing else in the sweep is.
            foreach (string code in new[] { "cm1", "cm2", "cm3" })
            {
                Add(cases, "charm-" + code, code, 2, 16,
                    string.Empty, "{ \"id\": 39, \"value\": 15 }", Player(3, 50));
            }

            // AttackSpeed needs a WEAPON and a viewer with a class: the animation is keyed on
            // PlrType token + PlrMode + the weapon's wclass.
            foreach (string code in new[] { "ssd", "2hs", "axe", "wnd", "bow", "tax" })
            {
                for (int playerClass = 0; playerClass <= 6; ++playerClass)
                {
                    Add(cases, "speed-" + code + "-c" + playerClass, code, 2, 16,
                        "{ \"id\": 21, \"value\": 5 }, { \"id\": 22, \"value\": 12 }",
                        "{ \"id\": 93, \"value\": 20 }", Player(playerClass, 40));
                }
            }

            // The Barbarian dual-wield arm (BARBARIAN_CheckItemData_b1or2Handed_isTrue 0x62a1e0),
            // which draws BOTH a one-hand and a two-hand line where every other class draws one.
            // A Bastard Sword carries `1or2handed` and `2handed` together, which is what the arm
            // is for. Only one case reached this incidentally, with a 0-to-0 two-hand line, so
            // neither the second line's VALUES nor the order of the two was policed. The four
            // numbers are pairwise distinct so a swapped pair cannot read as correct.
            foreach (int playerClass in new[] { 4, 3 })
            {
                Add(cases, "dualwield-bsw-c" + playerClass, "bsw", 2, 16,
                    "{ \"id\": 21, \"value\": 10 }, { \"id\": 22, \"value\": 25 }, "
                    + "{ \"id\": 23, \"value\": 20 }, { \"id\": 24, \"value\": 40 }",
                    string.Empty, Player(playerClass, 40));
            }

            // Smite is Paladin-and-shield; kick is Assassin-and-boots. Voodoo heads are shields
            // that REFUSE smite because they are class-restricted to Necromancer.
            foreach (string code in new[] { "lrg", "pa1", "ne1", "ne9", "vbt", "xtb" })
            {
                foreach (int playerClass in new[] { 3, 6, 1 })
                {
                    Add(cases, "smite-" + code + "-c" + playerClass, code, 2, 16,
                        "{ \"id\": 31, \"value\": 90 }", "{ \"id\": 20, \"value\": 20 }",
                        Player(playerClass, 60));
                }
            }

            // RuneLetters needs runes actually IN the sockets.
            int crs = Items.ClassIdForCode("crs");
            for (int runes = 1; runes <= 3; ++runes)
            {
                var fillers = new List<string>();
                foreach (string rune in new[] { "r01", "r08", "r14" })
                {
                    if (fillers.Count >= runes)
                    {
                        break;
                    }

                    int runeId = Items.ClassIdForCode(rune);
                    fillers.Add("{ \"unitType\": 4, \"classId\": " + runeId
                        + ", \"statsLists\": [ { \"stateNo\": 0, \"flags\": 64, \"stats\": [] } ] }");
                }

                cases.Add(Case(
                    "runeletters-" + runes,
                    "{ \"unitType\": 4, \"classId\": " + crs + ", \"quality\": 2, "
                    + "\"itemFlags\": " + (16 | 0x800) + ", "
                    + "\"statsLists\": [ { \"stateNo\": 0, \"flags\": 2147483648, "
                    + "\"stats\": [ { \"id\": 194, \"value\": " + runes + " } ] } ], "
                    + "\"items\": [" + string.Join(", ", fillers) + "] }",
                    Player(3, 50)));
            }

            // Elixirs replace the whole modifier block; fileIndex picks the attribute.
            foreach (int fileIndex in new[] { 0, 1, 2, 3, 7, 9, 42 })
            {
                cases.Add(Case(
                    "elixir-" + fileIndex,
                    "{ \"unitType\": 4, \"classId\": " + Items.ClassIdForCode("elx")
                    + ", \"quality\": 2, \"itemFlags\": 16, \"fileIndex\": " + fileIndex
                    + ", \"statsLists\": [ { \"stateNo\": 0, \"flags\": 64, "
                    + "\"stats\": [ { \"id\": 71, \"value\": 5120 } ] } ] }",
                    Player(3, 50)));
            }

            // Throwing potions take a completely different damage arm off missiles.txt.
            foreach (string code in new[] { "gps", "gps", "opl", "ops", "gpm", "opm" })
            {
                Add(cases, "tpot-" + code, code, 2, 16, string.Empty, string.Empty, Player(3, 50));
            }

            // Ears and monster body parts are their own naming arms.
            foreach (int fileIndex in new[] { 0, 3, 4, 6, 7 })
            {
                cases.Add(Case(
                    "ear-" + fileIndex,
                    "{ \"unitType\": 4, \"classId\": " + Items.ClassIdForCode("ear")
                    + ", \"quality\": 2, \"itemFlags\": 16, \"fileIndex\": " + fileIndex
                    + ", \"earLevel\": 42, \"playerName\": \"Bob\", \"statsLists\": [] }",
                    null));
            }

            foreach (string code in new[] { "hrt", "brz", "jaw", "eyz", "hrn", "tal", "flg" })
            {
                foreach (int fileIndex in new[] { -1, 0, 5 })
                {
                    Add(cases, "bodypart-" + code + "-" + fileIndex, code, 2, 16,
                        string.Empty, string.Empty, null, fileIndex);
                }
            }

            // Tomes and scrolls pick their spell from the magic SUFFIX, not the code.
            foreach (string code in new[] { "tbk", "ibk", "tsc", "isc" })
            {
                for (int suffix = 0; suffix <= 2; ++suffix)
                {
                    cases.Add(Case(
                        "spell-" + code + "-s" + suffix,
                        "{ \"unitType\": 4, \"classId\": " + Items.ClassIdForCode(code)
                        + ", \"quality\": 2, \"itemFlags\": 16"
                        + ", \"magicSuffix\": [" + suffix + ", 0, 0]"
                        + ", \"statsLists\": [ { \"stateNo\": 0, \"flags\": 2147483648, "
                        + "\"stats\": [ { \"id\": 70, \"value\": 20 } ] } ] }",
                        Player(3, 50)));
                }
            }

            // Shop modes drive the TransactionCost gate and the book usage lines.
            foreach (int shopMode in new[] { 0, 1, 4, 9, 10 })
            {
                Add(cases, "shop-" + shopMode, "lrg", 2, 16,
                    "{ \"id\": 31, \"value\": 100 }", string.Empty, Player(3, 50));
            }

            // COLOUR MARKERS. Measured from a corpus run: 738 colour-3 markers appeared overall but
            // ZERO on a Defense line, because nothing carried an `ac%` modifier — so base 31 always
            // equalled merged 31 and the marker branch was dead. Each of these moves a base stat
            // through its op-13 percent so the blue number actually fires. The defense marker was a
            // real bug once (audit round 1); an uncovered branch is one the differential cannot
            // police.
            foreach (int percent in new[] { 0, 25, 100, 150 })
            {
                // 16 ac% -> 31 defense.
                Add(cases, "marker-ac-" + percent, "lrg", 2, 16,
                    "{ \"id\": 31, \"value\": 120 }",
                    "{ \"id\": 16, \"value\": " + percent + " }", Player(3, 50));

                // 75 dur% -> 73 max durability. Reaches items through qualityitems.txt only.
                Add(cases, "marker-dur-" + percent, "lrg", 2, 16,
                    "{ \"id\": 72, \"value\": 40 }, { \"id\": 73, \"value\": 62 }",
                    "{ \"id\": 75, \"value\": " + percent + " }", Player(3, 50));

                // 17/18 dmg% -> the weapon damage pairs, one-hand and throw.
                Add(cases, "marker-dmg-" + percent, "ssd", 2, 16,
                    "{ \"id\": 21, \"value\": 8 }, { \"id\": 22, \"value\": 15 }",
                    "{ \"id\": 18, \"value\": " + percent + " }, "
                    + "{ \"id\": 17, \"value\": " + percent + " }", Player(3, 50));

                Add(cases, "marker-throw-" + percent, "tax", 2, 16,
                    "{ \"id\": 159, \"value\": 8 }, { \"id\": 160, \"value\": 12 }",
                    "{ \"id\": 18, \"value\": " + percent + " }, "
                    + "{ \"id\": 17, \"value\": " + percent + " }", Player(3, 50));
            }

            // A raised block chance colours its number too, and the label carries an explicit 0.
            foreach (int toBlock in new[] { 0, 15, 40 })
            {
                Add(cases, "marker-block-" + toBlock, "lrg", 2, 16,
                    "{ \"id\": 31, \"value\": 90 }",
                    "{ \"id\": 20, \"value\": " + toBlock + " }", Player(3, 60));
            }
        }

        /// <summary>
        /// ITEM_BuildSetItemTooltip 0x48d1d0. Nothing else in the corpus reaches it, so the branches
        /// have to be laid out deliberately: each `add func`, an empty and a full piece list, both
        /// bonus blocks present and absent, the shop tail, and the two type gates that make this
        /// writer emit LESS than the generic one.
        ///
        /// `add func` reachability, counted against the shipped setitems.txt (127 post-splice
        /// rows): 44 blank, 82 twos, and exactly ONE row with 1 — Civerb's Ward, row 0. Without
        /// that row in the corpus the per-sibling tier arithmetic at 0x4e6622 is untested by the
        /// differential.
        /// </summary>
        private static void AddSetItemCases(List<string> cases)
        {
            // (setitems row, item code, add func). Angelic Halo is the worked example; Civerb's
            // Ward is the only add func 1; Telling of Beads and Cow King's Hoofs are add func 0,
            // and the latter is a BOOT, which is where the missing Kick Damage line shows.
            var pieces = new[]
            {
                new[] { "52", "rin", "angelic-halo" },
                new[] { "53", "amu", "angelic-wings" },
                new[] { "0", "lrg", "civerbs-ward" },
                new[] { "2", "gsc", "civerbs-cudgel" },
                new[] { "119", "vbt", "cowking-hoofs" },
                new[] { "95", "amu", "telling-of-beads" },
                new[] { "38", "hbt", "sigons-sabot" },
                new[] { "3", "mbt", "hsarus-heel" },

                // Tal Rasha's Horadric Crest, the five-member set. It is the only piece in this
                // list whose set reaches property funcs 21 (`sor`) and 24 (`state`), and its own
                // `add func` is blank, so the derived GOLD block is the only bonus text it draws.
                new[] { "80", "xsk", "talrasha-crest" },
            };

            string tierStats =
                "{ \"stateNo\": 165, \"flags\": 64, \"stats\": [ { \"id\": 39, \"value\": 20 } ] }, "
                + "{ \"stateNo\": 166, \"flags\": 64, \"stats\": [ { \"id\": 41, \"value\": 15 } ] }, "
                + "{ \"stateNo\": 167, \"flags\": 8256, \"stats\": [ { \"id\": 43, \"value\": 12 } ] }";

            foreach (string[] piece in pieces)
            {
                int classId = Items.ClassIdForCode(piece[1]);
                if (classId < 0)
                {
                    continue;
                }

                // Masks chosen to straddle every tier boundary, including 0 (nothing worn) and
                // 0x3F (all six), which is the one that never reaches STATE_ITEMSET6.
                foreach (int mask in new[] { 0x00, 0x01, 0x05, 0x0F, 0x3F })
                {
                    // Three shapes, because the full-set block has three sources in precedence
                    // order: not equipped (no block at all, 0x48d870), equipped with the block
                    // SUPPLIED, and equipped with nothing supplied — the last is the only one that
                    // reaches the ITEMMOD_ApplySetBonuses 0x660120 derivation.
                    foreach (string shape in new[] { "bag", "worn", "derived" })
                    {
                        bool equipped = shape != "bag";

                        cases.Add(SetCase(
                            "set-" + piece[2] + "-m" + mask + "-" + shape,
                            classId, piece[0], tierStats,
                            "{ \"ownedSetItemIds\": [" + piece[0] + ", 53]"
                            + ", \"wornMaskIncludingSelf\": " + mask
                            + ", \"wornMaskExcludingSelf\": " + (mask & ~(1 << 2))
                            + ", \"isEquipped\": " + (equipped ? "true" : "false")
                            + (shape == "worn"
                                ? ", \"fullSetStats\": [ { \"id\": 0, \"value\": 15 }, "
                                  + "{ \"id\": 39, \"value\": 30 } ]"
                                : string.Empty)
                            + " }",
                            Player(3, 50), 0));
                    }
                }
            }

            // The kick gate. RecordSections would hand a Kick Damage line to an ASSASSIN holding
            // boots, and the generic path emits it; this writer wraps the call in
            // `IsOfType(item, 51)` (0x48d681) and so never does. With a Paladin viewer the writer
            // returns null anyway and the gate is dead, which is why the class matters here.
            foreach (string[] boot in new[]
                { new[] { "119", "vbt" }, new[] { "38", "hbt" }, new[] { "3", "mbt" } })
            {
                int bootId = Items.ClassIdForCode(boot[1]);
                if (bootId < 0)
                {
                    continue;
                }

                cases.Add(SetCase(
                    "set-kick-" + boot[1], bootId, boot[0], tierStats,
                    "{ \"wornMaskIncludingSelf\": 7, \"wornMaskExcludingSelf\": 3 }",
                    Player(6, 50), 0));
            }

            // No siblings at all: every piece red, no tier, and the redundant leading marker still
            // in front of the list (0x48d93b).
            cases.Add(SetCase(
                "set-lonely", Items.ClassIdForCode("rin"), "52", tierStats, "{ }",
                Player(3, 50), 0));

            // No viewer: the class gate, the smite gate and every per-level tier scale by zero.
            cases.Add(SetCase(
                "set-no-viewer", Items.ClassIdForCode("rin"), "52", tierStats,
                "{ \"wornMaskIncludingSelf\": 15, \"wornMaskExcludingSelf\": 11 }", null, 0));

            // fileIndex past the 127 records: GetSetItemsLine returns null and the writer draws
            // NOTHING (0x48d397).
            cases.Add(SetCase(
                "set-unknown-piece", Items.ClassIdForCode("rin"), "900", string.Empty, "{ }",
                Player(3, 50), 0));

            // The shop tail is inlined at 0x48da03 rather than routed through
            // INV_FormatItemTooltipWithCost, and mode 4 suppresses the refusal line.
            foreach (int shopMode in new[] { 1, 4, 9, 10 })
            {
                cases.Add(SetCase(
                    "set-shop-" + shopMode, Items.ClassIdForCode("lrg"), "0", tierStats,
                    "{ \"wornMaskExcludingSelf\": 3, \"isEquipped\": true }",
                    Player(3, 50), shopMode));
            }

            // Socketed and ethereal, which share var_4F90 with the modifier block and are gated on
            // the SOCKETED flag alone (0x48d7e6).
            foreach (int flags in new[] { 16, 16 | 0x800, 16 | 0x400000, 16 | 0x800 | 0x400000 })
            {
                cases.Add(Case(
                    "set-buffer-f" + flags,
                    "{ \"unitType\": 4, \"classId\": " + Items.ClassIdForCode("lrg")
                    + ", \"quality\": 5, \"itemFlags\": " + flags + ", \"fileIndex\": 0"
                    + ", \"statsLists\": [ "
                    + "{ \"stateNo\": 0, \"flags\": 2147483648, \"stats\": ["
                    + "{ \"id\": 31, \"value\": 90 }, { \"id\": 194, \"value\": 2 } ] }, "
                    + "{ \"stateNo\": 0, \"flags\": 64, \"stats\": ["
                    + "{ \"id\": 39, \"value\": 22 } ] }, " + tierStats + " ] }",
                    Player(3, 50),
                    "{ \"wornMaskExcludingSelf\": 3, \"isEquipped\": true }"));
            }

            // ITEM_RecalcAllEquippedItems 0x4c1350 detaches an EQUIPPED quality-5 item's whole stat
            // list (0x4c1658) and rebuilds it through ITEM_ApplySocketableAndEquipStats with the
            // SET ITEM as a2 (0x4c1661), which lands on ITEM_ProcessSetItemEquip (0x4c0e06) and
            // never re-applies the fillers — so the GAME shows the same Um's `All Resistances +15`
            // in the backpack and nothing at all when worn. Both engines show it either way and
            // show it either way. Both shapes are here because the render must NOT move between
            // them, which is only policed if the corpus reaches the arm BOTH ways.
            //
            // Tal Rasha's Horadric Crest with an Um, which is the pair a real capture showed.
            // `location` tracks isEquipped so the pair really is worn versus carried, rather than
            // differing only in the set input the fillers no longer read.
            foreach (bool equipped in new[] { false, true })
            {
                cases.Add(Case(
                    "set-socketed-um-" + (equipped ? "worn" : "bag"),
                    "{ \"unitType\": 4, \"classId\": " + Items.ClassIdForCode("xsk")
                    + ", \"quality\": 5, \"itemFlags\": " + (16 | 0x800) + ", \"fileIndex\": 80"
                    + ", \"location\": " + (equipped ? 1 : 3) + ", \"x\": 1"
                    + ", \"statsLists\": [ "
                    + "{ \"stateNo\": 0, \"flags\": 2147483648, \"stats\": ["
                    + "{ \"id\": 31, \"value\": 100 }, { \"id\": 194, \"value\": 1 } ] } ], "
                    + "\"items\": [ { \"unitType\": 4, \"classId\": "
                    + Items.ClassIdForCode("r22") + " } ] }",
                    Player(1, 70),
                    "{ \"wornMaskIncludingSelf\": 23, \"wornMaskExcludingSelf\": 7"
                    + ", \"isEquipped\": " + (equipped ? "true" : "false") + " }"));
            }
        }

        private static string SetCase(
            string name, int classId, string fileIndex, string tierStats, string set,
            string player, int shopMode)
        {
            var lists = new List<string>
            {
                "{ \"stateNo\": 0, \"flags\": 2147483648, \"stats\": [ "
                + "{ \"id\": 31, \"value\": 90 }, { \"id\": 21, \"value\": 6 }, "
                + "{ \"id\": 22, \"value\": 14 }, { \"id\": 72, \"value\": 30 }, "
                + "{ \"id\": 73, \"value\": 44 } ] }",
                "{ \"stateNo\": 0, \"flags\": 64, \"stats\": [ { \"id\": 39, \"value\": 18 } ] }",
            };

            if (tierStats.Length != 0)
            {
                lists.Add(tierStats);
            }

            // The composer reads ShopMode off the context, which RecordSections does not set from
            // the record — so it is carried on the case and the reference passes it through.
            return Case(
                name + (shopMode == 0 ? string.Empty : "-s" + shopMode),
                "{ \"unitType\": 4, \"classId\": " + classId
                + ", \"quality\": 5, \"itemFlags\": 16"
                + ", \"fileIndex\": " + fileIndex
                + ", \"statsLists\": [" + string.Join(", ", lists) + "] }",
                player,
                set,
                shopMode);
        }

        /// <summary>
        /// Two or more stats sharing a descpriority. SORT_ItemDescPriority 0x6379d0 has no
        /// tie-break, so their relative order is whatever the CRT qsort at 0x638571 leaves — and
        /// not one of the other 851 cases carried two members of a tie group, so that permutation
        /// was a branch the differential could not police. A Call to Arms capture found an
        /// ordering bug there that had survived every previous round.
        /// </summary>
        private static void AddDescPriorityTieCases(List<string> cases)
        {
            var byPriority = new SortedDictionary<int, List<int>>();

            foreach (int statId in Data.ItemStatCost.StatIdsByDescPriority)
            {
                StatDescriptor descriptor;
                if (!Data.ItemStatCost.TryGetStat(statId, out descriptor))
                {
                    continue;
                }

                List<int> bucket;
                if (!byPriority.TryGetValue(descriptor.DescPriority, out bucket))
                {
                    bucket = new List<int>();
                    byPriority.Add(descriptor.DescPriority, bucket);
                }

                bucket.Add(statId);
            }

            foreach (KeyValuePair<int, List<int>> group in byPriority)
            {
                if (group.Value.Count < 2)
                {
                    continue;
                }

                var stats = new List<string>();
                int value = 1;

                foreach (int statId in group.Value)
                {
                    stats.Add("{ \"id\": " + statId + ", \"value\": " + value + " }");
                    ++value;
                }

                Add(cases, "tie-p" + group.Key, "lrg", 4, 16, string.Empty,
                    string.Join(", ", stats), Player(0, 40));
            }

            // The captured shape itself: one oskill stat at three layers, tied at priority 81 with
            // Prevent Monster Heal. The layers order within the stat, the qsort orders across it.
            Add(cases, "tie-p81-oskill", "lrg", 4, 16, string.Empty,
                "{ \"id\": 97, \"layer\": 146, \"value\": 1 }, "
                + "{ \"id\": 97, \"layer\": 149, \"value\": 6 }, "
                + "{ \"id\": 97, \"layer\": 155, \"value\": 4 }, "
                + "{ \"id\": 117, \"value\": 1 }",
                Player(0, 40));
        }

        private static void Add(
            List<string> cases, string name, string code, int quality, int flags,
            string baseStats, string modStats, string player, int fileIndex = 0)
        {
            int classId = Items.ClassIdForCode(code);
            if (classId < 0)
            {
                return;
            }

            var lists = new List<string>();
            if (baseStats.Length != 0)
            {
                lists.Add("{ \"stateNo\": 0, \"flags\": 2147483648, \"stats\": [ " + baseStats + " ] }");
            }

            if (modStats.Length != 0)
            {
                lists.Add("{ \"stateNo\": 0, \"flags\": 64, \"stats\": [ " + modStats + " ] }");
            }

            cases.Add(Case(
                name,
                "{ \"unitType\": 4, \"classId\": " + classId
                + ", \"quality\": " + quality
                + ", \"itemFlags\": " + flags
                + ", \"fileIndex\": " + fileIndex
                + ", \"statsLists\": [" + string.Join(", ", lists) + "] }",
                player));
        }

        /// <summary>
        /// Cases that exist for the ROLL-RANGE reconstruction rather than for any rendered line.
        /// Measured against the generated reference, the rest of the corpus reaches source masks
        /// {Base, Affix, Unique, SetItem, Runeword, Socket, Superior} but leaves two things
        /// untouched: the layer-rolling funcs 12 and 36, and every arm that needs an item level. A
        /// branch the corpus never reaches is a branch the differential cannot police, which is how
        /// the colour-3 marker gap survived, so those get explicit cases here.
        /// </summary>
        private static void AddRollRangeCases(List<string> cases)
        {
            // Func 12 (`skill-rand`) and func 36 (`randclassskill`) have exactly one shipped user
            // each, so they are named rather than swept.
            foreach (string index in new[] { "Ormus' Robes", "Hellfire Torch" })
            {
                int row = Data.UniqueItems.FindRow("index", index);
                if (row < 0)
                {
                    continue;
                }

                int classId = Items.ClassIdForCode(Data.UniqueItems.GetString(row, "code").Trim());
                if (classId < 0)
                {
                    continue;
                }

                cases.Add(Case(
                    "layerroll-" + index.Replace("'", string.Empty).Replace(" ", string.Empty),
                    UniqueRecord(classId, row, -1),
                    null));

                // The same item WITH its rolled stat, so the LayerVaries entry has to explain it:
                // a skill inside 36..60 for the robes, a class inside 0..6 for the torch.
                string rolled = index == "Ormus' Robes"
                    ? "{ \"id\": 107, \"layer\": 54, \"value\": 3 }"
                    : "{ \"id\": 83, \"layer\": 4, \"value\": 3 }";
                string record = UniqueRecord(classId, row, -1);
                record = record.Substring(0, record.Length - 4)
                    + ", { \"stateNo\": 0, \"flags\": 64, \"stats\": [ " + rolled + " ] } ] }";
                cases.Add(Case(
                    "layerroll-recorded-" + index.Replace("'", string.Empty).Replace(" ", string.Empty),
                    record,
                    null));
            }

            // A unique whose props are all fixed, plus one with six ranged props: the two extremes
            // of the span logic on the same code path.
            foreach (string index in new[] { "The Eye of Etlich", "Harlequin Crest" })
            {
                int row = Data.UniqueItems.FindRow("index", index);
                int classId = row < 0
                    ? -1
                    : Items.ClassIdForCode(Data.UniqueItems.GetString(row, "code").Trim());

                if (classId >= 0)
                {
                    cases.Add(Case(
                        "ranged-" + index.Replace(" ", string.Empty),
                        UniqueRecord(classId, row, -1),
                        null));
                }
            }

            // The item-level arms, on inputs where the level actually CHANGES the answer. Both were
            // first written against a Crystal Sword and a positive-max `charged`, where neither arm
            // binds: the socket cap was already below every MaxSock tier, and a positive max skips
            // the level derivation entirely. Those cases plumbed the field without exercising it.
            //
            // `aar` is a torso with gemsockets 4 against MaxSock1 3 / MaxSock25 4 / MaxSock40 6, so
            // the tier IS the binding constraint below level 26 and the span moves.
            int sockAffix = FirstAffixWithCode("sock");
            int torso = Items.ClassIdForCode("aar");

            if (sockAffix > 0 && torso >= 0)
            {
                foreach (int itemLevel in new[] { -1, 10, 30, 70 })
                {
                    cases.Add(Case(
                        "ilvl-sock-" + itemLevel,
                        AffixRecord(torso, sockAffix, itemLevel),
                        null));
                }
            }

            // The socket TIER only binds when a roll EXCEEDS it, and a `sock` affix rolls 1..2 —
            // below every tier, so an affix can never show it. Runemaster rolls 3..5 against a base
            // whose MaxSock1 is lower, which is what makes the level move the answer.
            int runemaster = Data.UniqueItems.FindRow("index", "Runemaster");
            if (runemaster >= 0)
            {
                int baseId = Items.ClassIdForCode(
                    Data.UniqueItems.GetString(runemaster, "code").Trim());

                if (baseId >= 0)
                {
                    foreach (int itemLevel in new[] { -1, 10, 30, 70 })
                    {
                        cases.Add(Case(
                            "ilvl-socktier-" + itemLevel,
                            UniqueRecord(baseId, runemaster, itemLevel),
                            null));
                    }
                }
            }

            // 211 of the 464 func-11/19 cells in shipped data carry a NON-POSITIVE max, which is
            // the only arm that derives the skill level from the item's. Picking one of those is
            // what makes the level observable.
            int chargedAffix = FirstAffixWithNonPositiveMax("charged");
            int crs = Items.ClassIdForCode("crs");

            if (chargedAffix > 0 && crs >= 0)
            {
                foreach (int itemLevel in new[] { -1, 20, 60 })
                {
                    cases.Add(Case(
                        "ilvl-charged-" + itemLevel,
                        AffixRecord(crs, chargedAffix, itemLevel),
                        null));
                }
            }

            // Func 10's skill-tab packing and func 18's by-time triple, each on the affix that
            // carries them.
            foreach (string code in new[] { "skilltab", "ac/time" })
            {
                int affix = FirstAffixWithCode(code);
                if (affix > 0 && crs >= 0)
                {
                    cases.Add(Case(
                        "affix-" + code.Replace("/", "-"),
                        AffixRecord(crs, affix, 55),
                        null));
                }
            }

            // The three ValShift 8 stats — life, mana and stamina are stored 8.8 fixed point, and
            // every WRITER shifts them down before printing. Nothing else in the corpus carries a
            // shifted stat, so a span reported in storage units rather than display units — "+11 to
            // Life [2816-3840]" — was invisible to the differential.
            //
            // The stat VALUE is carried too, not just the affix: the reconstruction alone covers
            // the span, but only a record that draws the line puts the annotation in front of it.
            foreach (ShiftedStat shifted in new[]
                     {
                         new ShiftedStat("hp", 7),
                         new ShiftedStat("mana", 9),
                         new ShiftedStat("stam", 11),
                     })
            {
                List<int> ranged = RangedAffixes(shifted.Code);
                if (ranged.Count == 0 || crs < 0)
                {
                    continue;
                }

                cases.Add(Case(
                    "affix-" + shifted.Code,
                    AffixRecord(
                        crs,
                        ranged[0],
                        55,
                        "{ \"id\": " + shifted.StatId + ", \"value\": "
                        + (MidRollOf(ranged[0], shifted.Code) << 8) + " }"),
                    null));
            }
        }

        /// <summary>One itemstatcost row with a non-zero ValShift, and the affix code reaching it.</summary>
        private struct ShiftedStat
        {
            public readonly string Code;
            public readonly int StatId;

            public ShiftedStat(string code, int statId)
            {
                Code = code;
                StatId = statId;
            }
        }

        /// <summary>The midpoint of the roll <paramref name="affix"/> gives <paramref name="code"/>.</summary>
        private static int MidRollOf(int affix, string code)
        {
            TxtFile table;
            int row;
            if (!Affixes.TryResolve(affix, out table, out row))
            {
                return 0;
            }

            for (int mod = 1; mod <= 3; ++mod)
            {
                if (table.GetString(row, "mod" + mod + "code").Trim() == code)
                {
                    return (table.GetInt(row, "mod" + mod + "min")
                            + table.GetInt(row, "mod" + mod + "max")) / 2;
                }
            }

            return 0;
        }

        /// <summary>
        /// Crafted items, whose recipe the reconstruction deduces rather than reads. Nothing else in
        /// the corpus is quality 8, so without these the slot derivation, the item-type fallback and
        /// the all-stats-present filter are outside the differential entirely.
        ///
        /// Stat ids are literal because the recipes' property codes reach them by several different
        /// routes — `dmg%` writes two stats and carries no stat1 cell at all, `gethit-skill` packs
        /// the skill and the level into the LAYER (0x65f54f). CraftedRecipeTests pins each of them.
        /// </summary>
        private static void AddCraftedRecipeCases(List<string> cases)
        {
            const int RedDmg = 34, RedMag = 35, ResLtng = 41, AcPercent = 16;
            const int SkillOnGetHit = 201, Thorns = 78, AcMissile = 32;
            const int LifeSteal = 60, MaxHp = 7, Deadly = 141;
            const int RegenMana = 27, MaxMana = 9, ManaSteal = 62, FasterCast = 105;
            const int MinDamagePercent = 18, MaxDamagePercent = 17;
            const int ResFire = 39;

            // gethit-skill(44) at level 4 — the layer the func 11 handler packs it into.
            const int FrostNovaOnStruck = (4 & 0x3F) + (44 << 6);

            int crown = Items.ClassIdForCode("crn");
            int axe = Items.ClassIdForCode("lax");
            int amulet = Items.ClassIdForCode("amu");
            int bow = Items.ClassIdForCode("swb");
            int charm = Items.ClassIdForCode("cm1");

            // A crafted item always carries affixes as well as its recipe's fixed mods, so most of
            // these roll one and record its stat: the deduction's real job is finding the recipe
            // among stats it does not explain, and a record with no affix never asks it to.
            List<int> ranged = RangedAffixes("res-fire");

            if (crown < 0 || axe < 0 || amulet < 0 || bow < 0 || charm < 0 || ranged.Count == 0)
            {
                return;
            }

            int affix = ranged[0];

            // Affix-free on purpose, and the only one: the same recipe as crafted-with-affix with
            // nothing else in the record, so a divergence between the two separates the recipe's
            // own mods from the affix handling.
            cases.Add(Case("crafted-safety-helm", Crafted(
                crown, 0, Stat(RedDmg, 3), Stat(RedMag, 2), Stat(ResLtng, 8), Stat(AcPercent, 20)),
                null));

            // Func 11's stat lives on a packed layer, so this is the case that proves the match is
            // key-aware rather than stat-id-aware.
            cases.Add(Case("crafted-hitpower-helm", Crafted(
                crown, affix, Stat(SkillOnGetHit, 5, FrostNovaOnStruck), Stat(Thorns, 5),
                Stat(AcMissile, 30), Stat(ResFire, 8)),
                null));

            // A weapon: its four recipes name item TYPES, so this reaches the type-tree fallback.
            cases.Add(Case("crafted-blood-weapon", Crafted(
                axe, affix, Stat(LifeSteal, 3), Stat(MaxHp, 15), Stat(MinDamagePercent, 40),
                Stat(MaxDamagePercent, 40), Stat(ResFire, 8)),
                null));

            // `amul` is a type with no item of that code, so nothing here resolves as an item code.
            cases.Add(Case("crafted-caster-amulet", Crafted(
                amulet, affix, Stat(RegenMana, 6), Stat(MaxMana, 15), Stat(FasterCast, 10),
                Stat(ResFire, 8)),
                null));

            // Two families both fit, so the recipe stays unknown and its mods stay unattributed.
            cases.Add(Case("crafted-ambiguous", Crafted(
                crown, 0, Stat(LifeSteal, 3), Stat(MaxHp, 15), Stat(Deadly, 7),
                Stat(RegenMana, 6), Stat(MaxMana, 15), Stat(ManaSteal, 3)),
                null));

            // A bow IS in a craft slot — itemtypes gives bow -> miss -> weap — so all four weapon
            // recipes are candidates and none of them survives the stats: -1 by zero VIABLE
            // candidates, which is a different arm from -1 by no candidates at all.
            cases.Add(Case("crafted-no-viable-recipe", Crafted(
                bow, 0, Stat(LifeSteal, 3), Stat(MaxHp, 15), Stat(Deadly, 7)),
                null));

            // A small charm is `scha` -> `char` -> `misc`, under none of the nine craft slots, so
            // the slot lookup gives -1 before any candidate is gathered. This is the arm that would
            // regress if the lookup started guessing a slot.
            cases.Add(Case("crafted-no-recipe-slot", Crafted(
                charm, 0, Stat(LifeSteal, 3), Stat(MaxHp, 15), Stat(Deadly, 7)),
                null));

            // The realistic shape on the family whose recipe writes four mods rather than three.
            cases.Add(Case("crafted-with-affix", Crafted(
                crown, affix, Stat(RedDmg, 3), Stat(RedMag, 2), Stat(ResLtng, 8),
                Stat(AcPercent, 20), Stat(ResFire, 12)),
                null));

            AddCraftedSweep(cases, affix);
        }

        /// <summary>
        /// One case per crafted recipe. The eight cases above single out the shapes worth naming;
        /// this is what puts every ROW in front of the differential — six of the nine slots and 30
        /// of the 36 rows were otherwise reached by nothing, so a slot derivation that broke for,
        /// say, belts would have diverged silently.
        ///
        /// Each item carries exactly the stats its recipe writes plus one affix, since finding the
        /// recipe among stats it does not explain is the deduction's actual job.
        /// </summary>
        private static void AddCraftedSweep(List<string> cases, int affix)
        {
            for (int row = 0; row < Data.CubeMain.RowCount; ++row)
            {
                if (!IsCraftedRecipe(row))
                {
                    continue;
                }

                int classId = Items.ClassIdForCode(CraftedBaseCode(row));
                if (classId < 0)
                {
                    continue;
                }

                var stats = new List<string>(CraftedRecipeStats(row));
                stats.Add(Stat(39, 8));

                cases.Add(Case(
                    "craftsweep-" + CraftedName(row).Replace(' ', '-'),
                    Crafted(classId, affix, stats.ToArray()),
                    null));
            }
        }

        private static bool IsCraftedRecipe(int row)
        {
            foreach (string part in
                Data.CubeMain.GetString(row, "output").Replace("\"", string.Empty).Split(','))
            {
                if (part.Trim() == "crf")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The "safety helm" half of the shipped description, for the case name.</summary>
        private static string CraftedName(int row)
        {
            string description = Data.CubeMain.GetString(row, "description");
            return description.Substring(
                description.LastIndexOf("-> ", StringComparison.Ordinal) + 3).Trim();
        }

        /// <summary>
        /// A base the recipe's slot holds. Twelve rows name an item TYPE in `input 1` rather than an
        /// item code, and `amul` / `ring` have no item of that code at all, so a member of the type
        /// stands in for those.
        /// </summary>
        private static string CraftedBaseCode(int row)
        {
            string spec = Data.CubeMain.GetString(row, "input 1").Replace("\"", string.Empty);
            int comma = spec.IndexOf(',');
            string code = (comma < 0 ? spec : spec.Substring(0, comma)).Trim();

            switch (code)
            {
                case "blun": return "clb";
                case "axe": return "lax";
                case "rod": return "wnd";
                case "spea": return "spr";
                case "amul": return "amu";
                case "ring": return "rin";
                default: return code;
            }
        }

        /// <summary>
        /// The stats one recipe writes, derived from the tables rather than restated: each mod
        /// code's properties.txt `stat1` through itemstatcost.txt. Two codes need more, and both are
        /// recognised by their FUNC and thrown on rather than assumed, so a drift that moved either
        /// fails the generator instead of quietly emitting a case that proves nothing — `dmg%` is
        /// func 7 with no `stat1` at all, and `gethit-skill` is func 11, whose stat sits on the
        /// packed layer `(level &amp; 0x3F) + (skill &lt;&lt; 6)` (0x65f54f).
        /// </summary>
        private static List<string> CraftedRecipeStats(int row)
        {
            var stats = new List<string>();

            for (int mod = 1; mod <= 5; ++mod)
            {
                string where = "cubemain row " + row + " mod " + mod;

                string code = Data.CubeMain.GetString(row, "mod " + mod).Trim();
                if (code.Length == 0)
                {
                    continue;
                }

                int property = Data.Properties.FindRow("code", code);
                if (property < 0)
                {
                    throw new InvalidOperationException(where + ": no properties.txt row");
                }

                if (Data.Properties.GetString(property, "stat2").Trim().Length != 0)
                {
                    throw new InvalidOperationException(where + ": writes more than one stat");
                }

                int min = Data.CubeMain.GetInt(row, "mod " + mod + " min");
                int max = Data.CubeMain.GetInt(row, "mod " + mod + " max");
                int func = Data.Properties.GetInt(property, "func1");
                string statName = Data.Properties.GetString(property, "stat1").Trim();

                if (func == 7)
                {
                    stats.Add(Stat(18, Math.Min(min, max)));
                    stats.Add(Stat(17, Math.Min(min, max)));
                    continue;
                }

                int statId = Data.ItemStatCost.StatIdForName(statName);
                if (statId < 0)
                {
                    throw new InvalidOperationException(where + ": no stat named " + statName);
                }

                // Func 11's two cells are a chance and a LEVEL, not a range: the chance is the
                // value and the level rides in the layer alongside the skill. The chance is not
                // shifted — ITEMPROP_AddSkillCharges bypasses ITEMMOD_AddStatToItem entirely.
                if (func == 11)
                {
                    int skill = Data.CubeMain.GetInt(row, "mod " + mod + " param");
                    stats.Add(Stat(statId, min, (max & 0x3F) + (skill << 6)));
                    continue;
                }

                if (func != 1 && func != 2 && func != 8)
                {
                    throw new InvalidOperationException(where + ": unhandled func " + func);
                }

                // Recorded SHIFTED, the way the game stores it: ITEMMOD_AddStatToItem shifts by
                // nValShift before writing (0x65ea50), so `hp` 10..20 reaches the record as
                // 2560..5120. Emitting the unshifted cell put maxhp and maxmana in `outOfRange`,
                // which is the reconstruction correctly saying the record could not have happened.
                stats.Add(Stat(statId, Math.Min(min, max) << ValShift(statId)));
            }

            return stats;
        }

        private static int ValShift(int statId)
        {
            StatDescriptor descriptor;
            return Data.ItemStatCost.TryGetStat(statId, out descriptor) ? descriptor.ValShift : 0;
        }

        private static string Stat(int id, int value, int layer = 0)
        {
            return "{ \"id\": " + id + ", \"value\": " + value
                + (layer == 0 ? string.Empty : ", \"layer\": " + layer) + " }";
        }

        private static string Crafted(int classId, int affixId, params string[] stats)
        {
            return "{ \"unitType\": 4, \"classId\": " + classId
                + ", \"quality\": 8, \"itemFlags\": 16"
                + ", \"fileIndex\": 0, \"itemLevel\": 70"
                + ", \"magicPrefix\": [ " + affixId + ", 0, 0 ]"
                + ", \"statsLists\": [ { \"stateNo\": 0, \"flags\": 64, "
                + "\"stats\": [ " + string.Join(", ", stats) + " ] } ] }";
        }

        /// <summary>
        /// A host whose FILLER carries its own rolled affixes — a jewel. That filler contributes
        /// nothing through gems.txt, so its roll reaches the host only through the jewel's own
        /// affixes, and the merged line holds the SUM of both halves while each separated block
        /// holds one. Six existing cases have a self-stat filler but none has a RANGED affix on it,
        /// so the summing was covered by hand-written tests alone.
        /// </summary>
        private static void AddSelfStatFillerCases(List<string> cases)
        {
            List<int> ranged = RangedAffixes("res-fire");
            if (ranged.Count < 3)
            {
                return;
            }

            int host = Items.ClassIdForCode("xhn");
            int jewelId = Items.ClassIdForCode("jew");
            int gem = Items.ClassIdForCode("gpr");

            if (host < 0 || jewelId < 0 || gem < 0)
            {
                return;
            }

            // The same stat on the item AND on the jewel: the case where a summed span and an
            // own-only span differ, so a view annotating the wrong one is visible.
            cases.Add(Case(
                "jewel-sharedstat",
                SocketedHost(host, ranged[2], new[] { Jewel(jewelId, ranged[0]) }),
                null));

            // A jewel alongside a gem, so the two filler kinds are ranged by different routes in one
            // render — gems.txt for the gem, its own affixes for the jewel.
            cases.Add(Case(
                "jewel-and-gem",
                SocketedHost(host, ranged[2], new[] { Jewel(jewelId, ranged[0]), Gem(gem) }),
                null));

            // A jewel on a host with NO affix of its own, so the merged span is the jewel's alone.
            cases.Add(Case(
                "jewel-only",
                SocketedHost(host, 0, new[] { Jewel(jewelId, ranged[1]) }),
                null));
        }

        /// <summary>
        /// 1-based affix ids carrying this mod code and passing <paramref name="accept"/>,
        /// ascending, one entry per matching MOD — an affix carrying the code twice appears twice.
        /// </summary>
        private static List<int> ScanAffixes(string code, Func<TxtFile, int, int, bool> accept)
        {
            var found = new List<int>();

            for (int id = 1; id <= Affixes.RowCount; ++id)
            {
                TxtFile table;
                int row;
                if (!Affixes.TryResolve(id, out table, out row))
                {
                    continue;
                }

                for (int mod = 1; mod <= 3; ++mod)
                {
                    if (table.GetString(row, "mod" + mod + "code").Trim() == code
                        && (accept == null || accept(table, row, mod)))
                    {
                        found.Add(id);
                    }
                }
            }

            return found;
        }

        /// <summary>1-based ids of affixes carrying this code with a genuine range, ascending.</summary>
        private static List<int> RangedAffixes(string code)
        {
            return ScanAffixes(
                code,
                (table, row, mod) => table.GetInt(row, "mod" + mod + "min")
                    != table.GetInt(row, "mod" + mod + "max"));
        }

        private static string Jewel(int classId, int affixId)
        {
            return "{ \"unitType\": 4, \"classId\": " + classId
                + ", \"quality\": 4, \"itemFlags\": 16"
                + ", \"fileIndex\": 0"
                + ", \"magicPrefix\": [ " + affixId + ", 0, 0 ]"
                + ", \"statsLists\": [ { \"stateNo\": 0, \"flags\": 64, "
                + "\"stats\": [ { \"id\": 39, \"value\": 7 } ] } ] }";
        }

        private static string Gem(int classId)
        {
            return "{ \"unitType\": 4, \"classId\": " + classId
                + ", \"quality\": 2, \"itemFlags\": 16, \"fileIndex\": 0 }";
        }

        private static string SocketedHost(int classId, int affixId, string[] fillers)
        {
            return "{ \"unitType\": 4, \"classId\": " + classId
                + ", \"quality\": 6, \"itemFlags\": " + (16 | 0x800)
                + ", \"fileIndex\": 0, \"itemLevel\": 70"
                + ", \"magicPrefix\": [ " + affixId + ", 0, 0 ]"
                + ", \"statsLists\": [ { \"stateNo\": 0, \"flags\": 2147483648, "
                + "\"stats\": [ { \"id\": 31, \"value\": 300 }, "
                + "{ \"id\": 194, \"value\": " + fillers.Length + " } ] }"
                + ", { \"stateNo\": 0, \"flags\": 64, "
                + "\"stats\": [ { \"id\": 39, \"value\": 15 } ] } ]"
                + ", \"items\": [" + string.Join(", ", fillers) + "] }";
        }

        /// <summary>
        /// The first affix whose mod of this code has a NON-POSITIVE max — the arm that derives its
        /// value from the item's level. An affix with a positive max skips that derivation, so a
        /// case built on one cannot tell whether the level was used.
        /// </summary>
        private static int FirstAffixWithNonPositiveMax(string code)
        {
            List<int> found = ScanAffixes(
                code, (table, row, mod) => table.GetInt(row, "mod" + mod + "max") <= 0);

            return found.Count == 0 ? -1 : found[0];
        }

        /// <summary>The 1-based id of the first affix carrying this mod code, or -1.</summary>
        private static int FirstAffixWithCode(string code)
        {
            List<int> found = ScanAffixes(code, null);

            return found.Count == 0 ? -1 : found[0];
        }

        private static string UniqueRecord(int classId, int fileIndex, int itemLevel)
        {
            return "{ \"unitType\": 4, \"classId\": " + classId
                + ", \"quality\": 7, \"itemFlags\": 16"
                + ", \"fileIndex\": " + fileIndex
                + ", \"itemLevel\": " + itemLevel
                + ", \"statsLists\": [ { \"stateNo\": 0, \"flags\": 2147483648, "
                + "\"stats\": [ { \"id\": 31, \"value\": 120 } ] } ] }";
        }

        private static string AffixRecord(
            int classId, int affixId, int itemLevel, string modStats = "")
        {
            string mods = modStats.Length == 0
                ? string.Empty
                : ", { \"stateNo\": 0, \"flags\": 64, \"stats\": [ " + modStats + " ] }";

            return "{ \"unitType\": 4, \"classId\": " + classId
                + ", \"quality\": 4, \"itemFlags\": 16"
                + ", \"fileIndex\": 0"
                + ", \"itemLevel\": " + itemLevel
                + ", \"magicPrefix\": [ " + affixId + ", 0, 0 ]"
                + ", \"statsLists\": [ { \"stateNo\": 0, \"flags\": 2147483648, "
                + "\"stats\": [ { \"id\": 21, \"value\": 8 }, { \"id\": 22, \"value\": 15 } ] }"
                + mods + " ] }";
        }

        private static string Record(
            int classId, int quality, int flags, string baseStats, string modStats)
        {
            var lists = new List<string>();
            if (baseStats.Length != 0)
            {
                lists.Add("{ \"stateNo\": 0, \"flags\": 2147483648, \"stats\": [ " + baseStats + " ] }");
            }

            if (modStats.Length != 0)
            {
                lists.Add("{ \"stateNo\": 0, \"flags\": 64, \"stats\": [ " + modStats + " ] }");
            }

            return "{ \"unitType\": 4, \"classId\": " + classId
                + ", \"quality\": " + quality
                + ", \"itemFlags\": " + flags
                + ", \"fileIndex\": 0"
                + ", \"statsLists\": [" + string.Join(", ", lists) + "] }";
        }

        private static string Player(int classId, int level, string carried = null)
        {
            return "{ \"unitType\": 0, \"classId\": " + classId
                + ", \"flagsEx\": 33554432"
                + ", \"skills\": [ { \"skill\": 117, \"level\": 10 } ]"
                + (carried == null ? string.Empty : ", \"items\": [ " + carried + " ]")
                + ", \"statsLists\": [ { \"stateNo\": 0, \"flags\": 2147483648, \"stats\": ["
                + "{ \"id\": 12, \"value\": " + level + " }, "
                + "{ \"id\": 0, \"value\": " + (20 + level) + " }, "
                + "{ \"id\": 2, \"value\": " + (20 + level) + " } ] } ] }";
        }

        /// <summary>setitems.txt post-splice, 0-based. `xsk`, a Death Mask.</summary>
        private const int TalRashasHoradricCrest = 80;

        /// <summary>
        /// A set piece as a WEARER carries it, at a given location. `location` 1 is the body and `x`
        /// is then the equip slot, which is what separates a worn piece from one on the alternate
        /// weapon set (11 and 12).
        /// </summary>
        private static string CarriedPiece(int setItemRow, string code, int location, int x)
        {
            int classId = Items.ClassIdForCode(code);
            return "{ \"unitType\": 4, \"classId\": " + classId
                + ", \"quality\": 5, \"itemFlags\": 16"
                + ", \"fileIndex\": " + setItemRow
                + ", \"location\": " + location + ", \"x\": " + x + " }";
        }

        /// <summary>
        /// Set state DERIVED from the viewer rather than handed over as masks. The cases above pass
        /// an explicit `set` object, which is the override path — these leave it out, so the
        /// `annotated` and `socketsSplit` layers compare what Render itself derived.
        ///
        /// The discriminating one is the swap case: GetSetItem takes grid types 1, 3 and 4
        /// (0x4867d4) so the piece is OWNED and green, while the worn mask takes type 3 alone
        /// (0x62a3f0) so it lights no bit. An implementation that conflated the two would light one
        /// bonus tier too many, and only this case would show it.
        /// </summary>
        private static void AddSetDerivationCases(List<string> cases)
        {
            const int Halo = 52, Wings = 53, Mantle = 51, Sickle = 50;
            // Arctic HORN, slot 0 — not Arctic Binding, whose slot 2 collides with Angelic Halo's,
            // so a dropped set-id filter would OR into a bit already set and render identically.
            const int ArcticHorn = 54;

            int ring = Items.ClassIdForCode("rin");
            if (ring < 0)
            {
                return;
            }

            string hovered = "{ \"unitType\": 4, \"classId\": " + ring
                + ", \"quality\": 5, \"itemFlags\": 16, \"fileIndex\": " + Halo
                + ", \"location\": 1, \"x\": 6 }";

            string self = CarriedPiece(Halo, "rin", 1, 6);

            // Worn siblings only: two pieces, so the first partial tier lights.
            cases.Add(Case("setderive-two-worn", hovered,
                Player(1, 40, self + ", " + CarriedPiece(Mantle, "rng", 1, 3))));

            // A third piece in the INVENTORY: owned and green, but no extra tier.
            cases.Add(Case("setderive-inventory-sibling", hovered,
                Player(1, 40, self + ", " + CarriedPiece(Mantle, "rng", 1, 3)
                    + ", " + CarriedPiece(Wings, "amu", 3, 0))));

            // A WORN set piece with a rune in it. ITEM_RecalcAllEquippedItems 0x4c1350 throws an
            // equipped set item's fillers away, so the GAME grants 15 where the item is worth 30.
            // Both implementations render the 30 regardless, and this is the case that puts a WORN
            // socketed set piece in front of the totals surface.
            int deathMask = Items.ClassIdForCode("xsk");
            int umRune = Items.ClassIdForCode("r22");
            if (deathMask >= 0 && umRune >= 0)
            {
                string socketedCrest = "{ \"unitType\": 4, \"classId\": " + deathMask
                    + ", \"quality\": 5, \"itemFlags\": 2064"
                    + ", \"fileIndex\": " + TalRashasHoradricCrest
                    + ", \"location\": 1, \"x\": 1"
                    + ", \"statsLists\": [ { \"stateNo\": 0, \"flags\": 2147483648, "
                    + "\"stats\": [ { \"id\": 31, \"value\": 76 }, { \"id\": 194, \"value\": 1 } ] } ]"
                    + ", \"items\": [ { \"unitType\": 4, \"classId\": " + umRune
                    + ", \"itemFlags\": 16 } ] }";

                cases.Add(Case("setderive-worn-socketed", socketedCrest, Player(1, 70, "")));
            }

            // The same third piece on the ALTERNATE WEAPON SET. Owned, green, and still no tier —
            // the one case that separates the owned predicate from the worn one.
            cases.Add(Case("setderive-weapon-swap", hovered,
                Player(1, 40, self + ", " + CarriedPiece(Mantle, "rng", 1, 3)
                    + ", " + CarriedPiece(Sickle, "sbr", 1, 11))));

            // ... and in the ACTIVE weapon slot, which DOES light a tier. The pair differs by one
            // integer, so a divergence here is unambiguous.
            cases.Add(Case("setderive-weapon-active", hovered,
                Player(1, 40, self + ", " + CarriedPiece(Mantle, "rng", 1, 3)
                    + ", " + CarriedPiece(Sickle, "sbr", 1, 4))));

            // The whole set worn, which is what reaches the full-set block — dead on this path
            // until the derivation landed.
            cases.Add(Case("setderive-full-set", hovered,
                Player(1, 40, self + ", " + CarriedPiece(Mantle, "rng", 1, 3)
                    + ", " + CarriedPiece(Wings, "amu", 1, 5)
                    + ", " + CarriedPiece(Sickle, "sbr", 1, 4))));

            // A piece of ANOTHER set contributes nothing, so this must render as the two-worn case.
            cases.Add(Case("setderive-foreign-piece", hovered,
                Player(1, 40, self + ", " + CarriedPiece(Mantle, "rng", 1, 3)
                    + ", " + CarriedPiece(ArcticHorn, "swb", 1, 4))));

            // Hovered from the INVENTORY while the set is worn: isEquipped is false, so the
            // full-set block is suppressed even though the tiers are earned.
            string loose = "{ \"unitType\": 4, \"classId\": " + ring
                + ", \"quality\": 5, \"itemFlags\": 16, \"fileIndex\": " + Halo
                + ", \"location\": 3, \"x\": 0 }";

            cases.Add(Case("setderive-hovered-loose", loose,
                Player(1, 40, CarriedPiece(Mantle, "rng", 1, 3)
                    + ", " + CarriedPiece(Wings, "amu", 1, 5))));
        }

        /// <summary>
        /// The branches only D2R has: the Warlock class in every class-indexed table, the belt,
        /// rune and event-item lines, the "of" quantity, data-driven potions, the new descfuncs and
        /// the one-affix magic names.
        /// </summary>
        private static void AddResurrectedCases(List<string> cases)
        {
            const int Warlock = 7;

            foreach (string code in new[] { "wa1", "wa5", "waf", "lbl", "vbl", "ulc", "zlb", "r01", "r33",
                                            "pk1", "ua1", "aqv", "cqv", "tkf", "hp5", "mp5", "rvs", "rvl",
                                            "box", "bkd", "xa1", "jav", "7gd", "lrg" })
            {
                int classId = Items.ClassIdForCode(code);
                if (classId < 0)
                {
                    continue;
                }

                string record = Record(classId, 2, 16,
                    "{ \"id\": 31, \"value\": 40 }, { \"id\": 72, \"value\": 20 }, "
                    + "{ \"id\": 73, \"value\": 30 }, { \"id\": 70, \"value\": 60 }, "
                    + "{ \"id\": 21, \"value\": 5 }, { \"id\": 22, \"value\": 12 }, "
                    + "{ \"id\": 159, \"value\": 5 }, { \"id\": 160, \"value\": 12 }",
                    "{ \"id\": 254, \"value\": 20 }, { \"id\": 160, \"value\": 4 }");

                foreach (int viewerClass in new[] { 1, 3, 4, Warlock })
                {
                    cases.Add(Case("d2r-" + code + "-c" + viewerClass, record, Player(viewerClass, 50)));
                }
            }

            int shard = Items.ClassIdForCode("xa1");
            if (shard >= 0)
            {
                string record = Record(shard, 2, 16, string.Empty, string.Empty);
                cases.Add(Case("d2r-xa1-hell-zones", record, Player(0, 80), difficulty: 2, desecrated: true));
                cases.Add(Case("d2r-xa1-hell", record, Player(0, 80), difficulty: 2));
            }

            // spelldescstr resolves through the HD table even under legacy text; the legacy table
            // holds none of these ids, so the legacy references pin the missing-string line.
            foreach (string code in new[] { "xa1", "xa2", "xa3", "xa4", "xa5" })
            {
                int id = Items.ClassIdForCode(code);
                if (id >= 0)
                {
                    cases.Add(Case("d2r-" + code + "-spelldesc", Record(id, 2, 16, string.Empty, string.Empty), Player(0, 60)));
                }
            }

            // The new and changed descfuncs, each on its own ring so a divergence names the stat.
            int ring = Items.ClassIdForCode("rin");
            int levitate = Data.SkillRows.FindRow("skill", "Levitate");
            int battleOrders = Data.SkillRows.FindRow("skill", "Battle Orders");
            string[] modifiers =
            {
                "{ \"id\": 36, \"value\": 10 }",
                "{ \"id\": 36, \"value\": -15 }",
                "{ \"id\": 97, \"layer\": " + battleOrders + ", \"value\": 6 }",
                "{ \"id\": 107, \"layer\": " + levitate + ", \"value\": 2 }",
                "{ \"id\": 83, \"layer\": 7, \"value\": 1 }",
                "{ \"id\": 188, \"layer\": 57, \"value\": 2 }",
                "{ \"id\": 112, \"value\": 64 }",
                "{ \"id\": 214, \"value\": 8 }",
                "{ \"id\": 48, \"value\": 5 }, { \"id\": 49, \"value\": 10 }",
                "{ \"id\": 48, \"value\": 7 }, { \"id\": 49, \"value\": 7 }",
                "{ \"id\": 57, \"value\": 256 }, { \"id\": 58, \"value\": 512 }, { \"id\": 59, \"value\": 75 }",
                "{ \"id\": 17, \"value\": 40 }, { \"id\": 18, \"value\": 40 }",
                "{ \"id\": 0, \"value\": 5 }, { \"id\": 1, \"value\": 5 }, { \"id\": 2, \"value\": 5 }, { \"id\": 3, \"value\": 5 }",
            };

            for (int at = 0; at < modifiers.Length; ++at)
            {
                foreach (int viewerClass in new[] { 4, Warlock })
                {
                    cases.Add(Case(
                        "d2r-mod" + at + "-c" + viewerClass,
                        Record(ring, 6, 16, string.Empty, modifiers[at]),
                        Player(viewerClass, 60)));
                }
            }

            // Magic names: both affixes, prefix only, suffix only.
            int shield = Items.ClassIdForCode("lrg");
            int prefix = Data.MagicSuffix.RowCount + 1 + Data.MagicPrefix.FindRow("Name", "Sturdy");
            int suffix = 1 + Data.MagicSuffix.FindRow("Name", "of Health");
            foreach (int[] affixes in new[] { new[] { prefix, suffix }, new[] { prefix, 0 }, new[] { 0, suffix } })
            {
                cases.Add(Case(
                    "d2r-magic-" + affixes[0] + "-" + affixes[1],
                    "{ \"unitType\": 4, \"classId\": " + shield + ", \"quality\": 4, \"itemFlags\": 16, "
                    + "\"magicPrefix\": [" + affixes[0] + ", 0, 0], \"magicSuffix\": [" + affixes[1] + ", 0, 0], "
                    + "\"statsLists\": [ { \"stateNo\": 0, \"flags\": 2147483648, \"stats\": [ "
                    + "{ \"id\": 31, \"value\": 60 }, { \"id\": 72, \"value\": 30 }, { \"id\": 73, \"value\": 30 } ] } ] }",
                    Player(3, 40)));
            }

            // Names that reach the possessive and the rare formatter — what a non-English reference
            // of this corpus polices (the grammar header and sub_140478a70).
            int crown = Items.ClassIdForCode("crn");
            int beast = Data.RareSuffix.RowCount + 1 + Data.RarePrefix.FindRow("name", "Beast");
            int bite = 1 + Data.RareSuffix.FindRow("name", "bite");
            foreach (string owner in new[] { "Hans", "Anna", "anna" })
            {
                cases.Add(Case(
                    "d2r-personal-" + owner,
                    "{ \"unitType\": 4, \"classId\": " + crown + ", \"quality\": 2, \"itemFlags\": 16777232, "
                    + "\"playerName\": \"" + owner + "\" }",
                    Player(0, 40)));
                cases.Add(Case(
                    "d2r-personal-rare-" + owner,
                    "{ \"unitType\": 4, \"classId\": " + crown + ", \"quality\": 6, \"itemFlags\": 16777232, "
                    + "\"playerName\": \"" + owner + "\", \"rarePrefix\": " + beast + ", \"rareSuffix\": " + bite + " }",
                    Player(0, 40)));
            }

            foreach (string code in new[] { "crn", "ghm", "scp", "lbt" })
            {
                int classId = Items.ClassIdForCode(code);
                foreach (string[] pair in new[]
                         {
                             new[] { "Sturdy", null }, new[] { null, "of the Whale" },
                             new[] { "Virulent", null }, new[] { "Screaming", "of Thorns" },
                         })
                {
                    int p = pair[0] == null ? 0 : Data.MagicSuffix.RowCount + 1 + Data.MagicPrefix.FindRow("Name", pair[0]);
                    int s = pair[1] == null ? 0 : 1 + Data.MagicSuffix.FindRow("Name", pair[1]);
                    cases.Add(Case(
                        "d2r-name-" + code + "-" + p + "-" + s,
                        "{ \"unitType\": 4, \"classId\": " + classId + ", \"quality\": 4, \"itemFlags\": 16, "
                        + "\"magicPrefix\": [" + p + ", 0, 0], \"magicSuffix\": [" + s + ", 0, 0] }",
                        Player(0, 40)));
                }
            }

            // A Levitate Warlock: stat 203 on layer `weap | 1 << 14` cuts a weapon's requirements.
            int sword = Items.ClassIdForCode("lsd");
            int weap = Data.ItemTypes.FindRow("Code", "weap");
            cases.Add(Case(
                "d2r-mastery-sword",
                Record(sword, 2, 16, "{ \"id\": 21, \"value\": 3 }, { \"id\": 22, \"value\": 19 }", string.Empty),
                "{ \"unitType\": 0, \"classId\": 7, \"flagsEx\": 33554432, \"statsLists\": [ { \"stateNo\": 0, "
                + "\"flags\": 2147483648, \"stats\": [ { \"id\": 12, \"value\": 30 }, { \"id\": 0, \"value\": 45 }, "
                + "{ \"id\": 2, \"value\": 30 }, { \"id\": 203, \"layer\": " + (weap | (1 << 14)) + ", \"value\": -20 } ] } ] }"));

            AddMasteryCases(cases, weap);

            AddPropertyGroupCases(cases);

            // Ops 4/5 fold the viewer's level into Defense / damage (0x14020c57d / 0x14020c5ef), at
            // three viewer levels so the scaling is policed.
            int paleocene = Data.MagicPrefix.FindRow("Name", "Paleocene");
            int gritty = Data.MagicPrefix.FindRow("Name", "Gritty");
            foreach (int level in new[] { 1, 40, 99 })
            {
                if (paleocene >= 0)
                {
                    cases.Add(Case("d2r-ac-per-level-" + level,
                        AffixRecord(Items.ClassIdForCode("qui"), Data.MagicSuffix.RowCount + paleocene + 1, 40,
                            "{ \"id\": 214, \"value\": 24 }, { \"id\": 215, \"value\": 12 }")
                            .Replace("{ \"id\": 21, \"value\": 8 }, { \"id\": 22, \"value\": 15 }",
                                "{ \"id\": 31, \"value\": 10 }, { \"id\": 72, \"value\": 20 }, { \"id\": 73, \"value\": 20 }"),
                        Player(0, level)));
                }

                if (gritty >= 0)
                {
                    cases.Add(Case("d2r-dmg-per-level-" + level,
                        AffixRecord(Items.ClassIdForCode("hax"), Data.MagicSuffix.RowCount + gritty + 1, 40,
                            "{ \"id\": 218, \"value\": 6 }, { \"id\": 219, \"value\": 10 }")
                            .Replace("{ \"id\": 21, \"value\": 8 }, { \"id\": 22, \"value\": 15 }",
                                "{ \"id\": 21, \"value\": 3 }, { \"id\": 22, \"value\": 6 }, { \"id\": 72, \"value\": 28 }, { \"id\": 73, \"value\": 28 }"),
                        Player(0, level)));
                }
            }

            // Records with RotW class ids, so the base table set skips them.
            if (Data.Variant == GameVariant.ReignOfTheWarlock)
            {
                AddRangeHuntCases(cases);
            }

            // stat 36 both signs: frFR's "%d% %" leaves a conversion open at the NUL, which the
            // printf drops (0x140b477d5) — the locale references are where that shows.
            foreach (var resist in new[] { new { Name = "Shaftstop", Value = 30 }, new { Name = "Bone Break", Value = -15 } })
            {
                int row = Data.UniqueItems.FindRow("index", resist.Name);
                if (row < 0)
                {
                    continue;
                }

                string record = Record(Items.ClassIdForCode(Data.UniqueItems.GetString(row, "code").Trim()), 7, 16,
                    "{ \"id\": 31, \"value\": 1000 }, { \"id\": 72, \"value\": 60 }, { \"id\": 73, \"value\": 60 }",
                    "{ \"id\": 36, \"value\": " + resist.Value + " }").Replace("\"fileIndex\": 0", "\"fileIndex\": " + row);
                cases.Add(Case("d2r-damageresist-" + resist.Name.Replace(" ", string.Empty), record, Player(1, 80)));
            }

            // The set path's shared buffer (UI_DrawSetItemDescBox 0x1401d49ad / 0x1401d49fb /
            // 0x1401d4da7): Griswold's Redemption holding four max-roll unique Colossal Jewels.
            int redemption = Data.SetItems.FindRow("index", "Griswolds's Redemption");   // sic, setitems.txt
            if (redemption >= 0 && Items.ClassIdForCode("cjw") >= 0)
            {
                var jewels = new List<string>();
                string[] colossal =
                {
                    "{ \"id\": 57, \"value\": 975 }, { \"id\": 58, \"value\": 975 }, { \"id\": 59, \"value\": 25 }, { \"id\": 79, \"value\": 50 }, { \"id\": 80, \"value\": 35 }, { \"id\": 85, \"value\": 5 }, { \"id\": 326, \"value\": 1 }, { \"id\": 332, \"value\": 10 }, { \"id\": 336, \"value\": 10 }, { \"id\": 201, \"layer\": 4377, \"value\": 1 }",
                    "{ \"id\": 50, \"value\": 1 }, { \"id\": 51, \"value\": 75 }, { \"id\": 79, \"value\": 50 }, { \"id\": 80, \"value\": 35 }, { \"id\": 85, \"value\": 5 }, { \"id\": 330, \"value\": 10 }, { \"id\": 334, \"value\": 10 }, { \"id\": 201, \"layer\": 15065, \"value\": 1 }",
                    "{ \"id\": 54, \"value\": 10 }, { \"id\": 55, \"value\": 30 }, { \"id\": 56, \"value\": 125 }, { \"id\": 79, \"value\": 50 }, { \"id\": 80, \"value\": 35 }, { \"id\": 85, \"value\": 5 }, { \"id\": 331, \"value\": 10 }, { \"id\": 335, \"value\": 10 }, { \"id\": 201, \"layer\": 2585, \"value\": 1 }",
                    "{ \"id\": 48, \"value\": 20 }, { \"id\": 49, \"value\": 60 }, { \"id\": 79, \"value\": 50 }, { \"id\": 80, \"value\": 35 }, { \"id\": 85, \"value\": 5 }, { \"id\": 329, \"value\": 10 }, { \"id\": 333, \"value\": 10 }, { \"id\": 201, \"layer\": 2969, \"value\": 1 }",
                };
                for (int i = 0; i < colossal.Length; ++i)
                {
                    string jewel = Record(Items.ClassIdForCode("cjw"), 7, 16, string.Empty, colossal[i])
                        .Replace("\"fileIndex\": 0", "\"fileIndex\": " + (420 + i));
                    jewels.Add(jewel);
                }

                string set = Record(Items.ClassIdForCode(Data.SetItems.GetString(redemption, "item").Trim()), 5, 16 | 0x800,
                    "{ \"id\": 72, \"value\": 250 }",
                    "{ \"id\": 17, \"value\": 240 }, { \"id\": 18, \"value\": 240 }, { \"id\": 91, \"value\": -20 }, "
                    + "{ \"id\": 93, \"value\": 40 }, { \"id\": 122, \"value\": 200 }, { \"id\": 194, \"value\": 4 }")
                    .Replace("\"fileIndex\": 0", "\"fileIndex\": " + redemption);
                set = set.Substring(0, set.Length - 2) + ", \"items\": [ " + string.Join(", ", jewels) + " ] }";
                cases.Add(Case("d2r-bytecap-set-colossal-jewels", set,
                    "{ \"unitType\": 0, \"classId\": 2, \"flagsEx\": 33554432, \"statsLists\": [ { \"stateNo\": 0, "
                    + "\"flags\": 2147483648, \"stats\": [ { \"id\": 12, \"value\": 90 }, { \"id\": 0, \"value\": 200 }, "
                    + "{ \"id\": 2, \"value\": 200 } ] } ] }"));
            }

            // ruRU's `[pl]...\n` prefixes (Corosive, Spiritual): the name splits onto two rows.
            foreach (string[] plural in new[] { new[] { "Corosive", "clw" }, new[] { "Spiritual", "dr3" } })
            {
                int prefixRow = Data.MagicPrefix.FindRow("Name", plural[0]);
                if (prefixRow >= 0)
                {
                    cases.Add(Case("d2r-plural-prefix-" + plural[0],
                        AffixRecord(Items.ClassIdForCode(plural[1]), Data.MagicSuffix.RowCount + prefixRow + 1, 40),
                        Player(1, 80)));
                }
            }

            // The modifier walk's 1023-byte strlcat plus the 4-byte colour wrap (0x1401e91f6 /
            // 0x14008c9f0): a max-roll Arm of King Leoric with one +15% IAS jewel is past it in
            // ruRU, and the locale references are where that shows.
            int leoric = Data.UniqueItems.FindRow("index", "Arm of King Leoric");
            if (leoric >= 0)
            {
                var probe = new Unit();
                probe.UnitType = 4;
                probe.Quality = 7;
                probe.FileIndex = leoric;
                probe.ClassId = Items.ClassIdForCode(Data.UniqueItems.GetString(leoric, "code").Trim());
                probe.ItemFlags = ItemRecordFlags.Identified;
                probe.ItemLevel = 85;

                var rolled = new List<string>();
                foreach (RolledStatRange range in TooltipEngine.FromData(Data).Ranges(probe).Stats)
                {
                    rolled.Add("{ \"id\": " + range.StatId + ", \"layer\": " + range.Layer
                               + ", \"value\": " + range.High + " }");
                }

                string jewel = Record(Items.ClassIdForCode("jew"), 4, 16, string.Empty, "{ \"id\": 93, \"value\": 15 }");
                string record = Record(probe.ClassId, 7, 16 | 0x800,
                    "{ \"id\": 21, \"value\": 10 }, { \"id\": 22, \"value\": 22 }, { \"id\": 72, \"value\": 50 }, "
                    + "{ \"id\": 73, \"value\": 50 }, { \"id\": 194, \"value\": 1 }",
                    string.Join(", ", rolled)).Replace("\"fileIndex\": 0", "\"fileIndex\": " + leoric);
                record = record.Substring(0, record.Length - 2) + ", \"items\": [ " + jewel + " ] }";
                cases.Add(Case("d2r-bytecap-leoric-jewel", record,
                    "{ \"unitType\": 0, \"classId\": 2, \"flagsEx\": 33554432, \"statsLists\": [ { \"stateNo\": 0, "
                    + "\"flags\": 2147483648, \"stats\": [ { \"id\": 12, \"value\": 90 }, { \"id\": 0, \"value\": 200 }, "
                    + "{ \"id\": 2, \"value\": 200 } ] } ] }"));
            }

            // Every descfunc that formats through a positional wrapper (sub_14060cb00 / cea0 /
            // de20 / e430): the locale references are where their `%+0 %1` templates show.
            int teleport = Data.SkillRows.FindRow("skill", "Teleport");
            int meditation = Data.SkillRows.FindRow("skill", "Meditation");
            int fireBall = Data.SkillRows.FindRow("skill", "Fire Ball");
            cases.Add(Case("d2r-positional-ring",
                Record(Items.ClassIdForCode("rin"), 6, 16, string.Empty,
                    "{ \"id\": 151, \"layer\": " + meditation + ", \"value\": 12 }, "
                    + "{ \"id\": 204, \"layer\": " + ((teleport << 6) | 1) + ", \"value\": " + (20 | (20 << 8)) + " }, "
                    + "{ \"id\": 97, \"layer\": " + teleport + ", \"value\": 1 }, "
                    + "{ \"id\": 107, \"layer\": " + fireBall + ", \"value\": 3 }, "
                    + "{ \"id\": 252, \"value\": 10 }"),
                Player(1, 80)));

            // ITEMDESC_GetMinMaxStats' MAX(min, max) (0x1401d0987) on the two unclamped lines.
            cases.Add(Case("d2r-damage-throw-min-above-max",
                Record(Items.ClassIdForCode("tkf"), 2, 16,
                    "{ \"id\": 21, \"value\": 10 }, { \"id\": 22, \"value\": 11 }, "
                    + "{ \"id\": 159, \"value\": 12 }, { \"id\": 160, \"value\": 9 }", string.Empty),
                Player(1, 80)));
            cases.Add(Case("d2r-damage-barbarian-min-above-max",
                Record(Items.ClassIdForCode("2hs"), 2, 16,
                    "{ \"id\": 21, \"value\": 10 }, { \"id\": 22, \"value\": 9 }, "
                    + "{ \"id\": 23, \"value\": 14 }, { \"id\": 24, \"value\": 20 }", string.Empty),
                Player(4, 80)));

            // Metamorphosis's procs (skills 371/372, stat 198 layer `skill << 6 | level`): the only
            // rows with `item proc text`, so the only reach of the func 15 proc arm (0x1401ec332).
            cases.Add(Case("d2r-proc-metamorphosis",
                Record(Items.ClassIdForCode("cap"), 2, 16,
                    "{ \"id\": 72, \"value\": 30 }, { \"id\": 73, \"value\": 30 }",
                    "{ \"id\": 198, \"layer\": " + ((371 << 6) | 1) + ", \"value\": 100 }, "
                    + "{ \"id\": 198, \"layer\": " + ((372 << 6) | 1) + ", \"value\": 100 }"),
                Player(2, 60)));

            // D2R's worn mask has no identified test (0x14022ec4e-0x14022ec75): Sigon's Gage with
            // the Visor worn unidentified raises tier 0 while the Visor stays red in the piece list.
            const int sigonsGage = 35;
            const int sigonsVisor = 36;
            foreach (bool identified in new[] { false, true })
            {
                string gage = "{ \"unitType\": 4, \"classId\": " + Items.ClassIdForCode("hgl")
                    + ", \"quality\": 5, \"itemFlags\": 16, \"fileIndex\": " + sigonsGage
                    + ", \"location\": 1, \"x\": 10, \"statsLists\": [ "
                    + "{ \"stateNo\": 0, \"flags\": 2147483648, \"stats\": [ { \"id\": 31, \"value\": 10 }, "
                    + "{ \"id\": 72, \"value\": 20 }, { \"id\": 73, \"value\": 20 } ] }, "
                    + "{ \"stateNo\": 165, \"flags\": 64, \"stats\": [ { \"id\": 93, \"value\": 30 } ] } ] }";
                string visor = "{ \"unitType\": 4, \"classId\": " + Items.ClassIdForCode("ghm")
                    + ", \"quality\": 5, \"itemFlags\": " + (identified ? 16 : 0)
                    + ", \"fileIndex\": " + sigonsVisor + ", \"location\": 1, \"x\": 1 }";
                cases.Add(Case("d2r-set-worn-visor-" + (identified ? "identified" : "unidentified"),
                    gage, Player(0, 60, gage + ", " + visor)));
            }
        }

        // The mastery terms' branches: the throwing arm keyed off lastUsedSkill (0x14024d380), the
        // dual-melee condition bits (0x140239c30) and the stat-209 level tail (0x140228913).
        private static void AddMasteryCases(List<string> cases, int weap)
        {
            int levitate = weap | (1 << 14);
            int whileDual = weap | (2 << 14);

            foreach (string code in new[] { "tkf", "9ja", "sbr" })
            {
                string record = Record(Items.ClassIdForCode(code), 2, 16,
                    "{ \"id\": 72, \"value\": 30 }, { \"id\": 73, \"value\": 30 }", string.Empty);

                foreach (int skill in new[] { -1, 0, 2, 15 })
                {
                    cases.Add(Case("d2r-mastery-throw-" + code + "-" + skill, record,
                        MasteryViewer(7, 203, levitate, -20, skill, 18)));
                }
            }

            string saber = Record(Items.ClassIdForCode("sbr"), 2, 16,
                "{ \"id\": 72, \"value\": 30 }, { \"id\": 73, \"value\": 30 }", string.Empty);
            var hands = new[]
            {
                new { Name = "dual", Layer = levitate, OffHand = "scm", X = 5, Flags = 16 },
                new { Name = "broken", Layer = levitate, OffHand = "scm", X = 5, Flags = 16 | 0x100 },
                new { Name = "swap", Layer = levitate, OffHand = "scm", X = 12, Flags = 16 },
                new { Name = "cond2", Layer = whileDual, OffHand = "scm", X = 5, Flags = 16 },
                new { Name = "cond2-alone", Layer = whileDual, OffHand = (string)null, X = 5, Flags = 16 },
                new { Name = "shield", Layer = levitate, OffHand = "lrg", X = 5, Flags = 16 },
            };

            foreach (var hand in hands)
            {
                string carried = Worn("sbr", 4, 16)
                    + (hand.OffHand == null ? string.Empty : ", " + Worn(hand.OffHand, hand.X, hand.Flags));
                cases.Add(Case("d2r-mastery-hands-" + hand.Name, saber,
                    MasteryViewer(4, 203, hand.Layer, -20, -1, 100, carried)));
            }

            string ring = Record(Items.ClassIdForCode("rin"), 2, 16, string.Empty, "{ \"id\": 92, \"value\": 40 }");
            cases.Add(Case("d2r-mastery-level-any", ring, MasteryViewer(1, 209, 0, -25)));
            cases.Add(Case("d2r-mastery-level-weap", ring, MasteryViewer(1, 209, weap, -25)));
            cases.Add(Case("d2r-mastery-level-zero", ring, MasteryViewer(1, 209, 0, -100)));

            string ber = Record(Items.ClassIdForCode("r30"), 2, 16, string.Empty, string.Empty);
            string cap = Record(Items.ClassIdForCode("cap"), 2, 16 | 0x800,
                "{ \"id\": 72, \"value\": 30 }, { \"id\": 73, \"value\": 30 }, { \"id\": 194, \"value\": 1 }",
                string.Empty);
            cap = cap.Substring(0, cap.Length - 2) + ", \"items\": [ " + ber + " ] }";
            cases.Add(Case("d2r-mastery-level-socket", cap, MasteryViewer(1, 209, 0, -50)));
        }

        // Every shipped PropertyGroups.txt user — 32 uniqueitems cells, 12 magicprefix cells —
        // recorded resolved, ambiguous, contradicted and bare, so the `choices` the reconstructor
        // reports (ITEMMODS_AssignProperty 0x14028a490, the group applier sub_14028A190) are
        // compared layer for layer. A table set that lacks a row simply skips it.
        private static void AddPropertyGroupCases(List<string> cases)
        {
            var uniques = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Wraithstep", "{ \"id\": 188, \"layer\": 56, \"value\": 1 }"),
                new KeyValuePair<string, string>("Wraithstep", "{ \"id\": 188, \"layer\": 58, \"value\": 1 }"),
                new KeyValuePair<string, string>("Wraithstep", "{ \"id\": 96, \"value\": 30 }"),
                new KeyValuePair<string, string>("Opalvein", "{ \"id\": 357, \"value\": 4 }"),
                new KeyValuePair<string, string>("Opalvein", "{ \"id\": 17, \"value\": 33 }, { \"id\": 18, \"value\": 33 }"),
                new KeyValuePair<string, string>("Opalvein", "{ \"id\": 332, \"value\": 4 }"),
                new KeyValuePair<string, string>("Opalvein", "{ \"id\": 329, \"value\": 4 }, { \"id\": 331, \"value\": 4 }"),
                new KeyValuePair<string, string>("Opalvein",
                    "{ \"id\": 195, \"layer\": " + ((398 << 6) | 15) + ", \"value\": 2 }"),
                new KeyValuePair<string, string>("Crafted Cold Rupture",
                    "{ \"id\": 187, \"value\": 300 }, { \"id\": 43, \"value\": -70 }, { \"id\": 335, \"value\": 7 }, "
                    + "{ \"id\": 9, \"value\": 10240 }, { \"id\": 80, \"value\": 20 }, { \"id\": 99, \"value\": 18 }, "
                    + "{ \"id\": 34, \"value\": 6 }"),
            };

            foreach (string charm in new[]
                     {
                         "Crafted Flame Rift", "Crafted Crack of the Heavens", "Crafted Rotting Fissure",
                         "Crafted Bone Break", "Crafted Black Cleft",
                     })
            {
                uniques.Add(new KeyValuePair<string, string>(charm, string.Empty));
            }

            int n = 0;
            foreach (KeyValuePair<string, string> entry in uniques)
            {
                int row = Data.UniqueItems.FindRow("index", entry.Key);
                if (row < 0)
                {
                    continue;
                }

                int classId = Items.ClassIdForCode(Data.UniqueItems.GetString(row, "code").Trim());
                string record = Record(classId, 7, 16, "{ \"id\": 31, \"value\": 10 }", entry.Value);
                record = record.Replace("\"fileIndex\": 0", "\"fileIndex\": " + row);
                cases.Add(Case("d2r-group-" + entry.Key.Replace(" ", string.Empty) + "-" + n++, record, Player(1, 80)));
            }

            // The six group prefixes are the magicprefix rows whose mod2 names an `-Affix1` group
            // (ids 1501..1506 in RotW); earlier rows reuse the same names without one.
            var prefixes = new[]
            {
                new { Name = "Virulent", Code = "cm2", Mods = "{ \"id\": 336, \"value\": 12 }, { \"id\": 79, \"value\": 30 }" },
                new { Name = "Virulent", Code = "cm2", Mods = "{ \"id\": 336, \"value\": 7 }" },
                new { Name = "Virulent", Code = "cm2", Mods = "{ \"id\": 336, \"value\": 20 }" },
                new { Name = "Incendiary", Code = "cm2", Mods = "{ \"id\": 333, \"value\": 3 }, { \"id\": 331, \"value\": 11 }" },
                new { Name = "Gelid", Code = "qui", Mods = "{ \"id\": 335, \"value\": 3 }, { \"id\": 330, \"value\": 11 }" },
                new { Name = "Magnetic", Code = "qui", Mods = "{ \"id\": 334, \"value\": 3 }, { \"id\": 329, \"value\": 11 }" },
                new { Name = "Mystical", Code = "qui", Mods = "{ \"id\": 358, \"value\": 3 }, { \"id\": 17, \"value\": 80 }, { \"id\": 18, \"value\": 80 }" },
                new { Name = "Breaching", Code = "qui", Mods = "{ \"id\": 366, \"value\": 3 }, { \"id\": 357, \"value\": 11 }" },
            };

            foreach (var prefix in prefixes)
            {
                int row = -1;
                for (int i = 0; i < Data.MagicPrefix.RowCount && row < 0; ++i)
                {
                    if (Data.MagicPrefix.GetString(i, "Name") == prefix.Name
                        && Data.MagicPrefix.GetString(i, "mod2code").EndsWith("-Affix1", StringComparison.Ordinal))
                    {
                        row = i;
                    }
                }

                int classId = Items.ClassIdForCode(prefix.Code);
                if (row < 0 || classId < 0)
                {
                    continue;
                }

                // 1-based over [MagicSuffix][MagicPrefix][automagic].
                int affixId = Data.MagicSuffix.RowCount + row + 1;
                cases.Add(Case("d2r-group-prefix-" + prefix.Name + "-" + n++,
                    AffixRecord(classId, affixId, 60, prefix.Mods), Player(1, 80)));
            }
        }

        // The roll-range bug hunt's records (D2R spawn order): ethereal ac% after the maximised
        // base (0x1402e14c0), runeword/socket ac% assigned to the filler (0x1402d39ec), superior
        // file index = the rolled row (0x140381dc3), set tier aprops in states 165..169 (0x140286a2b).
        private static void AddRangeHuntCases(List<string> cases)
        {
            cases.Add(Case("ranges-EtherealVipermagi",
                "{\"unitType\":4,\"classId\":360,\"quality\":7,\"itemFlags\":4194320,\"format\":100,\"fileIndex\":210,\"itemLevel\":60," + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":190},{\"id\":73,\"value\":19},{\"id\":72,\"value\":19}]}," + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":16,\"value\":120},{\"id\":39,\"value\":30},{\"id\":41,\"value\":30},{\"id\":43,\"value\":30}," + "{\"id\":45,\"value\":30},{\"id\":105,\"value\":30},{\"id\":35,\"value\":10},{\"id\":127,\"value\":1}]}]}",
                null));
            cases.Add(Case("ranges-EtherealSuperiorAncientArmor",
                "{\"unitType\":4,\"classId\":326,\"quality\":3,\"itemFlags\":4194320,\"format\":100,\"fileIndex\":2,\"itemLevel\":60," + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":351},{\"id\":73,\"value\":31},{\"id\":72,\"value\":31}]}," + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":16,\"value\":10}]}]}",
                null));
            cases.Add(Case("ranges-PulInGrandCrown",
                "{\"unitType\":4,\"classId\":357,\"quality\":2,\"itemFlags\":2064,\"format\":100,\"fileIndex\":-1,\"itemLevel\":60," + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":90},{\"id\":194,\"value\":1}]}]," + "\"items\":[{\"unitType\":4,\"classId\":645,\"quality\":2,\"itemFlags\":16,\"format\":100,\"statsLists\":[]}]}",
                null));
            cases.Add(Case("ranges-SuperiorAncientArmorDurability",
                "{\"unitType\":4,\"classId\":326,\"quality\":3,\"itemFlags\":16,\"format\":100,\"fileIndex\":4,\"itemLevel\":60," + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":220},{\"id\":73,\"value\":60},{\"id\":72,\"value\":60}]}," + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":75,\"value\":12}]}]}",
                null));
            cases.Add(Case("ranges-SuperiorJavelinAttackRating",
                "{\"unitType\":4,\"classId\":47,\"quality\":3,\"itemFlags\":16,\"format\":100,\"fileIndex\":0,\"itemLevel\":60," + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[]},{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":19,\"value\":2}]}]}",
                null));
            cases.Add(Case("ranges-IrathasCollar",
                "{\"unitType\":4,\"classId\":535,\"quality\":5,\"itemFlags\":16,\"format\":100,\"fileIndex\":9,\"itemLevel\":60," + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[]}," + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":45,\"value\":30},{\"id\":110,\"value\":75}]}," + "{\"stateNo\":165,\"flags\":8256,\"stats\":[{\"id\":39,\"value\":15},{\"id\":41,\"value\":15},{\"id\":43,\"value\":15},{\"id\":45,\"value\":15}]}]}",
                null));
            cases.Add(Case("ranges-ImmortalKingsForge",
                "{\"unitType\":4,\"classId\":384,\"quality\":5,\"itemFlags\":16,\"format\":100,\"fileIndex\":73,\"itemLevel\":60," + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":50},{\"id\":73,\"value\":24},{\"id\":72,\"value\":24}]}," + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":31,\"value\":65},{\"id\":0,\"value\":20},{\"id\":2,\"value\":20},{\"id\":201,\"layer\":2436,\"value\":12}]}," + "{\"stateNo\":165,\"flags\":8256,\"stats\":[{\"id\":93,\"value\":25}]}," + "{\"stateNo\":166,\"flags\":8256,\"stats\":[{\"id\":31,\"value\":120}]}," + "{\"stateNo\":167,\"flags\":8256,\"stats\":[{\"id\":60,\"value\":10}]}," + "{\"stateNo\":168,\"flags\":8256,\"stats\":[{\"id\":62,\"value\":10}]}," + "{\"stateNo\":169,\"flags\":8256,\"stats\":[{\"id\":134,\"value\":2}]}]}",
                null));
            cases.Add(Case("ranges-Fortitude", "{\"unitType\":4,\"classId\":443,\"quality\":2,\"itemFlags\":67110928,\"format\":100,\"fileIndex\":-1,\"itemLevel\":60,\"magicPrefix\":[20547,0,0],\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":500},{\"id\":73,\"value\":60},{\"id\":72,\"value\":60},{\"id\":194,\"value\":4}]},{\"stateNo\":171,\"flags\":64,\"stats\":[{\"id\":16,\"value\":200}]}]}", null));
            cases.Add(Case("ranges-FortitudeEthereal", "{\"unitType\":4,\"classId\":443,\"quality\":2,\"itemFlags\":71305232,\"format\":100,\"fileIndex\":-1,\"itemLevel\":60,\"magicPrefix\":[20547,0,0],\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":750},{\"id\":73,\"value\":60},{\"id\":72,\"value\":60},{\"id\":194,\"value\":4}]},{\"stateNo\":171,\"flags\":64,\"stats\":[{\"id\":16,\"value\":200}]}]}", null));
            cases.Add(Case("ranges-ImmortalKingsForgeEarned",
                "{\"unitType\":4,\"classId\":384,\"quality\":5,\"itemFlags\":16,\"format\":100,\"fileIndex\":73,\"itemLevel\":60," + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":50},{\"id\":73,\"value\":24},{\"id\":72,\"value\":24}]}," + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":31,\"value\":65},{\"id\":0,\"value\":20},{\"id\":2,\"value\":20},{\"id\":201,\"layer\":2436,\"value\":12}]}," + "{\"stateNo\":165,\"flags\":64,\"stats\":[{\"id\":93,\"value\":25}]}," + "{\"stateNo\":166,\"flags\":64,\"stats\":[{\"id\":31,\"value\":120}]}," + "{\"stateNo\":167,\"flags\":8256,\"stats\":[{\"id\":60,\"value\":10}]}," + "{\"stateNo\":168,\"flags\":8256,\"stats\":[{\"id\":62,\"value\":10}]}," + "{\"stateNo\":169,\"flags\":8256,\"stats\":[{\"id\":134,\"value\":2}]}]}",
                null));
            cases.Add(Case("ranges-StormshieldLevel80",
                "{\"unitType\":4,\"classId\":447,\"quality\":7,\"itemFlags\":16,\"format\":100,\"fileIndex\":253,\"itemLevel\":80," + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":140},{\"id\":73,\"value\":86},{\"id\":72,\"value\":86}]}," + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":214,\"value\":30},{\"id\":36,\"value\":35},{\"id\":0,\"value\":30},{\"id\":152,\"value\":1}," + "{\"id\":20,\"value\":25},{\"id\":41,\"value\":25},{\"id\":43,\"value\":60},{\"id\":128,\"value\":10}]}]}",
                "{\"unitType\":0,\"classId\":1,\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":12,\"value\":80},{\"id\":0,\"value\":200},{\"id\":2,\"value\":100}]}]}"));

            const string vipermagiWithUm =
                "{\"unitType\":4,\"classId\":360,\"quality\":7,\"itemFlags\":2064,\"format\":100,\"fileIndex\":210,\"itemLevel\":60," + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":127},{\"id\":73,\"value\":38},{\"id\":72,\"value\":38},{\"id\":194,\"value\":1}]}," + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":16,\"value\":120},{\"id\":39,\"value\":30},{\"id\":41,\"value\":30},{\"id\":43,\"value\":30}," + "{\"id\":45,\"value\":30},{\"id\":105,\"value\":30},{\"id\":35,\"value\":10},{\"id\":127,\"value\":1}]}]," + "\"items\":[{\"unitType\":4,\"classId\":646,\"quality\":2,\"itemFlags\":16,\"format\":100,\"statsLists\":RUNE}]}";
            cases.Add(Case("ranges-VipermagiUmClient", vipermagiWithUm.Replace("RUNE", "[]"), null));
            cases.Add(Case("ranges-VipermagiUmServer",
                vipermagiWithUm.Replace("RUNE", "[{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":39,\"value\":15},{\"id\":41,\"value\":15},{\"id\":43,\"value\":15},{\"id\":45,\"value\":15}]}]"),
                null));

            cases.Add(Case("ranges-ClassicMilabregasRobe",
                "{\"unitType\":4,\"classId\":326,\"quality\":5,\"itemFlags\":16,\"format\":0,\"fileIndex\":24,\"itemLevel\":30," + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":225},{\"id\":73,\"value\":60},{\"id\":72,\"value\":60}]}," + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":78,\"value\":3},{\"id\":34,\"value\":2}]}]}",
                null));

            const string upgradedVipermagi =
                "{\"unitType\":4,\"classId\":430,\"quality\":7,\"itemFlags\":FLAGS,\"format\":100,\"fileIndex\":210,\"itemLevel\":60,\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648," + "\"stats\":[{\"id\":31,\"value\":BASE},{\"id\":73,\"value\":36},{\"id\":72,\"value\":36}]}," + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":16,\"value\":120},{\"id\":39,\"value\":30},{\"id\":41,\"value\":30}," + "{\"id\":43,\"value\":30},{\"id\":45,\"value\":30},{\"id\":105,\"value\":30},{\"id\":35,\"value\":10},{\"id\":127,\"value\":1}]}]}";
            cases.Add(Case("ranges-UpgradedVipermagi",
                upgradedVipermagi.Replace("FLAGS", "16").Replace("BASE", "400"), null));
            cases.Add(Case("ranges-UpgradedVipermagiEthereal",
                upgradedVipermagi.Replace("FLAGS", "4194320").Replace("BASE", "600"), null));

            // Holy (`ac%` 81..100) on Wyrmhide, a base an upgrade can produce.
            cases.Add(Case("ranges-RareWyrmhide",
                "{\"unitType\":4,\"classId\":430,\"quality\":6,\"itemFlags\":16,\"format\":100,\"fileIndex\":-1,\"itemLevel\":60," + "\"magicPrefix\":[" + (Data.MagicSuffix.RowCount + 7) + ",0,0],\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648," + "\"stats\":[{\"id\":31,\"value\":471},{\"id\":73,\"value\":36},{\"id\":72,\"value\":36}]}," + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":16,\"value\":90}]}]}",
                null));
        }


        private static string Worn(string code, int bodyLocation, int flags)
        {
            return "{ \"unitType\": 4, \"classId\": " + Items.ClassIdForCode(code)
                + ", \"quality\": 2, \"itemFlags\": " + flags
                + ", \"location\": 1, \"x\": " + bodyLocation + " }";
        }

        private static string MasteryViewer(
            int classId, int statId, int layer, int value, int lastUsedSkill = -1, int dexterity = 100,
            string carried = null)
        {
            return "{ \"unitType\": 0, \"classId\": " + classId + ", \"flagsEx\": 33554432"
                + (lastUsedSkill < 0 ? string.Empty : ", \"lastUsedSkill\": " + lastUsedSkill)
                + (carried == null ? string.Empty : ", \"items\": [ " + carried + " ]")
                + ", \"statsLists\": [ { \"stateNo\": 0, \"flags\": 2147483648, \"stats\": [ "
                + "{ \"id\": 12, \"value\": 90 }, { \"id\": 0, \"value\": 100 }, "
                + "{ \"id\": 2, \"value\": " + dexterity + " }, "
                + "{ \"id\": " + statId + ", \"layer\": " + layer + ", \"value\": " + value + " } ] } ] }";
        }

        private static string Case(
            string name, string record, string player, string set = null, int shopMode = 0,
            int difficulty = 0, bool desecrated = false)
        {
            var builder = new StringBuilder("{ \"name\": \"")
                .Append(name).Append("\", \"record\": ").Append(record);

            if (player != null)
            {
                builder.Append(", \"player\": ").Append(player);
            }

            if (set != null)
            {
                builder.Append(", \"set\": ").Append(set);
            }

            if (shopMode != 0)
            {
                builder.Append(", \"shopMode\": ").Append(shopMode);
            }

            if (difficulty != 0)
            {
                builder.Append(", \"difficulty\": ").Append(difficulty);
            }

            if (desecrated)
            {
                builder.Append(", \"desecratedZones\": true");
            }

            return builder.Append(" }").ToString();
        }
    }
}
