using System;

namespace D2ItemToolkit
{
    // Mirrors SKILLDESC_BuildStatListDesc (0x4e49c0), which collects every damage kind into a
    // 42-int struct, and SKILLDESC_BuildStatDescription (0x4e5a20), which the emission loop
    // consults at 0x4e62a6 AHEAD of the DescFunc engine. A non-zero return means "handled, do not
    // run DescFunc" — which is why sixteen stat ids fold into one "Adds X-Y" line.
    //
    // COLLECTION AND EMISSION USE DIFFERENT ARRAYS. Collection walks a table compiled into the
    // binary (immediate at 0x4e49e3, 143 rows, stride 0x10) and dereferences only each row's
    // first dword, the stat id — it never looks at descfunc. Emission walks the descfunc-FILTERED
    // array built at 0x638530. So stat 59 is collected but never emitted, and the poison length
    // and divisor reads below are live even though poisonlength ships with a blank descfunc.

    internal static class DamageStatIds
    {
        public const int MinDamage = 21;
        public const int MaxDamage = 22;
        public const int SecondaryMinDamage = 23;
        public const int SecondaryMaxDamage = 24;

        public const int ItemMaxDamagePercent = 17;
        public const int ItemMinDamagePercent = 18;

        public const int FireMinDamage = 48;
        public const int FireMaxDamage = 49;
        public const int LightningMinDamage = 50;
        public const int LightningMaxDamage = 51;
        public const int MagicMinDamage = 52;
        public const int MagicMaxDamage = 53;
        public const int ColdMinDamage = 54;
        public const int ColdMaxDamage = 55;
        public const int PoisonMinDamage = 57;
        public const int PoisonMaxDamage = 58;
        public const int PoisonLength = 59;

        public const int PoisonLengthDivisor = 326;

        public const int UndeadDamagePercent = 122;
    }

    internal static class DamageStringIds
    {
        public const int EnhancedDamage = 10023;

        public const int PhysicalRange = 3623;

        public const int FireSingle = 3612;
        public const int FireRange = 3613;
        public const int ColdSingle = 3614;
        public const int ColdRange = 3615;
        public const int LightningSingle = 3616;
        public const int LightningRange = 3617;
        public const int MagicSingle = 3618;
        public const int MagicRange = 3619;
        public const int PoisonSingle = 3620;
        public const int PoisonRange = 3621;

        public const int DamageToUndead = 3554;
    }

    internal sealed class DamagePair
    {
        public int Min;
        public int Max;

        public bool BothPresent;

        public int SingleStringId;
        public int RangeStringId;
    }

    internal sealed class ItemDamageAggregate
    {
        private readonly DamagePair _physical = new DamagePair();
        private readonly DamagePair _enhanced = new DamagePair();
        private readonly DamagePair _fire = new DamagePair();
        private readonly DamagePair _cold = new DamagePair();
        private readonly DamagePair _lightning = new DamagePair();
        private readonly DamagePair _magic = new DamagePair();
        private readonly DamagePair _poison = new DamagePair();

        private int _poisonLength;
        private int _poisonDivisor;

        private bool _physicalEmitted;

        private readonly IStringTable _strings;

