
namespace D2ItemToolkit
{
    /// <summary>
    /// The slice of missiles.txt the throwing-potion damage arm reads (0x485410). The record is 420
    /// bytes; the fields used here are dwMinDamage +0xB0, dwMaxDamage +0xB4, nElemType +0xE4,
    /// dwElemMin +0xE8, dwElemMax +0xEC, dwElemLen +0x11C and nHitShift +0x196.
    /// </summary>
    public sealed class MissileTable
    {
        private readonly TxtFile _missiles;
        private readonly TxtFile _elementTypes;

        public MissileTable(TxtFile missiles, TxtFile elementTypes)
        {
            _missiles = missiles;
            _elementTypes = elementTypes;
        }

        /// <summary>Rows in missiles.txt; 0 when the file was not supplied.</summary>
        public int RowCount
        {
            get { return _missiles == null ? 0 : _missiles.RowCount; }
        }

        public bool TryGetThrowDamage(int missileId, out MissileThrowDamage damage)
        {
            damage = default(MissileThrowDamage);

            if (_missiles == null || missileId < 0 || missileId >= _missiles.RowCount)
            {
                return false;
            }

            int hitShift = _missiles.GetInt(missileId, "HitShift");
            int elementType = ElementType(_missiles.GetString(missileId, "EType"));

            // GetMinDamage/GetMinElemDamage 0x64af20 / 0x64b100 with level 1:
            // SKILLS_GetValueByLevelBreakpoints returns 0 below level 2 (0x644b7b), and every
            // shipped potion missile has DmgSymPerCalc/EDmgSymPerCalc = -1, so no calc runs.
            int min = _missiles.GetInt(missileId, "MinDamage") << hitShift;
            int max = _missiles.GetInt(missileId, "MaxDamage") << hitShift;
            int elementMin = _missiles.GetInt(missileId, "EMin") << hitShift;
            int elementMax = _missiles.GetInt(missileId, "Emax") << hitShift;

            if (elementType == ElementPoison)
            {
                // 0x4854e7-0x485515: the elemental halves are spread over the cloud's duration,
                // GetElementalLength at level 1 being plain ELen (0x64b2ca).
                int divisor = _missiles.GetInt(missileId, "ELen") / 25;
                if (divisor <= 0)
                {
                    divisor = 1;
                }

                elementMin /= divisor;
                elementMax /= divisor;
            }

            damage.Min = (min + elementMin) >> 8;
            damage.Max = (max + elementMax) >> 8;

            // 0x48555c: max is raised to min, never the other way round.
            if (damage.Max <= damage.Min)
            {
                damage.Max = damage.Min;
            }

            damage.Color = ElementColor(elementType);
            return true;
        }

        private const int ElementPoison = 5;

        // The jump table at 0x4854d0, indexed by elemType - 1. Magic (3) and everything outside
        // 1..5 take the default arm, which leaves the colour at 0.
        private static int ElementColor(int elementType)
        {
            switch (elementType)
            {
                case 1: return 1;   // fire
                case 2: return 4;   // lightning
                case 4: return 3;   // cold
                case ElementPoison: return 2;
                default: return 0;
            }
        }

        private int ElementType(string code)
        {
            if (_elementTypes == null || string.IsNullOrEmpty(code))
            {
                return 0;
            }

            int row = _elementTypes.FindRow("Code", code);
            return row < 0 ? 0 : row;
        }
    }

    public struct MissileThrowDamage
    {
        public int Min;
        public int Max;

        public int Color;
    }
}
