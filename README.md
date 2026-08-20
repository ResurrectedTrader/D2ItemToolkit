# D2ItemToolkit

[![NuGet](https://img.shields.io/nuget/v/D2ItemToolkit.svg)](https://www.nuget.org/packages/D2ItemToolkit)
[![npm](https://img.shields.io/npm/v/d2itemtoolkit.svg)](https://www.npmjs.com/package/d2itemtoolkit)

Diablo II 1.14d's item description engine, reimplemented from the disassembly. Hand it a unit and
it renders the tooltip the game would draw for it — the same text, the same order, the same
colours.

Available for **C#** (`D2ItemToolkit`) and **TypeScript** (`d2itemtoolkit`). The game's tables are
embedded in both packages, so there is nothing to download or point at.

```
Vigorous Large Shield of Absorption
Defense: 300
Chance to Block: 30%
Smite Damage: 2 to 4
Durability: 40 of 62
Required Strength: 34
Required Level: 24
+150% Enhanced Defense
Fire Resist +25%
30% Better Chance of Getting Magic Items
```

That is the [quick start](#quick-start) below, with the game's colour markers stripped for
readability — see [Render](#render) for what the raw string contains.

Useful for trade sites, loot filters, stash viewers, bots, drop notifiers — anywhere you have item
data and need to show it the way a player expects to see it.

> **Pre-1.0.** The public surface may still move between minor versions.

## Install

```bash
dotnet add package D2ItemToolkit     # netstandard2.0
npm install d2itemtoolkit            # ESM only, Node 18+ and browsers
```

> The MIT licence covers the source. Both packages also embed Diablo II's shipped data tables,
> which are Blizzard's property and are not covered by it — see [Licence](#licence).

## Quick start

Two inputs: the **item**, and optionally the **viewer** — the player looking at it. The viewer
decides requirement colours, class gates, attack speed, block chance, smite damage and every
level-scaled line. Pass `null` and you get the item rendered with no player context, which is a
legal call that produces fewer lines.

Both are unit documents of the same shape, because an item and a player are the same struct in the
game. A socket filler is another one nested inside.

### C#

```csharp
using D2ItemToolkit;

Unit item = Unit.FromJson(itemJson);
Unit player = Unit.FromJson(playerJson);

Tooltip tip = TooltipEngine.Embedded.Render(item, player);

Console.WriteLine(tip.Text);
```

### TypeScript

```ts
import { TooltipEngine, unitFromJson } from 'd2itemtoolkit';

const item = unitFromJson(itemJson);
const player = unitFromJson(playerJson);

const tip = TooltipEngine.embedded.render(item, player);

console.log(tip.text);
```

Same call chain, camelCased. `TooltipEngine.Embedded` / `.embedded` parses the tables once and
caches them — hold onto it rather than building an engine per item. Rendering is pure, so one
engine can serve concurrent callers.

The tables are inflated **synchronously**, which is what keeps the whole API non-async.

## Building a unit in code

You do not need JSON. Everything required to author a record is public.

```csharp
var item = new Unit();
item.UnitType = 4;
item.ClassId = 330;                              // Large Shield
item.Quality = ItemQualityNo.Magic;
item.ItemFlags = ItemRecordFlags.Identified;

// 1-based indices into the CONCATENATED [magicsuffix][magicprefix][automagic] array, so 962 is
// past the 747 suffix rows and lands in the prefix table.
item.MagicPrefix[0] = 962;
item.MagicSuffix[0] = 121;

item.StatsLists.Add(
    new UnitStatList(0, ItemStatListFlags.Extended)   // the base array
        .Add(31, 120).Add(72, 40).Add(73, 62));
item.StatsLists.Add(
    new UnitStatList(0, ItemStatListFlags.Magic)      // what the item itself grants
        .Add(16, 150).Add(39, 25).Add(80, 30));

Tooltip tip = TooltipEngine.Embedded.Render(item);
```

In TypeScript use `createUnit`, which defaults every field you do not set. There is no fluent
statlist builder — write them as object literals:

```ts
const item = createUnit({
  unitType: 4,
  classId: 330,
  quality: 4,
  itemFlags: 16,
  magicPrefix: [962, 0, 0],
  magicSuffix: [121, 0, 0],
  statsLists: [
    { stateNo: 0, flags: 0x80000000, stats: [{ id: 31, value: 120 }, { id: 72, value: 40 }] },
    { stateNo: 0, flags: 0x40, stats: [{ id: 16, value: 150 }, { id: 39, value: 25 }] },
  ],
});
```

**`IUnit` is the contract; `Unit` is one implementation of it.** Every C# entry point takes the
interface, so if you already hold unit state in your own shape — a live client, another DTO, a
database row — implement `IUnit` over it instead of copying. In TypeScript `Unit` is an interface,
but every one of its fields is required, so `createUnit` is the practical way in.

## What the engine offers

```
IUnit (item) ──────┐
                   ├── TooltipEngine ──┬── Render         → the tooltip
IUnit (viewer) ────┘                   ├── RenderSetItem  → a set piece, with siblings supplied
    optional                           ├── Breakdown      → where each modifier came from
                                       ├── Appearance     → inventory sprite and palette shift
                                       ├── Requirements   → strength / dexterity / level / class
                                       └── ClassIdsOfType → every classId under a type code
```

It also exposes the parsed game tables — see [Reading the tables](#reading-the-tables). The section
writers and the description engine stay internal.

### Render

`Tooltip.Lines` comes back in display order, top row first.

| member | what it gives you |
|---|---|
| `tip.Kind` | which builder produced this — `Generic`, `Book` or `IdentifiedSetItem` |
| `tip.Lines` | one entry per row, each with `Text`, `Color` and originating `Section` |
| `tip.Text` | the rows joined with newlines |
| `tip.ColoredText` | the same, plus the game's per-line `U+00FF 'c' N` colour marker |

`ItemTooltipKind` also declares `ShopTransaction` and `Transmogrify`. Nothing currently produces
them — they belong with the transaction-cost gap in [what is not
implemented](#what-is-not-implemented-and-why).

**Neither string form is marker-free.** Some section writers embed a marker mid-line, and the game
embeds those too, so they survive in `Text`. The same shield as above:

```
Vigorous Large Shield of Absorption
Defense: ÿc3300
ÿc0Chance to Block: ÿc330%
Smite Damage: 2 to 4
...
```

Two markers on the block line is not a bug: `INV_FormatBlockChanceText` prepends colour 0 to its
own label buffer and `LoadItemDesc` prepends the section's on top. If you want plain text, strip
them — `Regex.Replace(tip.Text, "ÿc.", "")` in C#, `tip.text.replace(/ÿc./g, '')` in
TypeScript. Prefer `Lines` if you are rendering yourself: you get the colour per row without
parsing markers out of a string.

> **Do not join `line.Text` yourself.** Both string forms spend the game's 1023-character budget
> across the rows before joining, so a long tooltip truncates where the game truncates. (Set-item
> tooltips are exempt — that path has no limit.) Each `line.Text` also ends with its own `\n`.

In TypeScript `line.text` is typed `string | null`, so narrow it before use.

### Options

```csharp
var options = new TooltipOptions();
options.IncludeSockets = false;   // render the base item as if nothing were socketed in it
options.ShopMode = 1;             // 1-9 mark a shop context (see the gap below)
options.Difficulty = 2;           // only a quest item with questdiffcheck reads this
options.QuestColorPrefix = true;  // append the trailing quest-colour marker
options.ClientPlayer = character; // see below
```

`ClientPlayer` exists for exactly one case: a **mercenary's** panel. Requirements, class
restriction, block chance and the smite gate all use the viewer — who is the merc — but the attack
speed line is timed against the *character*. If you are rendering a merc's equipment, set this to
the player. Leave it null everywhere else.

### Breakdown — where each modifier came from

The game draws one blue block. `Breakdown` splits it by source, which is what a "hold shift" view
wants:

```csharp
TooltipBreakdown b = TooltipEngine.Embedded.Breakdown(item, player);
```

For the shield above with a **Perfect Ruby** socketed:

| | |
|---|---|
| `b.Base` | `+120 Defense` |
| `b.Magic` | `+150% Enhanced Defense`, `Fire Resist +25%`, `30% Better Chance of Getting Magic Items` |
| `b.Sockets` | `Fire Resist +40%` |
| `b.SetBonuses` | *(none)* |

`Render` merges those into one `Fire Resist +65%`, because that is what the game does.
`IncludeSockets = false` gives `Fire Resist +25%` instead.

**Caveat:** the game never draws these separately, so unlike `Render` this cannot be checked
against the original — it is the one part of the API with no ground truth. Every line is still
produced by the same writers; only the stat selection differs.

### Appearance — drawing the item in a grid

```csharp
ItemAppearance look = TooltipEngine.Embedded.Appearance(item);

string image = look.Image;   // "rin3"  — sprite name, fetched as image + ".dc6"
int color = look.Color;      // 0-20 palette shift, -1 for none
int invTrans = look.InvTrans;// which transform table the shift indexes; 0 means no tint
bool tinted = look.IsTinted; // Color >= 0 && InvTrans != 0
```

`Image` is **not** the item code, so don't shortcut it. Exceptional and elite tiers share the base
tier's art (`xap`, the exceptional Cap, resolves to `cap`), set and unique items get their own, and
the four types with a random inventory graphic append the rolled variant — a ring is
`rin1`..`rin5`, which is the only thing `gfxIndex` in the record is for.

### Requirements

```csharp
ItemRequirements req = TooltipEngine.Embedded.Requirements(item, player);

int strength = req.Strength;
int level = req.Level;
int classRestriction = req.ClassRestriction;   // 0-6, or 7 for unrestricted
bool metStrength = req.MetStrength;
bool metLevel = req.MetLevel;
```

The numbers do not depend on the viewer, but the flags do. **With no viewer every `Met*` flag reads
false** except `MetClass` — a null unit's stats read as 0, and the test is `available > 0 &&
available >= required`, so even a zero requirement fails. Pass a viewer if you care about the flags.

(That is only the API. The rendered tooltip does not paint anything red without a viewer.)

### Finding items by type

```csharp
IReadOnlyList<int> swords = TooltipEngine.Embedded.ClassIdsOfType("swor");
```

Every classId whose type chains up to `swor`, including exceptional and elite tiers and the
class-specific sword types.

### Set items

An identified set item uses a different builder, and `Render` routes to it automatically. But the
item's own record cannot say which sibling pieces the viewer is carrying, so on its own it renders
every piece red — which is what the game draws for a character holding that piece alone.

To render it properly, supply what the record cannot know. `engine.Sets` is how you enumerate a
set's pieces to build those ids and masks:

```csharp
var set = new SetItemTooltipInput();
set.OwnedSetItemIds = ownedIds;            // setitems row indices the viewer holds anywhere
set.WornMaskIncludingSelf = wornMask;      // bit per set index, pieces actually equipped
set.WornMaskExcludingSelf = wornMaskMinusThisPiece;
set.IsEquipped = true;

Tooltip tip = TooltipEngine.Embedded.RenderSetItem(item, set, player);
```

> **Build the worn masks from body locations 1-10 only.** Locations 11 and 12 are the weapon-swap
> slots, and a piece on your swap bar does not count toward the set bonus. Including them is the
> easiest way to get this wrong.

### Reading the tables

The engine hands you the parsed game tables for lookups it does not do for you. Every one is walked
the same way — `RowCount` for the bound, `RowAt(index)` for the row:

```csharp
TooltipEngine engine = TooltipEngine.Embedded;

for (int classId = 0; classId < engine.Items.RowCount; ++classId)
{
    ItemRow item = engine.Items.RowAt(classId);
    Console.WriteLine($"{item.Code} tier={item.Tier} lvl={item.RequiredLevel}");
}
```

`RowAt` returns null past the end rather than throwing. The same shape holds for `engine.Types`,
`engine.Data.ItemStatCost`, `engine.Data.Skills`, `engine.Data.Classes`, and the tables you build
yourself from `engine.Data` — `ColorTable`, `GemTable`, `PropertiesTable`, `MagicAffixTable`,
`MissileTable`, `SkillDamage`.

Two tables have two row spaces and name their accessors after them instead:

```csharp
engine.Sets.SetCount     / engine.Sets.SetAt(i)      // sets.txt
engine.Sets.PieceCount   / engine.Sets.PieceAt(i)    // setitems.txt — the ids a set item needs

engine.Data.MonsterTypes.MonsterCount     / .MonsterAt(i)
engine.Data.MonsterTypes.MonsterTypeCount / .MonsterTypeAt(i)
```

`TxtFile` is the raw column reader underneath all of them, and keeps `RowCount` with
`GetString(row, column)` / `GetInt` / `GetBool` — a row there has no fixed shape.

TypeScript is identical, camelCased: `rowCount`, `rowAt`, `setAt`, `pieceAt`, `monsterAt`.

### Loading tables from disk

If you would rather read a real MPQ extraction than use the embedded copy — a mod with altered
tables, or a different locale:

```csharp
TooltipEngine fromDisk = TooltipEngine.FromFiles(excelDir, localeDir, globalDir);
TooltipEngine fromTables = TooltipEngine.FromData(data);   // tables you already hold
```

TypeScript has `TooltipEngine.fromFiles` (Node-only) and `fromData`. Note the loader names differ:
C# has `D2DataFiles.LoadEmbedded()` and `D2DataFiles.Load(dirs...)`; TypeScript has
`D2DataFiles.load()` for both.

## The record format

A unit document is self-similar — a socket filler is another unit, and its **position in the array
is the socket index**:

```json
{
  "unitType": 4,
  "classId": 442,
  "statsLists": [
    { "stateNo": 0,   "flags": 2147483648, "stats": [ { "id": 31, "value": 445 } ] },
    { "stateNo": 0,   "flags": 64,         "stats": [ { "id": 39, "value": 40 } ] },
    { "stateNo": 165, "flags": 8256,       "stats": [ { "id": 0, "value": 20 } ] }
  ],
  "sockets": [
    { "unitType": 4, "classId": 620 }
  ]
}
```

`flags` and `stateNo` are copied verbatim from the statlist node and the consumer derives
everything from them — there is deliberately no "source" or classification field for you to fill
in, because getting it wrong is how a real bug got in.

| bit | meaning |
|---|---|
| `0x80000000` `STATLIST_EXTENDED` | the base array |
| `0x40` `STATLIST_MAGIC` | everything the item itself grants |
| `0x2000` `STATLIST_SET` | the node sits on the `pMyStats` chain contributing nothing |

Set tiers are identified by `stateNo` 165-170, not by that bit.

A socket filler needs no stats of its own: a real client never instantiates them, so the engine
rebuilds a gem's or rune's contribution from `gems.txt` given the host it sits in.

## Fidelity

Correctness here means byte-identical to the original, including its oddities — where the game is
inconsistent, this reproduces the inconsistency rather than tidying it up. Every behaviour is
traced to the instruction that causes it, and the code cites the address.

| | |
|---|---|
| differential corpus cases, C# vs TypeScript, agreeing layer by layer | **864** |
| hostile producer-legal inputs, both engines agreeing | **11,972** |
| tests | **920** C# / **930** TypeScript |
| captured client tooltips reproduced byte-identically | **64 / 64** |

The capture set is a private `captures.db` of real client tooltips and is not part of this
repository, so that last row is the one number you cannot reproduce from a clone.

## What is not implemented, and why

Everything below is a known gap rather than a bug. None of it throws; the output is simply absent
or English-only.

**Transaction-cost text.** `ShopMode` 1-9 is accepted and suppresses the book usage lines, but no
price line is produced on the generic path at all — the routine that computes the price has not
been decompiled. On the set-item path you get the "cannot be traded here" refusal, which is real.

**Only English.** The embedded tables are the ENG locale, and the possessive form used in item
names ("Bob's Hat") is wired for English only — twelve other language cases are identified but not
transcribed. Point `FromFiles` at another locale's tables and the strings change, but the
possessive grammar does not.

**Item-level-dependent values.** One property function has two arms that scale with the *item's*
level, which the record does not carry, so they fall back to level 1. No shipped item reaches them
— a test asserts that — but a modded table could.

**Eight property functions are unimplemented.** They are unreachable through this API rather than
merely unused: affixes, uniques and runewords arrive with their stats already computed, so nothing
calls them. A test walks the shipped tables and fails the build if that ever stops being true.

**The C++ producer is unfinished.** An optional capture half that reads a live game's memory. You
do not need it to use the library — only to generate records from a running client.

**No Diablo II: Resurrected support.** This models 1.14d; D2R's tables and item format differ.

## Contributing

Source, tests and the differential harness live at
[github.com/ResurrectedTrader/D2ItemToolkit](https://github.com/ResurrectedTrader/D2ItemToolkit).
`dotnet test` and `npm test` run the two suites; the repository's `CLAUDE.md` documents how the two
implementations are kept in agreement and what the working rules are.

## Licence

Source code is
[MIT](https://github.com/ResurrectedTrader/D2ItemToolkit/blob/main/LICENSE).

`data/` contains tables extracted from Diablo II 1.14d and embedded in the published packages so
the library works without a game install. Those files are the property of Blizzard Entertainment
and are **not** covered by the MIT licence. Diablo II is a trademark of Blizzard Entertainment,
Inc. This project is not affiliated with, endorsed by, or sponsored by Blizzard Entertainment.
