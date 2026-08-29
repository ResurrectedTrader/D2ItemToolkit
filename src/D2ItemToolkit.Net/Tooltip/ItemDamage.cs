using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// Which damage line a range belongs to. The game draws each with its own label and its own
    /// rules, so they are not interchangeable: a two-handed weapon has no one-hand line at all,
    /// and a Barbarian holding a `1or2handed` weapon gets BOTH.
    /// </summary>
    public enum ItemDamageKind
    {
        /// <summary>Stats 21/22, locale 3465. What most weapons draw.</summary>
        OneHand = 0,

        /// <summary>Stats 23/24, locale 3466.</summary>
        TwoHand = 1,

        /// <summary>Stats 159/160, locale 3467. Only a `thro` item, and always in addition.</summary>
        Throw = 2,

        /// <summary>
        /// A throwing potion, whose numbers come from its missiles.txt record rather than from any
        /// stat (0x48545f). It is the whole line — 0x485459 tests `tpot` first and takes an arm
        /// that writes the buffer outright, so such an item has no ordinary damage or throw line.
        /// </summary>
        ThrowingPotion = 3,
    }

    /// <summary>One damage line's numbers, before the game formats them into a string.</summary>
    public struct ItemDamageRange
    {
        internal ItemDamageRange(ItemDamageKind kind, int min, int max, bool modified)
        {
            Kind = kind;
            Min = min;
            Max = max;
            Modified = modified;
        }

        public ItemDamageKind Kind;

        public int Min;

        /// <summary>
        /// The high end AS DRAWN, which for the single-line path has already had the `max = min + 1`
        /// clamp applied (0x485931). The dual-wield path does not clamp (0x485669 has no such
        /// step), so a Barbarian's two lines can legitimately show min == max.
        /// </summary>
        public int Max;

        /// <summary>
        /// INV_CalcWeaponDamageRange's `pModified` out-param, which is what paints the numbers
        /// colour 3. Set when the BASE stat is below the merged one at either end (0x485300), or
        /// when either by-time damage stat contributes (0x485372 / 0x4853eb). The throw line adds a
        /// pre-seed from stats 18, 17, 159 and 160 (0x485a14-0x485a54).
        ///
        /// Always false for <see cref="ItemDamageKind.ThrowingPotion"/>, which has no such flag —
        /// its colour comes from the missile's elemental type instead.
        /// </summary>
        public bool Modified;
    }

    /// <summary>
    /// The weapon-damage numbers a tooltip would draw, as numbers.
    ///
    /// This is exactly what <see cref="TooltipEngine.Render"/> puts in the WeaponDamage section,
    /// so the two cannot disagree about a value — only about how it is written. It is NOT the
    /// damage the character deals: SMITE and KICK are a different writer entirely
    /// (INV_FormatDefenseRangeText 0x485d40, reading items.txt `nMinDam` +0xFE and `nMaxDam` +0xFF
    /// rather than any stat) and are not included, and neither is elemental damage, which the game
    /// draws as its own modifier lines.
    ///
    /// Three things INV_CalcWeaponDamageRange 0x485240 does are NOT reproduced here — see the OPEN
    /// note on RecordSections.WeaponDamage. In short: the max is read straight off the item rather
    /// than as MAX(mergedMax, mergedMin) plus stats 272/273, and the wielder's own damage stats are
    /// not merged in. Whether any of the three moves a shipped item is uncounted, so treat these
    /// numbers as "what the tooltip shows", which they are, rather than as "what the game computes".
    /// </summary>
    public sealed class ItemDamage
    {
        internal ItemDamage(IReadOnlyList<ItemDamageRange> lines)
        {
            Lines = lines;
        }

        /// <summary>
        /// One entry per line the tooltip draws, in DISPLAY order — top row first, so a Barbarian's
        /// two-hand line precedes its one-hand line and a throw line precedes both.
        ///
        /// Empty when the item draws no damage line at all: anything that is not a `weap`, and a
        /// weapon whose own stat 21 or 22 is NEGATIVE (0x48e704 / 0x48e716 — zero PASSES and yields
        /// "0 to 1" through the clamp).
        /// </summary>
        public IReadOnlyList<ItemDamageRange> Lines { get; private set; }
    }
}