        public ItemDamageAggregate(IStringTable strings, IStatValueSource values)
        {
            if (strings == null) throw new ArgumentNullException("strings");

            _strings = strings;

            _fire.SingleStringId = DamageStringIds.FireSingle;
            _fire.RangeStringId = DamageStringIds.FireRange;
            _cold.SingleStringId = DamageStringIds.ColdSingle;
            _cold.RangeStringId = DamageStringIds.ColdRange;
            _lightning.SingleStringId = DamageStringIds.LightningSingle;
            _lightning.RangeStringId = DamageStringIds.LightningRange;
            _magic.SingleStringId = DamageStringIds.MagicSingle;
            _magic.RangeStringId = DamageStringIds.MagicRange;
            _poison.SingleStringId = DamageStringIds.PoisonSingle;
            _poison.RangeStringId = DamageStringIds.PoisonRange;

            if (values == null)
            {
                return;
            }

            _physical.Min = values.GetBaseStatValue(DamageStatIds.MinDamage, 0);
            if (_physical.Min == 0)
            {
                _physical.Min = values.GetBaseStatValue(DamageStatIds.SecondaryMinDamage, 0);
            }

            _physical.Max = values.GetBaseStatValue(DamageStatIds.MaxDamage, 0);
            if (_physical.Max == 0)
            {
                _physical.Max = values.GetBaseStatValue(DamageStatIds.SecondaryMaxDamage, 0);
            }

            _enhanced.Min = values.GetBaseStatValue(DamageStatIds.ItemMinDamagePercent, 0);
            _enhanced.Max = values.GetBaseStatValue(DamageStatIds.ItemMaxDamagePercent, 0);

            _fire.Min = values.GetBaseStatValue(DamageStatIds.FireMinDamage, 0);
            _fire.Max = values.GetBaseStatValue(DamageStatIds.FireMaxDamage, 0);

            _lightning.Min = values.GetBaseStatValue(DamageStatIds.LightningMinDamage, 0);
            _lightning.Max = values.GetBaseStatValue(DamageStatIds.LightningMaxDamage, 0);

            _magic.Min = values.GetBaseStatValue(DamageStatIds.MagicMinDamage, 0);
            _magic.Max = values.GetBaseStatValue(DamageStatIds.MagicMaxDamage, 0);

            _cold.Min = values.GetBaseStatValue(DamageStatIds.ColdMinDamage, 0);
            _cold.Max = values.GetBaseStatValue(DamageStatIds.ColdMaxDamage, 0);

            _poison.Min = values.GetBaseStatValue(DamageStatIds.PoisonMinDamage, 0);
            _poison.Max = values.GetBaseStatValue(DamageStatIds.PoisonMaxDamage, 0);
            _poisonLength = values.GetBaseStatValue(DamageStatIds.PoisonLength, 0);
            _poisonDivisor = values.GetItemStatValue(DamageStatIds.PoisonLengthDivisor);

            SetLatch(_physical);
            SetLatch(_enhanced);
            SetLatch(_cold);
            SetLatch(_lightning);
            SetLatch(_fire);
            SetLatch(_poison);
            SetLatch(_magic);
        }

        // Strictly greater than zero on BOTH halves (the seven `jle` pairs at 0x4e4b53-0x4e4bce).
        private static void SetLatch(DamagePair pair)
        {
            pair.BothPresent = pair.Min > 0 && pair.Max > 0;
        }

        /// <summary>
        /// Whether the aggregated line for this stat shows TWO numbers rather than one — "Adds 1-4
        /// cold damage" against "+175% Enhanced Damage".
        ///
        /// Only the enhanced-damage line is single-valued: it prints the MIN half alone and the max
        /// half emits nothing at all (0x4e5aa4 returns the latch), so one roll span sits against it
        /// unambiguously. A min-max line's span would belong to neither of the two numbers on it,
        /// which is why those are left un-annotated.
        /// </summary>
        public static bool ShowsSeveralValues(int statId)
        {
            return statId != DamageStatIds.ItemMinDamagePercent;
        }

