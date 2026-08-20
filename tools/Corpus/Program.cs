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
                    + "\"sockets\": [ { \"unitType\": 4, \"classId\": " + fillerId + ", "
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
                        + "\"sockets\": [ "
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
                    + "\"sockets\": [" + string.Join(", ", fillers) + "] }",
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
        /// ITEM_BuildSetItemTooltip 0x48d1d0. Nothing else in the corpus reaches it — every other
        /// quality-5 case used to record a NotSupportedException and stop there — so the branches
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
                    + "\"sockets\": [ { \"unitType\": 4, \"classId\": "
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

        private static string Player(int classId, int level)
        {
            return "{ \"unitType\": 0, \"classId\": " + classId
                + ", \"flagsEx\": 33554432"
                + ", \"skills\": [ { \"skill\": 117, \"level\": 10 } ]"
                + ", \"statsLists\": [ { \"stateNo\": 0, \"flags\": 2147483648, \"stats\": ["
                + "{ \"id\": 12, \"value\": " + level + " }, "
                + "{ \"id\": 0, \"value\": " + (20 + level) + " }, "
                + "{ \"id\": 2, \"value\": " + (20 + level) + " } ] } ] }";
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
