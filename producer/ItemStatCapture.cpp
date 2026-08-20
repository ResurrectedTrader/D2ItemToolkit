#include "ItemStatCapture.h"

// A jewel cannot itself hold sockets, so one level of nesting is all vanilla produces.

// Helper function
static void __fastcall ITEMSTATS_VisitChain(D2StatListStrc* pChainTail, D2UnitStrc* pItem,
	ItemStatGroupVisitor pfVisit, ItemStatContainedVisitor pfContained, void* pContext)
{
	// Chains are appended at the tail and only ever walked backwards.
	for (D2StatListStrc* pCurrent = pChainTail; pCurrent != nullptr; pCurrent = pCurrent->pPrevLink)
	{
		if (D2StatListExStrc* pCurrentEx = STATLIST_StatListExCast(pCurrent))
		{
			// An extended child of an item is a socket filler: this node is that unit's
			// own statlist, posted here by STATLIST_MergeStatLists when the gem, rune or
			// jewel went into the socket. Descend so its mods stay attributed to it.
			D2UnitStrc* pFiller = pCurrentEx->pOwner;
			if (pFiller && pFiller->dwUnitType == UNIT_ITEM && pFiller->pStatListEx == pCurrentEx)
			{
				pfContained(pFiller, pContext);
			}

			continue;
		}

		D2ItemStatGroupStrc tGroup = {};
		tGroup.pStats       = &pCurrent->Stats;
		tGroup.pItem        = pItem;
		tGroup.pStatList    = pCurrent;
		tGroup.nStateNo     = int32_t(pCurrent->dwStateNo);

		pfVisit(&tGroup, pContext);
	}
}

void __fastcall ITEMSTATS_VisitUnitStatLists(D2UnitStrc* pUnit,
	ItemStatGroupVisitor pfVisit, ItemStatContainedVisitor pfContained, void* pContext)
{
	if (!pUnit || !pfVisit || !pfContained)
	{
		return;
	}


	D2StatListExStrc* pStatListEx = STATLIST_StatListExCast(pUnit->pStatListEx);
	if (!pStatListEx)
	{
		return;
	}

	D2ItemStatGroupStrc tBaseGroup = {};
	tBaseGroup.pStats       = &pStatListEx->Stats;
	tBaseGroup.pItem        = pUnit;
	tBaseGroup.pStatList    = pStatListEx;
	tBaseGroup.nStateNo     = STATE_NONE;

	pfVisit(&tBaseGroup, pContext);

	// Contributing sources, then the ones out of circulation. A node is only ever in one of the
	// two chains, so nothing is visited twice. Which chain it was on needs no separate flag: it
	// is exactly `dwFlags & STATLIST_SET`, which the visitor already sees.
	ITEMSTATS_VisitChain(pStatListEx->pMyLastList, pUnit, pfVisit, pfContained, pContext);
	ITEMSTATS_VisitChain(pStatListEx->pMyStats,    pUnit, pfVisit, pfContained, pContext);
}