        /// <summary>
        /// The stats whose numbers an aggregated damage line prints, in print order — the min/max
        /// pair for "Adds 1-4 cold damage", and the min half alone for "+175% Enhanced Damage".
        /// Null when the stat drives no aggregated line.
        /// </summary>
        public static int[] StatsShownBy(int statId)
        {
            switch (statId)
            {
                case DamageStatIds.ItemMinDamagePercent:
                    return new[] { DamageStatIds.ItemMinDamagePercent };

                // The physical line prefers the one-hand pair and falls back to the secondary, and
                // TryDescribePhysical has already chosen by the time this is asked — so both pairs
                // are named and a stat with no span simply contributes nothing.
                case DamageStatIds.MinDamage:
                case DamageStatIds.MaxDamage:
                    return new[] { DamageStatIds.MinDamage, DamageStatIds.MaxDamage };

                case DamageStatIds.SecondaryMinDamage:
                case DamageStatIds.SecondaryMaxDamage:
                    return new[]
                    {
                        DamageStatIds.SecondaryMinDamage, DamageStatIds.SecondaryMaxDamage,
                    };

                case DamageStatIds.FireMinDamage:
                case DamageStatIds.FireMaxDamage:
                    return new[] { DamageStatIds.FireMinDamage, DamageStatIds.FireMaxDamage };

                case DamageStatIds.LightningMinDamage:
                case DamageStatIds.LightningMaxDamage:
                    return new[]
                    {
                        DamageStatIds.LightningMinDamage, DamageStatIds.LightningMaxDamage,
                    };

                case DamageStatIds.MagicMinDamage:
                case DamageStatIds.MagicMaxDamage:
                    return new[] { DamageStatIds.MagicMinDamage, DamageStatIds.MagicMaxDamage };

                case DamageStatIds.ColdMinDamage:
                case DamageStatIds.ColdMaxDamage:
                    return new[] { DamageStatIds.ColdMinDamage, DamageStatIds.ColdMaxDamage };

                // Poison prints its two damage ends and a duration, but the duration is a divisor
                // rather than a rolled magnitude, so only the pair is named.
                case DamageStatIds.PoisonMinDamage:
                case DamageStatIds.PoisonMaxDamage:
                    return new[] { DamageStatIds.PoisonMinDamage, DamageStatIds.PoisonMaxDamage };

                default:
                    return null;
            }
        }

        public bool TryDescribe(int statId, out string text)
        {
            text = null;

            switch (statId)
            {
                case DamageStatIds.ItemMaxDamagePercent:
                    if (!_enhanced.BothPresent)
                    {
                        return false;
                    }

                    text = string.Empty; // 0x4e5aa4: returns the latch, emitting nothing
                    return true;

                case DamageStatIds.ItemMinDamagePercent:
                    if (!_enhanced.BothPresent)
                    {
                        return false;
                    }

                    text = Str(DescStringIds.Plus)
                           + TblFormat.FormatNumber(_enhanced.Min)
                           + Str(DescStringIds.Percent)
                           + Str(DescStringIds.Space)
                           + Str(DamageStringIds.EnhancedDamage);
                    return true;

                case DamageStatIds.MinDamage:
                case DamageStatIds.MaxDamage:
                case DamageStatIds.SecondaryMinDamage:
                    return TryDescribePhysical(out text);

                case DamageStatIds.SecondaryMaxDamage:
                    if (_physicalEmitted || _physical.BothPresent)
                    {
                        text = string.Empty;
                        return true;
                    }

                    return false;

                case DamageStatIds.FireMinDamage:
                    return TryDescribeElemental(_fire, out text);
                case DamageStatIds.FireMaxDamage:
                    return Suppress(_fire, out text);

                case DamageStatIds.LightningMinDamage:
                    return TryDescribeElemental(_lightning, out text);
                case DamageStatIds.LightningMaxDamage:
                    return Suppress(_lightning, out text);

                case DamageStatIds.MagicMinDamage:
                    return TryDescribeElemental(_magic, out text);
                case DamageStatIds.MagicMaxDamage:
                    return Suppress(_magic, out text);

                case DamageStatIds.ColdMinDamage:
                    return TryDescribeElemental(_cold, out text);
                case DamageStatIds.ColdMaxDamage:
                    return Suppress(_cold, out text);

                case DamageStatIds.PoisonMinDamage:
                    return TryDescribePoison(out text);
                case DamageStatIds.PoisonMaxDamage:
                case DamageStatIds.PoisonLength:
                    return Suppress(_poison, out text);

                default:
                    return false;
            }
        }

        // Stateful: once the range line is emitted the latch makes every later physical stat return
        // "handled with no text" (0x4e5d06 -> 0x4e5e0a). A degenerate min >= max clears BOTH the
        // emitted flag and the pair latch (0x4e5d1a / 0x4e5d1d) and returns not-handled, which is
        // what lets stat 24 fall through to its own DescFunc line.
        private bool TryDescribePhysical(out string text)
        {
            text = null;

            if (_physicalEmitted)
            {
                text = string.Empty; // already printed: silent skip
                return true;
            }

            if (!_physical.BothPresent)
            {
                return false; // fall through to the per-stat DescFunc lines
            }

            if (_physical.Min >= _physical.Max)
            {
                _physicalEmitted = false;
                _physical.BothPresent = false;
                return false;
            }

            text = Format(DamageStringIds.PhysicalRange, _physical.Min, _physical.Max);
            _physicalEmitted = true;
            return true;
        }

