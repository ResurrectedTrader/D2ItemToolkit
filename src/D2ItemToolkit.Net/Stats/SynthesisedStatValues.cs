using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// An <see cref="IStatValueSource"/> over a stat set that was built rather than captured.
    ///
    /// SKILLDESC_BuildStatListDesc (0x4e49c0) collects the damage kinds by walking the UNIT'S OWN
    /// statlists, which includes the temporary 0x40 list a socket-filler description attaches. So the
    /// same synthesised stats have to be visible here, not only in the packed set handed to
    /// Describe — the aggregate reads exclusively through this interface, and with no source the
    /// paired damage lines silently degrade into one line per stat.
    /// </summary>
    internal sealed class SynthesisedStatValues : IStatValueSource
    {
        private readonly IDictionary<int, int> _stats;
        private readonly IDictionary<int, int> _unitStats;
        private readonly ItemIdentity _item;
        private readonly ItemViewer _viewer;
        private readonly ItemTable _items;
        private readonly ItemTypeTree _types;
        private readonly bool _describedUnitIsItem;

        /// <summary>
        /// Two stat scopes, deliberately separate. <c>stats</c> is the DESCRIBE scope: the temp
        /// list the engine builds at 0x4e612b, which the damage aggregate (0x4e49c0) and the 23/24
        /// suppression pair (0x4e62d2) both read. <c>unitStats</c> is the UNIT scope: every list on
        /// the item, which is what GetTxtMaxDurability 0x625e00 and the never-breaks gate query,
        /// and it defaults to <c>stats</c>. Feeding one dictionary to both over-describes the item.
        ///
        /// <c>describedUnitIsItem</c> is false only for the full-set bonus block, whose described
        /// unit is the PLAYER (SKILLDESC_AppendItemBuffTextAlt passes a1 at 0x4e670c). The
        /// never-breaks tail tests `*v8 == 4` at 0x4e6375, so a player-scoped block cannot reach it.
        /// </summary>
        public SynthesisedStatValues(
            IDictionary<int, int> stats,
            ItemIdentity item,
            ItemViewer viewer,
            ItemTable items,
            ItemTypeTree types,
            IDictionary<int, int> unitStats = null,
            bool describedUnitIsItem = true)
        {
            _stats = stats ?? new Dictionary<int, int>();
            _unitStats = unitStats ?? _stats;
            _item = item;
            _viewer = viewer;
            _items = items;
            _types = types;
            _describedUnitIsItem = describedUnitIsItem;
        }

        public int GetBaseStatValue(int statId, int layer)
        {
            int value;
            return _stats.TryGetValue(ItemStatReader.PackStatKey(layer, statId), out value)
                ? value
                : 0;
        }

        public int GetItemStatValue(int statId)
        {
            int value;
            return _unitStats.TryGetValue(ItemStatReader.PackStatKey(0, statId), out value)
                ? value
                : 0;
        }

        /// <summary>
        /// The VIEWER's stat, not the item's. SKILLDESC_CalcStatGroupValue 0x4e4c50 scales an
        /// op 2-5 stat by `GetStatUnsignedValue(GetPlayerUnit(), opBase, 0)` (0x4e4c93/0x4e4c99),
        /// and GetPlayerUnit 0x463dd0 returns the local client player — categorically not the item
        /// being described. Returning 0 here made every "(Based on Character Level)" modifier
        /// render its number as 0.
        ///
        /// No viewer stays 0: GetStatUnsignedValue 0x625483 returns 0 for a null unit rather than
        /// halting, so the line is still emitted with a zero value.
        /// </summary>
        public int GetPlayerStatValue(int statId)
        {
            return _viewer == null ? 0 : _viewer.Stat(statId);
        }

        public int PlayerClass
        {
            get { return _viewer == null ? -1 : _viewer.ClassId; }
        }

        public bool IsItemOfType(int itemTypeId)
        {
            if (_types == null || _items == null || _item == null)
            {
                return false;
            }

            return _types.IsOfType(
                _types.Row(_items.PrimaryTypeCode(_item.ClassId)),
                _types.Row(_items.SecondaryTypeCode(_item.ClassId)),
                itemTypeId);
        }

        public bool DescribedUnitIsItem
        {
            get { return _describedUnitIsItem; }
        }

        public bool ItemTableAllowsDurability
        {
            get
            {
                if (_items == null || _item == null)
                {
                    return false;
                }

                return _items.GetInt(_item.ClassId, "nodurability") == 0
                       && _items.GetInt(_item.ClassId, "durability") != 0;
            }
        }

        /// <summary>
        /// Despite the name, GetTxtMaxDurability 0x625e00 reads the item's STAT 73 (record 73 of
        /// the 324-byte ItemStatCost array, 0x5C64/0x144 at 0x625e21), not an items.txt column.
        /// The never-breaks gate depends on the difference: it wants a table durability with no
        /// stat behind it.
        /// </summary>
        public int GetTxtMaxDurability()
        {
            return GetItemStatValue(StatMaxDurability);
        }

        private const int StatMaxDurability = 73;
    }
}
