#pragma once

//------------------------------------------------------------------------------
// Traversal of an item's stat sources. Nothing here classifies them — see below.
//
// Belongs in D2Common: no STL, no exceptions, no allocation. The JSON writer that
// consumes this lives in ItemStatStorage.h on the tool side.
//
// The point of this file is that it never reads FullStats. FullStats is a derived
// rollup produced by sub_6FDB6C10 / sub_6FDB64A0: activated set bonuses and socket
// contributions are already folded into it, and op-stats (per-level, by-time) are
// resolved into it against whoever is currently holding the item. Recovering the
// parts from it requires subtraction and guesswork. The leaf Stats arrays are the
// ground truth and are already partitioned by source, so this walks those instead.
//------------------------------------------------------------------------------

#include "D2StatList.h"
#include "D2States.h"
#include "Units/Units.h"

// Classification is deliberately NOT done here. dwFlags and dwStateNo are copied verbatim and
// the consumer derives what it needs: STATLIST_EXTENDED marks the base array and STATLIST_MAGIC
// covers item mods. Quality and runeword nodes are both MAGIC and nothing downstream separates
// them, so no state test is needed to classify at all.
//
// Which chain a node hangs off is NOT captured separately, because it is not independent
// information: STATLIST_MergeStatLists picks the chain purely on STATLIST_SET (D2StatList.cpp:1083),
// and D2Common_10574 (#10574) keeps the two in sync by flipping the bit and re-posting. So
// `dwFlags & STATLIST_SET` IS "hangs off pMyStats, contributing nothing". Despite the name that
// bit does not mean "is a set bonus" — set tiers are merely its main user, created by
// ItemMods.cpp:2335 as MAGIC|SET and cleared when the equipped count reaches the tier.

struct D2ItemStatGroupStrc
{
	const D2StatsArrayStrc*	pStats;			// leaf array; never a FullStats rollup
	D2UnitStrc*				pItem;			// unit these stats belong to — always the visited item
	D2StatListStrc*			pStatList;		// node pStats lives on
	int32_t					nStateNo;		// dwStateNo verbatim: which set tier / STATE_RUNEWORD / 0
};

using ItemStatGroupVisitor = void(__fastcall*)(const D2ItemStatGroupStrc* pGroup, void* pContext);

// A unit hanging off pUnit's statlist chain as an extended child: a socket filler on an item, or a
// piece of equipment on a player. The visitor does NOT descend into it.
using ItemStatContainedVisitor = void(__fastcall*)(D2UnitStrc* pContained, void* pContext);

// Visits every leaf stat array belonging to pUnit ITSELF, newest contributing source first, then
// the non-contributing ones. Contained units — a socket filler on an item, an equipped item on a
// player — are reported through pfContained rather than walked, so the caller decides whether to
// nest them.
//
// Fillers surface in REVERSE ordinal order, because the chains are appended at the tail and only ever
// walked backwards. A caller that cares about socket order must sort by UNITS_GetXPosition.
void __fastcall ITEMSTATS_VisitUnitStatLists(D2UnitStrc* pUnit,
	ItemStatGroupVisitor pfVisit, ItemStatContainedVisitor pfContained, void* pContext);
