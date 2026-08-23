#pragma once

//------------------------------------------------------------------------------
// Writes an item's stat sources to JSON, verbatim.
//
// Tool side only: nlohmann pulls in STL and exceptions, so this cannot live in
// D2Common. The traversal it consumes is in ItemStatCapture.h.
//
// Nothing is merged, filtered or resolved here. The document is a superset of every
// view a reader can ask for, and it stores raw provenance only — dwFlags, dwStateNo
// and which chain the node hung off. There is no classification field: the consumer
// derives base / set-bonus / item-mod from the flags.
//
// The readers are src/D2ItemToolkit.Net/Stats/ItemStatReader.cs and the TypeScript peer
// at src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.ts. docs/record-format.md is the spec.
//------------------------------------------------------------------------------

#include "ItemStatCapture.h"

// D2BasicTypes.h promotes C4820/C4121 (struct padding) to errors to police the reverse
// engineered layouts, which third party headers cannot satisfy. Same treatment as
// Fog/include/Safesock.h gives the Winsock headers.
#pragma warning(push)
#pragma warning(disable:4820 4121)
#include <nlohmann/json.hpp>
#pragma warning(pop)


// A record is SELF-SIMILAR: identity fields inline, then `statsLists[]` and `items[]`. On an ITEM,
// `items[]` holds its socket fillers and POSITION in the array is the socket index. The format also
// allows a WEARER's carried gear there — that is what lets a consumer derive set state instead of
// being handed bit masks — but THIS EXAMPLE DOES NOT CAPTURE IT; see ITEMSTATS_StoreUnit.
//
// There is no envelope: no `version`, no `item`/`groups` wrapper, and no nested `player` — a viewer
// is a separate document of the same shape, handed to the consumer alongside this one.

// Named once so a typo is a compile error rather than a silently missing field.
// Mirrored by ItemStatKeys/ItemRecordKeys on the consumer side.
namespace ItemStatKeys
{
	static constexpr const char* StatsLists = "statsLists";
	static constexpr const char* Stats      = "stats";
	static constexpr const char* Items     = "items";
	static constexpr const char* Location  = "location";
	static constexpr const char* GridX     = "x";
	static constexpr const char* StateNo   = "stateNo";
	static constexpr const char* Flags     = "flags";
	static constexpr const char* StatId    = "id";
	static constexpr const char* StatLayer = "layer";
	static constexpr const char* StatValue = "value";
	static constexpr const char* ClassId   = "classId";

	// The description engine needs two things beyond the stat groups: what the item IS
	// (to reach its items.txt row and its name tables) and who is looking at it (class gates and
	// requirement colours).

	static constexpr const char* Code          = "code";
	static constexpr const char* Quality       = "quality";
	static constexpr const char* ItemFlags     = "itemFlags";
	static constexpr const char* Format        = "format";
	static constexpr const char* FileIndex     = "fileIndex";
	static constexpr const char* RarePrefix    = "rarePrefix";
	static constexpr const char* RareSuffix    = "rareSuffix";
	static constexpr const char* AutoAffix     = "autoAffix";
	static constexpr const char* MagicPrefix   = "magicPrefix";
	static constexpr const char* MagicSuffix   = "magicSuffix";
	static constexpr const char* EarLevel      = "earLevel";
	static constexpr const char* PlayerName    = "playerName";

	static constexpr const char* UnitType      = "unitType";
	static constexpr const char* FlagsEx    = "flagsEx";
	static constexpr const char* Name       = "name";
	static constexpr const char* Skills     = "skills";
	static constexpr const char* SkillId    = "skill";
	static constexpr const char* SkillLevel = "level";

}

// Layer-major, which is the MIRROR of the engine's own packing: D2SLayerStatIdStrc puts nLayer at
// 0x00 and nStat at 0x02, so a D2StatStrc::nPackedValue is (nStat << 16) | nLayer. A key from here
// is NOT directly comparable with one of those — convert with ((p & 0xFFFF) << 16) | (p >> 16).
// Mirrored by ItemStatReader.PackStatKey, which uses the same layer-major order.
inline int32_t ITEMSTATS_PackStatKey(uint16_t nLayer, uint16_t nStat)
{
	return int32_t(uint32_t(nLayer) << 16 | nStat);
}

inline uint16_t ITEMSTATS_StatFromKey(int32_t nKey)  { return uint16_t(uint32_t(nKey) & 0xFFFF); }
inline uint16_t ITEMSTATS_LayerFromKey(int32_t nKey) { return uint16_t(uint32_t(nKey) >> 16); }


// Serialises a unit — an item or a player — to one self-similar shape:
//
//   { unitType, classId, <identity fields>, statsLists: [ StatList ], items: [ Unit ] }
//
// Both are D2UnitStrc in the game, so both serialise the same way. There is no version field and no
// nesting of one inside the other: a caller that wants a player-dependent description stores the two
// documents separately and hands both to the consumer.
//
// `items[]` means one relation on each kind of unit: an ITEM's socket fillers, or a WEARER's carried
// gear. That is ONE FIELD carrying TWO relations, and a consumer must tell them apart: a reader that
// recurses — as a socket reader must — folds a wearer's carried gear into the wearer's own stats.
//
// This example emits the ITEM half only. A wearer's carried gear needs an inventory walk it does
// not do, so set derivation is unreachable from a capture until that lands.
//
// A wearer's items are NOT nested inside an item document — the two documents stay separate, and
// re-serialising the whole kit inside the item being described would duplicate it.
//
// Nothing precomputed is emitted. Everything the description engine needs beyond the stat lists is
// derivable on the consumer side from the excel tables plus this document:
//
//   required level        ITEM_CalcRequiredLevel      -> RequiredLevelCalculator
//   required str/dex      ItemsTxt + stat 91          -> EquipRequirements
//   the four met flags    ITEM_CheckEquipRequirements -> EquipRequirements
//   attack speed          ITEM_CalcWeaponAttackSpeed  -> AttackSpeedCalculator (AnimData.D2)
//   Holy Shield contribs  SKILL_CalcMin/MaxDamage     -> SkillDamage
//
// A player document additionally carries `skills`: SKILLS_GetSkillLevel reads pSkill->nSkillLevel
// off the SKILL list, so unlike level, strength and dexterity a skill level is not a stat and cannot
// be derived. Whether a state is up IS derivable — a state is a stat list carrying its own
// dwStateNo.
//
// What it does NOT yet carry is the wearer's equipped items, so the viewer's attributes are base
// only and `ITEM_CheckEquipRequirements` can colour a requirement red where the game colours it
// white. See "What the record deliberately does not carry" in docs/record-format.md.
//
// There was a "runtime" object here carrying the difficulty and the period-of-day angle. Nothing
// consumed either, so it is gone; it would come back only for the by-time stat variation and the
// quest-item colour, neither of which is implemented.
nlohmann::json ITEMSTATS_StoreUnit(D2UnitStrc* pUnit);