        // min < max is a RANGE; otherwise a single value printed from the MAX, not the min — the max
        // is pushed before the comparison and is the sole argument on that path (0x4e5abf-0x4e5ac2).
        private bool TryDescribeElemental(DamagePair pair, out string text)
        {
            text = null;

            if (!pair.BothPresent)
            {
                return false;
            }

            text = pair.Min >= pair.Max
                ? Format(pair.SingleStringId, pair.Max)
                : Format(pair.RangeStringId, pair.Min, pair.Max);

            return true;
        }

        // Frames are divided by stat 326 (clamped to 1 and WRITTEN BACK, 0x4e5c41), the damage is
        // scaled by frames and rounded with (x + 0x80) >> 8, and the seconds argument is
        // frames / 25 truncating toward zero. The scaled values are written back too (0x4e5c92 /
        // 0x4e5c95), so a second visit to the same stat re-scales what is already scaled.
        private bool TryDescribePoison(out string text)
        {
            text = null;

            if (!_poison.BothPresent)
            {
                return false;
            }

            if (_poisonDivisor <= 0)
            {
                _poisonDivisor = 1; // 0x4e5c41 writes the clamp back
            }

            int frames = _poisonLength / _poisonDivisor;
            int min = (frames * _poison.Min + 128) >> 8;
            int max = (frames * _poison.Max + 128) >> 8;
            int seconds = frames / 25;

            text = min >= max
                ? Format(_poison.SingleStringId, max, seconds)
                : Format(_poison.RangeStringId, min, max, seconds);

            _poisonLength = frames;
            _poison.Min = min;
            _poison.Max = max;

            return true;
        }

        private static bool Suppress(DamagePair pair, out string text)
        {
            text = pair.BothPresent ? string.Empty : null;
            return pair.BothPresent;
        }

        private string Format(int stringId, params object[] args)
        {
            return TblFormat.FormatBounded(Str(stringId), TblFormat.DefaultMaxLength, args);
        }

        private string Str(int index)
        {
            return _strings.GetByIndex(index) ?? string.Empty;
        }
    }

    internal static class UndeadDamageLine
    {
        // itemtypes.txt row 57 "Blunt". IsOfType (0x629bb0) probes a precomputed Equiv1/Equiv2
        // closure matrix, so the whole subtree qualifies: Club 29, Hammer 31, Mace 36, and via
        // "Staves And Rods" 55 also Scepter 24, WAND 25 and STAFF 26. A miss on the items.txt first
        // type is retried against the second (0x629c27 onwards) — implementors of IsItemOfType must
        // test both or they drop the line.
        public const int BluntItemType = 57;

        public const int InherentPercent = 50;

        public static string Build(IStringTable strings, IStatValueSource values, bool isMainStatBlock)
        {
            if (strings == null) throw new ArgumentNullException("strings");

            if (!isMainStatBlock)
            {
                return null; // 0x4e61d7: set-bonus blocks never repeat this line
            }

            if (values == null || !values.IsItemOfType(BluntItemType))
            {
                return null;
            }

            if (values.GetItemStatValue(DamageStatIds.UndeadDamagePercent) != 0)
            {
                return null;
            }

            return Nz(strings.GetByIndex(DescStringIds.Plus))
                   + InherentPercent.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + Nz(strings.GetByIndex(DescStringIds.Percent))
                   + Nz(strings.GetByIndex(DescStringIds.Space))
                   + Nz(strings.GetByIndex(DamageStringIds.DamageToUndead))
                   + Nz(strings.GetByIndex(DescStringIds.Newline));
        }

        private static string Nz(string text)
        {
            return text ?? string.Empty;
        }
    }
}

