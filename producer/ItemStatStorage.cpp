#include "ItemStatStorage.h"

#include "D2Inventory.h"	// UNITS_GetXPosition
#include "D2Skills.h"		// SKILLS_GetSkillLevel, D2SkillStrc
#include "Units/Player.h"	// D2PlayerDataStrc
#include "DataTbls/ItemsTbls.h"	// DATATBLS_GetItemsTxtRecord
#include "Units/Item.h"		// D2ItemDataStrc

#include <cstring>

#include <algorithm>
#include <tuple>
#include <utility>
#include <vector>

using namespace ItemStatKeys;

// The traversal carries ONE void* but the two visitors want different things: one appends to the
// document, the other collects contained units. Passing the document to both made
// ITEMSTATS_CollectContained push_back into a nlohmann::json as though it were a vector — so the
// filler list stayed empty and `sockets` was never emitted for ANY item.
struct D2ItemStatStoreContextStrc
{
	nlohmann::json*				pUnit;
	std::vector<D2UnitStrc*>*	pContained;
};

// Helper function
static void __fastcall ITEMSTATS_StoreVisitor(const D2ItemStatGroupStrc* pGroup, void* pContext)
{
	nlohmann::json& jUnit = *((D2ItemStatStoreContextStrc*)pContext)->pUnit;

	if (pGroup->pStats->nStatCount == 0)
	{
		return;	// an empty leaf carries no information
	}

	nlohmann::json jStats = nlohmann::json::array();
	for (int32_t i = 0; i < pGroup->pStats->nStatCount; ++i)
	{
		const D2StatStrc& tStat = pGroup->pStats->pStat[i];

		// Values are stored raw: pre nValShift, pre op-stat resolution. That is what
		// makes them stable across wearers. Shift and resolve at display time.
		nlohmann::json jStat = nlohmann::json::object();
		jStat[StatId]    = tStat.nStat;
		jStat[StatValue] = tStat.nValue;

		if (tStat.nLayer != 0)
		{
			jStat[StatLayer] = tStat.nLayer;
		}

		jStats.push_back(std::move(jStat));
	}

	// dwFlags already says which chain the node was on: STATLIST_SET is set if and only if it
	// hung off pMyStats. Emitting that a second time would let a hand written record contradict
	// itself, so it is not emitted at all.
	nlohmann::json jGroup = nlohmann::json::object();
	jGroup[StateNo] = pGroup->nStateNo;
	jGroup[Flags]   = pGroup->pStatList->dwFlags;
	jGroup[Stats]   = std::move(jStats);

	jUnit[StatsLists].push_back(std::move(jGroup));
}

// Helper function. Identity fields go straight on the unit record: an item and a player are both
// D2UnitStrc, so the two documents differ only in which of these apply.
static void __fastcall ITEMSTATS_StoreUnitIdentity(nlohmann::json& jUnit, D2UnitStrc* pUnit)
{
	jUnit[UnitType] = int32_t(pUnit->dwUnitType);
	jUnit[ClassId]  = pUnit->dwClassId;

	if (pUnit->dwUnitType != UNIT_ITEM)
	{
		// Raw, not decoded into named booleans: the consumer masks what it needs. UNITFLAGEX_ISEXPANSION
		// is the only bit the description engine reads today (0x62b877 through CheckFlagWithMask).
		jUnit[FlagsEx] = pUnit->dwFlagEx;

		if (const D2PlayerDataStrc* pPlayerData = UNITS_GetPlayerData(pUnit))
		{
			char szName[17] = {};
			memcpy(szName, pPlayerData->szName, 16);
			jUnit[Name] = szName;
		}

		// A skill LEVEL is the one thing a stat capture cannot reach: SKILLS_GetSkillLevel reads
		// pSkill->nSkillLevel off this list. The bonused level is what the tooltip asks for
		// (0x485df1 passes bBonus = 1), so that is what is stored.
		if (pUnit->pSkills)
		{
			nlohmann::json jSkills = nlohmann::json::array();

			for (D2SkillStrc* pSkill = pUnit->pSkills->pFirstSkill; pSkill;
				pSkill = pSkill->pNextSkill)
			{
				if (!pSkill->pSkillsTxt)
				{
					continue;
				}

				nlohmann::json jSkill = nlohmann::json::object();
				jSkill[SkillId]    = pSkill->pSkillsTxt->nSkillId;
				jSkill[SkillLevel] = SKILLS_GetSkillLevel(pUnit, pSkill, TRUE);

				jSkills.push_back(std::move(jSkill));
			}

			// List order is allocation order; sort so the document is stable.
			std::sort(jSkills.begin(), jSkills.end(),
				[](const nlohmann::json& a, const nlohmann::json& b)
				{
					return a.value(SkillId, 0) < b.value(SkillId, 0);
				});

			jUnit[Skills] = std::move(jSkills);
		}

		return;
	}

	// dwClassId indexes the ONE table TXT_AllocTxt_items builds by compiling weapons, then armor,
	// then misc (0x633351 / 0x63336d / 0x63338c) and summing the counts at 0x6333ab. The code is
	// emitted alongside it so a consumer can validate its own table ordering rather than trust it.
	const D2ItemsTxt* pItemsTxt = DATATBLS_GetItemsTxtRecord(pUnit->dwClassId);
	if (pItemsTxt)
	{
		char szCode[5] = {};
		memcpy(szCode, pItemsTxt->szCode, 4);

		// The compiled code is space padded, not NUL padded.
		for (int32_t i = 3; i >= 0 && szCode[i] == ' '; --i)
		{
			szCode[i] = '\0';
		}

		jUnit[Code] = szCode;
	}

	const D2ItemDataStrc* pItemData = pUnit->pItemData;
	if (!pItemData)
	{
		return;
	}

	jUnit[Quality]   = int32_t(pItemData->dwQualityNo);
	jUnit[ItemFlags] = pItemData->dwItemFlags;

	// 0 is a classic item. ITEM_CalcRequiredLevel hides a classic unique's level requirement from a
	// non-expansion viewer (0x62b877), so the consumer needs the format to reproduce that.
	jUnit[Format] = pItemData->wItemFormat;

	// Overloaded by quality: lowqualityitems row, UniqueItems row (or -1), SetItems row, a monstats
	// row for body parts, or a character class for ears.
	jUnit[FileIndex] = pItemData->dwFileIndex;

	jUnit[RarePrefix] = pItemData->wRarePrefix;
	jUnit[RareSuffix] = pItemData->wRareSuffix;
	jUnit[AutoAffix]  = pItemData->wAutoAffix;

	// 1-based indices into concatenated affix tables — [magicsuffix][magicprefix][automagic] for
	// the magic pair, [raresuffix][rareprefix] for the rare pair. Emitted verbatim.
	jUnit[MagicPrefix] = { pItemData->wMagicPrefix[0], pItemData->wMagicPrefix[1],
		pItemData->wMagicPrefix[2] };
	jUnit[MagicSuffix] = { pItemData->wMagicSuffix[0], pItemData->wMagicSuffix[1],
		pItemData->wMagicSuffix[2] };

	jUnit[EarLevel] = pItemData->nEarLvl;

	char szPlayerName[17] = {};
	memcpy(szPlayerName, pItemData->szPlayerName, 16);
	jUnit[PlayerName] = szPlayerName;
}

