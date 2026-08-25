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
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly MagicAffixTable Affixes = new MagicAffixTable(Data);

        public static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: Corpus <out.json>");
                return 2;
            }

            var cases = new List<string>();

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
            // never re-applies the fillers. So the same Um shows `All Resistances +15` in the
            // backpack and nothing at all when worn. Both shapes are here because the gate is only
            // policed if the corpus reaches it BOTH ways.
            //
            // Tal Rasha's Horadric Crest with an Um, which is the pair a real capture showed.
            foreach (bool equipped in new[] { false, true })
            {
                cases.Add(Case(
                    "set-socketed-um-" + (equipped ? "worn" : "bag"),
                    "{ \"unitType\": 4, \"classId\": " + Items.ClassIdForCode("xsk")
                    + ", \"quality\": 5, \"itemFlags\": " + (16 | 0x800) + ", \"fileIndex\": 80"
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
            // equipped set item's fillers away, so Render draws none of the rune's mods â while
            // MergedStats deliberately keeps them and reports the disagreement through
            // FillersIgnoredBecauseWorn. Nothing else in the corpus sets that flag, so without
            // this case the whole worn-set arm of the totals surface is unpoliced.
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

        private static string Case(
            string name, string record, string player, string set = null, int shopMode = 0)
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

            return builder.Append(" }").ToString();
        }
    }
}