// Helper function. Collects the StatListEx children rather than descending, so the caller decides
// whether they are sockets worth nesting.
static void __fastcall ITEMSTATS_CollectContained(D2UnitStrc* pContained, void* pContext)
{
	((D2ItemStatStoreContextStrc*)pContext)->pContained->push_back(pContained);
}

nlohmann::json ITEMSTATS_StoreUnit(D2UnitStrc* pUnit)
{
	nlohmann::json jUnit = nlohmann::json::object();
	if (!pUnit)
	{
		return jUnit;
	}

	ITEMSTATS_StoreUnitIdentity(jUnit, pUnit);
	jUnit[StatsLists] = nlohmann::json::array();

	std::vector<D2UnitStrc*> tContained;
	D2ItemStatStoreContextStrc tContext = { &jUnit, &tContained };
	ITEMSTATS_VisitUnitStatLists(pUnit, ITEMSTATS_StoreVisitor, ITEMSTATS_CollectContained, &tContext);

	// Chain order is allocation order, which differs between a freshly rolled item and
	// the same item after a save/load round trip. Sort so the document is stable and can
	// be hashed as a fingerprint.
	nlohmann::json& jStatsLists = jUnit[StatsLists];
	std::sort(jStatsLists.begin(), jStatsLists.end(),
		[](const nlohmann::json& a, const nlohmann::json& b)
		{
			const auto tKey = [](const nlohmann::json& j)
			{
				// Raw struct fields only — there is no classification to sort on any more.
				// Chain membership needs no third component: it lives in STATLIST_SET.
				return std::make_tuple(
					j.value(Flags, uint32_t(0)),
					j.value(StateNo, 0));
			};

			return tKey(a) < tKey(b);
		});

	// Only an ITEM nests its contained units. A player's chain carries the same kind of extended
	// child for every piece of equipment, and re-serialising the wearer's whole kit inside one item
	// document would duplicate the very item being described.
	if (pUnit->dwUnitType != UNIT_ITEM || tContained.empty())
	{
		return jUnit;
	}

	// The chains are walked BACKWARDS, so fillers arrive newest first. Array position carries the
	// socket index now, so sort by the ordinal INVENTORY_PlaceItemInSocket assigned — it writes
	// dwItemCount-before-insert into the filler's static X, giving a contiguous 0..n-1.
	std::sort(tContained.begin(), tContained.end(),
		[](D2UnitStrc* a, D2UnitStrc* b)
		{
			return UNITS_GetXPosition(a) < UNITS_GetXPosition(b);
		});

	nlohmann::json jSockets = nlohmann::json::array();
	for (D2UnitStrc* pFiller : tContained)
	{
		jSockets.push_back(ITEMSTATS_StoreUnit(pFiller));
	}

	jUnit[Sockets] = std::move(jSockets);

	return jUnit;
}
